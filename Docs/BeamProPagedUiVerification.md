# Beam Pro Paged UI Validation Record

Date: 2026-09-16

Unity: 6000.0.83f1

Target platform: Android ARM64 / IL2CPP

## 2026-09-17 Hover / Follow Increment

- Added mutually exclusive "Hover" and "Follow" buttons to the Beam Pro Monitor page; Follow is the default.
- "Hover" preserves the HUD and 3D model world poses at the instant of switching. "Follow" reapplies their configured head-relative positions and restores the model's head-relative scale.
- Added portrait and landscape button layout checks to ensure both buttons are at least 56 units high and do not overlap visibility buttons or asset status.
- Added `DentalDisplayLayoutAnchorTests`: default Follow mode, ignored invalid enum values, no duplicate notifications for repeated settings, no PlayerPrefs writes, no changes to `ControlVersion`, and remote layout updates cannot override the local anchor.
- Restored accidentally modified `ProjectSettings` preload lists and Image Tracking reference library caches.
- As arranged by the user, Unity compilation, test-result export, screenshots, APK generation, and device deployment for this increment are to be performed by the user. The 58 / 58 result, screenshots, and APK information below belong to the 2026-09-16 paged UI baseline and do not include this increment. One layout test and seven anchor tests were added to the original 58 EditMode tests; a rerun should contain 66 tests.

## Implementation

- `BeamProPagedController` renders the Monitor, Connection, and engineering Debug pages through one entry point. The top status bar and bottom tabs remain fixed while the body scrolls independently.
- `BeamProPageLayoutCalculator` handles safe areas, the soft keyboard, portrait/landscape layout, dynamic text, and scrollbar space using a short-edge baseline of 540 logical units.
- The old `HelloMR` button column, navigation dashboard, left-eye preview, and log window stop rendering independently when the paged controller is active, while continuing to provide state and operations.
- The Connection page preserves input drafts and validates IPv4 and port values on submission. Repeated submission is locked while connecting; success, failure, and timeout appear in the operation result.
- The Debug page appears only in Engineer Mode and hides disabled control groups according to Inspector settings. Local RGB preview remains distinct from RGB streaming back to the navigation system.
- XR preview accepts only real XR left-eye frames no older than 250 ms. When the paged UI is active, no substitute main-camera rendering runs and stale frames are not presented as live images.

## Automated and Editor Validation

Unity Test Runner EditMode: **58 / 58 passed, 0 failed**. This includes 11 Beam Pro paged layout tests covering:

- Consistent logical geometry at 1080×2400 and 720×1600.
- Side-by-side preview and metrics at 2400×1080 landscape resolution.
- Safe areas, extreme soft keyboard heights, and scroll access to the submit button.
- Height limits for very long top warnings, with no overlap between body and tabs.
- Dynamic long-text expansion and vertical scrollbar space without horizontal scrolling.
- All visible touch targets at least 56 units high, without overlap among metric cards, connection controls, or debug controls.
- No access to the Debug page outside Engineer Mode; disabled engineering controls are hidden.

Test results: [BeamProPagedUiTestResults.xml](BeamProPagedUiTestResults.xml). Its diagnostic message has been translated; the recorded test outcomes and measurements are unchanged.

Editor Play Mode generated 1080×2400 screenshots using the production IMGUI code:

- [Monitor page](BeamProScreenshots/monitor-editor.png)
- [Connection page](BeamProScreenshots/connection-editor.png)
- [Debug page](BeamProScreenshots/debug-editor.png)

The editor has neither XREAL left-eye output nor the Android MediaPipe runtime, so the screenshots show the intended XR waiting state. The editor's unavailable gRPC HTTP/2 state was converted into an operator-facing message, while the full exception remained in engineering logs. These historical screenshots retain the Chinese UI used at capture time and do not validate the later English translation.

## Android Build

- Result: successful.
- APK: `Builds/First XR.apk`
- Size: 234,872,678 bytes.
- SHA-256: `638c2580698a5b0c7e01f03ca27f1ec4d1c01fd6cda5d6963b901fc3e72101e5`
- The build log still reports an existing 16 KB page-size alignment warning for XREAL native libraries; it did not cause this build to fail.

## Device Validation Still Required

This record does not treat editor results as completed device validation. Beam Pro + XREAL testing is still required for touch scrolling, the soft keyboard, system safe areas, orientation changes, real XR left-eye frames, concurrent RGB/gesture operation, streaming to the navigation system, and continuous operation while switching pages. The Hover / Follow increment also requires verification that Follow maintains the configured relative view position during head movement, Hover preserves the world pose, switching works while hidden, and returning to Follow immediately brings content back into view.
