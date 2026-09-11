# First XR

基于 **XREAL XR Plugin 3.1.0** 的 Unity MR 项目，主场景为 **HelloMR**。在 XREAL 眼镜（如 One Pro）与 **Beam Pro** 联机运行时，作为手术机器人的导航 HUD：医生低头看患者时，眼镜下沿显示剩余深度 / 侧偏 / 轴向偏差；Beam Pro 作为助手台镜像同一组状态。gRPC 同时接收 teeth / drill STL，以头锁定小脑图显示。RGB 预览、手势识别与参考物体默认关闭，可在 Inspector 打开 **Engineer Mode** 恢复。

## 环境要求

| 项目 | 说明 |
|------|------|
| Unity | 2022.3 LTS（与 XREAL Plugin 要求一致） |
| 平台 | Android（`minSdk 29`） |
| 硬件 | XREAL 眼镜 + Beam Pro（或支持 adb 的 Android 设备） |
| 可选 | XREAL Eye（RGB 相机模块，用于实时 RGB 预览与 RGB 手势识别） |
| 可选 | 手术机器人 gRPC 服务端（默认 `192.168.31.166:50051`） |

主要依赖：`com.xreal.xr`、`com.unity.xr.hands`、`com.unity.xr.interaction.toolkit`、`com.unity.xr.arfoundation`，以及 `Assets/Plugins/Grpc/` 下的 gRPC / Protobuf 运行时。

Android 权限（`Assets/Plugins/Android/CameraPermission.androidlib/AndroidManifest.xml`）：

- `CAMERA`：RGB 相机
- `INTERNET` / `ACCESS_NETWORK_STATE`：手术机器人 gRPC

## 快速开始

1. 用 Unity 打开本仓库。
2. 确认 **Build Settings** 中已启用场景：  
   `Assets/Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR/HelloMR.unity`
3. 如需连接手术机器人，在 `DentalRobotConnectionDefaults.cs` 中修改默认 IP / 端口，或在真机 Beam Pro 顶部输入后点 **开始搜索**。
4. 连接 Beam Pro，开启 USB 调试，在 Unity 中选择 **Run Device**。
5. 构建并运行：
   - **File → Build And Run**（若 Unity 启动报 NullReferenceException，APK 通常已成功生成，见下方「构建与部署」）
   - 或菜单 **XREAL → Build → Android APK (Build Only, No Unity Launch)**，再 **XREAL → Launch App On Android Device**

## 功能概览

### 手术导航 HUD（产品默认）

眼镜端由 `DentalHudController` 在主相机前 **1.80m** 挂一块 World Space Canvas（1000×560mm，`scale=0.001`），中央术野留空，内容贴在视场下沿：

| 块 | 内容 |
|---|---|
| A 状态胶囊 | 连接点、dataset、数据年龄 |
| B 告警 | 仅在未连接 / 数据中断 / 过目标 / 超差时出现 |
| C 外框 | 对齐头锁定小脑图的镂空框 |
| D 剩余深度 | `ModelMetadata.distance`（默认按米→mm） |
| E 通道靶心 | 侧偏点只沿竖直轴移动（协议无方向） |
| F 轴向偏差 | `ModelMetadata.angle`（默认按度） |

`DentalNavigationState` 是总线：gRPC metadata 写入后，HUD 每帧 `Capture` + `DentalNavigationBand` 评估阈值。过期规则：≤0.20s 正常；0.20–0.50s 显示年龄；0.50–1.00s 变灰并告警；>1.00s 或断流显示 `—`，禁止残留绿色。

侧偏默认绿 ≤0.50mm、红 >1.00mm；角度绿 ≤2°、红 >5°；深度过目标为红，≤1mm 琥珀，更大为中性（不刷绿）。单位可在 `DentalNavigationState` Inspector 调整；真机第一帧数字若差约 1000 倍，把 `Distance To Millimeters` 改为 `1`。

头锁定小脑图（`DentalRobotModelRenderer`）挂在相机本地 `(0.320, -0.024, 1.80)`，外接球约 0.11m，牙半透明、针青色，按 `drill_from_teeth` 摆位。这是导航小窗，**不是**配准到真牙上的叠加。

Beam Pro 右侧默认两键：**Show/Hide HUD**、**Show/Hide Nav Widget**。HelloMR 勾选 **Engineer Mode** 后恢复调试按钮与统一日志。

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

产品模式 Beam Pro 不显示输入切换。勾选 HelloMR **Engineer Mode** 后，右侧可切换 Controller / Hand；眼镜端 Canvas 也有 **Controller / Hand** 按钮。

### RGB 相机浮动窗口

`RGBCameraFloatingWindow` 可在 MR 空间显示 **XREAL Eye** RGB 实时画面。**产品默认隐藏窗口且不自动采集。** Engineer Mode 下可 Show / Hide RGB Window。

- 自动请求 Android `CAMERA` 权限
- 等待 Eye 插入（PLUGIN 状态）后重试启动采集
- 使用 YUV → RGB Shader 渲染到世界空间 Quad
- 调试状态写入 Beam Pro 日志（来源名：`RGB 相机`；仅 Engineer Mode 显示日志窗）

### RGB 手势识别（张开 / 握拳 / 捏合）

`RgbHandGestureRecognizer` 从 **同一路 XREAL Eye RGB 画面**（不另开摄像头）识别 3 种手势。**产品默认关闭。**

| 手势 | 中文 | 判定 |
|------|------|------|
| `OpenPalm` | 张开 | 多数手指伸直（凸缺陷 / 指尖峰较多） |
| `Fist` | 握拳 | 手指收拢，轮廓较圆、较紧致 |
| `Pinch` | 捏合 | 拇指与食指靠近（两峰接近，或轮廓内有孔） |
| `None` | 无 | 未稳定检测到手 |

实现要点：

- 复用 `RGBCameraFloatingWindow` 已采集的 YUV 纹理，Blit 到 160×90 后 `AsyncGPUReadback`
- CPU 上做肤色分割、最大连通域、轮廓与凸缺陷分类（未引入 MediaPipe / Sentis，无需下载模型）
- 连续多帧确认（约 0.3～0.5s）后才切换结果，避免逐帧闪烁
- 状态写入统一日志（来源名：`手势识别`）
- 其他脚本可订阅变化事件，例如：

```csharp
RgbHandGestureRecognizer.Instance.GestureChanged += gesture =>
{
    Debug.Log(RgbHandGestureNames.ToChinese(gesture));
};
```

HelloMR 启动时默认关闭；Engineer Mode 下 Beam Pro 右侧 **Enable / Disable Gesture** 可开关。请把手伸到 Eye 前方、保证光照充足，掌心大致朝向相机。

### 左眼预览（Beam Pro 屏幕）

`LeftEyeDisplayWindow` 将 **XR 左眼渲染输出** 镜像到 Beam Pro 手机屏幕底部预览区，便于在手机上查看眼镜端 One Pro 视角。

### 参考物体

`ReferenceCubeSpawner` 在头显前方生成空间参考内容：

- 可选 **参考立方体**（RGB 坐标轴、六面图案、自动旋转；场景默认关闭）
- 可选 **Check Plane**（从 StreamingAssets 加载 `check_plane.STL`；**场景默认关闭**）
- Engineer Mode 下可通过 Beam Pro **Move X/Y/Z ±** 平移；有 Check Plane 时还可调透明度与 RGB 颜色通道

### 手术机器人模型（gRPC）

按 `dental_model_transfer.proto` 从手术机器人服务端双向流式接收模型与导航数据：

| 组件 | 作用 |
|------|------|
| `DentalRobotConnectionDefaults` | 共享默认连接参数（Host / Port / DeviceId / DatasetId） |
| `DentalRobotGrpcClient` | gRPC 客户端；启动可自动搜索，也可由 UI 触发 `SearchEndpoint` |
| `DentalNavigationState` | 导航总线：三轴、矩阵、连接状态、新鲜度 |
| `DentalNavigationBand` | 阈值、滞回、告警文案 |
| `DentalHudController` | 眼镜头锁定 HUD |
| `DentalRobotBeamProDisplay` | 助手台：IP/端口搜索 + 导航镜像 |
| `DentalRobotModelRenderer` | 解析 STL，头锁定小脑图，按 `drill_from_teeth` 放置 drill |
| `DentalStlMeshUtility` | 共享二进制 / ASCII STL 网格解析 |

默认连接：

```
Host: 192.168.31.166
Port: 50051
device_id: beam-pro
dataset_id: default
```

运行时行为：

1. 客户端发送 `ModelRequest`，接收 `ModelMetadata`、`StlChunk`、`TransferEnd`
2. `distance` / `lateral_distance` / `angle` 进入 `DentalNavigationState`，驱动眼镜 HUD 与 Beam Pro 仪表
3. teeth / drill STL 导入后作为头锁定小脑图；drill 位姿由 `drill_from_teeth`（行主序 4×4）决定
4. 显示颜色：teeth **灰白半透明**，drill **青色不透明**
5. 连接事件写入日志（来源名：`手术机器人`）；metadata **不会**逐帧刷日志

### 其他 UI

- **Show / Hide Glasses UI**：Engineer Mode 下切换眼镜端控制 Canvas（追踪 Toggle 等）
- **Vibrate**：Controller 震动测试（眼镜端按钮）

## Beam Pro 屏幕布局与控件

以下 overlay **仅在 Android 真机（Beam Pro）运行时显示**，由 `OnGUI` 绘制。区域划分由 `BeamProOverlayLayout` 统一计算。

### 整体分区

Beam Pro 实体屏为 **1080×2400（20:9 竖屏）**。产品模式左侧是导航仪表，右侧两枚产品按钮；勾选 HelloMR **Engineer Mode** 后主区改回日志并追加调试按钮。

```
                    Beam Pro  ·  1080 × 2400  ·  20:9 竖屏
┌────────────────────────────────────────┬────────────────┐
│ 16px                                   │ 16px           │
│  ┌──────────────────────────────────┐  │ ┌────────────┐ │
│  │ 手术导航                         │  │ │ Show/Hide  │ │
│  │ [IP] [........] [端口] [..] [搜索]│  │ │ HUD        │ │
│  │                                  │  │ ├────────────┤ │
│  │ 已连接  dataset  12ms            │  │ │ Show/Hide  │ │
│  │                                  │  │ │ Nav Widget │ │
│  │  剩余深度     侧偏               │  │ └────────────┘ │
│  │  2.3 mm       0.4 mm             │  │                │
│  │                                  │  │  Engineer Mode │
│  │  轴向偏差     综合               │  │  时追加调试按钮 │
│  │  1.2°         在容差             │  │  与日志窗口    │
│  │          ≈ 76% 屏高              │  │  ≈24% 屏宽     │
│  └──────────────────────────────────┘  │                │
│                 12px                   │                │
│  ┌──────────────────────────────────┐  │                │
│  │ Left Eye (One Pro view)          │  │                │
│  │      [XR 左眼预览画面]           │  │                │
│  │         ≈ 24% 屏高               │  │                │
│  └──────────────────────────────────┘  │                │
│ 16px                                   │           16px │
└────────────────────────────────────────┴────────────────┘
```

说明：

- 右侧按钮列从顶部向下堆叠，行高随行数自适应；底部预览**仅占左侧栏**
- 日志区标题下方为手术机器人 **IP / 端口 / 开始搜索** 控件（`GetDentalEndpointControlsRect`）

布局常量（`BeamProOverlayLayout`）：

| 常量 | 值 | 含义 |
|------|----|------|
| `Margin` | 16px | 屏幕外边距 |
| `ColumnGap` | 12px | 左侧内容与右侧按钮列间距 |
| `RightColumnWidth` | 最大 280px | 右侧按钮列宽度（约屏宽 24%，夹在 120～280） |
| `BottomPreviewMaxFraction` | 0.24 | 底部左眼预览最大高度占比 |
| `RightColumnMaxHeightFraction` | 0.9 | 右侧按钮列可用高度上限 |
| `MaxButtonRows` | 11 | 布局预留的最大按钮行数 |
| `DentalEndpointControlsHeight` | 42px | 日志区顶部端点输入条高度 |

可在 HelloMR / 各组件 Inspector 中通过 `m_Show*OnBeamPro`、`Show Dental Robot Beam Pro Panel` 等字段开关对应面板。

### 1. 右侧操作按钮列（`HelloMR`）

产品模式固定两行：

| 行 | 控件 | 用途 |
|---|---|---|
| 1 | **Show HUD** / **Hide HUD** | 眼镜导航 HUD 显隐 |
| 2 | **Show Nav Widget** / **Hide Nav Widget** | 小脑图与 C 外框显隐 |

HelloMR Inspector 勾选 **Engineer Mode** 后追加：Controller/Hand、Glasses UI、RGB Window、Gesture、Move X/Y/Z、Check Plane 外观。

### 2. 统一日志窗口（`BeamProUnifiedLogWindow`）

产品模式隐藏。Engineer Mode 下占用主区，来源包括 `RGB 相机`、`手势识别`、`手术机器人`。手术机器人 metadata 每秒最多刷新一次状态摘要，不再逐帧写行。

### 3. 手术机器人端点控件（`DentalRobotBeamProDisplay`）

叠在统一日志区标题下方：

| 控件 | 说明 |
|------|------|
| **IP** | IPv4 地址输入框 |
| **端口** | 1–65535 |
| **开始搜索** | 校验输入后调用 `DentalRobotGrpcClient.SearchEndpoint`，断开当前流并立即重连 |

默认值来自 `DentalRobotConnectionDefaults`；HelloMR 也可在 Inspector 覆盖 Host / Port / DeviceId / DatasetId。

### 4. 左眼预览（`LeftEyeDisplayWindow`）

| 项目 | 说明 |
|------|------|
| **用途** | 在 Beam Pro 上镜像**眼镜 One Pro 左眼** XR 渲染。 |
| **位置** | `GetBottomPreviewRect`：左侧内容区底部，高度约屏高 **12%～24%**（默认上限 24%）。 |
| **交互** | 只读；右下角可显示分辨率等 `DebugInfo`；无帧时显示等待/错误文案。 |
| **Inspector** | `Show On Beam Pro`、`Max Screen Height Fraction`。 |

### 与眼镜端 UI 的区别

| 位置 | 内容 |
|------|------|
| **Beam Pro 手机屏** | 导航仪表（或 Engineer 日志）、手术机器人端点、左眼预览、右侧 HUD/小脑图按钮 |
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
│   └── Grpc/                            # gRPC / Protobuf 运行时 DLL
├── StreamingAssets/
│   ├── check_plane.STL                  # Check Plane 模型
│   └── nr_plugins.json
└── Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR/
    ├── HelloMR.unity                    # 主场景
    ├── HelloMR.cs                       # 追踪/输入/UI 总控 + 右侧按钮列
    ├── BeamProOverlayLayout.cs          # Beam Pro 分区布局
    ├── BeamProUnifiedLogWindow.cs       # 统一日志窗口
    ├── RGBCameraFloatingWindow.cs       # RGB 世界空间预览 + 日志写入
    ├── RgbHandGesture.cs                # 手势枚举与 Observation
    ├── RgbHandGestureAnalyzer.cs        # RGB 帧肤色/轮廓分类
    ├── RgbHandGestureRecognizer.cs      # Eye RGB 手势识别 + 事件/日志
    ├── LeftEyeDisplayWindow.cs          # Beam Pro 底部左眼预览
    ├── ReferenceCubeSpawner.cs          # 参考立方体 / Check Plane
    ├── DentalRobotConnectionDefaults.cs # 默认 gRPC 连接参数
    ├── DentalRobotGrpcClient.cs         # 手术机器人 gRPC 客户端
    ├── DentalNavigationState.cs         # 导航总线
    ├── DentalNavigationBand.cs          # 阈值 / 滞回 / 告警
    ├── DentalHudController.cs           # 眼镜头锁定 HUD
    ├── DentalRobotModelRenderer.cs      # teeth/drill 小脑图
    ├── DentalRobotBeamProDisplay.cs     # 助手台仪表 + IP/端口搜索
    ├── DentalStlMeshUtility.cs          # 共享 STL 网格解析
    └── GrpcGenerated/                   # dental_model_transfer 生成代码
```

根目录另有 `dental_model_transfer.proto`（手术机器人模型传输协议）。

## 常见问题

**Build And Run 报 NullReferenceException**  
多为 Unity Editor 启动 APK 失败，APK 可能已生成。使用 **XREAL → Launch App On Android Device**，或手动 `adb install -r`。

**看不到双手模型**  
需在真机运行；手部 mesh 仅在追踪成功（`isTracked`）后显示。可尝试切换到 **Hands** 输入对比；确认设备支持手部追踪且手在 RGB 相机视野内。

**RGB 相机无画面**  
产品默认不采集。Engineer Mode 下确认 XREAL Eye 已连接、Camera 权限已授予，查看日志 **`[RGB 相机]`** 的 plug 状态与 capture 日志。

**RGB 手势一直显示「无」**  
确认 Eye RGB 已出画面（`[RGB 相机]` 有 first frame）；把手伸到眼镜前方、掌心朝向 Eye，避免逆光/过暗。识别来自 RGB 轮廓而非 XR Hands API，一次主要识别一只手；肤色接近背景、或画面中有其他人脸时可能误检。日志 **`[手势识别]`** 会打印缺陷数/紧致度等调试特征。

**手术机器人 gRPC 连不上**  
确认手机与机器人服务端在同一局域网；在 Beam Pro 顶部核对 IP/端口后点 **开始搜索**。产品模式看仪表「未连接」；Engineer Mode 看日志 **`[手术机器人]`**。默认地址见 `DentalRobotConnectionDefaults.cs`。

**眼镜 HUD 数字大了或小了约 1000 倍**  
在 `Dental Navigation State` 上改 `Distance To Millimeters`（米→mm 用 1000，若服务端已是 mm 则改为 1）。角度若接近 0.01 量级，把 `Angle To Degrees` 改为 `57.2958`。

**Editor 中 gRPC 脚本报找不到程序集**  
确认 `Assets/Plugins/Grpc/` 下各 DLL 的 PluginImporter 已对 **Editor** 启用。

## 许可证

本项目基于 XREAL 与 Unity 官方 Sample 扩展开发；第三方 Sample 与 Plugin 遵循各自许可证。
