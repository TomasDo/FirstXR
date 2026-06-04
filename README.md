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
| **Controller + 手部显示** | `ControllerAndHands`：开启手部追踪与双手模型显示，**禁用手部 Interactor**，手势不控制 UI/物体 |
| **Hands** | 完整手势输入，启用手部 Interactor |

启动默认：**Controller 控制 + 仅显示手部追踪**（`Enable Hand Tracking Visualization On Start`）。

在 **Beam Pro** 屏幕右上角 OnGUI 按钮可切换 Controller / Hand；眼镜端 Canvas 也有 **Controller / Hand** 按钮。

### RGB 相机浮动窗口

`RGBCameraFloatingWindow` 在 MR 空间中显示 **XREAL Eye** RGB 实时画面：

- 自动请求 Android `CAMERA` 权限
- 等待 Eye 插入（PLUGIN 状态）后重试启动采集
- 使用 YUV → RGB Shader 渲染到世界空间 Quad

Beam Pro 上提供 **可拖动、可滚动** 的 RGB 调试日志面板（标题栏拖动，内容区滑动，新日志自动滚到底部）。

### 左眼预览（Beam Pro 屏幕）

`LeftEyeDisplayWindow` 将 **XR 左眼渲染输出** 镜像到 Beam Pro 手机屏幕，便于在手机上查看眼镜端 One Pro 视角。

### 参考物体

`ReferenceCubeSpawner` 在头显前方生成空间参考内容：

- 可选 **参考立方体**（RGB 坐标轴、六面图案、自动旋转）
- 可选 **Check Plane**（从 StreamingAssets 加载 `check_plane.STL`）
- 通过 Beam Pro OnGUI **Move X/Y/Z ±** 按钮平移参考物体

### 其他 UI

- **Show / Hide Glasses UI**：切换眼镜端控制 Canvas 显示
- **Vibrate**：Controller 震动测试

### 语音口令（离线中文，20 条）

- 词表文件：`Assets/StreamingAssets/VoiceCommands/commands_zh.json`（可单独编辑口令与 `action`）
- 引擎：`VoiceCommandRecognizer` + Vosk 语法约束识别，麦克风来源为 `XREALMicrophoneStream`
- Beam Pro 屏幕左侧 **「语音口令识别」** 小窗：显示识别原文、命中口令、执行状态
- 首次使用在 Unity 菜单执行 **XREAL → Download Chinese Vosk Model**（约 42 MB），再 **Regenerate Vosk Model Manifest**，然后重新打 Android 包

## Beam Pro 屏幕控件

在 Android 设备（Beam Pro）上运行时，屏幕 overlay 提供：

| 控件 | 功能 |
|------|------|
| Switch to Controller / Hand | 切换输入源 |
| Show / Hide Glasses UI | 显示/隐藏眼镜 UI |
| Move X± / Y± / Z± | 移动参考立方体与 Check Plane |
| RGB Debug Log 面板 | 拖动标题栏移动；滑动查看完整日志 |
| 语音口令识别 面板 | 显示最近识别结果与是否执行 |

## 构建与部署

项目包含 Editor 辅助脚本，菜单路径 **XREAL**：

| 菜单 | 说明 |
|------|------|
| **Launch App On Android Device** | adb 安装 APK 并启动 `NRXRActivity` |
| **Build → Android APK (Build Only, No Unity Launch)** | 仅打 APK，避免 Unity 自带启动失败 |
| **Build → Enable / Disable Debug Build (Android)** | Development Build + Script Debugging |
| **Setup Hand Tracking** | 为 Input Action Asset 写入 XREAL 手部绑定（需先在 Project 中选中 `.inputactions` 文件） |

手势追踪 Input 绑定目标示例：

`Assets/Samples/XR Interaction Toolkit/2.6.5/Starter Assets/XRI Default Input Actions.inputactions`

操作步骤：在 Project 窗口选中该文件 → **XREAL → Setup Hand Tracking**。

## 项目结构（核心）

```
Assets/
├── Editor/
│   ├── XREALAndroidLaunchHelper.cs    # adb 部署与启动
│   └── ProjectDebugBuildSettings.cs   # Debug 构建默认选项
└── Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR/
    ├── HelloMR.unity                  # 主场景
    ├── HelloMR.cs                     # 追踪/输入/UI 总控
    ├── RGBCameraFloatingWindow.cs     # RGB 相机 + Beam Pro 日志面板
    ├── LeftEyeDisplayWindow.cs        # Beam Pro 左眼预览
    ├── ReferenceCubeSpawner.cs          # 参考立方体 / Check Plane
    ├── XREALMicrophoneStream.cs         # 眼镜麦 PCM 流
    └── VoiceCommands/                   # 离线口令识别（词表 + Vosk + Beam Pro 面板）
```

仓库根目录另有 `VisionProMsgLocal.proto`、`VisionProMsgRemote.proto`，为 Vision Pro 实时通信协议定义，**尚未接入 Unity 运行时**。

## 常见问题

**Build And Run 报 NullReferenceException**  
多为 Unity Editor 启动 APK 失败，APK 可能已生成。使用 **XREAL → Launch App On Android Device**，或手动 `adb install -r`。

**看不到双手模型**  
需在真机运行；手部 mesh 仅在追踪成功（`isTracked`）后显示。可尝试切换到 **Hands** 输入对比；确认设备支持手部追踪且手在 RGB 相机视野内。

**Setup Hand Tracking 报错**  
须先在 Project 窗口选中 `.inputactions` 资源，再执行菜单。

**RGB 相机无画面**  
确认 XREAL Eye 已连接、Camera 权限已授予，查看 Beam Pro 上 RGB Debug Log 中的 plug 状态与 capture 日志。

## 许可证

本项目基于 XREAL 与 Unity 官方 Sample 扩展开发；第三方 Sample 与 Plugin 遵循各自许可证。
