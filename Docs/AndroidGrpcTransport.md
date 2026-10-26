# Android gRPC HTTP/2 Transport Fix Record

Recorded: 2026-09-29. Target environment: Unity `6000.0.83f1`, Android IL2CPP, Beam Pro.

This fix addresses the Android default `GrpcChannel` HTTP transport being unable to establish the required HTTP/2 bidirectional streams. The APK described here built successfully at `16:21:03` and was installed at `16:28:36`. After relaunch, the real device returned final asset confirmation; cache reuse required sending only **757,712 bytes**, while the confirmed total remained **305,701,088 bytes**. **The user reported an automatic exit, not a manual closure, at 16:29 on first launch. Its cause remains under investigation; investigation was deferred at the user's request, and the issue is not marked fixed.** The 105 Unity tests for connection indication/font fixes and five Mono tests for the new resume patch passed separately. The complete 110-test Unity suite after that patch had not run before this APK was built. The user visually confirmed that the dental model and current middle CT slice were visible. For the later RGB-paused APK, 121-test Unity regression, and device results, see [RGB pause device validation](RgbPauseDeviceValidation.md).

## Fix Mechanism

- `DentalRobotGrpcClient.RunConnectionAsync` connects through `DentalRobotGrpcChannelFactory.Create`. The factory explicitly supplies `Cysharp.Net.Http.YetAnotherHttpHandler` as `GrpcChannelOptions.HttpHandler`, replacing the runtime's default HTTP handler with its native HTTP/2 backend. Editor and Android use the same factory.
- The current navigation simulator uses cleartext HTTP/2 over `http://`, or h2c. The handler sets `Http2Only = true` and a `5-second` connection timeout, providing the HTTP/2 transport required for persistent bidirectional request streams. This change adds no TLS, certificate exemptions, or extra protocol wrapping.
- `Grpc.Net.Client` / `Grpc.Net.Common` / `Grpc.Core.Api` remain at the same version, `2.60.0`; all three project DLLs now use the NuGet `netstandard2.1` builds. The Client/Common change aligns the gRPC client's trailer-reading path with `HttpResponseMessage.TrailingHeaders` used by the handler's Unity branch, avoiding calls that connect and receive bodies but fail to complete normally because `grpc-status` is missing. Core.Api changes target framework at the same time to avoid a mixed dependency combination that triggered an Editor crash during this investigation.
- Each connection attempt creates its own handler. `DisposeHttpClient = true` transfers its lifetime to the channel; existing client `using` statements dispose the channel at connection completion, cancellation, or reconnection. If channel creation fails, the factory disposes the created handler directly.
- `StreamSession`, `StreamAssets`, v2 messages, and existing v1 fallback behavior are unchanged. The change is confined to the channel's underlying HTTP transport.
- The integration server for this session was `192.168.31.64:50051`, device ID `beam-pro`, dataset ID `default`. Defaults are defined in `DentalRobotConnectionDefaults`; the effective address on the Connection page is authoritative on site.

Code:

- [Channel factory](../Assets/Samples/XREAL%20XR%20Plugin/3.1.0/Interaction%20Basics/HelloMR/DentalRobotGrpcChannelFactory.cs)
- [gRPC client](../Assets/Samples/XREAL%20XR%20Plugin/3.1.0/Interaction%20Basics/HelloMR/DentalRobotGrpcClient.cs)
- [Connection defaults](../Assets/Samples/XREAL%20XR%20Plugin/3.1.0/Interaction%20Basics/HelloMR/DentalRobotConnectionDefaults.cs)

## Dependency Sources and Checksums

### YetAnotherHttpHandler 1.11.5

- Source: [version 1.11.5 in Cysharp's official repository](https://github.com/Cysharp/YetAnotherHttpHandler/tree/1.11.5).
- Download: [official tag tar.gz](https://codeload.github.com/Cysharp/YetAnotherHttpHandler/tar.gz/refs/tags/1.11.5).
- Installed at `Packages/com.cysharp.yetanotherhttphandler` as an embedded Unity package. Its `package.json` records version `1.11.5`.
- Includes matching native libraries. The Android ARM64 plugin's `.meta` enables Android / ARM64. The new APK was verified to contain the native library; file and size are recorded below.
- License: [MIT LICENSE](../Packages/com.cysharp.yetanotherhttphandler/LICENSE); third-party notices: [THIRD-PARTY-NOTICES](../Packages/com.cysharp.yetanotherhttphandler/THIRD-PARTY-NOTICES).

Archive SHA-256 from this dependency installation record:

```text
56ec4a79897cfb8b25d630cae561d8f3b9fe8d7b6be47d52032dff7096016020
```

### System.IO.Pipelines 8.0.0

- Source: [official NuGet System.IO.Pipelines 8.0.0](https://www.nuget.org/packages/System.IO.Pipelines/8.0.0).
- Download: [official NuGet flat-container nupkg](https://api.nuget.org/v3-flatcontainer/system.io.pipelines/8.0.0/system.io.pipelines.8.0.0.nupkg).
- Uses `lib/netstandard2.0/System.IO.Pipelines.dll` from the package, installed at `Assets/Plugins/Grpc/System.IO.Pipelines.dll`, satisfying `System.IO.Pipelines` references in the handler source.
- License copy: [System.IO.Pipelines.LICENSE.txt](../Assets/Plugins/Grpc/System.IO.Pipelines.LICENSE.txt).

Nupkg SHA-256 from this dependency installation record:

```text
2dda41d6ce2f433b0e3836b188ab2d2e4b39ed7f434c3e43e9c3f1f03135c301
```

Installed DLL SHA-256, reread from the local file during the original documentation check:

```text
3702e2403cf265588d1544a292ea17c01d337cdb91b5bdd64690894331345ad4
```

Recheck the DLL from the project root:

```bash
shasum -a 256 Assets/Plugins/Grpc/System.IO.Pipelines.dll
```

### Grpc.Net.Client / Grpc.Net.Common / Grpc.Core.Api 2.60.0

The version remains `2.60.0`; only the target-framework builds selected within the packages changed. All three DLLs use `lib/netstandard2.1/` and remain installed in `Assets/Plugins/Grpc/`.

| Dependency | Official source | File within package |
| --- | --- | --- |
| Grpc.Net.Client 2.60.0 | [Official NuGet nupkg](https://api.nuget.org/v3-flatcontainer/grpc.net.client/2.60.0/grpc.net.client.2.60.0.nupkg) | `lib/netstandard2.1/Grpc.Net.Client.dll` |
| Grpc.Net.Common 2.60.0 | [Official NuGet nupkg](https://api.nuget.org/v3-flatcontainer/grpc.net.common/2.60.0/grpc.net.common.2.60.0.nupkg) | `lib/netstandard2.1/Grpc.Net.Common.dll` |
| Grpc.Core.Api 2.60.0 | [Official NuGet nupkg](https://api.nuget.org/v3-flatcontainer/grpc.core.api/2.60.0/grpc.core.api.2.60.0.nupkg) | `lib/netstandard2.1/Grpc.Core.Api.dll` |

The following nupkg SHA-256 values come from the installation record. The three installed DLL hashes were reread during the original documentation check.

```text
Grpc.Net.Client 2.60.0 nupkg
1112bbcb094a46590a5ba80d4821bd6126ee630e932a5fc92f5a521943d88d19
Grpc.Net.Client.dll (netstandard2.1)
f74a44a2dab125744482a6a79b30dac51e70ac6786f4f53b3ccd3078ff6ba6dc

Grpc.Net.Common 2.60.0 nupkg
98fdb3eda90d52b29b91dd0bf2c22e1ec68476eefdbdd56fede550a7546c3736
Grpc.Net.Common.dll (netstandard2.1)
bac6b1cf614a563fe3fd25010529c3e344c51d1203d76d8b3fbcf18303bf5d21

Grpc.Core.Api 2.60.0 nupkg
15b6015af9228086ba41e5f5c458f629d51e8eb649ba2192d50471b6e2dc339a
Grpc.Core.Api.dll (netstandard2.1)
052b414199fd3aa145f013b3442148fe1760893a3e242fbf764bc208a379d8de
```

During troubleshooting, initially replacing only Client/Common without matching Core.Api triggered a Unity Mono JIT native crash in the first loopback probe; see the [initial crash diagnostic log](../Logs/Http2/editor-initial-probe-crash.log). After all three `2.60.0` DLLs were aligned to `netstandard2.1`, reimport, compilation, and all three tests below passed. The initial crash is not a passing test and does not establish an Android build or device pass.

## Added Transport Tests

[DentalRobotGrpcTransportTests.cs](../Assets/Tests/TransportEditMode/DentalRobotGrpcTransportTests.cs) belongs to the Editor test assembly `DentalNavigation.Transport.EditModeTests`. Tests start a real local TCP peer and initiate a gRPC bidirectional stream through the native handler above, checking:

1. Receipt of the HTTP/2 connection preface `PRI * HTTP/2.0`, rather than treating successful TCP connection as successful HTTP/2.
2. The peer can read gRPC request frames and the `hello` request body after sending only SETTINGS, before returning response headers; bidirectional request streaming must not wait for response headers before sending.
3. While the response is pending, either cancelling the call or disposing the channel ends the pending call with `StatusCode.Cancelled`.
4. The peer returns response headers, gRPC DATA, and ending trailers with `grpc-status: 0`; the client must read the response body, finish the response stream normally, and receive `StatusCode.OK`. This prevents mistaking an established HTTP/2 connection for a complete successful gRPC call.

The assembly ran in Unity `6000.0.83f1` Editor EditMode on 2026-09-29: **3 passed, 0 failed**. Cancellation/disposal accounts for two parameterized cases; normal response and trailers is the third case. The [test-result XML](../Logs/Http2/transport-tests.xml) root records `Passed`, `total=3`, `passed=3`, and `failed=0`, with total duration `3.6350093 seconds`; the [Test Runner screenshot](../Logs/Http2/transport-tests-passed.jpg) shows `3.615 seconds` for the assembly. Each duration is retained exactly as recorded by its evidence.

This covers local native transport, request/response streams, successful trailers, and cancellation/disposal in the Editor. It does not substitute for Android native-library loading, APK plugin packaging, Beam Pro service connection, or complete asset-transfer testing. The other 96 tests visible in the screenshot did not run in this three-test run and cannot be counted as passes here; the subsequent full run is recorded separately below.

## Connection Indicator Fix and Full Regression

Connection text and indicator color are now independent of navigation frame age. While the gRPC session remains connected, waiting for the first frame, delayed or stale navigation, invalid tracking frames, and intentional pause/stop do not change the connection indicator to disconnected. Actual connection/disconnection events still update it. Navigation warnings remain independent: **over 200 ms indicates delay; over 500 ms marks data stale and hides values**. Invalid frames and stopped navigation also hide values.

Six regression cases were added to [DentalNavigationBandTests.cs](../Assets/Tests/EditMode/DentalNavigationBandTests.cs), covering delay/recovery, staleness/recovery, waiting for the first frame, invalid tracking frames, stopped navigation, and actual connection/disconnection events. Delay and staleness are two parameterized cases of the same test.

The first full run had **104/105 passes and one failure**: missing baked glyphs for a new Chinese HTTP/2 message. See the [initial regression XML](../Logs/Http2/connection-regression-initial.xml). After rebuilding **425 glyphs** through the project's existing font menu, the suite reran at `2026-09-29 16:04:16–16:04:18` (Asia/Shanghai): **105 passed, 0 failed, 0 skipped, duration 2.1525731 seconds**. Evidence: [full regression XML](../Logs/Http2/connection-regression.xml) and [Test Runner screenshot](../Logs/Http2/connection-regression-passed.jpg).

These are source and Editor results. Later installation and cache-reuse results from the real device appear below; these tests do not substitute for complete device display acceptance.

A restart/resume defect was subsequently found: completed DICOM files had moved from `.part` to their final paths and their progress files had been removed, but a new process recreated `.part` and transferred from zero instead of reusing the final DICOM. The `TransferStore` patch was completed; all five new regressions passed in an actual Mono run, and independent review passed. **The 105-test Unity result predates the resume patch. A native Test Runner UI click failure prevented the complete post-patch 110-test Unity suite from running in this session. The 105 Unity tests and five Mono tests must not be added together and described as "all 110 Unity tests passed."** The `16:05:36` build was not installed. The subsequent `16:21:03` build was installed and obtained real final confirmation after cache reuse.

The Mono regressions compiled and ran the actual [DentalDicomTransferStoreTests.cs](../Assets/Tests/EditMode/DentalDicomTransferStoreTests.cs) with NUnit. They cover complete-file recovery and idempotent completion after store reconstruction, two corrupt-final-file scenarios, recovery from interrupted rename and leftover progress files, and a final file changing again before completion. Output from all five remains in the tool execution record; no separate result file was saved, and no passing Unity XML record is claimed.

## Validation Record

| Validation level | Status at the time | Evidence / remaining items |
| --- | --- | --- |
| Code and dependency inspection | Checked factory configuration, embedded package version, ARM64 plugin configuration, and installed Pipelines / gRPC Client / Common / Core.Api DLL SHA-256 values | This document and corresponding files |
| TCP 50051 reachability | Reported as passed in this integration record | Add probe direction, command, time, and raw output |
| Editor compilation and native HTTP/2 / bidirectional-stream tests | Unity 6000.0.83f1 compilation passed; 3 tests passed, 0 failed; XML duration 3.6350093 seconds, assembly screenshot 3.615 seconds | [Test-result XML](../Logs/Http2/transport-tests.xml), [Test Runner screenshot](../Logs/Http2/transport-tests-passed.jpg) |
| Full Editor regression | At 16:04, 105 passed, 0 failed, 0 skipped, 2.1525731 seconds; includes six added connection-indicator cases | [Full regression XML](../Logs/Http2/connection-regression.xml), [screenshot](../Logs/Http2/connection-regression-passed.jpg) |
| Restart/resume patch regression | Five new actual Mono regressions passed; independent review passed; the complete 110-test post-patch Unity suite had not run | [Regression source](../Assets/Tests/EditMode/DentalDicomTransferStoreTests.cs) and tool output; the 105-test XML describes only the pre-patch version |
| Android IL2CPP build and native APK library check | The 15:52 installation build passed: 242,005,714 bytes; APK Signature Scheme v2 passed, one signer; contains the 3,245,032-byte ARM64 native HTTP/2 library | [Build summary](../Logs/Http2/android-build-summary.txt); SHA-256 below |
| APK with connection-indicator, font, and restart/resume fixes | Built successfully at 16:21:03; 242,005,851 bytes; v2 signature passed, one signer; `adb install -r` returned `Success` at 16:28:36 | APK SHA-256 and native-library entry below; intermediate 16:05:36 build not installed |
| Beam Pro installation and launch | `adb install -r` returned `Success`; `lastUpdateTime=2026-09-29 15:52:51`; launched `NRXRActivity`; user confirmed the application was running | Device integration record; raw installation/launch output can still be archived |
| Device HTTP/2 and `StreamSession` | At 15:53:33, real `beam-pro` at `192.168.31.46` connected to `192.168.31.64:50051`; context/control ACKs accepted and heartbeats normal | Connection record below; excludes final asset confirmation and device display acceptance |
| Device `StreamAssets` and final confirmation | Old version fully confirmed; new version returned `confirmed` after cache reuse, with sent=757,712 and confirmed=total=305,701,088 bytes | Old version: [device transfer record](../Logs/Http2/device-transfer-watch.jsonl); new version: 16:33 record below; not visual/registration acceptance |
| Device exit and reconnection | 15:58:32 was a user-confirmed manual closure; 16:29 on the new version was a user-reported automatic exit of unknown cause; relaunch and connection succeeded at 16:33:46 | Events recorded separately; automatic-exit investigation deferred at the user's request and not marked fixed |
| Dental model visibility and CT slice | User visually confirmed the teeth STL and current middle CT slice; at 16:50:51 the device returned `slice state applied` for the middle-slice control | Visibility at 39.6 mm confirmed; complete interaction and spatial correspondence remain unverified; see below |

### APK Installed at 15:52

- Original output: `Builds/First XR.apk`, **242,005,714 bytes**; this path was later replaced by the 16:21 build below.
- SHA-256: `e507cacaf960d6456a65eed0ecf587be275f89edc27585aa6f252a7c6b8e8b5f`.
- `apksigner`: APK Signature Scheme v2 passed, one signer.
- ARM64 library inside APK: `lib/arm64-v8a/libCysharp.Net.Http.YetAnotherHttpHandler.Native.so`, **3,245,032 bytes**.

Size, SHA-256, and the native ZIP entry were checked directly against the APK when the historical record was originally completed. Signature results come from the [15:52 build summary](../Logs/Http2/android-build-summary.txt).

### Version Built at 16:21 and Installed at 16:28

- Build completed: `2026-09-29 16:21:03`; file: [Builds/First XR.apk](../Builds/First%20XR.apk).
- Size: **242,005,851 bytes**.
- SHA-256: `82f1a307dc5feb162a7655b826ee863c108c8220c3814a5e923a244e2fd7de42`.
- Signature verification: APK Signature Scheme v2 passed, one signer.
- ARM64 native library: `lib/arm64-v8a/libCysharp.Net.Http.YetAnotherHttpHandler.Native.so`, **3,245,032 bytes**.
- `16:28:36`: `adb install -r` returned `Success`.

Size, SHA-256, and the native ZIP entry were read directly during the original record update. Build time, signature, and installation results come from the actual integration record. This package includes the resume patch, but the automatic-exit cause remained unresolved; stability acceptance is not claimed.

### Real Beam Pro Connection Record

All times below are on 2026-09-29 in Asia/Shanghai:

1. `15:52:51`: `lastUpdateTime` after installing the new APK; `NRXRActivity` launched afterward.
2. `15:53:33`: the server received a session connection from real device `beam-pro` at `192.168.31.46`, connecting to `192.168.31.64:50051`.
3. The device returned accepted `context applied`, `tolerance applied`, `slice waiting for CT`, `layout applied`, and `observation applied`; heartbeats were normal. `slice waiting for CT` means slice control is waiting for CT, not that CT reception, volume construction, or display has completed.
4. Approximately `15:53:56`: the navigation simulator started a normal scenario at `30 Hz`. Files were still transferring at this point; no final CompleteAck was recorded, and simulator startup was not counted as a device HUD display pass.
5. `15:58:32`: the user explicitly confirmed manually closing the application. This event is not a confirmed application crash.
6. `16:03:23`: the device reconnected, and the user confirmed normal display. This does not substitute for final asset CompleteAck or establish device validation of the connection-indicator fix, which had not yet been installed.
7. `16:08:54`: the [device transfer record](../Logs/Http2/device-transfer-watch.jsonl) first recorded `transfer.status=confirmed`, with the message "The client has verified and committed all assets" (translated); `sentBytes=confirmedBytes=totalBytes=305701088`. This is distinct from the earlier `sent` state, "Sending completed; waiting for client verification/volume confirmation" (translated).
8. Approximately `16:10:04`: `/api/state` was checked again and retained final confirmation. Since `16:03:23`, the device had used the same peer, `192.168.31.46:47982`, with normal heartbeats. ADB reported application PID `23716`, and application data contained **534 `.dcm` files**. The user confirmed normal display.
9. The new version first launched as PID `24687`. At `16:29:05`, ActivityManager recorded `wm_finish_activity` with reason `app-request`; at `16:29:09`, the background process received `SIGKILL`. No native crash record was found in this inspection. The user explicitly said the application exited by itself and was not manually closed. **These logs do not establish the root cause; `app-request` must not be equated with a deliberate user action. The automatic exit remains open.**
10. After the user relaunched it, the device connected at `16:33:46` as peer `192.168.31.46:48536`, PID `25045`, with `UnityPlayerActivity` as foreground `topResumed`. Heartbeats continued until at least `16:34:50`.
11. The new version again returned `confirmed` after cache reuse: `sentBytes=757712`, `confirmedBytes=totalBytes=305701088`. Reusing 534 completed CT files avoided full retransmission, and final client confirmation passed. This is real asset-reuse evidence for the resume patch, not CT/STL visual-display or registration acceptance.

### On-Site CT Slice Diagnosis

The actual CT's first slice at `Z=0.15 mm` and third slice at `Z=0.45 mm` were entirely air (`HU=-1000`), while middle slices contained image content. The planned entry was then `Z=0.45 mm` with slice offset `0`. Control state overrode the middle-slice selection made during client volume construction, selecting an air slice in the glasses and producing an all-black display.

At `16:50:50`, the console synchronized the slice to `offset=39.6 mm`, plane `Z=40.05 mm`, `control_version=3`; at `16:50:51`, the real device accepted it and returned `slice state applied`. See the [console-state screenshot](../Logs/Http2/ct-middle-slice-applied.jpg). The user then reported "I can see the CT now" (translated), confirming the middle grayscale CT slice in the glasses, and had separately visually confirmed the teeth STL. User visual confirmation, control ACK, and screenshot are recorded separately; this does not validate every slice, CT/STL spatial correspondence, or navigation overlay accuracy.

The navigation simulator now persistently defaults the observation plane to the CT's geometric middle when importing/switching datasets, preserving the planned entry point and correctly applying patient-coordinate transforms. Three regression cases were added; this `npm test` run passed **50/50**. The running service had not restarted, so the active session still used the manually selected `39.6 mm`; the source fix was not treated as reloaded in that service. Details and test locations: [simulator test record](../../xreal%20server/docs/TestResults.md).

Installation, actual connection, cache reuse, final confirmation, and visibility of this middle CT slice were verified for the new version. **Automatic-exit investigation remained deferred at the user's request and unresolved; the complete post-patch 110-test Unity suite had still not run.** User confirmation that the dental model and CT were visible does not establish spatial correspondence or clinical validity.
