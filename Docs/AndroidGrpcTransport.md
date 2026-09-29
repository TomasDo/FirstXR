# Android gRPC HTTP/2 传输修复记录

记录日期：2026-09-29。目标环境为 Unity `6000.0.83f1`、Android IL2CPP、Beam Pro。

本次修复针对 Android 默认 `GrpcChannel` HTTP 传输无法建立所需 HTTP/2 双向流的问题。本记录所述 APK 于 `16:21:03` 构建成功、`16:28:36` 安装；再次启动后真实设备返回资产最终确认，复用缓存仅发送 **757,712 字节**，确认总量仍为 **305,701,088 字节**。**首次启动在 16:29 发生用户报告的非手动自动退出，原因待查，按用户要求暂搁置，未标为已修复。** 当时连接指示及字体修复的 Unity 105 项测试与新增续传补丁的 Mono 五项测试分别通过；该 APK 构建前未完成补丁后的 Unity 全套 110 项测试。用户已目视确认牙模及当前中层 CT 切片在视野内可见。后续 RGB 暂停版 APK、121 项 Unity 回归与真机结果见 [RGB 暂停真机验证](RGB暂停真机验证.md)。

## 修复机制

- `DentalRobotGrpcClient.RunConnectionAsync` 通过 `DentalRobotGrpcChannelFactory.Create` 建立连接。工厂给 `GrpcChannelOptions.HttpHandler` 显式传入 `Cysharp.Net.Http.YetAnotherHttpHandler`，使用其原生 HTTP/2 后端，替代运行时默认 HTTP 处理器。Editor 和 Android 使用同一工厂。
- 当前导航模拟服务使用 `http://` 明文 HTTP/2，即 h2c。处理器设置 `Http2Only = true`，连接超时为 `5 秒`，满足持续双向请求流所需的 HTTP/2 传输方式。该修改不增加 TLS、证书豁免或额外协议封装。
- 保持 `Grpc.Net.Client` / `Grpc.Net.Common` / `Grpc.Core.Api` 为同一版本 `2.60.0`，将三个项目内 DLL 统一为 NuGet 的 `netstandard2.1` 构建。Client/Common 的调整使 gRPC 客户端读取 trailers 的路径与处理器 Unity 分支使用的 `HttpResponseMessage.TrailingHeaders` 一致，避免已连接、已收正文却因丢失 `grpc-status` 而无法正常完成调用。Core.Api 同步切换目标框架，避免混用此次已触发 Editor 崩溃的依赖组合。
- 每次连接尝试创建独立处理器。`DisposeHttpClient = true` 将其生命周期交给通道；客户端现有 `using` 在连接结束、取消或重连时释放通道。若创建通道失败，工厂直接释放已创建的处理器。
- `StreamSession`、`StreamAssets`、协议 v2 消息及原有 v1 回退逻辑保持不变。本次调整集中在通道底层 HTTP 传输。
- 本次现场联调服务器为 `192.168.31.64:50051`，设备 ID 为 `beam-pro`，数据集 ID 为 `default`。默认值定义在 `DentalRobotConnectionDefaults`；现场以连接页面实际生效的地址为准。

代码位置：

- [通道工厂](../Assets/Samples/XREAL%20XR%20Plugin/3.1.0/Interaction%20Basics/HelloMR/DentalRobotGrpcChannelFactory.cs)
- [gRPC 客户端](../Assets/Samples/XREAL%20XR%20Plugin/3.1.0/Interaction%20Basics/HelloMR/DentalRobotGrpcClient.cs)
- [默认连接参数](../Assets/Samples/XREAL%20XR%20Plugin/3.1.0/Interaction%20Basics/HelloMR/DentalRobotConnectionDefaults.cs)

## 依赖来源与校验值

### YetAnotherHttpHandler 1.11.5

- 来源：[Cysharp 官方仓库的 1.11.5 标签](https://github.com/Cysharp/YetAnotherHttpHandler/tree/1.11.5)。
- 下载归档：[官方标签 tar.gz](https://codeload.github.com/Cysharp/YetAnotherHttpHandler/tar.gz/refs/tags/1.11.5)。
- 安装位置：`Packages/com.cysharp.yetanotherhttphandler`，作为 embedded Unity package 使用。当前 `package.json` 记录版本 `1.11.5`。
- 包含该版本配套原生库；Android ARM64 插件的 `.meta` 已启用 Android / ARM64。本次新 APK 已核实包含该原生库，具体文件与大小见下方构建记录。
- 许可：[MIT LICENSE](../Packages/com.cysharp.yetanotherhttphandler/LICENSE)；第三方声明：[THIRD-PARTY-NOTICES](../Packages/com.cysharp.yetanotherhttphandler/THIRD-PARTY-NOTICES)。

归档 SHA-256，来自本次依赖安装记录：

```text
56ec4a79897cfb8b25d630cae561d8f3b9fe8d7b6be47d52032dff7096016020
```

### System.IO.Pipelines 8.0.0

- 来源：[NuGet 官方 System.IO.Pipelines 8.0.0](https://www.nuget.org/packages/System.IO.Pipelines/8.0.0)。
- 下载包：[NuGet 官方 flat-container nupkg](https://api.nuget.org/v3-flatcontainer/system.io.pipelines/8.0.0/system.io.pipelines.8.0.0.nupkg)。
- 使用包内 `lib/netstandard2.0/System.IO.Pipelines.dll`，安装到 `Assets/Plugins/Grpc/System.IO.Pipelines.dll`，供处理器源码中的 `System.IO.Pipelines` 引用使用。
- 许可副本：[System.IO.Pipelines.LICENSE.txt](../Assets/Plugins/Grpc/System.IO.Pipelines.LICENSE.txt)。

Nupkg SHA-256，来自本次依赖安装记录：

```text
2dda41d6ce2f433b0e3836b188ab2d2e4b39ed7f434c3e43e9c3f1f03135c301
```

安装后 DLL SHA-256，已于本次文档核对中重新读取本地文件确认：

```text
3702e2403cf265588d1544a292ea17c01d337cdb91b5bdd64690894331345ad4
```

可在项目根目录复核 DLL：

```bash
shasum -a 256 Assets/Plugins/Grpc/System.IO.Pipelines.dll
```

### Grpc.Net.Client / Grpc.Net.Common / Grpc.Core.Api 2.60.0

版本保持 `2.60.0`，本次更换的是包内目标框架构建。三个 DLL 均选用 `lib/netstandard2.1/`，安装到原有 `Assets/Plugins/Grpc/` 路径。

| 依赖 | 官方来源 | 包内文件 |
| --- | --- | --- |
| Grpc.Net.Client 2.60.0 | [NuGet 官方 nupkg](https://api.nuget.org/v3-flatcontainer/grpc.net.client/2.60.0/grpc.net.client.2.60.0.nupkg) | `lib/netstandard2.1/Grpc.Net.Client.dll` |
| Grpc.Net.Common 2.60.0 | [NuGet 官方 nupkg](https://api.nuget.org/v3-flatcontainer/grpc.net.common/2.60.0/grpc.net.common.2.60.0.nupkg) | `lib/netstandard2.1/Grpc.Net.Common.dll` |
| Grpc.Core.Api 2.60.0 | [NuGet 官方 nupkg](https://api.nuget.org/v3-flatcontainer/grpc.core.api/2.60.0/grpc.core.api.2.60.0.nupkg) | `lib/netstandard2.1/Grpc.Core.Api.dll` |

以下 nupkg SHA-256 来自本次安装记录；安装后三个 DLL 的 SHA-256 已在本次文档核对中重新读取确认。

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

排障时，最初仅替换 Client/Common、尚未同步 Core.Api 的组合在首次回环探测中触发了 Unity Mono JIT 原生崩溃，记录见 [初始崩溃诊断日志](../Logs/Http2/editor-initial-probe-crash.log)。将三个 `2.60.0` DLL 统一为 `netstandard2.1` 后，重新导入、编译并运行下述测试，三项全部通过。该初始崩溃不计为测试通过，也不以此推断 Android 构建或真机已通过。

## 已加入的传输测试

[DentalRobotGrpcTransportTests.cs](../Assets/Tests/TransportEditMode/DentalRobotGrpcTransportTests.cs) 属于 Editor 测试程序集 `DentalNavigation.Transport.EditModeTests`。测试启动实际本机 TCP 对端，并使用上述原生处理器发起 gRPC 双向流，检查：

1. 接收到 HTTP/2 连接前言 `PRI * HTTP/2.0`，而不是把 TCP 连接成功当作 HTTP/2 成功。
2. 对端仅发送 SETTINGS、尚未返回响应头时，就能读到 gRPC 请求帧和 `hello` 请求体，检查双向流请求不会等待响应头才发送。
3. 在响应仍等待时，分别取消调用或释放通道；挂起调用应以 `StatusCode.Cancelled` 结束。
4. 对端返回响应头、gRPC DATA 和带 `grpc-status: 0` 的结束 trailers；客户端应读到响应正文、正常结束响应流，并获得 `StatusCode.OK`。此项防止把“建立了 HTTP/2 连接”误判为完整 gRPC 调用通过。

2026-09-29 在 Unity `6000.0.83f1` 的 Editor EditMode 中运行该程序集：**3 项通过，0 项失败**。其中取消/释放测试为两个参数化用例，正常响应及 trailers 测试为第三个用例。[测试结果 XML](../Logs/Http2/transport-tests.xml) 的根节点记录 `Passed`、`total=3`、`passed=3`、`failed=0`，整次运行耗时 `3.6350093 秒`；[Test Runner 截图](../Logs/Http2/transport-tests-passed.jpg) 显示该测试程序集耗时 `3.615 秒`。耗时按各自证据原样记录。

此结果覆盖 Editor 中的本机原生传输、请求/响应流、成功 trailers 和取消/释放行为，不替代 Android 原生库装载、APK 内插件打包、Beam Pro 端服务连接或完整资产传输测试。截图中的其他 96 项测试未在这次三项运行中执行，不能计入该次通过数；随后完整测试结果另列如下。

## 连接指示修复与完整回归

连接文字和灯号现与导航帧年龄分离：只要 gRPC 会话仍连接，等待首帧、导航延迟、帧过期、无效追踪帧或主动暂停/停止都不会把连接指示改成断开；真实连接/断开事件仍更新文字和灯号。导航提示独立保留：**超过 200 ms 提示延迟，超过 500 ms 判为过期并隐藏数值**，无效帧或停止导航时同样隐藏数值。

在 [DentalNavigationBandTests.cs](../Assets/Tests/EditMode/DentalNavigationBandTests.cs) 新增六个回归用例，覆盖延迟与恢复、过期与恢复、等待首帧、无效追踪帧、停止导航，以及实际连接/断开事件；延迟和过期为同一测试的两个参数用例。

首次完整运行结果为 **104/105 通过、1 失败**，失败项为新 HTTP/2 中文提示缺少已烘焙字形，记录见 [初次回归 XML](../Logs/Http2/connection-regression-initial.xml)。通过项目现有字体菜单重建 **425 个字形**后，在 `2026-09-29 16:04:16–16:04:18`（Asia/Shanghai）重新运行：**105 项全部通过、0 失败、0 跳过，耗时 2.1525731 秒**。证据为 [完整回归 XML](../Logs/Http2/connection-regression.xml) 和 [Test Runner 截图](../Logs/Http2/connection-regression-passed.jpg)。

以上是源码与 Editor 的验证结果。后续新 APK 安装和缓存复用的真实设备结果另见下文，不用这些测试替代完整设备显示验收。

随后另发现重启续传缺陷：完成的 DICOM 已从 `.part` 移至最终文件并删除进度文件，新进程却没有先复用最终 DICOM，而是重新创建 `.part` 从零发送。`TransferStore` 补丁已完成，新增五项回归在实际 Mono 运行中全部通过，独立审查亦通过。**以上 105 项 Unity 结果发生在续传补丁之前；本轮因原生 Test Runner UI 点击故障，补丁后的 Unity 全套 110 项尚未实际运行，不能把 105 项与 Mono 五项相加后称为“Unity 110 项全过”。** `16:05:36` 构建批次未安装；后续 `16:21:03` 构建版本已安装并取得缓存复用后的真实最终确认。

Mono 回归使用实际 [DentalDicomTransferStoreTests.cs](../Assets/Tests/EditMode/DentalDicomTransferStoreTests.cs) 与 NUnit 编译运行，覆盖重建存储后的完整文件恢复及幂等完成、两种损坏最终文件情况、重命名中断与遗留进度文件恢复、完成前最终文件再次变化。五项输出保留于本次工具执行记录，未另存独立结果文件，不虚构 Unity XML 通过记录。

## 验证记录

| 验证层级 | 当前状态 | 证据 / 待补项 |
| --- | --- | --- |
| 代码与依赖检查 | 已核对工厂配置、embedded package 版本、ARM64 插件配置，以及 Pipelines / gRPC Client / Common / Core.Api 安装后 DLL SHA-256 | 本文及对应文件 |
| TCP 50051 网络连通 | 本次联调记录报告通过 | 补充探测方向、命令、时间与原始输出 |
| Editor 编译及原生 HTTP/2 / 双向流测试 | Unity 6000.0.83f1 编译通过；3 项测试通过、0 失败；XML 总耗时 3.6350093 秒，程序集截图 3.615 秒 | [测试结果 XML](../Logs/Http2/transport-tests.xml)、[Test Runner 截图](../Logs/Http2/transport-tests-passed.jpg) |
| 完整 Editor 回归 | 16:04 复测 105 项通过、0 失败、0 跳过，2.1525731 秒；包括六项新增连接指示回归 | [完整回归 XML](../Logs/Http2/connection-regression.xml)、[截图](../Logs/Http2/connection-regression-passed.jpg) |
| 重启续传补丁回归 | 新增 5 项实际 Mono 回归通过；独立审查通过；补丁后 Unity 全套 110 项尚未实际运行 | [回归源码](../Assets/Tests/EditMode/DentalDicomTransferStoreTests.cs)及本次工具输出；105 项 XML 仅代表补丁前版本 |
| Android IL2CPP 构建与 APK 原生库检查 | 15:52 安装批次已通过：242,005,714 字节；APK 签名方案 v2 通过、1 个签名者；含 3,245,032 字节 ARM64 HTTP/2 原生库 | [构建摘要](../Logs/Http2/android-build-summary.txt)；该批次 SHA-256 见下文 |
| 连接指示、字体及重启续传修复的该批次 APK | 16:21:03 构建成功；242,005,851 字节；签名 v2 通过、1 个签名者；16:28:36 `adb install -r` 返回 `Success` | 该批次 APK SHA-256 与原生库条目见下文；16:05:36 中间版本未安装 |
| Beam Pro 安装与启动 | `adb install -r` 返回 `Success`；`lastUpdateTime=2026-09-29 15:52:51`；启动 `NRXRActivity`，用户确认程序已运行 | 本次设备联调记录；原始安装/启动输出可继续归档 |
| 真机 HTTP/2 和 `StreamSession` | 15:53:33，真实 `beam-pro` 从 `192.168.31.46` 连接 `192.168.31.64:50051`；上下文及控制 ACK 接受，心跳正常 | 见下方真实连接记录；这不包含最终资产确认和设备显示验收 |
| 真机 `StreamAssets` 与最终确认 | 旧版本全量确认通过；新版本复用缓存后再次 `confirmed`，本次 sent=757,712、confirmed=total=305,701,088 字节 | 旧版本见[设备传输记录](../Logs/Http2/device-transfer-watch.jsonl)；新版本见下方 16:33 现场记录；不是视觉/配准验收 |
| 真机关闭与再次连接 | 15:58:32 为用户确认的手动关闭；新版本 16:29 为用户报告的自动退出，原因待查；16:33:46 再启动连接正常 | 两次事件分开记录；按用户要求暂搁置自动退出排查，未标为已修复 |
| 牙模可见与 CT 切片 | 用户目视确认牙齿 STL 和当前中层 CT 切片在视野内；16:50:51 中层切片控制获真机 `slice state applied` ACK | 此次 39.6 mm 切片可见已确认；完整交互与空间对应关系仍待验证，见下方现场记录 |

### 15:52 安装批次 APK

- 原输出路径：`Builds/First XR.apk`，该批次大小 **242,005,714 字节**；同路径现已更新为下述 16:21 构建版本。
- SHA-256：`e507cacaf960d6456a65eed0ecf587be275f89edc27585aa6f252a7c6b8e8b5f`。
- `apksigner` 验证：APK Signature Scheme v2 通过，1 个签名者。
- APK 内 ARM64 库：`lib/arm64-v8a/libCysharp.Net.Http.YetAnotherHttpHandler.Native.so`，**3,245,032 字节**。

上述大小、SHA-256 与 ZIP 内原生库条目已在当次回填历史记录时读取 APK 核对；签名结果来自[15:52 批次构建摘要](../Logs/Http2/android-build-summary.txt)。

### 16:21 构建、16:28 安装版本

- 构建完成：`2026-09-29 16:21:03`；文件：[Builds/First XR.apk](../Builds/First%20XR.apk)。
- 大小：**242,005,851 字节**。
- SHA-256：`82f1a307dc5feb162a7655b826ee863c108c8220c3814a5e923a244e2fd7de42`。
- 签名验证：APK Signature Scheme v2 通过，1 个签名者。
- ARM64 原生库：`lib/arm64-v8a/libCysharp.Net.Http.YetAnotherHttpHandler.Native.so`，**3,245,032 字节**。
- `16:28:36`：`adb install -r` 返回 `Success`。

该批次 APK 大小、SHA-256 和 ZIP 内原生库条目已在此次回填时直接读取核对；构建时间、签名与安装结果据本次实际联调记录填写。它包含续传补丁，但当时的自动退出原因尚未解决，不据此宣称稳定性验收通过。

### Beam Pro 真实连接记录

以下时间为 2026-09-29、Asia/Shanghai：

1. `15:52:51`：新 APK 安装后的 `lastUpdateTime`，随后启动 `NRXRActivity`。
2. `15:53:33`：服务器收到真实设备 `beam-pro` 的会话连接，来源 `192.168.31.46`，服务器为 `192.168.31.64:50051`。
3. 设备返回 `context applied`、`tolerance applied`、`slice waiting for CT`、`layout applied`、`observation applied`，均为接受；心跳正常。`slice waiting for CT` 表示切片控制等待 CT，不能解读为 CT 已接收、建体或显示完成。
4. 约 `15:53:56`：导航模拟器启动正常场景、`30 Hz`。截至该条记录，文件仍在传输，没有记录最终 CompleteAck，也没有把模拟器启动当作设备 HUD 显示通过。
5. `15:58:32`：用户明确表示手动关闭了应用。此事件不能记为已证实的应用崩溃。
6. `16:03:23`：设备重新连接，用户确认应用已正常显示。该确认不替代资产最终 CompleteAck，也不代表随后尚未安装的连接指示修复已经在真机验证。
7. `16:08:54`：[设备传输记录](../Logs/Http2/device-transfer-watch.jsonl) 首次记录 `transfer.status=confirmed`，消息为“客户端已校验并提交全部资产”；`sentBytes=confirmedBytes=totalBytes=305701088`。这与此前仅为 `sent` 的“发送完成，等待客户端校验/建体确认”明确区分。
8. 约 `16:10:04`：现场再次核实 `/api/state` 保持上述最终确认；设备自 `16:03:23` 持续使用同一 peer `192.168.31.46:47982`，心跳正常。adb 查询应用 PID 为 `23716`，应用数据中有 **534 个 `.dcm` 文件**；用户确认实际应用正常显示。
9. 新版本首次启动 PID 为 `24687`。`16:29:05`，ActivityManager 记录 `wm_finish_activity`、原因 `app-request`；`16:29:09` 记录后台进程 `SIGKILL`，本次检查未见原生崩溃记录。用户明确表示并非手动关闭、是程序自行退出。**这些日志不足以确定退出根因；不能把 `app-request` 等同于用户主动操作，自动退出仍待跟踪。**
10. 用户再次启动后，`16:33:46` 设备以 peer `192.168.31.46:48536` 连接，PID 为 `25045`，前台 `topResumed` 为 `UnityPlayerActivity`，心跳持续至至少 `16:34:50`。
11. 此次新版本缓存复用后再次返回 `confirmed`：`sentBytes=757712`，`confirmedBytes=totalBytes=305701088`。534 个已完成 CT 文件避免全量重传，客户端最终确认通过。这是续传补丁的真实资源复用证据，不是 CT/STL 视觉显示或配准验收。

### CT 现场切片诊断

实际资源 CT 首层 `Z=0.15 mm` 与第三层 `Z=0.45 mm` 都是全空气（`HU=-1000`），中层有影像内容。当时规划入口为 `Z=0.45 mm`、切片偏移为 `0`，控制状态覆盖客户端建体时的中层选择，导致眼镜选择到空气层并显示全黑。

`16:50:50`，控制台同步切片到 `offset=39.6 mm`，平面 `Z=40.05 mm`、`control_version=3`；`16:50:51` 真机接受并返回 `slice state applied`。见[控制台状态截图](../Logs/Http2/ct-middle-slice-applied.jpg)。随后用户反馈“CT可以看到了”，确认此次中层灰度 CT 已在眼镜内显示；用户另已目视确认牙齿 STL 在视野内。这是用户视觉确认，与控制 ACK、截图分别记录，不代表所有切片、CT/STL 空间对应关系或导航叠加精度已完成验证。

导航模拟器已把导入/切换数据集的默认观察平面持久修复为 CT 几何中层，保持规划入口不变并正确应用患者坐标变换；新增三项回归，本轮 `npm test` 为 **50/50 通过**。当前运行服务未重启，活跃会话仍使用手动 `39.6 mm`，不把源码修复视为已在该服务重新加载。细节与回归位置见 [模拟器测试记录](../../xreal%20server/docs/测试记录.md)。

新版本的安装、实际连接、缓存复用、最终确认及此次中层 CT 切片可见已得到验证；**自动退出调查按用户要求暂搁置且未解决，补丁后 Unity 全套 110 项仍未运行**。牙模与 CT 可见的用户确认不扩大为空间对应关系或临床有效性通过。
