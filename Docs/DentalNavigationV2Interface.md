# XREAL Dental Implant Navigation Interface v2

This document describes the client interface implemented in this repository. The root-level `dental_model_transfer.proto` is the sole authority for the wire format; the navigation system must not infer field numbers from this prose.

## 1. Channels and Lifecycles

The `dentalmodeltransfer.DentalModelTransfer` service provides three bidirectional streams:

| RPC | Purpose | Lifecycle |
|---|---|---|
| `StreamSession` | v2 context, live navigation frames, tolerances, slices, layout, and observation control | Persistent session; ends only on `SessionEnd` or transport disconnection |
| `StreamAssets` | Manifests, chunks, resume, and verification for complete DICOM and STL assets | Independent connection; `AssetTransferComplete` does not end navigation |
| `StreamDentalModel` | v1 compatibility | Used only when the server returns `UNIMPLEMENTED` for `StreamSession` |

The client's initial request specifies `protocol_version=2` and declares its DICOM, slicing, layout, XR mirror, and RGB capabilities. The server should send `NavigationContext` before other data sharing that `session_id/context_version`.

All v1 field numbers are unchanged. v1 scalars can only be shown as compatibility data; the client neither infers Buccal/Lingual or Mesial/Distal direction from total deviation nor supplies local clinical thresholds.

## 2. Coordinates, Directions, and Units

- v2 accepts only `DISTANCE_UNIT_MILLIMETER` for distances and `ANGLE_UNIT_DEGREE` for angles. Navigation frames with mismatched units are invalid.
- All matrices are row-major 4×4 arrays of 16 `double` values. A field named `target_from_source` transforms points from source coordinates to target coordinates.
- `NavigationContext.patient_from_dicom` transforms DICOM Patient coordinates into navigation patient/plan coordinates. The client centrally computes its inverse, transforms the physical planning or slice plane back into DICOM Patient coordinates, and samples there.
- `buccal_axis` and `mesial_axis` belong to the current tooth's navigation patient/plan coordinates. The display fixes Buccal at the top, Lingual at the bottom, Mesial on the left, and Distal on the right; these directions do not rotate with head movement.
- `lateral_buccal_mm > 0` means the drill tip deviates toward Buccal; `< 0` means Lingual. `lateral_mesial_mm > 0` means Mesial; `< 0` means Distal.
- Tilt components use the same sign convention. The drill axis is always defined from the tail toward the tip.
- `current_depth_mm` is the projection of the drill tip relative to the planned entry point along the planned axis; `remaining_depth_mm = target_depth_mm - current_depth_mm`. Negative values must be transmitted unchanged; the client displays "Over-depth".

The navigation system computes authoritative metrics. The client performs version checks, display, and state classification; it does not turn deviation directions into automatic force commands.

## 3. Live Session Sequence

The recommended server sequence for starting a context is:

1. `NavigationContext`: case, CT, plan, tooth, drill, step, planned geometry, and versions.
2. `ToleranceConfig`: thresholds, hysteresis, boundary rule, and configuration version matching the context version.
3. `SliceState`: current synchronization state, physical slice, and control version.
4. `DisplayLayout`: HUD/model visibility, local head-locked positions, and control version.
5. `NavigationStatus(ACTIVE)`.
6. Continuous `NavigationFrame` messages in acquisition order.

Every received item is checked against the session/context version. When changing the plan, tooth, drill, or step, the navigation system must increment `context_version` and resend the context, tolerances, and slice state. A new context clears the client's previous dynamic frame, tolerances, and control state.

`NavigationFrame.sequence` must increase strictly within a context. The client uses one latest-only slot, and the main thread consumes only the newest frame; assets and video do not queue historical navigation frames for display.

Invalidation rules:

| Condition | Client behavior |
|---|---|
| Frame explicitly has `valid=false` | Immediately removes dynamic markers and numbers and displays `invalid_reason` |
| More than 200 ms since the last valid frame | Removes valid green/amber/red colors and indicates delay |
| More than 500 ms since the last valid frame | Hides dynamic markers and values and displays "Navigation data interrupted" |
| `NavigationStatus(STOPPED)`, session disconnection, or context mismatch | Immediately invalidates navigation; does not retain old green status |

The static CT slice may remain visible alongside the invalid navigation status.

## 4. Tolerances

`ToleranceConfig` must match the current `context_version`, and `config_version` must increase monotonically; identical content may be resent idempotently with the same version. The client has no fallback clinical values.

Position and angle receive separate green, amber, and red classifications, each with its own hysteresis. Depth configuration includes approach distance, target tolerance, red over-depth threshold, and hysteresis. Displayed values use one decimal place; all comparisons use unrounded values.

For position/angle, `THRESHOLD_BOUNDARY_RULE_UPPER_BOUNDS_INCLUSIVE` means:

- `value <= green_max`: green
- `green_max < value <= red_min`: amber
- `value > red_min`: red

`UPPER_BOUNDS_EXCLUSIVE` assigns equality at each boundary to the higher grade. Negative depth values are not clamped.

## 5. DICOM and STL Assets

The navigation system sends a complete manifest for the current `dataset_id/context_version` through `StreamAssets`. The initial DICOM implementation accepts only uncompressed Explicit VR Little Endian, UID `1.2.840.10008.1.2.1`; the navigation system must losslessly decompress compressed source data before sending it.

Each `AssetDescriptor` must supply a stable `asset_id`, byte count, and 32-byte SHA-256. Chunks may arrive out of order or be repeated; a chunk must not exceed the declared size. When CRC-32 is nonzero, the client verifies each chunk.

Client behavior:

1. Creates a size-limited random-access `.part` file and persists covered ranges.
2. Sends `AssetChunkAck(accepted=true)` only after the chunk is successfully written to disk; failures return a rejection and the next missing offset.
3. Reports each asset's `next_offset` through `AssetResumeRequest` on reconnection.
4. After `AssetTransferComplete`, verifies complete coverage and SHA-256 for each file, then decodes DICOM.
5. Enables CT only after all instances/frames form a volume successfully. Failures retain a clear error and do not enable partial data.

Volume construction uses Image Position Patient, Image Orientation Patient, Pixel Spacing, Slice Thickness/adjacent positions, Bits Stored/Pixel Representation, and Rescale Slope/Intercept. Complete HU/raw intensity data remains on the CPU; the GPU caches only a limited number of display slices.

The repository includes a testable managed Explicit VR Little Endian parser and an `IDicomDecoder` replacement boundary. Android DCMTK native libraries are not yet included. Before integrating real clinical samples, complete the DCMTK adapter, ABI packaging, and sample comparison validation; see the validation record.

## 6. Slicing and Navigation-System Priority

Without a remote `SliceState`, once CT is ready the client displays a physical slice through the planned entry point and perpendicular to the planned axis, with `buccal_axis` toward the top. If a `SlicePlane` is provided, the client samples using origin/normal/up/offset.

- With synchronization enabled, the navigation system's state wins at connection or activation. Local OK gestures send changes as `SliceCommand`; after applying them, the navigation system broadcasts a new `SliceState`.
- With synchronization disabled, both ends may browse independently. The navigation system can still send an explicit glasses slice command with `source=NAVIGATION_SOFTWARE`.
- Every local command carries `base_control_version`. When taking control, the navigation system increments the version and rejects commands based on an older version. The client also ends any active OK gesture and requires release before restarting.

## 7. Layout and Observation Control

`DisplayLayout` controls positions and visibility separately for the HUD/CT group and the 3D model. Navigation-system versions take priority; applied layouts are stored on the device. `reset_to_default=true` restores HUD `(0,-0.13,1.80)m` and model `(0.32,-0.024,1.80)m`; the model is hidden by default.

`ObservationControl` can request the XR left-eye mirror and RGB clinician view independently. When either view is enabled, the client uses the XREAL singleton encoder to produce one fixed-layout RTP stream:

- Default: 1280×720, 15 fps, no audio; actual XR left eye on the left half, optional RGB on the right.
- Unrequested regions are black. If real XR frames are unavailable, other Unity cameras must not be presented as substitutes; status reports unavailability.
- Disabling RGB releases only the video consumer; a gesture consumer may still hold the camera.
- The encoding format, RTP payload type, and navigation-system FFmpeg parameters must be fixed during initial device integration testing, not confirmed solely from editor results.

The client reports runtime availability and errors through `ObservationStatus`.

## 8. Simulator

`Tools/navigation-simulator` loads the proto dynamically rather than maintaining another generated-code copy. It sends deterministic direction/depth trajectories at 30 frames per second and implements versioned slice takeover; sending real DICOM/STL files is optional.

```bash
cd Tools/navigation-simulator
npm install
npm run smoke
npm start -- --port 50051 --dicom-dir /absolute/dicom --teeth /absolute/teeth.stl --drill /absolute/drill.stl
```

`--faults` inserts approximately three seconds of explicit tracker loss every 30 seconds to check removal and recovery of dynamic markers. Simulator thresholds belong only to the simulated context and must not be used as clinical defaults.
