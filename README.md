# First XR

基于 **XREAL XR Plugin 3.1.0** 的 Unity MR 示例项目，主场景为 **HelloMR**。在 XREAL 眼镜（如 One Pro）与 **Beam Pro** 手机端联机运行时，提供 6DoF 空间交互、RGB 相机预览、参考物体放置，以及 Controller / 手势的可切换输入模式。

## 环境要求

| 项目 | 说明 |
|------|------|
| Unity | 2022.3 LTS（与 XREAL Plugin 要求一致） |
| 平台 | Android（`minSdk 29`） |
| 硬件 | XREAL 眼镜 + Beam Pro（或支持 adb 的 Android 设备） |
| 可选 | XREAL Eye（RGB 相机模块，用于实时 RGB 预览） |

主要依赖：`com.xreal.xr`、`com.unity.xr.hands`、`com.unity.xr.interaction.toolkit`、`com.unity.xr.arfoundation`。

## 快速开始

1. 用 Unity 打开本仓库。
2. 确认 **Build Settings** 中已启用场景：  
   `Assets/Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR/HelloMR.unity`
3. 连接 Beam Pro，开启 USB 调试，在 Unity 中选择 **Run Device**。
4. 构建并运行：
   - **File → Build And Run**（若 Unity 启动报 NullReferenceException，APK 通常已成功生成，见下方「构建与部署」）
   - 或菜单 **XREAL → Build → Android APK (Build Only, No Unity Launch)**，再 **XREAL → Launch App On Android Device**

## 功能概览

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

在 **Beam Pro** 屏幕右上角 OnGUI 按钮可切换 Controller / Hand；眼镜端 Canvas 也有 **Controller / Hand** 按钮。

### RGB 相机浮动窗口

`RGBCameraFloatingWindow` 在 MR 空间中显示 **XREAL Eye** RGB 实时画面：

- 自动请求 Android `CAMERA` 权限
- 等待 Eye 插入（PLUGIN 状态）后重试启动采集
- 使用 YUV → RGB Shader 渲染到世界空间 Quad
- 调试状态写入 Beam Pro **统一日志窗口**（来源名：`RGB 相机`）
- Beam Pro 右侧按钮可 **Show / Hide RGB Window**（控制世界空间预览 Quad 显隐）

### 左眼预览（Beam Pro 屏幕）

`LeftEyeDisplayWindow` 将 **XR 左眼渲染输出** 镜像到 Beam Pro 手机屏幕底部预览区，便于在手机上查看眼镜端 One Pro 视角。

### 参考物体

`ReferenceCubeSpawner` 在头显前方生成空间参考内容：

- 可选 **参考立方体**（RGB 坐标轴、六面图案、自动旋转）
- 可选 **Check Plane**（从 StreamingAssets 加载 `check_plane.STL`）
- 通过 Beam Pro 右侧 **Move X/Y/Z ±** 平移；有 Check Plane 时还可调透明度与 RGB 颜色通道

### 手术机器人模型（gRPC）

- `DentalRobotGrpcClient` 连接手术机器人，按 `dental_model_transfer.proto` 接收 teeth / drill STL 与位姿矩阵
- `DentalRobotModelRenderer` 在空间中锚定牙齿模型，钻头按 `drill_from_teeth` 矩阵定位（牙齿灰白、钻头黄色）
- 连接与传输状态写入统一日志（来源名：`手术机器人`）

### 其他 UI

- **Show / Hide Glasses UI**：切换眼镜端控制 Canvas 显示
- **Vibrate**：Controller 震动测试

## Beam Pro 屏幕布局与控件

以下 overlay **仅在 Android 真机（Beam Pro）运行时显示**，由 `OnGUI` 绘制。区域划分由 `BeamProOverlayLayout` 统一计算，避免日志、预览与右侧按钮互相遮挡。

### 整体分区

Beam Pro 实体屏为 **1080×2400（20:9 竖屏）**。下图按该比例与 `BeamProOverlayLayout` 的分区规则绘制（左侧内容约 **76%** 屏宽、右侧按钮列约 **24%**；主日志约占屏高 **76%**、底部左眼预览约占 **24%**）：

```
                    Beam Pro  ·  1080 × 2400  ·  20:9 竖屏
┌────────────────────────────────────────┬────────────────┐
│ 16px                                   │ 16px           │
│  ┌──────────────────────────────────┐  │ ┌────────────┐ │
│  │ Beam Pro Logs                    │  │ │ Switch to  │ │
│  │ (可滚动)                          │  │ │ Hand /     │ │
│  │                                  │  │ │ Controller │ │
│  │ [RGB 相机]                       │  │ ├────────────┤ │
│  │  capture / 权限 / Eye plug …     │  │ │ Show/Hide  │ │
│  │                                  │  │ │ Glasses UI │ │
│  │ [手术机器人]                     │  │ ├────────────┤ │
│  │  gRPC / metadata / STL …         │  │ │ Show/Hide  │ │
│  │                                  │  │ │ RGB Window │ │
│  │                                  │  │ ├────────────┤ │
│  │                                  │  │ │ Move X+ X- │ │
│  │          ≈ 76% 屏高              │  │ ├────────────┤ │
│  │          主日志区                │  │ │ Move Y+ Y- │ │
│  │                                  │  │ ├────────────┤ │
│  │                                  │  │ │ Move Z+ Z- │ │
│  │                                  │  │ ├────────────┤ │
│  │                                  │  │ │ Trans ±10% │ │
│  │                                  │  │ ├────────────┤ │
│  │                                  │  │ │   R+  R-   │ │
│  │                                  │  │ ├────────────┤ │
│  │                                  │  │ │   G+  G-   │ │
│  │                                  │  │ ├────────────┤ │
│  │                                  │  │ │   B+  B-   │ │
│  │                                  │  │ └────────────┘ │
│  └──────────────────────────────────┘  │  ≈24% 屏宽     │
│                 12px                   │  上限 280px    │
│  ┌──────────────────────────────────┐  │  高 ≤90% 屏高  │
│  │ Left Eye (One Pro view)          │  │                │
│  │                                  │  │                │
│  │      [XR 左眼预览画面]           │  │                │
│  │         ≈ 24% 屏高               │  │                │
│  └──────────────────────────────────┘  │                │
│ 16px                                   │           16px │
└────────────────────────────────────────┴────────────────┘
 ←────────── ≈ 76% 屏宽 ──────────→ ←── ≈ 24% ──→
```

说明：右侧按钮列与左侧主区**等高占满可用区域**（按钮从顶部向下堆叠，行高随行数自适应）；底部预览**仅占左侧栏**，不伸入右侧按钮列。
布局常量（`BeamProOverlayLayout`）：

| 常量 | 值 | 含义 |
|------|----|------|
| `Margin` | 16px | 屏幕外边距 |
| `ColumnGap` | 12px | 左侧内容与右侧按钮列间距 |
| `RightColumnWidth` | 最大 280px | 右侧按钮列宽度（约屏宽 24%，夹在 120～280） |
| `BottomPreviewMaxFraction` | 0.24 | 底部左眼预览最大高度占比 |
| `RightColumnMaxHeightFraction` | 0.9 | 右侧按钮列可用高度上限 |
| `MaxButtonRows` | 9 | 布局预留的最大按钮行数 |

可在 HelloMR / 各组件 Inspector 中通过 `m_Show*OnBeamPro` 等字段开关对应面板。

### 1. 右侧操作按钮列（`HelloMR`）

纵向堆叠于屏幕右侧；行高按当前行数自适应（约 28～68px）。完整时行序如下：

| 行 | 控件 | 用途 |
|----|------|------|
| 1 | **Switch to Controller** / **Switch to Hand** | Controller ↔ Hands 输入切换；默认启动为 Controller。 |
| 2 | **Show Glasses UI** / **Hide Glasses UI** | 显示或隐藏**眼镜端** Canvas（追踪 Toggle 等），不影响 Beam Pro overlay。 |
| 3 | **Show RGB Window** / **Hide RGB Window** | 显示或隐藏 MR 世界空间中的 RGB 预览 Quad（有 `RGBCameraFloatingWindow` 时出现）。 |
| 4–6 | **Move X± / Y± / Z±** | 参考立方体与 Check Plane 沿世界轴平移一步（每行左右两个半宽按钮）。 |
| 7–10 | **Trans ±10%**、**R± / G± / B±** | 调整 Check Plane 透明度与颜色通道（仅当场景中已有 Check Plane 时出现）。 |

- **Inspector**：`Show Beam Pro Input Toggle`、`Show Beam Pro Object Move Buttons`、`Show Beam Pro Check Plane Appearance Buttons`。

### 2. 统一日志窗口（`BeamProUnifiedLogWindow`）

| 项目 | 说明 |
|------|------|
| **用途** | Beam Pro 左侧主日志区，汇总各模块状态与滚动记录。 |
| **位置** | `GetMainLogRect`：左上起，宽度为屏宽减去右侧列预留，高度为底部预览区之上的剩余空间。 |
| **标题** | `Beam Pro Logs` |
| **交互** | 内容区可滚动；每个来源最多保留约 32 条，新日志在上。 |
| **当前来源** | **`RGB 相机`**：采集状态、权限、Eye 插拔与错误；**`手术机器人`**：gRPC 连接、metadata、STL 分块与传输结束。 |

RGB / 手术机器人不再各自绘制独立拖动面板，而是写入本窗口。

### 3. 左眼预览（`LeftEyeDisplayWindow`）

| 项目 | 说明 |
|------|------|
| **用途** | 在 Beam Pro 上镜像**眼镜 One Pro 左眼** XR 渲染。 |
| **位置** | `GetBottomPreviewRect`：左侧内容区底部，高度约屏高 **12%～24%**（默认上限 24%）。 |
| **交互** | 只读；右下角可显示分辨率等 `DebugInfo`；无帧时显示等待/错误文案。 |
| **Inspector** | `Show On Beam Pro`、`Max Screen Height Fraction`。 |

### 与眼镜端 UI 的区别

| 位置 | 内容 |
|------|------|
| **Beam Pro 手机屏** | 右侧按钮列、统一日志、左眼预览。 |
| **XREAL 眼镜内 Canvas** | 追踪模式 Toggle（0/3/6 DoF 等）、**Controller / Hand** 按钮、当前 `Track` / `Input` 状态文字；可由 Beam Pro「Show/Hide Glasses UI」控制显隐。 |
| **MR 世界空间** | RGB 相机浮动预览 Quad、参考物体、手术机器人 teeth/drill 模型（非 Beam Pro 屏幕）。 |

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
│   ├── XREALAndroidLaunchHelper.cs    # adb 部署与启动
│   ├── ProjectDebugBuildSettings.cs   # Debug 构建默认选项
│   └── XREALLicenseSetup.cs           # License 文件引导（可选）
├── Plugins/
│   ├── Android/                       # Camera / 网络权限等
│   └── Grpc/                          # gRPC / Protobuf 运行时 DLL
├── StreamingAssets/
└── Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR/
    ├── HelloMR.unity                  # 主场景
    ├── HelloMR.cs                     # 追踪/输入/UI 总控 + 右侧按钮列
    ├── BeamProOverlayLayout.cs        # Beam Pro 分区布局（日志 / 预览 / 按钮）
    ├── BeamProUnifiedLogWindow.cs     # 统一日志窗口（RGB / 手术机器人等）
    ├── RGBCameraFloatingWindow.cs     # RGB 相机世界空间预览 + 日志写入
    ├── LeftEyeDisplayWindow.cs        # Beam Pro 底部左眼预览
    ├── ReferenceCubeSpawner.cs        # 参考立方体 / Check Plane
    ├── DentalRobotGrpcClient.cs       # 手术机器人 gRPC 客户端
    ├── DentalRobotModelRenderer.cs    # teeth/drill STL 空间渲染
    ├── DentalRobotBeamProDisplay.cs   # 手术机器人状态 → 统一日志
    ├── DentalRobotConnectionDefaults.cs
    └── DentalStlMeshUtility.cs        # 共享 STL 网格解析
```

根目录另有 `dental_model_transfer.proto`（手术机器人模型传输协议）。

## 常见问题

**Build And Run 报 NullReferenceException**  
多为 Unity Editor 启动 APK 失败，APK 可能已生成。使用 **XREAL → Launch App On Android Device**，或手动 `adb install -r`。

**看不到双手模型**  
需在真机运行；手部 mesh 仅在追踪成功（`isTracked`）后显示。可尝试切换到 **Hands** 输入对比；确认设备支持手部追踪且手在 RGB 相机视野内。

**RGB 相机无画面**  
确认 XREAL Eye 已连接、Camera 权限已授予，查看 Beam Pro 统一日志中 **`[RGB 相机]`** 的 plug 状态与 capture 日志。

## 许可证

本项目基于 XREAL 与 Unity 官方 Sample 扩展开发；第三方 Sample 与 Plugin 遵循各自许可证。
