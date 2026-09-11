#!/usr/bin/env node
'use strict';

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const grpc = require('@grpc/grpc-js');
const protoLoader = require('@grpc/proto-loader');

const PROTO_PATH = path.resolve(__dirname, '../../dental_model_transfer.proto');
const EXPLICIT_VR_LITTLE_ENDIAN = '1.2.840.10008.1.2.1';
const DEFAULT_PORT = 50051;
const CHUNK_BYTES = 256 * 1024;

const packageDefinition = protoLoader.loadSync(PROTO_PATH, {
  keepCase: true,
  longs: String,
  enums: Number,
  defaults: true,
  oneofs: true,
});
const api = grpc.loadPackageDefinition(packageDefinition).dentalmodeltransfer;

function parseArgs(argv) {
  const options = { port: DEFAULT_PORT, dicomDir: '', teeth: '', drill: '', faults: false };
  for (let index = 0; index < argv.length; index += 1) {
    const name = argv[index];
    const value = argv[index + 1];
    if (name === '--port' && value) { options.port = Number(value); index += 1; }
    else if (name === '--dicom-dir' && value) { options.dicomDir = value; index += 1; }
    else if (name === '--teeth' && value) { options.teeth = value; index += 1; }
    else if (name === '--drill' && value) { options.drill = value; index += 1; }
    else if (name === '--faults') options.faults = true;
    else if (name === '--help') options.help = true;
    else throw new Error(`Unknown or incomplete option: ${name}`);
  }
  if (!Number.isInteger(options.port) || options.port < 1 || options.port > 65535)
    throw new Error('--port must be an integer from 1 to 65535');
  return options;
}

function usage() {
  return [
    'Usage: npm start -- [--port 50051] [--dicom-dir DIR] [--teeth FILE] [--drill FILE] [--faults]',
    '',
    'The default stream sends deterministic navigation/context/threshold/control data.',
    'DICOM input must already use Explicit VR Little Endian transfer syntax.',
  ].join('\n');
}

function identityWithTranslation(x, y, z) {
  return [1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z, 0, 0, 0, 1];
}

function createContext() {
  return {
    session_id: 'sim-session-001',
    case_id: 'SIM-CASE-001',
    dataset_id: 'sim-dataset',
    ct_id: 'sim-ct',
    plan_id: 'sim-plan-36',
    tooth_id: '36',
    tool_id: 'sim-drill-2.0',
    step_id: 'osteotomy',
    context_version: '1',
    plan_entry_mm: { x: 0, y: 0, z: 0 },
    plan_axis: { x: 0, y: 0, z: 1 },
    target_depth_mm: 10,
    buccal_axis: { x: 0, y: 1, z: 0 },
    // For normal +Z and buccal-up +Y, Cross(normal, up) is screen-left (-X).
    mesial_axis: { x: -1, y: 0, z: 0 },
    coordinate_frame_id: 'DICOM_PATIENT_LPS',
    patient_from_dicom: identityWithTranslation(0, 0, 0),
    distance_unit: 1,
    angle_unit: 1,
  };
}

function createThresholds() {
  return {
    session_id: 'sim-session-001', context_version: '1', config_version: '1',
    lateral_green_max_mm: 0.5, lateral_red_min_mm: 1.0, lateral_hysteresis_mm: 0.05,
    angle_green_max_deg: 2.0, angle_red_min_deg: 5.0, angle_hysteresis_deg: 0.2,
    depth_approach_mm: 1.0, depth_at_target_tolerance_mm: 0.2,
    depth_overrun_red_mm: 0.5, depth_hysteresis_mm: 0.1,
    boundary_rule: 1, distance_unit: 1, angle_unit: 1,
  };
}

function createSliceState(controlVersion, offsetMm, source = 1) {
  return {
    session_id: 'sim-session-001', context_version: '1',
    control_version: String(controlVersion), sync_enabled: true, source,
    plane: {
      volume_id: 'sim-ct', frame_of_reference_uid: 'sim-frame-of-reference',
      origin_mm: { x: 0, y: 0, z: 0 }, normal: { x: 0, y: 0, z: 1 },
      up: { x: 0, y: 1, z: 0 }, offset_mm: offsetMm,
      slice_index: Math.round(offsetMm), sop_instance_uid: '', has_physical_plane: true,
    },
  };
}

function createLayout(controlVersion) {
  return {
    session_id: 'sim-session-001', context_version: '1',
    control_version: String(controlVersion), source: 1,
    hud_visible: true, model_visible: false,
    hud_position_m: { x: 0, y: -0.13, z: 1.8 },
    model_position_m: { x: 0.32, y: -0.024, z: 1.8 },
    reset_to_default: false,
  };
}

function createFrame(sequence, faults) {
  const seconds = sequence / 30;
  const lateralBuccal = 0.75 * Math.sin(seconds * 0.65);
  const lateralMesial = 0.55 * Math.cos(seconds * 0.43);
  const tiltBuccal = 2.8 * Math.sin(seconds * 0.31);
  const tiltMesial = 1.7 * Math.cos(seconds * 0.51);
  const currentDepth = -1 + ((seconds * 0.8) % 12.5);
  const valid = !(faults && sequence % 900 >= 810);
  return {
    session_id: 'sim-session-001', context_version: '1', sequence: String(sequence),
    capture_time_unix_ms: String(Date.now()), valid,
    invalid_reason: valid ? '' : 'simulated tracker loss',
    drill_tip_mm: { x: -lateralMesial, y: lateralBuccal, z: currentDepth },
    drill_axis: { x: -Math.sin(tiltMesial * Math.PI / 180), y: Math.sin(tiltBuccal * Math.PI / 180), z: 1 },
    drill_from_teeth: identityWithTranslation(-lateralMesial, lateralBuccal, currentDepth),
    lateral_mm: Math.hypot(lateralBuccal, lateralMesial),
    lateral_buccal_mm: lateralBuccal, lateral_mesial_mm: lateralMesial,
    angle_deg: Math.hypot(tiltBuccal, tiltMesial),
    tilt_buccal_deg: tiltBuccal, tilt_mesial_deg: tiltMesial,
    current_depth_mm: currentDepth, target_depth_mm: 10,
    remaining_depth_mm: 10 - currentDepth,
    distance_unit: 1, angle_unit: 1,
    has_lateral_direction: true,
    has_tilt_direction: true,
    has_depth_breakdown: true,
  };
}

function writeWhenReady(call, message) {
  return new Promise((resolve, reject) => {
    try {
      if (call.write(message)) resolve();
      else call.once('drain', resolve);
    } catch (error) { reject(error); }
  });
}

function readExplicitVrDicomIdentity(bytes) {
  if (!Buffer.isBuffer(bytes) || bytes.length < 132 || bytes.toString('ascii', 128, 132) !== 'DICM')
    throw new Error('DICOM file is missing the Part 10 preamble and DICM marker');

  const longLengthVrs = new Set(['OB', 'OD', 'OF', 'OL', 'OV', 'OW', 'SQ', 'UC', 'UR', 'UT', 'UN']);
  let offset = 132;
  let transferSyntaxUid = '';
  let mediaStorageSopInstanceUid = '';
  let sopInstanceUid = '';
  let numberOfFrames = 1;

  function textValue(start, length) {
    return bytes.toString('ascii', start, start + length).replace(/[\0 ]+$/g, '').trim();
  }

  while (offset + 8 <= bytes.length) {
    const group = bytes.readUInt16LE(offset);
    const element = bytes.readUInt16LE(offset + 2);
    const vr = bytes.toString('ascii', offset + 4, offset + 6);
    let headerBytes = 8;
    let valueLength;
    if (longLengthVrs.has(vr)) {
      if (offset + 12 > bytes.length) throw new Error('DICOM element header is truncated');
      headerBytes = 12;
      valueLength = bytes.readUInt32LE(offset + 8);
    } else {
      valueLength = bytes.readUInt16LE(offset + 6);
    }
    if (valueLength === 0xffffffff)
      throw new Error('Undefined-length DICOM elements are not supported by the simulator identity check');
    const valueOffset = offset + headerBytes;
    const nextOffset = valueOffset + valueLength;
    if (nextOffset < valueOffset || nextOffset > bytes.length)
      throw new Error('DICOM element value exceeds file bounds');

    if (group === 0x0002 && element === 0x0010)
      transferSyntaxUid = textValue(valueOffset, valueLength);
    else if (group === 0x0002 && element === 0x0003)
      mediaStorageSopInstanceUid = textValue(valueOffset, valueLength);
    else if (group === 0x0008 && element === 0x0018)
      sopInstanceUid = textValue(valueOffset, valueLength);
    else if (group === 0x0028 && element === 0x0008) {
      const parsed = Number(textValue(valueOffset, valueLength));
      if (!Number.isSafeInteger(parsed) || parsed < 1 || parsed > 0xffffffff)
        throw new Error('DICOM NumberOfFrames must be a positive uint32');
      numberOfFrames = parsed;
    }

    offset = nextOffset;
    if (group === 0x7fe0 && element === 0x0010) break;
  }

  if (transferSyntaxUid !== EXPLICIT_VR_LITTLE_ENDIAN)
    throw new Error(`DICOM TransferSyntaxUID must be ${EXPLICIT_VR_LITTLE_ENDIAN}; got ${transferSyntaxUid || '(missing)'}`);
  sopInstanceUid ||= mediaStorageSopInstanceUid;
  if (!sopInstanceUid)
    throw new Error('DICOM SOPInstanceUID is missing');
  return { transferSyntaxUid, sopInstanceUid, numberOfFrames };
}

function collectAssets(options) {
  const files = [];
  if (options.dicomDir) {
    const root = path.resolve(options.dicomDir);
    for (const name of fs.readdirSync(root).sort()) {
      const filePath = path.join(root, name);
      if (fs.statSync(filePath).isFile() && /\.dcm$/i.test(name))
        files.push({ type: 1, filePath, relativePath: name, transferSyntax: EXPLICIT_VR_LITTLE_ENDIAN });
    }
  }
  if (options.teeth) files.push({ type: 2, filePath: path.resolve(options.teeth), relativePath: path.basename(options.teeth) });
  if (options.drill) files.push({ type: 3, filePath: path.resolve(options.drill), relativePath: path.basename(options.drill) });

  return files.map((asset, index) => {
    const bytes = fs.readFileSync(asset.filePath);
    const dicomIdentity = asset.type === 1 ? readExplicitVrDicomIdentity(bytes) : null;
    return {
      ...asset,
      bytes,
      descriptor: {
        asset_id: `asset-${String(index + 1).padStart(4, '0')}`,
        dataset_id: 'sim-dataset', asset_type: asset.type,
        relative_path: asset.relativePath, filename: path.basename(asset.filePath),
        total_bytes: String(bytes.length), sha256: crypto.createHash('sha256').update(bytes).digest(),
        media_type: asset.type === 1 ? 'application/dicom' : 'model/stl',
        transfer_syntax_uid: dicomIdentity ? dicomIdentity.transferSyntaxUid : '',
        sop_instance_uid: dicomIdentity ? dicomIdentity.sopInstanceUid : '',
        frame_count: dicomIdentity ? dicomIdentity.numberOfFrames : 0,
        order_index: index,
      },
    };
  });
}

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit += 1)
      crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function createService(options) {
  const assets = collectAssets(options);

  function streamSession(call) {
    let started = false;
    let frameTimer = null;
    let sequence = 0;
    let sliceControlVersion = 1;
    let sliceOffsetMm = 0;

    function stop() {
      if (frameTimer !== null) clearInterval(frameTimer);
      frameTimer = null;
    }

    call.on('data', request => {
      if (request.request && !started) {
        started = true;
        call.write({ navigation_context: createContext() });
        call.write({ tolerance_config: createThresholds() });
        call.write({ slice_state: createSliceState(sliceControlVersion, sliceOffsetMm) });
        call.write({ display_layout: createLayout(1) });
        call.write({ navigation_status: { session_id: 'sim-session-001', context_version: '1', state: 1, reason: '' } });
        frameTimer = setInterval(() => call.write({ navigation_frame: createFrame(++sequence, options.faults) }), 1000 / 30);
      }

      if (request.slice_command) {
        const command = request.slice_command;
        const expected = Number(command.base_control_version);
        if (expected !== sliceControlVersion) {
          call.write({ command_result: {
            session_id: 'sim-session-001', context_version: '1',
            command_sequence: command.command_sequence, control_version: String(sliceControlVersion),
            accepted: false, message: 'stale slice control version',
          } });
          return;
        }
        if (command.selection === 'delta_steps') sliceOffsetMm += command.delta_steps;
        else if (command.selection === 'offset_mm') sliceOffsetMm = command.offset_mm;
        sliceControlVersion += 1;
        call.write({ slice_state: createSliceState(sliceControlVersion, sliceOffsetMm, command.source || 2) });
        call.write({ command_result: {
          session_id: 'sim-session-001', context_version: '1',
          command_sequence: command.command_sequence, control_version: String(sliceControlVersion),
          accepted: true, message: 'slice applied',
        } });
      }
    });
    call.on('end', () => { stop(); call.end(); });
    call.on('cancelled', stop);
    call.on('error', stop);
  }

  function streamDentalModel(call) {
    streamSession(call);
  }

  function streamAssets(call) {
    let transferStarted = false;
    const resumeOffsets = new Map();
    call.on('data', message => {
      if (message.resume) {
        for (const item of message.resume.assets || [])
          resumeOffsets.set(item.asset_id, Number(item.next_offset));
      }
      if (!message.request || transferStarted) return;
      transferStarted = true;
      const transferId = 'sim-transfer-001';
      call.write({ manifest: {
        transfer_id: transferId, session_id: 'sim-session-001', dataset_id: 'sim-dataset',
        context_version: '1', assets: assets.map(asset => asset.descriptor),
      } });

      setTimeout(async () => {
        try {
          for (const asset of assets) {
            const id = asset.descriptor.asset_id;
            for (let offset = resumeOffsets.get(id) || 0; offset < asset.bytes.length; offset += CHUNK_BYTES) {
              const data = asset.bytes.subarray(offset, Math.min(asset.bytes.length, offset + CHUNK_BYTES));
              await writeWhenReady(call, { chunk: {
                transfer_id: transferId, asset_id: id, offset: String(offset), data, crc32: crc32(data),
              } });
            }
          }
          call.write({ complete: {
            transfer_id: transferId, session_id: 'sim-session-001', dataset_id: 'sim-dataset',
            context_version: '1', ok: true, message: 'all simulator assets sent',
            results: assets.map(asset => ({
              asset_id: asset.descriptor.asset_id, ok: true, message: '',
              received_bytes: asset.descriptor.total_bytes, sha256: asset.descriptor.sha256,
            })),
          } });
        } catch (error) {
          console.error('asset stream failed:', error.message);
        }
      }, 150);
    });
    call.on('end', () => call.end());
  }

  return { streamDentalModel, streamSession, streamAssets };
}

function startServer(options, callback) {
  const server = new grpc.Server();
  server.addService(api.DentalModelTransfer.service, createService(options));
  server.bindAsync(`0.0.0.0:${options.port}`, grpc.ServerCredentials.createInsecure(), (error, port) => {
    if (error) throw error;
    console.log(`Dental navigation simulator listening on 0.0.0.0:${port}`);
    console.log(`Assets: DICOM=${options.dicomDir || '(none)'}, teeth=${options.teeth || '(none)'}, drill=${options.drill || '(none)'}`);
    if (callback) callback(server, port);
  });
  return server;
}

if (require.main === module) {
  try {
    const options = parseArgs(process.argv.slice(2));
    if (options.help) { console.log(usage()); process.exit(0); }
    const server = startServer(options);
    const stop = () => server.tryShutdown(() => process.exit(0));
    process.on('SIGINT', stop);
    process.on('SIGTERM', stop);
  } catch (error) {
    console.error(error.message);
    console.error(usage());
    process.exit(2);
  }
}

module.exports = {
  api,
  createFrame,
  createService,
  parseArgs,
  readExplicitVrDicomIdentity,
  startServer,
};
