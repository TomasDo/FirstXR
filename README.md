# First XR

基于 **Unity 6.0 LTS（6000.0.83f1）** 与 **XREAL XR Plugin 3.1.0** 的口腔种植导航客户端，主场景为 **HelloMR**。医生低头观察术野时，XREAL 视野下方同时显示 CT 横断切片、固定颊舌/近远中方向的圆形靶标、横向与角度偏差方向、量化数值和竖向深度尺。Beam Pro 显示相同导航状态及真实 XR 左眼画面，导航软件可按需打开医生 RGB 视角。

当前实现对应 `dental_model_transfer.proto` v2：导航/控制、DICOM/STL 资产、媒体分别走独立通道。v1 服务仍可连接，但缺少方向、阈值或 CT 时界面会明确显示能力缺失，不会从旧标量猜测完整导航。协议和联调细节见 [`Docs/DentalNavigationV2Interface.md`](Docs/DentalNavigationV2Interface.md)。

## 环境要求

| 项目 | 说明 |
|------|------|
| Unity | 6.0 LTS，固定 `6000.0.83f1`；Apple Silicon Mac 使用 ARM64 编辑器 |
| Android 工具 | Unity Hub 安装 Android Build Support、SDK & NDK Tools、OpenJDK；使用编辑器自带的 JDK 17、NDK r27c、SDK Build Tools 36.0.0 |
| 平台 | Android（`minSdk 29`） |
| 硬件 | XREAL 眼镜 + Beam Pro（或支持 adb 的 Android 设备） |
| 硬件 | XREAL Eye（离线 OK 手势与按需 RGB 回传需要） |
| 可选 | 手术机器人 gRPC 服务端（默认 `192.168.31.166:50051`） |

主要依赖：`com.xreal.xr`、`com.unity.xr.hands`、`com.unity.xr.interaction.toolkit`、`com.unity.xr.arfoundation`、固定版本 `com.google.mediapipe:tasks-vision:1.0.0`，以及 `Assets/Plugins/Grpc/` 下的 gRPC / Protobuf 运行时。MediaPipe 模型随 APK 打包，运行时不访问网络。

XR 包保持与已有场景和 Samples 匹配：AR Foundation `6.0.8`、XR Interaction Toolkit `2.6.5`、XR Hands `1.5.1`。不要只升级 XR Interaction Toolkit 或 XR Hands 而不迁移对应 Samples。Apple Silicon Mac 需安装 Rosetta，以运行当前编辑器工具链中的 Intel 组件。在 **Unity → Settings → External Tools** 中启用 Unity 自带的 JDK、SDK、NDK 和 Gradle。

本机升级内容、验证结果及备份位置见 [`Docs/Unity6Migration.md`](Docs/Unity6Migration.md)。

Android 权限（`Assets/Plugins/Android/CameraPermission.androidlib/AndroidManifest.xml`）：

- `CAMERA`：RGB 相机
- `INTERNET` / `ACCESS_NETWORK_STATE`：手术机器人 gRPC

## 快速开始

1. 用 Unity 打开本仓库。
2. 确认 **File → Build Profiles** 的 Android 平台及场景列表中已启用场景：

   `Assets/Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR/HelloMR.unity`
3. 如需连接手术机器人，在 `DentalRobotConnectionDefaults.cs` 中修改默认 IP / 端口，或在 Beam Pro **连接**页填写后点 **连接**。
4. 连接 Beam Pro，开启 USB 调试，在 Unity 中选择 **Run Device**。
5. 构建并运行：
   - **File → Build And Run**（若 Unity 启动报 NullReferenceException，APK 通常已成功生成，见下方「构建与部署」）
   - 或菜单 **XREAL → Build → Android APK (Build Only, No Unity Launch)**，再 **XREAL → Launch App On Android Device**

没有导航软件时，可先运行确定性 v2 模拟服务：

```bash
cd Tools/navigation-simulator
npm install
npm start -- --port 50051 --dicom-dir /absolute/path/to/explicit-vr-dicom
```

模拟器不会替代 Beam Pro + XREAL + 牙模验收；它用于协议、方向、阈值、失效和切片控制回放。

## 功能概览

### 手术导航 HUD（产品默认）

眼镜端由 `DentalHudController` 在主相机前 **1.80m** 挂一块 World Space Canvas（1000×560mm，`scale=0.001`），中央术野留空，内容贴在视场下沿：

| 块 | 内容 |
|---|---|
| 状态 | 病例/牙位/钻针/步骤、连接状态和导航数据年龄 |
| CT 靶标 | CT 切片、固定“上颊/下舌/左近中/右远中”、参考圆和规划中心 |
| 位置偏差 | 实心点显示二维方向；颜色只反映位置阈值；数字显示总量与解剖方向 |
| 角度偏差 | 外环颜色只反映角度阈值；空心三角显示倾斜方向，零角度隐藏 |
| 深度尺 | 入点为零、当前指针、目标线、超深区；剩余深度最突出 |
| 告警 | 每个异常通道独立报告，正常项不能掩盖异常项 |

`DentalNavigationState` 是状态总线：客户端只消费当前上下文的最新序号帧。≤200ms 为实时，200–500ms 撤下有效颜色并提示延迟，>500ms 或明确断流隐藏动态标记与数值。静态 CT 可保留并显示状态。

颜色阈值、边界规则与滞回参数全部由导航端按上下文版本下发。阈值缺失时显示“阈值未同步”，不会套用客户端临床默认值。协议距离固定为 mm、角度固定为 °；数值显示一位小数，判定使用原始精度。

三维模型是独立头锁定观察窗口，种植时默认隐藏；导航端可独立移动、显隐并恢复默认位置。它是观察窗口，不表示配准到真牙上的叠加。

Beam Pro 默认打开**监看**页，可切换 HUD 和三维模型显隐；HelloMR 勾选 **Engineer Mode** 后底部增加**调试**页。

### 追踪模式（Tracking）

通过眼镜端 UI 或场景内 Toggle 切换：

- **0 DoF**
- **0 DoF Stable**
- **3 DoF**
- **6 DoF**（默认，适合空间锚定与物体放置）

状态栏显示当前 `Track` 与 `Input` 模式。

### 输入模式（Input）

| 模式 | 行为 |
|------|------|
| **Controller（默认）** | 交互由虚拟 Controller 驱动 |
| **Hands** | 完整手势输入（手势可直接控制 UI/物体） |

启动默认：**Controller**。

产品模式 Beam Pro 不显示输入切换。勾选 HelloMR **Engineer Mode** 且启用对应 Inspector 配置后，可在**调试**页切换 Controller / Hand；眼镜端 Canvas 也有 **Controller / Hand** 按钮。

### RGB 相机浮动窗口

`RGBCameraFloatingWindow` 可在 MR 空间显示 **XREAL Eye** RGB 实时画面。**产品默认隐藏窗口且不自动采集。** Engineer Mode 下可 Show / Hide RGB Window。

- 自动请求 Android `CAMERA` 权限
- 等待 Eye 插入（PLUGIN 状态）后重试启动采集
- 使用 YUV → RGB Shader 渲染到世界空间 Quad
- 调试状态写入 Beam Pro 日志（来源名：`RGB 相机`；仅 Engineer Mode 的**调试**页显示）

### 离线 OK 手势切片

`RgbHandGestureRecognizer` 使用随 APK 打包的 MediaPipe Hand Landmarker，从共享的 XREAL Eye RGB 帧提取 21 个手部关键点，图像与关键点不上传。手势采集与 RGB 回传共用一个相机服务；关闭 RGB 回传不会关闭手势采集。

| 手势 | 中文 | 判定 |
|------|------|------|
| `Ok` | OK | 拇指与食指闭合，至少两根其余手指伸展 |
| `None` | 无 | 未检测到有效 OK 手势或置信度不足 |

实现要点：

- YUV 在 GPU 转为 256×144 RGBA，MediaPipe 在 Android 单工作线程运行；繁忙时丢弃旧帧，不堆积推理任务。
- 稳定保持 OK 300ms 进入“切片调整中”；保持 OK 上移减层、下移增层。
- 位移有死区与步进限速；明显头动会冻结并重建基准。
- 松开、遮挡、失帧超过 180ms或导航端接管会立即退出，重新做 OK 才能继续。
- 术用手套、口腔灯和遮挡下的识别率必须在真机验收中单独记录。
- 其他脚本可订阅切层事件：

```csharp
RgbSliceGestureController.AnySliceStepRequested += step =>
{
    Debug.Log($"slice delta={step.Step}, control={step.ControlVersion}");
};
```

产品启动会启用切片手势。若相机、模型或 MediaPipe 初始化失败，状态显示明确原因，切片不会响应。

### 左眼预览（Beam Pro 屏幕）

`LeftEyeDisplayWindow` 将 **XR 左眼实际渲染输出** 镜像到 Beam Pro 屏幕。`XrRgbRtpStreamer` 按导航端指令把左眼画面和可选 RGB 合成一路 1280×720、15fps、无音频的 RTP 视频；导航端可将两块区域分窗显示。编码不可用时会回报不可用，不用替代画面冒充 XR 输出。

### 参考物体

`ReferenceCubeSpawner` 在头显前方生成空间参考内容：

- 可选 **参考立方体**（RGB 坐标轴、六面图案、自动旋转；场景默认关闭）
- 可选 **Check Plane**（从 StreamingAssets 加载 `check_plane.STL`；**场景默认关闭**）
- Engineer Mode 下可通过 Beam Pro **调试**页的 **Move X/Y/Z ±** 平移；有 Check Plane 时还可调透明度与 RGB 颜色通道

### 手术机器人模型（gRPC）

按 `dental_model_transfer.proto` 从导航软件双向流式接收上下文、导航帧、控制和资产：

| 组件 | 作用 |
|------|------|
| `DentalRobotConnectionDefaults` | 共享默认连接参数（Host / Port / DeviceId / DatasetId） |
| `DentalRobotGrpcClient` | gRPC 客户端；启动可自动搜索，也可由 UI 触发 `SearchEndpoint` |
| `DentalNavigationState` | 上下文/帧/阈值/切片/布局/观察控制及版本过滤 |
| `DentalNavigationBand` | 阈值、滞回、告警文案 |
| `DentalHudController` | 眼镜头锁定 HUD |
| `DentalRobotBeamProDisplay` | 分页界面的连接操作与导航状态数据源 |
| `DentalRobotModelRenderer` | 解析 STL，头锁定小脑图，按 `drill_from_teeth` 放置 drill |
| `DentalCtVolumeService` | DICOM 分块续传、SHA 校验、解析、体数据和按需切片缓存 |
| `DentalCtSliceCoordinator` | 规划轴切片、解剖方向、双端控制应用 |
| `DentalStlMeshUtility` | 共享二进制 / ASCII STL 网格解析 |

默认连接：

```
Host: 192.168.31.166
Port: 50051
device_id: beam-pro
dataset_id: default
```

运行时行为：

1. `StreamSession` 长连接接收上下文、阈值和实时帧；旧上下文或旧序号帧会被拒绝。
2. `StreamAssets` 独立接收完整 DICOM/STL。DICOM 块持久化成功后才 ACK，支持覆盖区间续传和 SHA-256 校验；完成资产传输不会停止实时导航。
3. CT 默认显示垂直于规划轴、经过规划入点的物理切片；导航端下发的切片与控制版本优先。
4. `StreamSession` 还处理布局和观察控制；切片手势会携带当前控制版本，过期指令由导航端拒绝。
5. v1 `StreamDentalModel` 作为兼容入口保留，字段号未复用。

### 其他 UI

- **Show / Hide Glasses UI**：Engineer Mode 下切换眼镜端控制 Canvas（追踪 Toggle 等）
- **Vibrate**：Controller 震动测试（眼镜端按钮）

## Beam Pro 屏幕布局与控件

`BeamProPagedController` 是 Beam Pro 唯一绘制入口。旧导航仪表、左眼预览、日志和 HelloMR 按钮组件继续提供状态与操作，但分页界面激活时不再各自绘制，避免坐标重叠。导航、XR 捕获、手势和媒体服务不依赖当前页签，切页不会停止后台流程。

| 页面 | 内容 |
|---|---|
| **监看**（默认） | 固定连接状态、16:9 XR 左眼预览、剩余深度/位置偏移/角度偏差/综合状态四卡、HUD 与三维模型显隐、病例/CT/阈值/资产状态 |
| **连接** | 当前连接状态、独立 IP 与端口输入、连接/重新连接按钮、操作结果；输入草稿在提交前不会应用 |
| **调试** | 显示、相机、手势、导航对象与检查平面控件，以及统一日志；仅 Engineer Mode 可见，并按 Inspector 开关收起未配置的控件 |

布局以 **540 逻辑单位短边**统一缩放：外边距 20、模块间距 12、触控高度不小于 56。顶部状态栏和底部页签固定，正文独立滚动；支持安全区域、软键盘避让、长文本测量和滚动条预留。竖屏以预览优先，横屏将预览与指标卡并排。

预览只显示 `LeftEyeDisplayWindow.TryGetLiveXrFrame` 返回的真实 XR 左眼帧；帧失效后立即显示不可用状态，不继续展示旧画面或主相机替代画面。

编辑器 Play Mode 可通过 **Tools → Beam Pro Preview** 打开任一页面或一次生成三页 1080×2400 截图。当前截图：

- [监看页](Docs/BeamProScreenshots/monitor-editor.png)
- [连接页](Docs/BeamProScreenshots/connection-editor.png)
- [调试页](Docs/BeamProScreenshots/debug-editor.png)

编辑器预览复用正式 IMGUI 绘制代码。真机触摸、软键盘、XREAL 双屏和真实 XR 帧仍需在 Beam Pro 上复核。

### 与眼镜端 UI 的区别

| 位置 | 内容 |
|------|------|
| **Beam Pro 手机屏** | 分页监看、连接与工程调试界面 |
| **XREAL 眼镜 HUD** | 头锁定导航 Canvas（A–F）；HelloMR DoF Canvas 默认隐藏 |
| **MR 世界空间** | 头锁定 teeth/drill 小脑图；RGB Quad 与 Check Plane 默认不生成/隐藏 |

## 构建与部署

项目包含 Editor 辅助脚本，菜单路径 **XREAL**：

| 菜单 | 说明 |
|------|------|
| **Launch App On Android Device** | adb 安装 APK 并启动 `NRXRActivity` |
| **Build → Android APK (Build Only, No Unity Launch)** | 仅打 APK，避免 Unity 自带启动失败 |
| **Build → Enable / Disable Debug Build (Android)** | Development Build + Script Debugging |

## 项目结构（核心）

```
Assets/
├── Editor/
│   ├── XREALAndroidLaunchHelper.cs      # adb 部署与启动
│   ├── ProjectDebugBuildSettings.cs     # Debug 构建默认选项
│   └── XREALLicenseSetup.cs             # License 文件引导（可选）
├── Plugins/
│   ├── Android/CameraPermission.androidlib/  # CAMERA / 网络权限
│   ├── Android/MediaPipeHandLandmarker.androidlib/ # Android MediaPipe bridge + 离线模型
│   └── Grpc/                            # gRPC / Protobuf 运行时 DLL
├── StreamingAssets/
│   ├── check_plane.STL                  # Check Plane 模型
│   └── nr_plugins.json
└── Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR/
    ├── HelloMR.unity                    # 主场景
    ├── HelloMR.cs                       # 追踪/输入/UI 总控 + 分页操作接口
    ├── BeamProPagedController.cs        # Beam Pro 监看/连接/调试唯一绘制入口
    ├── BeamProPageLayout.cs             # 纯函数分页布局、安全区与键盘避让
    ├── BeamProOverlayLayout.cs          # 旧版 overlay 布局（分页激活时不绘制）
    ├── BeamProUnifiedLogWindow.cs       # 统一日志数据源
    ├── RgbCameraFrameService.cs         # RGB 相机唯一所有者与多消费者分发
    ├── RGBCameraFloatingWindow.cs       # RGB 世界空间预览
    ├── RgbHandGesture.cs                # 手势枚举与 Observation
    ├── RgbHandGestureAnalyzer.cs        # 21 点关键点 OK 规则
    ├── MediaPipeAndroidHandLandmarkProvider.cs # Android 离线关键点适配器
    ├── RgbHandGestureRecognizer.cs      # Eye RGB 关键点识别 + 事件/日志
    ├── RgbSliceGestureStateMachine.cs   # 300ms OK、死区、限速和失效处理
    ├── XrRgbRtpStreamer.cs              # XR 左眼 + 可选 RGB 合成 RTP
    ├── LeftEyeDisplayWindow.cs          # XR 左眼帧采集与分页预览数据源
    ├── ReferenceCubeSpawner.cs          # 参考立方体 / Check Plane
    ├── DentalRobotConnectionDefaults.cs # 默认 gRPC 连接参数
    ├── DentalRobotGrpcClient.cs         # 手术机器人 gRPC 客户端
    ├── DentalNavigationState.cs         # 导航总线
    ├── DentalNavigationBand.cs          # 阈值 / 滞回 / 告警
    ├── DentalDisplayLayoutController.cs # 双窗口位置、显隐和设备持久化
    ├── DentalHudController.cs           # 眼镜头锁定 HUD
    ├── DentalRobotModelRenderer.cs      # teeth/drill 小脑图
    ├── DentalRobotBeamProDisplay.cs     # 导航状态数据源 + IP/端口连接
    ├── DentalStlMeshUtility.cs          # 共享 STL 网格解析
    ├── Dicom/                           # DICOM 传输、解码、体数据和切片
    └── GrpcGenerated/                   # dental_model_transfer 生成代码
```

根目录另有 `dental_model_transfer.proto`；`Tools/navigation-simulator` 是可执行的 v2 gRPC 模拟服务与冒烟测试。

## 常见问题

**Build And Run 报 NullReferenceException**  
多为 Unity Editor 启动 APK 失败，APK 可能已生成。使用 **XREAL → Launch App On Android Device**，或手动 `adb install -r`。

**看不到双手模型**  
需在真机运行；手部 mesh 仅在追踪成功（`isTracked`）后显示。可尝试切换到 **Hands** 输入对比；确认设备支持手部追踪且手在 RGB 相机视野内。

**RGB 相机无画面**  
产品默认不采集。Engineer Mode 下确认 XREAL Eye 已连接、Camera 权限已授予，查看日志 **`[RGB 相机]`** 的 plug 状态与 capture 日志。

**RGB 手势一直显示「无」**  
确认 Eye RGB 已出画面，并检查 `[手势识别]` 是否显示 `MediaPipe Hand Landmarker 1.0.0` 为 running。手完整进入相机视野并稳定做 OK；编辑器没有 Android MediaPipe runtime，会明确显示不可用。真机仍需在实际手套、口腔灯、器械和遮挡环境下验证。

**手术机器人 gRPC 连不上**  
确认手机与机器人服务端在同一局域网；在 Beam Pro **连接**页核对 IP/端口后点 **连接**。产品模式看顶部状态；Engineer Mode 可在**调试**页查看 **`[手术机器人]`** 日志。默认地址见 `DentalRobotConnectionDefaults.cs`。

**眼镜 HUD 数字大了或小了约 1000 倍**  
v2 协议只接受 mm 和 °，请修正导航端发送单位；客户端不会用 Inspector 比例猜测单位。v1 兼容数据仍沿用旧服务约定，但不会补造方向和阈值能力。

**Editor 中 gRPC 脚本报找不到程序集**  
确认 `Assets/Plugins/Grpc/` 下各 DLL 的 PluginImporter 已对 **Editor** 启用。

## 许可证

本项目基于 XREAL 与 Unity 官方 Sample 扩展开发；第三方 Sample 与 Plugin 遵循各自许可证。
