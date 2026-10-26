# First XR Device Test Checklist and Issue Log

Prepared on: 2026-09-29. Checklist baseline: code `fe53288`, Unity `6000.0.83f1`, and XREAL XR Plugin `3.1.0`.

Purpose: support development integration on Beam Pro + XREAL glasses + XREAL Eye and record reproducible observations for subsequent diagnosis and fixes. Use test cases and a dental model. This checklist is not a clinical acceptance conclusion.

> **This checklist has not been executed; all device results remain to be filled in.** The 34 pure-logic tests and Editor / Android conditional compilation completed in the preceding round do not establish that this round's APK build, device execution, or navigation-side integration has passed. After changing code, record the actual version and do not reuse conclusions from an earlier APK. Setup and feature expectations below describe the stated baseline; verify them against the build under test.

## 1. How to Use This Checklist

1. Copy this file for each round and name it `DeviceTestRecord_Date_Round.md`, for example, `DeviceTestRecord_20260929_01.md`. Retain the previous round's record.
2. Complete the environment information in Section 2, then prepare the device and services according to Section 3. For an initial run, the “Round 1” path below is sufficient.
3. Replace “Not tested” for each item with **Passed / Failed / Blocked / Not applicable**. If a service, dataset, or diagnostic tool is missing, record “Blocked + missing prerequisite”; do not count it as passed. For “Not applicable,” explain why the item is excluded from this round.
4. Under “Actual result and evidence,” record successful attempts/total attempts, the time of any anomaly, a brief observation, and the evidence filename. Create an issue ID in Section 8 for each failure, such as `BUG-001`.
5. Append retest results after a fix; do not overwrite the initial failure evidence. Send this Markdown file and the associated logs/videos when providing feedback.

| Test sequence | Goal for this round | Suggested scope | Completion / blockers |
|---|---|---|---|
| Round 1: installation and display | Confirm the new APK, glasses view, phone controls, camera, and basic gestures work | DEP-01–04, UI-01–05, GES-01–03, GES-06; add NET-01–04 and NAV-01 when a simulator is available | To fill in |
| Round 2: complete operations | Connect matching CT/STL and verify OK slicing, head motion, release, and Hover/Follow | CT-01–04, GES-04–05, GES-07–08, UI-06 | To fill in |
| Round 3: integration and failures | Directions/thresholds, navigation-side takeover, context changes, video, and recovery | NAV-02–09, CT-05–06, NET-05, OBS-01–05, REC-01–03 | To fill in |
| Targeted checks and extended operation | Verify millisecond-level behavior and resource stability | GES-09–10, PERF-01–03; requires suitable diagnostics | To fill in |

“Initial” denotes foundational checks to perform first; “Integration” requires complete services or datasets; “Targeted” requires diagnostic/measurement capabilities. **Expected results in these cases are behavior to be verified, not existing device conclusions.**

## 2. Environment and Build Information for This Round

| Item | Record for this round |
|---|---|
| Test round / tester | To fill in |
| Start and end time / time zone | To fill in, for example, 2026-09-29 14:00–15:00, Asia/Shanghai |
| Actual Git commit / branch | To fill in; this checklist was prepared against `fe53288` |
| Uncommitted source or scene changes | To fill in; especially record Engineer Mode settings rather than only the commit |
| APK filename / build time / SHA-256 | To fill in; must identify the APK to be installed for this round |
| App package name / versionName / versionCode | To fill in; project configuration at the baseline is `com.DefaultCompany.FirstXR` / `0.1` / `1`; use the actual APK as the source of truth |
| Build configuration | To fill in: Development on/off, Android ARM64 / IL2CPP; Profiler or other diagnostic settings |
| Unity / XREAL SDK version | To fill in |
| Beam Pro model / Android and system version | To fill in |
| XREAL glasses model / firmware version | To fill in |
| XREAL Eye model / firmware / connection method | To fill in; state if it is disconnected |
| Phone orientation / system auto-rotate setting | To fill in |
| Starting and ending battery / charging status / ambient temperature | To fill in |
| Network / Beam Pro IP / server IP:port | To fill in; distinguish the USB debugging channel from the navigation network |
| Beam Pro / server system times and known offset | To fill in; synchronize clocks first. Navigation data age uses the capture timestamp, so clock skew can appear as delayed or invalid data. |
| Navigation service type and version | To fill in: repository simulator / actual navigation system / dedicated injection service |
| session_id / context_version / case, plan, tooth, and drill | To fill in; use test identifiers |
| CT sample identifier / file count / frame count / transfer syntax | To fill in; do not include a real patient's name |
| CT SeriesInstanceUID / FrameOfReferenceUID / extents | To fill in; used to check that context and slice planes match |
| teeth / drill STL files and units | To fill in; state if they were not provided |
| Threshold source / version / boundary rules | To fill in; use this round's navigation-side configuration, not invented clinical defaults |
| Lighting / glove material and color / left or right hand / estimated distance from Eye | To fill in |
| Video receiver tool / host and UDP port | To fill in; state if video is excluded |
| Evidence directory for this round | To fill in, for example, `Logs/device-test/20260929-140000/` |

## 3. Preparation and Operating Instructions

### 3.1 First Confirm That the Installed APK Is the New Build

1. Open the project with the specified Unity version and enable the `HelloMR` scene in Android Build Profiles.
2. Decide whether to test the standard or engineering interface. For the engineering interface, enable **Engineer Mode** and **Show Beam Pro Gesture Toggle** in the `HelloMR` component Inspector, save the scene, and rebuild. Record these scene changes. The standard interface defaults are listed below.
3. Enable Development configuration through **XREAL → Build → Enable Debug Build (Android)** if needed. If Profiler / Script Debugging is required, separately verify the actual Build Profile and build log. The menu name is not evidence that the APK includes every debugging capability.
4. Build a new APK through **XREAL → Build → Android APK (Build Only, No Unity Launch)** and confirm successful completion. The default output is `Builds/First XR.apk`; calculate and record its SHA-256.
5. Explicitly install the specified APK as described in Section 7, then launch it. **Clicking only `XREAL → Launch App On Android Device` does not prove the new APK was installed**: the menu entry at this baseline may directly launch an older package already on the device.
6. Record installation output and the version information on the device. The project's versionCode alone cannot uniquely identify every build; retain both the APK hash and installation record. If signatures differ, preserve existing test evidence before deciding whether to uninstall. Do not clear app data by default just to run a test.

| Setting / resource | Baseline code and action for this round | Value / confirmation for this round |
|---|---|---|
| Engineer Mode | Off by default; the device normally shows only Monitor/Connection. Enable it in the Inspector when the log page is needed. | To fill in |
| Show Beam Pro Gesture Toggle | Off by default; this must also be enabled in Engineer Mode for the gesture toggle to appear. | To fill in |
| Enable Rgb Gesture Recognition | On by default in the baseline code. The lack of a toggle in the standard interface does not mean RGB is not being captured for gestures. | To fill in |
| Local RGB preview / remote RGB video | Local preview is hidden by default; remote video is enabled on demand through navigation-side `ObservationControl`. These are separate controls. | To fill in |
| HUD / 3D model / content anchoring | HUD visible, 3D model hidden, and Follow active by default. Model tests require an STL first and an explicit action to show it. | To fill in |
| Offline MediaPipe model | Before building, confirm that `Assets/Plugins/Android/MediaPipeHandLandmarker.androidlib/src/main/assets/mediapipe/hand_landmarker.task` exists. Git ignores this file, so it is not automatically carried over to another machine. | To fill in |
| Model inside the APK | Use APK Analyzer or `unzip -l` to confirm that `assets/mediapipe/hand_landmarker.task` is included. | To fill in |
| XREAL configuration / license | Verify that the required XREAL configuration and valid resources are packaged; retain the original error if initialization fails. | To fill in |
| CT decoder | Record whether the actual path uses the managed Explicit VR decoder or DCMTK. Passing the managed path cannot be recorded as validation of Android DCMTK. | To fill in |

### 3.2 Use the Simulator to Check the Basic Connection When No Actual Navigation System Is Available

Run this in a separate terminal on the development machine and keep the service running during testing:

```bash
cd "/Users/sad/code/Unity/First XR/Tools/navigation-simulator"
npm ci
npm run smoke
npm start -- --port 50051
```

On the Beam Pro **Connection** page, enter the development machine's **LAN IPv4 address** and `50051`, not the phone's own `127.0.0.1`. Ensure LAN isolation/firewall settings do not block that port. `smoke` uses loopback port `50061`; passing it establishes only a local simulator self-check, not a working device connection.

For explicit tracking-invalid windows, stop the previous instance, confirm that the port has been released, then run:

```bash
npm start -- --port 50051 --faults
```

This mode sends tracking-invalid frames for approximately 3 seconds every 30 seconds, for NAV-01. It **does not stop sending frames**, so it cannot replace the delay/silent-frame-loss test in NAV-05. Stopping the script may wait for existing connections to end; for a network-disconnection test, operate the phone's Wi-Fi as described in NET-04.

When matching CT/STL data is available, use the following arguments after replacing the example absolute paths:

```bash
npm start -- --port 50051 \
  --dicom-dir /absolute/path/to/dicom \
  --teeth /absolute/path/to/teeth.stl \
  --drill /absolute/path/to/drill.stl
```

| Service / data constraint | Effect on testing |
|---|---|
| The simulator reads only `.dcm` files at the top level of the DICOM directory and sends them in lexicographic filename order. | Files without that extension or in subdirectories are not sent. Do not interpret this as the client dropping slices. |
| The starting sample currently supported is uncompressed Explicit VR Little Endian, transfer syntax `1.2.840.10008.1.2.1`. | Test compressed data or unsupported geometry separately as rejection cases, not as basic successful-display cases. |
| Simulator context uses fixed CT coordinate identifiers, an origin, and axes. | **An arbitrary real CT dataset cannot be expected to display successfully.** Use a fixture matching the context, or have the navigation system send the correct UIDs, axes, and slice-plane position for the actual data. |
| Repository `smoke` does not provide actual CT/STL files. | A passing self-check cannot establish complete CT transfer, reconstruction, or display. |
| The simulator has no interactive console or commands for video toggles, plan changes, navigation-side takeover, or synchronization changes. | Corresponding cases are marked “Integration” and require the actual navigation system or a dedicated injection service. Mark them blocked when neither is available. |
| The default simulated trajectory does not cover all alarm threshold boundaries or the red position/angle ranges. | Boundary tests require a service that can specify input values. Color changes alone do not justify passing the complete case. |

### 3.3 What to Observe During Gesture Tests

- In an engineering build, enable gesture recognition under **Debug → Camera and gestures**. Observe provider, availability, and current gesture under `[Gesture recognition]`. If needed, enable the local RGB preview to confirm that the entire hand is inside Eye's field of view.
- Before each OK test or after recovering from a lost hand, first show Eye a fully visible, detectable open palm, then make OK. A no-hand frame enters the waiting-for-release state; returning into view while continuously holding OK does not clear it.
- The stable gesture name is recognition feedback. Separately observe OK slice states under `[Slice gesture]`: “Hold the OK gesture,” “Adjusting slice,” “Head moving; slice frozen,” and “Release the OK gesture and try again.” These use a different confirmation timer.
- OK: form a ring with the thumb and index finger and extend at least two other fingers. Pinch test: bring the thumb and index finger together while curling the other fingers to distinguish it from OK. Two-finger gesture: extend and separate the index and middle fingers while curling the ring and little fingers. A fist or unknown pose may display “None”; fist recognition is not an acceptance item here.
- The device stable-gesture confirmation parameter is **250 ms**, OK slice activation is **300 ms**, and observation-loss timeout is **180 ms**. These are code timers after observations are received, not end-to-end performance guarantees measured from the physical hand action. Do not add 250 ms and 300 ms as sequential stages totaling 550 ms.
- Moving the hand out of view or covering it produces a no-hand result; disabling recognition explicitly resets it. Neither precisely substitutes for the targeted condition where recognition remains active but temporarily receives no new observations. Turning off local RGB preview/remote RGB streaming also cannot create that condition while gestures still hold the shared camera.
- Device MediaPipe input is fixed at 480×270. Rotating the phone interface does not change the recognition input aspect ratio. Precise validation of other input ratios requires a diagnostic build.
- Not every local UI state is written to logcat every frame. Record the screen or photograph the Debug page as well; a Unity logcat excerpt alone may omit the gesture and slice states at the time.

## 4. Basic Device Tests

### 4.1 Installation, Startup, and Permissions

| ID / level | Steps | Expected result / observation | Status | Actual result and evidence |
|---|---|---|---|---|
| DEP-01 Initial | Record the new APK hash; explicitly install it; record `Success` and the launch result; check the version. | The new package installs successfully and this round's build launches; no fatal installation/native-library/license errors. | Not tested | To fill in |
| DEP-02 Initial | Connect the glasses and Eye; cold-start 3 times, waiting for the main interface each time. | Both Beam Pro and glasses reach the interface; no crash, persistent black screen, or prolonged startup stall. Record each duration. | Not tested | Success __/3; durations __; evidence __ |
| DEP-03 Initial | Deny the first camera-permission request once and record it; then grant permission in system app settings and reopen the app. | Rejection produces a clear unavailable state without a crash; RGB/gestures recover after permission is granted. For an already-authorized device, validate through system settings and record any additional restart requirement. | Not tested | To fill in |
| DEP-04 Initial | Start once with Eye disconnected; restore the proper connection and restart. | The core interface remains usable; camera/gesture unavailability has a diagnosable cause and recovers after reconnection. Record connection changes and restart actions separately. | Not tested | To fill in |

### 4.2 Beam Pro Pages, Glasses HUD, and Hover/Follow

| ID / level | Steps | Expected result / observation | Status | Actual result and evidence |
|---|---|---|---|---|
| UI-01 Initial | Inspect the default Monitor page and top status area; check tabs against the actual build configuration. | Standard builds show Monitor/Connection only; engineering builds show Debug. Text, units, and status are not clipped. With no service, no fabricated navigation data is shown as valid. | Not tested | To fill in |
| UI-02 Initial | Scroll each page; switch portrait/landscape; show/hide the keyboard on Connection. | Inputs, connection button, and tabs remain reachable and do not obscure one another. Record whether system rotation is allowed; a configuration that cannot rotate does not count as a completed orientation-layout test. | Not tested | To fill in |
| UI-03 Initial | Switch pages 20 times; observe navigation when a service is available; turn your head while observing the left-eye preview. | Preview matches actual glasses content; page changes do not stop navigation/camera/gestures. Loss of actual XR frames shows waiting/unavailable status rather than labeling an old image live. | Not tested | Success __/20; evidence __ |
| UI-04 Initial | Toggle “HUD: visible/hidden”; when a model is available, toggle 3D model visibility; repeat each 5 times. | Phone actions match the glasses and each control operates independently. If no model is loaded, record the missing asset separately rather than diagnosing a button failure. | Not tested | To fill in |
| UI-05 Initial | Select Follow and turn your head gently; select Hover, then turn/move; switch back to Follow. | Follow holds a stable head-relative position; Hover preserves the world pose at the moment of switching; returning to Follow brings content back into view. Record tracking mode and drift. | Not tested | To fill in |
| UI-06 Integration | Switch Hover/Follow while HUD/model are hidden, then show them; observe while working on the dental model. | Reappearing content follows the selected mode; numbers and CT direction labels are legible and the central working area remains visible. Record obstruction locations and wearer feedback. | Not tested | To fill in |

### 4.3 Connection and Network

| ID / level | Steps | Expected result / observation | Status | Actual result and evidence |
|---|---|---|---|---|
| NET-01 Initial | Start the simulator/navigation system, enter the correct IPv4 address and port, and connect. | Status becomes connected; context and continuously updating navigation data arrive. Record time to first frame and endpoint. | Not tested | To fill in |
| NET-02 Initial | While connected, edit only the IP draft without submitting; leave and return to the page. Separately submit an invalid IP or port 0. | Editing the draft does not immediately change the active connection; invalid values receive explicit feedback. Record whether the draft persists. | Not tested | To fill in |
| NET-03 Initial | Submit a syntactically valid but unreachable address; restore the correct address and reconnect. | Duplicate submission is prevented while connecting; timeout/failure is visible; restoring the correct address allows continued operation without a permanent “Connecting” state. | Not tested | To fill in |
| NET-04 Initial | During live navigation, turn Beam Pro Wi-Fi off for 10 seconds and back on; wait for automatic recovery, then tap Reconnect if necessary and record it. | Valid dynamic navigation is withdrawn after disconnection; old values are not retained as normal. Recovery displays only current-context data. Record manual actions required and elapsed time. | Not tested | To fill in |
| NET-05 Integration | Keep the device running, restart the navigation service, and establish a new session; record both sides' logs. | Recovery does not mix in old session/control state. If automatic recovery fails, record the minimum recovery steps. | Not tested | To fill in |

### 4.4 RGB and Gesture Operations

GES-01–03 and 06 can first check recognition alone. **Validating slice changes requires matching CT, a valid v2 context, and a navigation service that handles slice commands.** Without these prerequisites, record recognition results and mark the slice-change portion blocked.

| ID / level | Steps | Expected result / observation | Status | Actual result and evidence |
|---|---|---|---|---|
| GES-01 Initial | Enable local RGB preview on the engineering page; move a hand in view; inspect the provider; close preview and continue recognition. | RGB is the current Eye image; provider is available/running; closing local preview does not stop capture still used by gestures. | Not tested | To fill in |
| GES-02 Initial | Make open-palm, pinch, OK, and two-finger poses; hold each for approximately 1 second and repeat 10 times; record left and right hands separately. | The matching stable gesture is reported. Distinguish pinch from OK as in Section 3.3. Record which class was reported, not only “inaccurate.” | Not tested | Success __/10 per pose; see statistics table |
| GES-03 Initial | Face Eye and keep the pose as constant as possible while rotating two-finger/open-palm/pinch poses within the image to 0°, 45°, and 90°; repeat each orientation 10 times. | In-image rotation alone does not systematically change classification. This is the device regression for the coordinate-scale fix. Exclude and record finger-pose changes, leaving the frame, and occlusion. | Not tested | Each angle __/10; video __ |
| GES-04 Integration | After CT is ready, show an open palm, then hold OK steadily for approximately 1 second; move slowly upward/downward; remain still for 3 seconds; repeat 10 times. | Enters “Adjusting slice”; upward movement requests a smaller slice offset and downward movement a larger offset; remaining still does not repeatedly change slices. Check consistency among requests, returned state, and image. | Not tested | Success __/10; extra/missed steps __; evidence __ |
| GES-05 Integration | Release during an OK operation; separately cover the hand or move it out of view and back; repeat each case 10 times. | Slice changes stop after release; after tracking loss, OK must be released and held again rather than continuing an old action. Record measurable time from the actual action to stopping. | Not tested | Success __/10; maximum stopping time __; measurement method __ |
| GES-06 Initial | Use the usual test gloves; alternate hands and normal/brighter lighting; introduce common instrument occlusion. | Record classification stability; occlusion/poor-quality input does not cause persistent unintended slice changes. Record distance, angle, gloves, and lighting without inventing a clinical success-rate threshold. | Not tested | Conditions and success counts __; evidence __ |
| GES-07 Integration | After OK activates, turn your head and stop; first keep the hand still, then move it slowly. | Significant head movement freezes slicing; after it stops, a new positional baseline is established without a sudden slice jump from accumulated head-motion displacement. | Not tested | Success __/10; jumps __; evidence __ |
| GES-08 Integration | Switch offline gesture recognition off/on 5 times while service and CT remain available; continue making OK while disabled. | Disabled recognition sends no gesture slice commands and can recover after re-enabling. Open palm, pinch, and two-finger gestures alone are not treated as OK for continuous slice changes. | Not tested | Success __/5; unintended steps __; evidence __ |
| GES-09 Targeted | With a diagnostic build/controlled provider, stop new observations for more than 180 ms before a candidate gesture is published, then resume the same pose after approximately 3 seconds. Simulate both normal Update and a main-thread stall. | Candidate timing and hysteresis clear; the first recovered frame cannot immediately become a stable gesture and must be confirmed continuously for another 250 ms. Also cover observation gaps after a published gesture and at pinch/finger-extension hysteresis boundaries. Mark blocked if timestamps/injection tools are missing. | Not tested | Injection method __; timeline/log __ |
| GES-10 Targeted | Measure stable-gesture publication, OK activation, and observation-gap clearing using timestamped observation/state events; compare with external action video. | Verify 250/300/180 ms logical behavior separately and independently record end-to-end delay from physical action to feedback. Visual inspection alone cannot confirm millisecond thresholds. | Not tested | Logical timing __; end-to-end __; evidence __ |

#### Gesture Statistics Appendix: Copy Rows for Each Glove/Lighting/Hand Condition

| Condition ID / related case | Hand, glove, lighting, distance | Pose / in-image angle | Correct / total attempts | Misclassification classes and counts | Unintended slice steps | Video/log |
|---|---|---|---|---|---|---|
| To fill in | To fill in | To fill in | __ / __ | To fill in | To fill in | To fill in |
| To fill in | To fill in | To fill in | __ / __ | To fill in | To fill in | To fill in |

## 5. Navigation, Assets, and Video Integration

### 5.1 Navigation State, Directions, and Control Priority

NAV-02–09 require a server that can specify data or inject errors; timed cases also require measurement capability. Synchronize device and server clocks and record their offset first. Save this round's input values, units, threshold version, and expected directions before comparing display results.

| ID / level | Steps | Expected result / observation | Status | Actual result and evidence |
|---|---|---|---|---|
| NAV-01 Initial | Observe continuously for at least 65 seconds with `--faults`; compare with server tracking-invalid windows. | Normal frames produce matching dynamic values; invalid tracking hides dynamic markers, invalidates numbers, and states the reason; valid data returns after recovery. | Not tested | Invalid/recovery times __; evidence __ |
| NAV-02 Integration | Send positive/negative buccal-lingual and positive/negative mesial-distal deviations in sequence, then tilt directions; compare left and right tooth positions separately. | Labels follow the convention: buccal above, lingual below, mesial left, distal right (B/L/M/D in the English HUD); solid dot denotes position and hollow triangle denotes tilt. Head rotation does not change anatomical meaning. | Not tested | Input → display comparison __; evidence __ |
| NAV-03 Integration | Specify zero deviation, near-threshold, boundary, out-of-range, and over-depth values; make position/angle/depth abnormal individually. | Units mm/° and directions are correct; each abnormal metric is evaluated independently and is not masked by normal ones. Boundaries/hysteresis follow server policy; zero angle shows no direction triangle. | Not tested | Thresholds/inputs/display __; evidence __ |
| NAV-04 Integration | Omit thresholds or directions, or use a compatible v1 service; record capability differences separately. | Missing capabilities/thresholds are explicit; directions are not guessed and clinical defaults are not substituted. Compatibility mode is not counted as full v2 acceptance. | Not tested | To fill in |
| NAV-05 Targeted | Stop live frames on the server while retaining the connection, covering 200–500 ms and more than 500 ms separately; then send pause/stop state and stop subsequent valid frames. | Delay removes valid colors and displays a delay warning; stale/explicitly invalid data removes dynamic values and markers. Static CT may remain but must carry invalid-state feedback. | Not tested | Frame/state timeline __; display __ |
| NAV-06 Integration | During active OK, send new slice control from the navigation side; release, make OK again, and operate continuously. | Navigation-side takeover cancels the old gesture and requires release before restarting; successful responses to XREAL's own commands must not cancel the gesture each time. | Not tested | Source/control version __; evidence __ |
| NAV-07 Integration | Toggle synchronization on/off on the server and change slices from XREAL and the navigation side separately. | With synchronization off, XREAL still sends SliceCommand. The navigation side maintains an independent device slice and returns SliceState without changing the workstation's own slice. This does not imply support for disconnected offline slicing. | Not tested | Slices/returned state on both sides __; evidence __ |
| NAV-08 Integration | Change case/CT/plan/tooth/drill/session separately, incrementing the corresponding versions; inject old controls, old frames, or duplicate sequence numbers afterward. | Old state is cleared after changes; mismatched/duplicate data is rejected and cannot replace the current valid display. Record all input identifiers and versions. | Not tested | Changed object/new and old versions __; evidence __ |
| NAV-09 Targeted | Film navigation-source changes and glasses/phone feedback together with one external camera, or use diagnostic timestamps with documented clock synchronization; collect at least 30 samples. | Record P50/P95/maximum latency and sample count from navigation to glasses and to phone separately. Keep these distinct from video-return latency; code thresholds cannot substitute for measurements. | Not tested | Glasses __; phone __; method/evidence __ |

### 5.2 CT, STL, and Slices

Start with a small complete sample, then test the full target CT. Determine volume/memory limits through device measurements. Before starting, confirm that UIDs, LPS/mm coordinates, and planned slice plane match the CT volume. The current slice protocol has no remote window-width/window-level interface, so navigation-side synchronized window adjustment is not required. Finished file transfer does not imply a ready volume or correct slice orientation.

| ID / level | Steps | Expected result / observation | Status | Actual result and evidence |
|---|---|---|---|---|
| CT-01 Integration | Send a known complete, supported CT dataset; record file/frame counts and the server manifest; wait for loading to finish. | Transfer, verification, decoding, and volume-construction states are distinguishable; correct slices can ultimately be displayed. Record the actual decoder, duration, and complete errors. | Not tested | File/frame counts __; duration __; evidence __ |
| CT-02 Integration | Select the same physical plane on the navigation and glasses sides; configure the reference display using DICOM default window values, then compare known structures, reference points, orientation, and grayscale. | No left/right or up/down flip, obvious scale error, or slice-plane offset. Compare physical positions, not only list indices. | Not tested | Reference points __; screenshots from both sides __ |
| CT-03 Integration | Change slice offset/physical plane/index on the navigation side; then perform OK slice operations from XREAL, including server state responses. | Both ends follow the synchronization policy; local commands and server acceptance/rejection are traceable. Mark blocked if the service cannot return state. | Not tested | Control version/offset __; evidence __ |
| CT-04 Integration | Provide teeth/drill STL and show the 3D model; hide/show it and switch Hover/Follow. | Models load correctly, without an obvious 1000× unit error, wrong axis, or leftover old model, and match the current case/drill. | Not tested | Model units/pose __; evidence __ |
| CT-05 Integration | Interrupt the network during a large CT transfer and restore it; retain transfer_id and missing-chunk/resume logs on both sides; then load a different CT. | No silent missing slices or mixed-in old data. Resume missing portions where supported; record any complete retransmission. Only a ready new volume becomes the current CT. | Not tested | Interruption point/recovery method __; evidence __ |
| CT-06 Targeted | Separately provide unsupported transfer syntax, failed checksums, and UID/context mismatches; archive each original input. | Explicit failure/rejection reasons are shown and the data is not displayed as valid current CT. Record data-preparation errors separately from client crashes. | Not tested | Input type/complete error __; evidence __ |

### 5.3 XR / RGB Video Return

Prerequisites: the navigation system or dedicated tool can send `ObservationControl`; increment `control_version` whenever observation toggles, endpoint, or format change; a working FFplay is installed on the development machine. Start the receiver in a separate terminal:

```bash
cd "/Users/sad/code/Unity/First XR"
bash Tools/observation-receiver/receive.sh 5555
```

On the navigation side, enter the receiver's LAN IP and UDP `5555`. The receiver script only receives; it **does not send a command to enable video**. If SDP or explicit demuxer parameters are required, preserve the error, actual encoding format, and working parameters before proceeding. The baseline code/documentation does not promise that every device's default parameters are directly playable.

| ID / level | Steps | Expected result / observation | Status | Actual result and evidence |
|---|---|---|---|---|
| OBS-01 Integration | Request XR mirroring only from the navigation side; turn your head and toggle HUD visibility. | In the default 1280×720 composite, the left half is actual XR left-eye output and the right half is black; content matches the glasses without a substitute main-camera view. | Not tested | Actual encoding/resolution/frame rate __; evidence __ |
| OBS-02 Integration | Request RGB only, then XR+RGB together. | The unrequested half is black; when both are enabled, left/right sources are correct and distinct. Local preview and remote RGB controls are separate. | Not tested | To fill in |
| OBS-03 Integration | While gestures work, toggle remote RGB off/on 5 times; keep CT/navigation running. | Disabling remote RGB does not stop camera capture needed by gestures; navigation and slicing continue without obvious resource deadlock/restarts. | Not tested | Success __/5; gesture/frame-rate changes __ |
| OBS-04 Integration | Disable all return video from the navigation side; disconnect the receiver; re-enable with the correct endpoint. | Return-video state matches the request. Record source/sender state separately from reception and verify recovery. A UDP sender is not guaranteed to detect an offline receiver; a black screen does not mean the encoder has stopped. | Not tested | Client state/receiver log __ |
| OBS-05 Targeted | Film the source event and received display together with one external camera, or use timestamps with documented synchronization; collect at least 30 samples. | Obtain XR and RGB end-to-end P50/P95/maximum latency and sample counts separately. Do not directly subtract timestamps from unsynchronized devices. | Not tested | XR __; RGB __; method/evidence __ |

## 6. Recovery and Sustained Operation

| ID / level | Steps | Expected result / observation | Status | Actual result and evidence |
|---|---|---|---|---|
| REC-01 Integration | Background/foreground and lock/unlock the app/device, 3 times each; record whether OK was held before and after. | Interface and services recover; old navigation is not displayed as valid before recovery, and an old gesture does not suddenly change slices. Record required reconnect/re-enable steps. | Not tested | Success __/3; evidence __ |
| REC-02 Integration | When no operation is underway, unplug/replug the glasses or Eye, 3 times each; record order and power supply throughout. | Connection loss has explicit feedback and no permanent freeze. Record whether reconnection recovers automatically, requires reopening the app, or requires a device restart. | Not tested | Success __/3; recovery method __ |
| REC-03 Integration | Stop/restart the app and reconnect with the same configuration; perform GES-04 once more. | No unintended slice changes from an old session/gesture; a new connection, resources, and gesture recognition establish normally. | Not tested | To fill in |
| PERF-01 Targeted | On the same device, start with navigation only, then add CT, gestures, XR video, and RGB video in sequence; run each configuration for approximately 5 minutes. | Record measured frame rate, stalls, memory, and temperature increases; state whether Profiler/screen recording was enabled. Do not substitute visual estimates for FPS measurements. | Not tested | Data for each configuration __; evidence __ |
| PERF-02 Targeted | Run the complete target configuration continuously for 60 minutes, sampling at 0/5/15/30/60 minutes. | No crashes/ANRs or continually increasing memory that fails to fall; navigation, gestures, and media remain usable. Record throttling/heat without inventing unmeasured performance values. | Not tested | Completed duration __; samples below |
| PERF-03 Targeted | For 30 minutes, perform normal hand movement, instrument handling, conversation, and slight head movement without intending to change slices; leave relevant services enabled. | Record unintended slice changes; expected count is 0. Preserve before/after video, recognition state, and server slice commands for any occurrence. | Not tested | Observed __ minutes; unintended changes __; evidence __ |

| Time | App Total PSS (MB) | XR FPS / measurement tool | Video FPS | Battery / charging state | Temperature / measurement source | Stalls, anomalies, and evidence |
|---|---|---|---|---|---|---|
| 0 minutes | To fill in | To fill in | To fill in | To fill in | To fill in | To fill in |
| 5 minutes | To fill in | To fill in | To fill in | To fill in | To fill in | To fill in |
| 15 minutes | To fill in | To fill in | To fill in | To fill in | To fill in | To fill in |
| 30 minutes | To fill in | To fill in | To fill in | To fill in | To fill in | To fill in |
| 60 minutes | To fill in | To fill in | To fill in | To fill in | To fill in | To fill in |

Identify whether temperature comes from the battery, system thermal service, or an external instrument. Do not combine different sources into a single trend column. If Total PSS is originally in KB, convert it to MB for the table and retain the original output.

## 7. Logs, Screenshots, and Installation Commands on macOS

These commands are instructions for manual execution during testing. Preparing this checklist did not install anything or operate a device. First replace the device serial, APK path, and actual package name. Reset variables after opening a new terminal. Do not copy an old IP/serial unchanged.

### 7.1 Create This Round's Directory and Check the Connection

```bash
cd "/Users/sad/code/Unity/First XR"
XR_ADB='/Applications/Unity/Hub/Editor/6000.0.83f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb'
"$XR_ADB" devices -l

# Use the serial from a row whose status is device; explicitly select when multiple devices are present.
XR_SERIAL='REPLACE_WITH_DEVICE_SERIAL'
XR_PACKAGE='com.DefaultCompany.FirstXR'
XR_APK="$PWD/Builds/First XR.apk"
XR_RUN="$PWD/Logs/device-test/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$XR_RUN"
git rev-parse HEAD > "$XR_RUN/git-commit.txt"
git status --short > "$XR_RUN/git-status.txt"
shasum -a 256 "$XR_APK" > "$XR_RUN/apk-sha256.txt"
unzip -l "$XR_APK" > "$XR_RUN/apk-contents.txt"
"$XR_ADB" -s "$XR_SERIAL" shell getprop ro.product.model > "$XR_RUN/device-model.txt"
"$XR_ADB" -s "$XR_SERIAL" shell getprop ro.build.fingerprint > "$XR_RUN/device-build.txt"
```

For `unauthorized`, approve debugging on Beam Pro first. For `offline` or no device, restore the connection before continuing installation. Search `apk-contents.txt` for `assets/mediapipe/hand_landmarker.task` to check the camera model. Recalculate the hash whenever the APK changes.

### 7.2 Explicitly Install the Specified APK, Then Launch

```bash
if "$XR_ADB" -s "$XR_SERIAL" install -r "$XR_APK" > "$XR_RUN/install.txt" 2>&1; then
  cat "$XR_RUN/install.txt"
  "$XR_ADB" -s "$XR_SERIAL" shell am start -n "$XR_PACKAGE/ai.nreal.activitylife.NRXRActivity" > "$XR_RUN/launch.txt" 2>&1
  "$XR_ADB" -s "$XR_SERIAL" shell dumpsys package "$XR_PACKAGE" > "$XR_RUN/package-info.txt"
else
  cat "$XR_RUN/install.txt"
  echo 'Installation failed: this block did not launch the app. Record and investigate the error first.'
fi
```

Stop after an installation failure and retain `install.txt`. If the main Activity is unavailable, record the Activity from the actual APK and the original exception before proceeding. Launching an old package does not establish successful installation.

### 7.3 Start Continuous Logging, Then Reproduce the Issue

Run this in a terminal where the variables are set. It occupies the terminal until capture stops; press `Ctrl+C` after reproduction. To capture startup issues, start logging first, then launch the app from the device interface.

```bash
"$XR_ADB" -s "$XR_SERIAL" logcat -b all -v threadtime > "$XR_RUN/logcat.txt" 2>&1
```

Do not clear logs before saving. Retain the full log rather than only the final exception line, and consistently record phone/computer times and time zones. USB disconnection affects log collection but does not itself prove the app exited. After reconnecting, save a separate historical buffer if needed:

```bash
"$XR_ADB" -s "$XR_SERIAL" logcat -b all -d -v threadtime > "$XR_RUN/logcat-after-reconnect.txt" 2>&1
```

### 7.4 Capture Evidence When an Issue Occurs

After stopping continuous logging, use the same terminal. If opening another terminal, reset the variables above and reuse the same `XR_RUN` path. Replace `BUG-001` with the current issue ID and use a new ID/suffix for repeated captures.

```bash
"$XR_ADB" -s "$XR_SERIAL" exec-out screencap -p > "$XR_RUN/BUG-001-beam.png"
"$XR_ADB" -s "$XR_SERIAL" shell dumpsys meminfo "$XR_PACKAGE" > "$XR_RUN/BUG-001-meminfo.txt"
"$XR_ADB" -s "$XR_SERIAL" shell dumpsys battery > "$XR_RUN/BUG-001-battery.txt"
"$XR_ADB" -s "$XR_SERIAL" shell dumpsys thermalservice > "$XR_RUN/BUG-001-thermal.txt"
"$XR_ADB" -s "$XR_SERIAL" shell pidof "$XR_PACKAGE" > "$XR_RUN/BUG-001-pid.txt"
```

- `screencap` usually captures only the Beam Pro display, not the actual view inside the glasses. Add external video for glasses content, head movement, and hand trajectories; filming the phone and hand together helps align timing.
- Capture screenshots/video of `[RGB camera]`, `[Gesture recognition]`, `[Slice gesture]`, and navigation/CT/video states on the Debug page. Later updates may replace these states, so capture them when the anomaly occurs.
- For hard-to-reproduce crashes/ANRs, optionally run `"$XR_ADB" -s "$XR_SERIAL" bugreport "$XR_RUN/BUG-001-bugreport.zip"`. System reports have broad scope; remove unrelated personal and real-patient information before sharing.
- For network, CT, slice, or video issues, retain navigation-side logs as well, including session/context/control versions, send times, asset manifests, and acceptance/rejection reasons. Device logs do not replace server evidence.

## 8. Issue Tracking and Retesting

### 8.1 Issue Index

Severity: **Blocker** = issues that must be resolved first, such as inability to launch, crashes, incorrect navigation direction, or unintended slice changes; **Major** = a core function cannot be completed; **Minor** = a display/interaction issue with a workaround. These are debugging priorities, not clinical risk classes.

| Issue ID | Test case ID | Severity | One-sentence observation | Reproductions / attempts | Evidence files | Current handling status |
|---|---|---|---|---|---|---|
| To fill in | To fill in | To fill in | To fill in | __ / __ | To fill in | Undiagnosed / Awaiting fix / Awaiting retest / Closed |

### 8.2 Copy This Template for Each Issue

```markdown
### BUG-001: Describe the Specific Observation

- Related test case:
- Test round / APK SHA-256 / Git commit:
- Occurrence time (including time zone) / video timestamp:
- Severity:
- Preconditions: page, connection, CT readiness, gestures/local RGB/remote RGB, Follow or Hover:
- Minimum reproduction steps:
  1. To fill in
  2. To fill in
  3. To fill in
- Expected result:
- Actual result (describe phone / glasses / navigation side separately):
- Reproductions / attempts:
- Gesture details: left/right hand, pose, gloves, lighting, distance, rotation angle, raw/stable/slice state:
- Connection/CT details: service version, session/context/control versions, sample ID, original error:
- Recovery: automatic / reconnect / toggle feature off and on / reopen app / restart device / still unrecovered:
- Evidence: logcat, navigation-side log, screenshots, video files and timestamps:
- First failing version / fixed version / retest round:
- Retest steps and results (append; retain the original failure record):
```

### 8.3 Handoff Summary for This Round

| Item | Entry |
|---|---|
| Passed / Failed / Blocked / Not applicable / Not tested counts | To fill in |
| Most complete workflow verified this round | To fill in, for example, “Simulator navigation works; CT not tested because matching data is unavailable.” |
| Top 1–3 issue IDs to address | To fill in |
| Remaining missing prerequisites | To fill in: navigation-side features, matching CT, engineering APK, timestamp diagnostics, etc. |
| Attached files | To fill in: record Markdown, logcat, server logs, screenshots/videos, and APK hash |
| Desired focus for the next round | To fill in |

Prioritize **this record + this round's APK hash/source version + issue IDs and reproduction steps + logs/video for the relevant times** when providing feedback. There is no need to complete every item first; report a blocker as soon as it appears in Round 1.

## 9. Reference Material

- [Navigation-Side XREAL Integration Guide](NavigationIntegrationGuide.md)
- [v2 Interface Description](DentalNavigationV2Interface.md)
- [Historical Navigation Validation Records](DentalNavigationValidation.md)
- [Historical Paged UI and Hover/Follow Validation](BeamProPagedUiVerification.md)
- [Simulator Instructions](../Tools/navigation-simulator/README.md)
- [Video Receiver Instructions](../Tools/observation-receiver/README.md)

If historical documentation differs from the code, use this round's actual build and code as the source of truth. For example, MediaPipe input in this checklist's round is **480×270**; the historical README's 256×144 is not that configuration. Existing “passed” results in the references do not automatically count as device passes in this checklist.
