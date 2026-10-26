# First XR

A dental implant navigation client built with **Unity 6.0 LTS (6000.0.83f1)** and **XREAL XR Plugin 3.1.0**, with **HelloMR** as its main scene. While the clinician looks down at the operative field, the lower XREAL view shows a CT cross-section, a circular target with fixed Buccal/Lingual and Mesial/Distal orientation, lateral and angular deviation directions, numerical values, and a vertical depth scale. Beam Pro displays the same navigation state and the real XR left-eye image; the navigation software can request left-eye streaming on demand.

`RgbFeaturePolicy.Enabled` is currently `false`: RGB camera capture, local RGB preview, MediaPipe gesture slicing, and RGB streaming are paused, and related operations display "RGB paused". Their implementations remain in the source; behavior after re-enabling the switch requires separate validation. Local XR left-eye preview and XR-only streaming with valid real frames remain available.

The implementation follows `dental_model_transfer.proto` v2, with separate channels for navigation/control, DICOM/STL assets, and media. v1 services can still connect, but missing directions, tolerances, or CT are explicitly reported as unavailable capabilities; complete navigation is not inferred from old scalars. Protocol and integration details: [`Docs/DentalNavigationV2Interface.md`](Docs/DentalNavigationV2Interface.md).

## Requirements

| Item | Details |
|------|------|
| Unity | 6.0 LTS, pinned to `6000.0.83f1`; use the ARM64 editor on Apple Silicon Macs |
| Android tools | Install Android Build Support, SDK & NDK Tools, and OpenJDK through Unity Hub; use the editor's bundled JDK 17, NDK r27c, and SDK Build Tools 36.0.0 |
| Platform | Android (`minSdk 29`) |
| Hardware | XREAL glasses + Beam Pro (or an Android device supporting adb) |
| Hardware | XREAL Eye (required for offline OK gestures and on-demand RGB streaming) |
| Optional | Surgical robot gRPC server (default `192.168.31.64:50051`, editable on the Connection page) |

Main dependencies: `com.xreal.xr`, `com.unity.xr.hands`, `com.unity.xr.interaction.toolkit`, `com.unity.xr.arfoundation`, pinned `com.google.mediapipe:tasks-vision:1.0.0`, and the gRPC / Protobuf runtimes in `Assets/Plugins/Grpc/`. The MediaPipe model is packaged with the APK and does not access the network at runtime.

XR packages remain aligned with existing scenes and Samples: AR Foundation `6.0.8`, XR Interaction Toolkit `2.6.5`, and XR Hands `1.5.1`. Do not upgrade XR Interaction Toolkit or XR Hands without migrating their Samples. Apple Silicon Macs require Rosetta for Intel components in the current editor toolchain. Enable Unity's bundled JDK, SDK, NDK, and Gradle in **Unity → Settings → External Tools**.

Local upgrade changes, validation results, and backup locations: [`Docs/Unity6Migration.md`](Docs/Unity6Migration.md).

Android permissions (`Assets/Plugins/Android/CameraPermission.androidlib/AndroidManifest.xml`):

- `CAMERA`: RGB camera; camera permission is not requested while RGB is paused.
- `INTERNET` / `ACCESS_NETWORK_STATE`: surgical robot gRPC.
- `WAKE_LOCK`: Wi-Fi low-latency lease during navigation connections, released on pause or connection completion.

## Quick Start

1. Open this repository in Unity.
2. In **File → Build Profiles**, confirm Android is selected and the scene list enables:

   `Assets/Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR/HelloMR.unity`
3. To connect a surgical robot, change the default IP / port in `DentalRobotConnectionDefaults.cs`, or enter them on Beam Pro's **Connection** page and select **Connect**.
4. Connect Beam Pro, enable USB debugging, and select **Run Device** in Unity.
5. Build and run:
   - For performance retesting, use **XREAL → Build → Performance APK (Release, Build Only)**, which outputs `Builds/First XR-performance.apk`. Then use **XREAL → Launch App On Android Device** to install and launch the most recently successful APK.
   - For a general build, use **XREAL → Build → Android APK (Build Only, No Unity Launch)**, retaining the current Development Build setting. If **File → Build And Run** throws NullReferenceException at launch, check the build result first, then use the installation action above.

Without navigation software, start the deterministic v2 simulator:

```bash
cd Tools/navigation-simulator
npm install
npm start -- --port 50051 --dicom-dir /absolute/path/to/explicit-vr-dicom
```

The simulator supports replay of protocol, direction, tolerance, invalidation, and slice-control behavior. It does not replace acceptance testing with Beam Pro + XREAL + a dental model.

## Features

### Surgical Navigation HUD (Product Default)

`DentalHudController` places a World Space Canvas **1.80 m** in front of the main camera (1000×560 mm, `scale=0.001`). The central operative field stays clear, with content along the lower edge:

| Region | Content |
|---|---|
| Status | Case/tooth/drill/step, connection state, and navigation data age |
| CT target | CT slice, fixed B/L/M/D markers (Buccal top, Lingual bottom, Mesial left, Distal right), reference circle, and planned center |
| Position deviation | A solid dot indicates 2D direction; color reflects only position thresholds; numbers show magnitude and anatomical direction |
| Angular deviation | Outer-ring color reflects only angle thresholds; an outlined triangle indicates tilt direction and hides at zero angle |
| Depth scale | Entry point at zero, current pointer, target line, over-depth region; remaining depth is most prominent |
| Warnings | Each abnormal channel reports independently; normal metrics cannot mask abnormal ones |

`DentalNavigationState` is the state bus: the client consumes only the newest sequence frame in the current context. Data is live at ≤200 ms; from 200–500 ms, valid colors are removed and delay is indicated; beyond 500 ms or on explicit interruption, dynamic markers and values are hidden. Static CT may remain visible with status.

Color thresholds, boundary rules, and hysteresis parameters all come from the navigation system for the current context version. Missing tolerances display "Thresholds not synced"; no local clinical defaults are substituted. Protocol distances are fixed to mm and angles to °. Values display one decimal place; classification uses original precision.

The 3D model is a separate observation window, hidden by default during implantation. The navigation system can independently move it, toggle visibility, and restore its default position. It is an observation window, not an overlay registered to real teeth.

Beam Pro's **Monitor** page provides mutually exclusive **Hover / Follow** buttons that control spatial anchoring for both HUD and 3D model. **Follow** is the default and maintains the configured head-relative positions. Selecting **Hover** fixes content at its world pose at that instant; selecting **Follow** again restores the configured head-relative positions. This changes only local display anchoring, not navigation metrics or the protocol.

Beam Pro opens on **Monitor** by default, where HUD and 3D model visibility can be toggled. Enabling **Engineer Mode** on HelloMR adds a **Debug** tab at the bottom.

### Tracking Modes

Switch through the glasses UI or scene Toggles:

- **0 DoF**
- **0 DoF Stable**
- **3 DoF**
- **6 DoF** (default, suitable for spatial anchoring and object placement)

The status bar shows the current `Track` and `Input` modes.

### Input Modes

| Mode | Behavior |
|------|------|
| **Controller (default)** | Interaction driven by a virtual Controller |
| **Hands** | Full hand input (gestures can directly control UI/objects) |

Startup default: **Controller**.

Beam Pro does not show an input switch in product mode. With HelloMR **Engineer Mode** and the corresponding Inspector settings enabled, the **Debug** page can switch Controller / Hand. The glasses Canvas also has **Controller / Hand** buttons.

### RGB Camera Floating Window

RGB is currently paused, including RGB preview operations in Engineer Mode. The retained `RGBCameraFloatingWindow` implementation behaves as follows after re-enabling the switch: it displays live **XREAL Eye** RGB imagery in MR space, hides the window by default, does not start capture automatically, and provides Show / Hide RGB Window in Engineer Mode.

- Automatically requests Android `CAMERA` permission.
- Waits for Eye insertion (PLUGIN state), then retries capture startup.
- Renders through a YUV → RGB shader onto a world-space Quad.
- Writes debug status to the Beam Pro log (source: `RGB camera`; visible only on the **Debug** page in Engineer Mode).

### Offline OK Gesture Slicing

Gesture recognition and slicing are currently paused along with RGB. The retained `RgbHandGestureRecognizer` uses the APK-bundled MediaPipe Hand Landmarker to extract 21 hand landmarks from shared XREAL Eye RGB frames; images and landmarks are not uploaded. After re-enabling RGB, gesture capture and RGB streaming share one camera service; disabling only RGB streaming does not stop gesture capture.

| Gesture | Label | Detection |
|------|------|------|
| `Ok` | OK | Thumb and index finger closed, with at least two other fingers extended |
| `None` | None | No valid OK gesture detected, or insufficient confidence |

Implementation details:

- YUV converts to 480×270 RGBA on the GPU. MediaPipe runs on one Android worker thread; old frames are dropped while busy rather than queuing inference work.
- Holding OK steadily for 300 ms enters slice adjustment. Moving upward while holding OK decreases the slice index; moving downward increases it.
- Motion has a dead zone and step-rate limit; substantial head motion freezes adjustment and resets the reference.
- Release, occlusion, frame loss over 180 ms, or navigation-system takeover exits immediately. A new OK gesture is required to resume.
- Recognition rates with surgical gloves, dental lighting, and occlusion must be recorded separately during device acceptance.
- Other scripts can subscribe to slice-step events:

```csharp
RgbSliceGestureController.AnySliceStepRequested += step =>
{
    Debug.Log($"slice delta={step.Step}, control={step.ControlVersion}");
};
```

The current product startup does not enable slice gestures. After re-enabling the switch, camera, model, or MediaPipe initialization failures report an explicit reason and prevent slicing.

### Left-Eye Preview (Beam Pro Screen)

`LeftEyeDisplayWindow` mirrors the **actual XR left-eye rendering output** to Beam Pro. `XrRgbRtpStreamer` currently accepts on-demand XR-only streaming, outputting one RTP video stream at 1280×720, 15 fps, without audio by default. Requests including RGB are rejected with "RGB paused". XR-only startup requires Android and an available real left-eye frame. Unavailable real frames or encoding report a reason; substitute imagery is not presented as XR output. Source code retains dual-view compositing for when RGB is re-enabled.

### Reference Objects

`ReferenceCubeSpawner` creates spatial reference content in front of the headset:

- Optional **reference cube** (RGB axes, six face patterns, automatic rotation; disabled by default in the scene).
- Optional **Check Plane** (loads `check_plane.STL` from StreamingAssets; **disabled by default in the scene**).
- In Engineer Mode, **Move X/Y/Z ±** on Beam Pro's **Debug** page translates objects. When Check Plane exists, opacity and RGB color channels can also be adjusted.

### Surgical Robot Models (gRPC)

Receives context, navigation frames, controls, and assets from navigation software through bidirectional streams defined by `dental_model_transfer.proto`:

| Component | Role |
|------|------|
| `DentalRobotConnectionDefaults` | Shared default connection parameters (Host / Port / DeviceId / DatasetId) |
| `DentalRobotGrpcClient` | gRPC client; can search automatically at startup or through UI-triggered `SearchEndpoint` |
| `DentalNavigationState` | Context/frame/tolerance/slice/layout/observation state and version filtering |
| `DentalNavigationBand` | Thresholds, hysteresis, and warning text |
| `DentalHudController` | Head-locked glasses HUD |
| `DentalRobotBeamProDisplay` | Connection actions and navigation-state data source for the paged UI |
| `DentalRobotModelRenderer` | Parses STL, renders a head-locked model overview, positions the drill using `drill_from_teeth` |
| `DentalCtVolumeService` | DICOM chunk resume, SHA verification, parsing, volume data, and on-demand slice cache |
| `DentalCtSliceCoordinator` | Planned-axis slicing, anatomical orientation, and applying control from both ends |
| `DentalStlMeshUtility` | Shared binary / ASCII STL mesh parsing |

Default connection:

```
Host: 192.168.31.64
Port: 50051
device_id: beam-pro
dataset_id: default
```

Runtime behavior:

1. Persistent `StreamSession` receives context, tolerances, and live frames; frames with old contexts or sequences are rejected.
2. `StreamAssets` independently receives complete DICOM/STL assets. DICOM chunks are acknowledged only after successful persistence, with covered-range resume and SHA-256 verification. Asset completion does not stop live navigation.
3. CT defaults to a physical slice through the planned entry point and perpendicular to the planned axis; navigation-system slices and control versions take priority.
4. `StreamSession` also handles layout and observation control. Slice gestures carry the current control version, and the navigation system rejects stale commands.
5. v1 `StreamDentalModel` remains a compatibility entry point; field numbers are not reused.

### Other UI

- **Show / Hide Glasses UI**: toggles the glasses control Canvas (tracking Toggles, etc.) in Engineer Mode.
- **Vibrate**: Controller vibration test (glasses-side button).

## Beam Pro Layout and Controls

`BeamProPagedController` is Beam Pro's sole rendering entry point. Legacy navigation dashboard, left-eye preview, log, and HelloMR button components continue providing state and actions but stop rendering independently when the paged UI is active, avoiding overlapping coordinates. Navigation, XR capture, and enabled media services do not depend on the active tab; switching pages does not stop background work.

| Page | Content |
|---|---|
| **Monitor** (default) | Fixed connection status, 16:9 XR left-eye preview, four cards for remaining depth/position offset/angular deviation/overall status, HUD/model visibility, Hover/Follow, case/CT/tolerance/asset status |
| **Connection** | Current connection state, separate IP/port inputs, Connect/Reconnect button, operation result; drafts are not applied until submitted |
| **Debug** | Display, camera, gesture, navigation-object, and Check Plane controls plus unified logs; visible only in Engineer Mode, with unconfigured controls hidden according to Inspector switches |

Layout scales consistently from a **540-logical-unit short edge**: outer margin 20, module spacing 12, minimum touch-target height 56. The top status bar and bottom tabs remain fixed while the body scrolls independently. Safe areas, soft-keyboard avoidance, long-text measurement, and scrollbar space are supported. Portrait prioritizes preview; landscape places preview beside metric cards.

Preview displays only real XR left-eye frames returned by `LeftEyeDisplayWindow.TryGetLiveXrFrame`. Invalid frames immediately produce an unavailable state; old images and substitute main-camera images are not shown as live output.

In Editor Play Mode, **Tools → Beam Pro Preview** can open any page or generate 1080×2400 screenshots of all three pages. Earlier editor layout screenshots are retained below; see the [paged UI validation record](Docs/BeamProPagedUiVerification.md) for their original validation scope, and the [navigation validation record](Docs/DentalNavigationValidation.md) for performance and device results. These historical screenshots retain their original Chinese UI and do not validate the English translation.

- [Monitor page](Docs/BeamProScreenshots/monitor-editor.png)
- [Connection page](Docs/BeamProScreenshots/connection-editor.png)
- [Debug page](Docs/BeamProScreenshots/debug-editor.png)

Editor preview reuses production IMGUI rendering code. Device touch, the soft keyboard, XREAL dual displays, and real XR frames still require Beam Pro verification.

### Differences from the Glasses UI

| Location | Content |
|------|------|
| **Beam Pro phone screen** | Paged Monitor, Connection, and engineering Debug UI |
| **XREAL glasses HUD** | Navigation Canvas (A–F), following the head by default, switchable to world-space Hover from Beam Pro; HelloMR DoF Canvas hidden by default |
| **MR world space** | Teeth/drill model overview shares Hover/Follow mode with the HUD; RGB Quad and Check Plane are not created or remain hidden by default |

## Build and Deployment

Editor helper scripts provide these **XREAL** menu actions:

| Menu | Behavior |
|------|------|
| **Launch App On Android Device** | Installs the most recently successful APK through adb and launches `NRXRActivity`; does not launch after an installation failure |
| **Build → Android APK (Build Only, No Unity Launch)** | Builds only the APK, avoiding Unity's own launch failure |
| **Build → Performance APK (Release, Build Only)** | Forces a Release build to `Builds/First XR-performance.apk` |
| **Build → Enable / Disable Debug Build (Android)** | Explicitly changes and saves Development Build / Script Debugging settings; disabled by default |

## Local Validation

On macOS, run these commands from the repository root. First close any Unity editor using the same project. The Test Runner exits after completing tests; do not add `-quit`. These headless EditMode tests do not validate actual visuals.

```bash
mkdir -p Logs/validation
/Applications/Unity/Hub/Editor/6000.0.83f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath "$(pwd)" -buildTarget Android \
  -runTests -testPlatform EditMode \
  -testResults Logs/validation/editmode.xml \
  -logFile Logs/validation/editmode.log
```

Check the XML totals, failure counts, and skipped counts. Results include both `Assets/Tests/EditMode` and `Assets/Tests/TransportEditMode` assemblies. The protocol simulator has a separate smoke-test entry point:

```bash
npm --prefix Tools/navigation-simulator ci
npm --prefix Tools/navigation-simulator run smoke
```

The smoke test uses local port `50061` and covers context, tolerances, continuous frames, versioned slices, and asset lifecycle. These are Editor and protocol checks, not substitutes for Android builds, Beam Pro / XREAL device experience, or clinical acceptance.

## Core Project Structure

```
Assets/
├── Editor/
│   ├── XREALAndroidLaunchHelper.cs      # adb deployment and launch
│   ├── ProjectDebugBuildSettings.cs     # Persist explicit Debug / Release settings
│   └── XREALLicenseSetup.cs             # License file setup (optional)
├── Plugins/
│   ├── Android/CameraPermission.androidlib/  # CAMERA / network permissions
│   ├── Android/MediaPipeHandLandmarker.androidlib/ # Android MediaPipe bridge + offline model
│   └── Grpc/                            # gRPC / Protobuf runtime DLLs
├── StreamingAssets/
│   ├── check_plane.STL                  # Check Plane model
│   └── nr_plugins.json
└── Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR/
    ├── HelloMR.unity                    # Main scene
    ├── HelloMR.cs                       # Tracking/input/UI control + page action interface
    ├── BeamProPagedController.cs        # Sole Beam Pro Monitor/Connection/Debug renderer
    ├── BeamProPageLayout.cs             # Pure-function page layout, safe areas, keyboard avoidance
    ├── BeamProOverlayLayout.cs          # Legacy overlay layout (not drawn with active pages)
    ├── BeamProUnifiedLogWindow.cs       # Unified log data source
    ├── RgbCameraFrameService.cs         # Sole RGB camera owner and multiple-consumer distribution
    ├── RgbFeaturePolicy.cs              # Pause switch for RGB capture, gestures, and streaming
    ├── RGBCameraFloatingWindow.cs       # World-space RGB preview
    ├── RgbHandGesture.cs                # Gesture enum and Observation
    ├── RgbHandGestureAnalyzer.cs        # OK rules for 21 landmarks
    ├── MediaPipeAndroidHandLandmarkProvider.cs # Android offline landmark adapter
    ├── RgbHandGestureRecognizer.cs      # Eye RGB landmark recognition + events/logs
    ├── RgbSliceGestureStateMachine.cs   # 300 ms OK hold, dead zone, rate limits, invalidation
    ├── XrRgbRtpStreamer.cs              # Composite RTP from XR left eye + optional RGB
    ├── LeftEyeDisplayWindow.cs          # XR left-eye capture and paged preview data source
    ├── ReferenceCubeSpawner.cs          # Reference cube / Check Plane
    ├── DentalRobotConnectionDefaults.cs # Default gRPC connection parameters
    ├── DentalRobotGrpcClient.cs         # gRPC connection, session, and asset flow
    ├── DentalRobotGrpcClient.Protocol.cs # Same partial class: protocol/domain mapping
    ├── DentalRobotGrpcClient.Streaming.cs # Same partial class: stream I/O, queues, receive progress
    ├── DentalNavigationState.cs         # Navigation state bus
    ├── DentalNavigationBand.cs          # Thresholds / hysteresis / warnings
    ├── DentalNavigationPresentation.cs  # Shared anatomical text and phone status summaries
    ├── DentalDisplayLayoutController.cs # Dual-window position, visibility, and Hover/Follow state
    ├── DentalHudController.cs           # Glasses HUD with Follow or world-space Hover
    ├── DentalRobotModelRenderer.cs      # Teeth/drill model overview
    ├── DentalRobotBeamProDisplay.cs     # Navigation state data source + IP/port connection
    ├── DentalStlMeshUtility.cs          # Shared STL mesh parsing
    ├── Dicom/                           # DICOM transfer, decoding, volume data, and slicing
    └── GrpcGenerated/                   # Generated dental_model_transfer code
```

The root also contains `dental_model_transfer.proto`; `Tools/navigation-simulator` provides an executable v2 gRPC simulator and smoke tests.

## Troubleshooting

**Build And Run throws NullReferenceException**
This often means the Unity Editor failed to launch the APK, which may already have been built. Use **XREAL → Launch App On Android Device** or run `adb install -r` manually.

**Hand models are not visible**
Run on a real device; hand meshes appear only after tracking succeeds (`isTracked`). Try **Hands** input for comparison. Confirm that the device supports hand tracking and that hands are within the RGB camera's field of view.

**No RGB camera image**
`RgbFeaturePolicy.Enabled` is currently `false`, so the RGB camera and preview are paused; "RGB paused" is expected. After re-enabling the switch, check the XREAL Eye connection, Camera permission, and plug / capture states in **`[RGB camera]`** logs.

**RGB gestures keep showing "None"**
Gestures are currently paused along with RGB. After re-enabling the switch, confirm that Eye RGB produces frames and that `[Gesture recognition]` reports `MediaPipe Hand Landmarker 1.0.0` as running. Keep the entire hand in view and hold OK steadily. The editor has no Android MediaPipe runtime and explicitly reports unavailability. Device testing still requires actual gloves, dental lighting, instruments, and occlusion.

**Cannot connect to the surgical robot through gRPC**
Confirm that the phone and robot server share a LAN. Check IP/port on Beam Pro's **Connection** page and select **Connect**. In product mode, inspect the top status bar; in Engineer Mode, inspect **`[Surgical robot]`** logs on the **Debug** page. Defaults are in `DentalRobotConnectionDefaults.cs`.

**Glasses HUD values are approximately 1000 times too large or too small**
v2 accepts only mm and °. Correct the units sent by the navigation system; the client does not infer units from an Inspector scale. v1 compatibility data follows the old service convention but does not invent missing direction or tolerance capabilities.

**gRPC scripts report missing assemblies in the Editor**
Confirm that PluginImporter enables **Editor** for every DLL in `Assets/Plugins/Grpc/`.

## Licenses

This project extends official XREAL and Unity Samples. Third-party Samples and Plugins retain their respective licenses.
