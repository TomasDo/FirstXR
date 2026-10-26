# Dental Navigation v2 Simulator

This tool is the navigation-side protocol fixture for the Unity client. It loads the repository proto at runtime and emits deterministic values, so generated JavaScript cannot drift from the wire contract.

```bash
npm install
npm run smoke
npm start -- --port 50051
```

Optional assets:

```bash
npm start -- \
  --port 50051 \
  --dicom-dir /absolute/path/to/dicom \
  --teeth /absolute/path/to/teeth.stl \
  --drill /absolute/path/to/drill.stl
```

The DICOM directory is sent in lexical filename order. Inputs must already use Explicit VR Little Endian (`1.2.840.10008.1.2.1`). Every file descriptor includes byte length and SHA-256; chunks include CRC-32 and can resume from offsets reported by the client.

Use `--faults` to inject a tracker-loss window into the live stream. The numeric thresholds in this simulator are test data for `sim-session-001`; they are not product or clinical defaults.

Point Beam Pro at the development machine's LAN IP. Port 50051 must be reachable from the device. `npm run smoke` uses loopback port 50061 and verifies context, thresholds, a valid navigation frame, and a versioned slice command.
