# XREAL Dental Implant Navigation v2 Validation Record

Updated: 2026-10-26 (historical records retained below)

## 2026-10-26 English Migration Validation

Translated authored Chinese code comments, runtime UI/status/log messages, tests, and documentation into English. Chinese document filenames were renamed and their references updated. Compact HUD orientation labels use B/L/M/D for Buccal/Lingual/Mesial/Distal; text sizing accommodates longer English labels. Existing runtime fonts remain in place. Stock TextMesh Pro line-breaking tables are character data, not authored prose.

Unity `6000.0.83f1` EditMode with Android selected passed **160/160 tests, 0 failed, 0 skipped**. Evidence: `Logs/english-migration-20261026/editmode-results.xml` and `editmode-full.log` in the same directory. A subsequent focused UI/font/layout regression passed **25/25 tests** after shortening the overall-status card detail to "See alert above" while retaining the full warning in the header; evidence: `Logs/english-migration-20261026/ui-results.xml` and `ui-regression.log`. The `Tools/navigation-simulator` smoke test also passed. No APK was built and no Beam Pro / XREAL device retest was performed for this language migration; these results do not establish device visual or clinical acceptance. Historical screenshots retain their original language and remain evidence of their original runs.

## 2026-10-15 Beam Pro / One Pro Performance Retest

The device was a Beam Pro X4100 / Android 14. The first installation preserved application data, and the device APK SHA-256 matched the local package. The user then connected One Pro and confirmed that the glasses displayed normally. Initial XR initialization reported `NotFindRuntime`; the SDK exited after waiting approximately five seconds. Once the XR service was ready, restarting through the normal `NRXRActivity` entry point succeeded. The initial failure window cannot be used for normal XR performance acceptance.

### Issues Found and Fixed on the Device

- XREAL configuration originally started with `Hands`, after which the HelloMR scene switched back to the default controller, unnecessarily initializing the native hand model. After changing `InitialInputSource` to `Controller`, `InputStart → InputStart End` on the same device fell from **2.323 seconds to 0.260 seconds**. This measures that initialization stage, not total cold-start time; XRLoader completion to BeforeSceneLoad still took approximately 4.74 seconds and has not yet been broken down.
- The phone Monitor page kept waiting for XR imagery even though the glasses displayed normally. The texture-array copy shader was referenced only through `Shader.Find`, with no build reference. It was added to Always Included Shaders and declared `target 3.5` / `require 2darray`. The new build log confirmed shader inclusion; the device recorded **`LeftEyePreview: first live frame (XR array slice 0, 1250x720)`**, and a screenshot confirmed the real left-eye image on the phone. Diagnostics print only once for the first frame or an initialization failure. Build-reference rules: [Unity Shader.Find documentation](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/shader/find).
- The new Release / ARM64 / IL2CPP build and APK v2 signature check passed; existing EditMode tests were rerun: **154/154 passed, 0 failed, 0 skipped**. APK SHA-256 at this stage: `cc7a04f732a81604380caf8e3d582d44130e217838086afc978d46dc147c7ba5`, 204,883,368 B. The same hash was verified after device installation.

### Continuous Operation and Network Issues

A local navigation simulator was started from an independent directory, with synthetic teeth/drill models and 32 CT images: 34 assets, 386,712 B in total. No patient assets were used. The device confirmed all assets and displayed the synthetic CT and models. The service was configured for 30 Hz; host sampling measured actual production at approximately 27.28 Hz. The difference from 30 Hz must not be treated as client frame loss.

Eleven complete 30-second stable-operation windows covered **330 seconds / 19,745 frames, with the application's Update loop averaging approximately 59.83 Hz**. Window frame-time P95 was **20.3–21.1 ms**, maximum **58.6 ms**, with three frames over 50 ms and none over 100 ms. No fatal errors, native RGB frame reads, or GPU readback warnings were recorded. This tests small synthetic assets with preview enabled on the Monitor page; it does not establish acceptance for large cases, full streaming, or every operation path, and it does not measure the glasses compositor's FPS.

Navigation reception still had gaps up to **3.613 seconds**, whereas applying received frames on the main thread usually took only one frame. During the same period, seven minutes / 420 host samples showed a maximum HTTP duration of 17.32 ms and maximum latest-frame age of 39 ms, with no multi-second pause in service production. The common approximately 100-second window for three independent network paths was:

| Path | RTT P95 | Maximum RTT | >200 ms |
|---|---:|---:|---:|
| Beam → gateway | 1881 ms | 3409 ms | 35/199 |
| Beam → Mac | 1787 ms | 3311 ms | 31/199 |
| Mac → gateway | 15.9 ms | 39.9 ms | 0/200 |

The complete approximately 120-second samples on all three paths had no packet loss. Both Beam paths repeatedly stalled and caught up together. Independent ICMP reproduced similar multi-second stalls, so Unity's main thread, gRPC, or host frame production alone cannot explain them. Investigation currently prioritizes the Beam Wi-Fi / AP client path or device network stack; the driver, power saving, and AP client queues have not yet been distinguished. Later Wi-Fi low-latency experiments must be recorded separately; this section does not establish that the network issue is fixed.

Evidence in `Logs/performance-device-20261015/`: `controller-preview-delivery.json`, `editmode-device-fix.xml`, `fixed-launch-all.log`, `beam-fixed-startup.png`, `navigation-after-device-fix.json`, `fixed-metrics-summary.json`, and `network/ping-comparison.json`. Service evidence is in `Logs/device-performance-20261015/simulator/`. All changes remain uncommitted.

### Wi-Fi Low-Latency Mode: Initial Short Run and Interruption

Added an application-owned `WIFI_MODE_FULL_LOW_LATENCY` lease, held only while the navigation connection task runs and the application is not paused; it is released on pause, connection completion, disable, and destruction. Android is called only when state changes, with no per-frame JNI repetition; only the normal `WAKE_LOCK` permission was added. With six lifecycle tests added, **160/160 EditMode tests passed**. Release APK SHA-256: `5192b6aba8d2010e06a2f087af8b1c6b9d23ca6fb5abbd92e065df4c1bc4c921`, 204,886,924 B. It was installed and its device hash verified; APK v2 signature verification passed and debuggable was not enabled.

The device acquired the lock at 13:45:34.886; the Wi-Fi HAL simultaneously logged `wifi_set_latency_mode: 1`. At 13:47:25.324, Device Disconnect was reported, and the SDK subsequently exited the application. This was not a completed 330-second valid run. The collector planned for 330 seconds obtained only **three complete windows / 90 seconds**: average Update frequency 59.82 Hz, frame-time P95 20.6–21.1 ms, maximum 60.1 ms, and no frames over 100 ms. Navigation-age P95 was 82.5–85.8 ms, and receive-gap P95 45.6–46.2 ms; one 1.052-second receive gap still occurred, so not all stalls can be considered eliminated. The table below is conservatively cut off at 13:47:24.235, before actual disconnection. `ActionDestroy` itself logs release of an SDK action object and is not independent evidence of glasses disconnection.

The three ping series must exclude data after glasses disconnection. The valid interval with stable XR and the lock held was **13:46:18.159–13:47:24.235, 66.076 seconds**, compared with a baseline interval of equal length:

| Path | Baseline RTT P95 | Low-latency RTT P95 | Low-latency maximum RTT |
|---|---:|---:|---:|
| Beam → gateway | 2328 ms | 10.7 ms | 17.4 ms |
| Beam → Mac | 2232 ms | 17.5 ms | 25.1 ms |
| Mac → gateway | 20.85 ms | 8.34 ms | 14.98 ms |

Neither Beam path had an RTT over 200 ms in this valid window. This sequential comparison supports a short-term improvement but is not a randomized crossover experiment. The mixed results from the complete 120-second ping run must not be described as entirely collected in low-latency mode, nor do they establish long-term stability acceptance. After the glasses disconnected, `OnApplicationPause` released the lock normally. The system ultimately recorded two acquisitions, two releases, no remaining lock, and approximately 114.947 seconds in low-latency mode. This version was retained while waiting for the glasses to be reconnected; the follow-up is below.

Evidence in `Logs/performance-device-20261015/wifi-experiment/`: `delivery.json`, `editmode.xml`, `navigation-observe-summary.json`, `running-progress.log`, and `wifi-after-disconnect.txt`. Strict network segmentation and equal-length comparison are in `network/low-latency/phase-comparison.json` and `comparison-to-baseline.json`. The preceding 330-second result belongs to the `cc7a…` package without the Wi-Fi lease and must not be attributed to this package.

At 13:55, this independent synthetic service was stopped and ports 8080 / 50051 were confirmed closed; the record is `Logs/device-performance-20261015/simulator/stop.json`. The application had exited after glasses disconnection, and the installed package was retained. The computer remained awake as requested by the user.

### Complete Continuous Retest After Reconnection at 14:07

After the user reconnected the glasses, the same independent synthetic-asset service was restored. The phone was verified to still have the `5192b6…` package, which launched as PID 29116. The Wi-Fi low-latency lock was acquired at 14:07:00.728, and the first real left-eye preview frame was confirmed at 14:07:01.482. All 34 synthetic assets were fully verified again. During this run, the user explicitly reported: **"It is smooth now, with no obvious stuttering"** (translated).

Twelve complete stable windows covered **360 seconds / 21,538 frames**, with average application Update frequency **59.83 Hz**, window frame-time P95 **20.5–21.1 ms**, maximum **57.2 ms**, two frames over 50 ms, and none over 100 ms. Sampled navigation-age P95 was **77.6–85.4 ms**, maximum **121.5 ms**; none of 3256 samples exceeded 200 ms. Receive-gap P95 was **45.6–49.6 ms**, maximum **98.5 ms**. No navigation interruption, fatal errors, native RGB frame reads, or GPU readback warnings occurred. There was no actual device disconnection, SDK exit, or low-latency lock release during collection.

Simultaneous pings on three paths covered **14:07:39–14:13:09, approximately 330 seconds**, all completed with zero packet loss:

| Path | Packets | RTT P95 | Maximum RTT | >200 ms |
|---|---:|---:|---:|---:|
| Beam → gateway | 659 | 12.9 ms | 44.9 ms | 0 |
| Beam → Mac | 659 | 19.1 ms | 39.8 ms | 0 |
| Mac → gateway | 656 | 12.05 ms | 47.36 ms | 0 |

Another 330 read-only host cadence samples showed maximum HTTP duration 5.70 ms, maximum latest-source-frame age 40 ms, average production 27.41 Hz, and no request errors. Host sampling covered 14:06:18–14:11:47; it does not cover approximately the last 78 seconds of device sampling.

Under this configuration—small synthetic assets, phone Monitor page open, RGB paused—the continuous smoothness retest completed without reproducing the earlier multi-second stalls and included user feedback while wearing the glasses. Update frequency is not the number of independently displayed glasses frames per second. The SDK's FramePresent counts presentations of the current frame, and the internal sampling window for DroppedFrame is unclear, so these cannot be converted into glasses FPS. This run also does not cover large cases, RGB/full video streaming, or hour-scale stability and does not replace full device acceptance.

Evidence in `Logs/performance-device-20261015/resume-1405/`: `navigation-observe-summary.json`, `app-runtime.log`, `launch-full.log`, and `phone-monitor-after.png`. Raw three-path ping results and summary are in `network/resume-1405/ping-comparison.json`; connection boundaries are in `lifecycle-observations.json`. Host results are in `Logs/device-performance-20261015/simulator/resume-1405/host-cadence-summary.json`. Repeated `HandleActionCallback: 2026 → ActionDestroy` entries were confirmed to release temperature message objects and are not disconnection boundaries.

The final delivery record is `Logs/performance-device-20261015/delivery-final.json`; APK and source file hashes were rechecked. After the retest, the synthetic service was stopped and both listening ports confirmed closed. The installed version, workspace changes, and user-requested computer wake lock were retained; nothing was committed or pushed.

## 2026-10-04 Startup and Runtime Performance Investigation

These changes are based on the workspace after `bbea7df` and remain uncommitted. Existing user changes and the old `Builds/First XR.apk` were preserved.

### Code Overhead Addressed

| Path | Change |
|---|---|
| Beam Pro pages | Cached layouts and status text; remeasurement occurs only when dimensions, safe area, keyboard, page, or content changes; hidden pages no longer concatenate and measure the entire log |
| HUD / logs | Reused unchanged CT status, context, navigation-value, and log text; navigation freshness and warnings are still evaluated every frame, including while the HUD is hidden |
| Left-eye preview | Phone Monitor page capped at 30 Hz, 1280×720; other pages maintain real-frame warmup at 10 Hz, 320×180, preserving aspect ratio. Actual XR streaming immediately restores full resolution and capture every frame |
| gRPC | Reused one timeout timer per stream and stopped it while processing messages; ensured writer disposal after cancellation/timeout; main-thread queue defaults to a 2 ms per-frame budget and yields between callbacks |
| STL | Removed a full byte-array copy before read-only parsing, preserving original geometry and coordinates |
| Builds and diagnostics | No longer forces development mode whenever the editor opens; added a separate Release APK build action and installation of the most recently successful APK; logs frame time, GC, and navigation age for the first five-second window after startup and every 30 seconds thereafter |

Android's existing default quality was Medium. These changes did not reduce glasses quality or change CT geometry or navigation thresholds. The first diagnostic window begins with the monitoring component after scene loading and is not total cold-start time.

### Local Validation Evidence

- Unity 6000.0.83f1 EditMode: **154/154 passed, 0 failed, 0 skipped**. Coverage includes real read timeouts in the native HTTP/2 handler, write cancellation cleanup, queue ordering, STL byte/geometry preservation, preview frequency/dimensions, and HUD stale-data warnings and recovery. Results: `Logs/performance-20261004/editmode-release.xml`.
- `npm run smoke` passed in `Tools/navigation-simulator`: context, tolerances, continuous frames, versioned slices, and asset lifecycle.
- Screenshots of the actual HelloMR scene's Monitor, Connection, and Debug pages in Play Mode were captured and inspected, including the top status bar and Exit button: `Logs/performance-20261004/preview/`. This is editor UI evidence without real navigation or glasses connected.
- With the same four log sources and 32 lines per source, 10,000 repeated reads reduced managed allocations from **507,920,000 B → 0 B**, with the same returned character count. Source, benchmark, and reproduction script: `Logs/performance-20261004/ui-benchmark/`.
- For 10,000 synthetic receives under desktop Mono, with the connection CTS kept alive and GC forced, retained memory changed from **2,863,968 B → 34,344 B** for buffered reads and **3,131,752 B → 432 B** for pending reads. Full comparison and reproduction instructions: `Logs/performance-20261004/transport-timer-results.txt` and `transport-timer-reproduce.txt`. These are localized memory measurements and cannot be converted into device frame-rate improvements.

### Performance Package and Device Retest

Separate output: `Builds/First XR-performance.apk`; **Android ARM64 Release build and APK v2 signature verification passed**, 204,880,800 B. It retains the original package's Android Debug signing certificate. The Manifest does not enable `debuggable`, and the native HTTP/2 library is packaged. SHA-256: `b64c8b3c27f420076be75d6c4e262014bb10eef347caa02122ce8c595a2a053a`.

Build action: `XREAL > Build > Performance APK (Release, Build Only)`. `XREAL > Launch App On Android Device` installs the most recently successful APK and does not launch an old package if installation fails. Complete delivery manifest: `Logs/performance-20261004/delivery.json`.

ADB found no connected device during this run, so **the package was not installed and actual Beam Pro / glasses smoothness was not validated**. After reconnecting a device, compare the old and new packages with the same CT/STL and navigation input: cold start, all three pages, CT changes, model loading, and remote XR streaming. Retain at least five minutes of `DentalPerf:` logs and compare frame-time P95/maximum, counts over 33/50/100/200 ms, GC counts, navigation receive intervals, and data age. View following, text clarity, and streaming's first frame also require testing in the glasses.

Remaining limits: the 2 ms queue budget cannot interrupt a callback already executing. Large STL mesh generation and initialization of many DICOM files can still cause isolated main-thread peaks; if device logs identify those stages, further incremental processing is needed. Clinical spatial correctness remains a separate device-acceptance requirement below.

## Automated Validation (Historical Record, 2026-09-11)

| Scope | Method | Result at the Time |
|---|---|---|
| proto syntax and field generation | `protoc --descriptor_set_out`, regenerated C# | Passed |
| Unity runtime C# | Mono `msbuild Unity.XR.XREAL.Samples.InteractionBasics.csproj` | Passed; only existing repository dependency-version warnings |
| EditMode test assembly compilation | Mono `msbuild DentalNavigation.Dicom.EditModeTests.csproj` | Passed |
| Simulator | `cd Tools/navigation-simulator && npm run smoke` | Passed: context, tolerances, continuous frames, versioned slice round trip |
| DICOM core | Programmatically generated Explicit VR samples; out-of-order/duplicate chunks, resume, SHA, pixel space, and volume construction | Passed (seven nongraphical core tests) |
| Gesture state machine | Deterministic landmarks and time series | Passed: OK hold, direction, rate limiting, frame loss, head movement, takeover |
| Source formatting | `git diff --check` | Passed |

The Unity Test Runner attempt exited with code 199 during startup after a local `LicensingClient` IPC timeout of 60 seconds and produced no XML. Therefore the assembly compilation and independent core tests above must not be described as a completed full Unity Test Runner run. Log: `/tmp/first-xr-editmode.log`.

## Validation Required on Devices or the Companion System

The current repository and local machine cannot substitute for the following checks. Evidence is required for each before release or clinical use:

- Installation, permissions, 60 fps XR rendering, and head-locked positions on Beam Pro + the target XREAL glasses.
- Real navigation software v2 dual streams; coordinate comparisons across teeth and positive/negative Buccal/Lingual/Mesial/Distal combinations.
- Voxel-level position, orientation, and window-display comparison between complete real CT and the navigation system; Android DCMTK native parsing and ARM64 ABI.
- OK gesture success rate, false activation, and stopping time with actual surgical gloves, dental lighting, instruments, occlusion, and head movement.
- Actual XREAL VideoEncoder format; FFmpeg RTP reception, left/right separation, and P95 end-to-end latency.
- Navigation-system RGB toggling on demand without interrupting gesture capture or navigation latency.
- Network loss, navigation-service exit, plan/drill changes, camera unplugging, reconnection, and delayed commands carrying old versions.
- Crashes, memory growth, and queues over 60 minutes with complete CT, live navigation, gestures, and media running concurrently.
- A clinician wearing the glasses confirms readable values, unambiguous directions, a default position that does not obscure the operative field, and the complete workflow on a dental model.

## Device Acceptance Record Template

| Item | Record |
|---|---|
| APK SHA-256 / Git commit | To be filled in |
| Beam Pro / XREAL models and system versions | To be filled in |
| Navigation software version / protocol version | To be filled in |
| DICOM sample identifier / instance and frame counts | To be filled in |
| Navigation display latency P50 / P95 / maximum | To be filled in |
| Remote observation latency P50 / P95 / maximum | To be filled in |
| Successful OK operations / total attempts | To be filled in |
| Unintended slice changes during 30 minutes without operation | To be filled in |
| Maximum stopping time after release / recognition loss | To be filled in |
| Memory at start / end / peak over 60 minutes | To be filled in |
| Fault recovery results | To be filled in |
| Clinician readability and occlusion confirmation | To be filled in |
| Conclusion, issues, and signatures | To be filled in |
