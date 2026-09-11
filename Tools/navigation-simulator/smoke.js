#!/usr/bin/env node
'use strict';

const grpc = require('@grpc/grpc-js');
const assert = require('assert');
const { api, readExplicitVrDicomIdentity, startServer } = require('./server');

function dicomElement(group, element, vr, text) {
  let value = Buffer.from(text, 'ascii');
  if ((value.length & 1) !== 0)
    value = Buffer.concat([value, Buffer.from(vr === 'UI' ? [0] : [0x20])]);
  const header = Buffer.alloc(8);
  header.writeUInt16LE(group, 0);
  header.writeUInt16LE(element, 2);
  header.write(vr, 4, 2, 'ascii');
  header.writeUInt16LE(value.length, 6);
  return Buffer.concat([header, value]);
}

const identityFixture = Buffer.concat([
  Buffer.alloc(128), Buffer.from('DICM', 'ascii'),
  dicomElement(0x0002, 0x0003, 'UI', '1.2.3.4'),
  dicomElement(0x0002, 0x0010, 'UI', '1.2.840.10008.1.2.1'),
  dicomElement(0x0008, 0x0018, 'UI', '1.2.3.4'),
  dicomElement(0x0028, 0x0008, 'IS', '3'),
]);
assert.deepStrictEqual(readExplicitVrDicomIdentity(identityFixture), {
  transferSyntaxUid: '1.2.840.10008.1.2.1',
  sopInstanceUid: '1.2.3.4',
  numberOfFrames: 3,
});

const port = 50061;
const timeout = setTimeout(() => fail(new Error('timed out waiting for simulator messages')), 8000);
let server;
let call;
let assetCall;
let sawContext = false;
let sawThresholds = false;
let sawFrame = false;
let sawSliceAfterCommand = false;
let sawAssetManifest = false;
let sawAssetComplete = false;
let sliceCommandSent = false;

function cleanup() {
  clearTimeout(timeout);
  if (call) call.cancel();
  if (assetCall) assetCall.cancel();
  if (server) server.tryShutdown(() => {});
}

function fail(error) {
  console.error(error.stack || error.message);
  cleanup();
  process.exitCode = 1;
}

server = startServer({ port, dicomDir: '', teeth: '', drill: '', faults: false }, () => {
  const client = new api.DentalModelTransfer(`127.0.0.1:${port}`, grpc.credentials.createInsecure());
  call = client.streamSession();
  assetCall = client.streamAssets();
  call.on('error', error => {
    if (error.code !== grpc.status.CANCELLED) fail(error);
  });
  call.on('data', message => {
    sawContext ||= Boolean(message.navigation_context);
    sawThresholds ||= Boolean(message.tolerance_config);
    sawFrame ||= Boolean(message.navigation_frame && message.navigation_frame.valid);
    if (message.slice_state && Number(message.slice_state.control_version) > 1)
      sawSliceAfterCommand = true;

    if (sawContext && sawThresholds && sawFrame && !sliceCommandSent) {
      sliceCommandSent = true;
      call.write({ slice_command: {
        session_id: 'sim-session-001', context_version: '1', base_control_version: '1',
        command_sequence: '1', source: 2, delta_steps: 1,
      } });
    }
    if (sawContext && sawThresholds && sawFrame && sawSliceAfterCommand
        && sawAssetManifest && sawAssetComplete) {
      console.log('PASS: context, thresholds, live frame, versioned slice control, and asset lifecycle');
      cleanup();
    }
  });
  assetCall.on('error', error => {
    if (error.code !== grpc.status.CANCELLED) fail(error);
  });
  assetCall.on('data', message => {
    sawAssetManifest ||= Boolean(message.manifest);
    sawAssetComplete ||= Boolean(message.complete && message.complete.ok);
  });
  call.write({ request: {
    device_id: 'sim-smoke', dataset_id: 'sim-dataset', protocol_version: 2,
    client_session_id: 'smoke', requested_capabilities: [1, 2, 3, 4, 5, 6],
  } });
  assetCall.write({ request: {
    device_id: 'sim-smoke', session_id: 'sim-session-001', dataset_id: 'sim-dataset',
    context_version: '1', include_all: true,
    accepted_transfer_syntax_uids: ['1.2.840.10008.1.2.1'],
  } });
});
