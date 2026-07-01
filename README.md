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

### 语音口令（离线中文，19 条）

- 词表文件：`Assets/StreamingAssets/VoiceCommands/commands_zh.json`（可单独编辑口令与 `action`）
- 引擎：`VoiceCommandRecognizer` + Vosk 语法约束识别，麦克风来源为 `XREALMicrophoneStream`
- Beam Pro 屏幕左侧 **「语音口令识别」** 小窗：显示识别原文、命中口令、执行状态
- 当前口令覆盖：输入模式切换、眼镜 UI 显隐、参考物体 X/Y/Z 移动、控制器震动、追踪模式切换、暂停/继续聆听、清空口令记录
- 首次使用如需重新生成模型清单，可在 Unity 菜单执行 **XREAL → Regenerate Vosk Model Manifest**，然后重新打 Android 包

## Beam Pro 屏幕布局与控件

以下 overlay **仅在 Android 真机（Beam Pro）运行时显示**，由 `OnGUI` 绘制。默认布局（以竖屏手机为参考，自上而下）如下：

```
┌─────────────────────────────────────────────────────────────┐
│ [RGB Debug Log] 左上角，可拖动                    [操作按钮列] │  ← 顶部
│  默认 (24,24) 920×520                          右上角 280px 宽 │
│  标题栏拖动 / 内容区滚动                        Switch / UI / Move │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  [语音口令识别] 左侧约 42% 高度处                             │
│   520×300，黄字滚动日志                                      │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│              [Left Eye (One Pro view)] 底部居中              │  ← 底部
│              左眼 XR 画面预览，约占屏高 45%                   │
│              顶部预留约 32% 给其它 overlay                    │
└─────────────────────────────────────────────────────────────┘
```

各区域由不同脚本负责；可在 HelloMR 场景 `Panel` 对象上通过 Inspector 开关对应 `m_Show*OnBeamPro` 字段。

### 1. 右上角操作按钮（`HelloMR`）

| 控件 | 用途 |
|------|------|
| **Switch to Controller** / **Switch to Hand** | 在 Controller 与完整手势输入（Hands）之间切换；当前为 Hands 时按钮文案为前者，反之亦然。默认启动为 Controller。 |
| **Show Glasses UI** / **Hide Glasses UI** | 显示或隐藏**眼镜端** Canvas 控制面板（追踪 Toggle、Hand 按钮等），不改变 Beam Pro overlay。 |
| **Move X+ / X-** | 将参考立方体与 Check Plane 沿世界 X 轴正/负方向平移一步（`ReferenceCubeSpawner`）。 |
| **Move Y+ / Y-** | 沿世界 Y 轴上/下平移。 |
| **Move Z+ / Z-** | 沿世界 Z 轴前/后平移。 |

- **位置**：距屏幕右、上边缘各约 30px，按钮宽约 280px、高约 90px，纵向堆叠。
- **Inspector**：`Show Beam Pro Input Toggle`、`Show Beam Pro Object Move Buttons`。

### 2. RGB Debug Log（`RGBCameraFloatingWindow`）

| 项目 | 说明 |
|------|------|
| **用途** | 排查 XREAL Eye RGB 相机：权限、设备类型、Eye 插拔状态、采集重试与错误日志。 |
| **位置** | 默认左上角 `(24, 24)`，面板约 **920×520**（可配置）。 |
| **交互** | 拖动**标题栏**（`RGB Debug Log (drag header)`）移动；**内容区**可滚动；新日志默认滚到底部。 |
| **标题行信息** | Capture 状态、Camera 权限、设备类型、RGB 功能支持、Eye plug 状态。 |
| **Inspector** | `Show Debug On Beam Pro`、`Debug Panel Width/Height`。 |

### 3. 语音口令识别（`VoiceCommandRecognizer`）

| 项目 | 说明 |
|------|------|
| **用途** | 显示离线中文口令识别：引擎状态、识别原文、命中口令名、置信度、是否已执行。 |
| **位置** | 屏幕**左侧**，纵向约在屏高 **42%** 处，面板 **520×300**。 |
| **交互** | 内容区**滚动**查看历史（最多约 24 条，新记录在上方）。 |
| **口令来源** | `Assets/StreamingAssets/VoiceCommands/commands_zh.json`（19 条，可说同义词）。 |
| **Inspector** | `Show Beam Pro Panel`。 |

与右上角按钮、语音等价的口令示例：「手势模式」「控制器模式」「显示界面」「向右」「六自由度」「暂停聆听」「清空记录」等（完整列表见词表 JSON）。

### 4. 左眼预览（`LeftEyeDisplayWindow`）

| 项目 | 说明 |
|------|------|
| **用途** | 在 Beam Pro 上镜像**眼镜 One Pro 左眼** XR 渲染，便于对照 MR 场景。 |
| **位置** | **底部居中**；预览区最大高度约为屏高 **45%**；整体布局为屏幕上方约 **32%** 预留给其它 overlay，避免与顶部按钮/RGB 面板重叠。 |
| **交互** | 只读画面；无拖动。右下角可显示分辨率等 `DebugInfo`；无帧时显示等待/错误文案。 |
| **Inspector** | `Show On Beam Pro`、`Top Reserved Fraction`、`Max Screen Height Fraction`。 |

### 5. 麦克风调试条（`XREALMicrophoneStream`，默认关闭）

| 项目 | 说明 |
|------|------|
| **用途** | 显示眼镜麦 PCM 采集状态、最近块大小、累计字节、采样格式。 |
| **位置** | **右下角**（宽约 480px）。 |
| **Inspector** | `Show Debug Overlay On Beam Pro`（HelloMR 场景中默认为 **关闭**，避免与语音面板重复）。 |

### 与眼镜端 UI 的区别

| 位置 | 内容 |
|------|------|
| **Beam Pro 手机屏** | 上文所有 OnGUI overlay（按钮、调试面板、左眼预览、语音日志）。 |
| **XREAL 眼镜内 Canvas** | 追踪模式 Toggle（0/3/6 DoF 等）、**Controller / Hand** 按钮、当前 `Track` / `Input` 状态文字；可由 Beam Pro「Show/Hide Glasses UI」或口令控制显隐。 |
| **MR 世界空间** | RGB 相机浮动预览 Quad（非 Beam Pro 屏幕）。 |

## 构建与部署

项目包含 Editor 辅助脚本，菜单路径 **XREAL**：

| 菜单 | 说明 |
|------|------|
| **Launch App On Android Device** | adb 安装 APK 并启动 `NRXRActivity` |
| **Build → Android APK (Build Only, No Unity Launch)** | 仅打 APK，避免 Unity 自带启动失败 |
| **Build → Enable / Disable Debug Build (Android)** | Development Build + Script Debugging |
| **Setup Hand Tracking** | 为 Input Action Asset 写入 XREAL 手部绑定（需先在 Project 中选中 `.inputactions` 文件） |
| **Download Chinese Vosk Model** | 下载中文离线语音模型到 `StreamingAssets/VoiceCommands` |
| **Regenerate Vosk Model Manifest** | 重新生成 Android 运行时复制 Vosk 模型所需的文件清单 |

手势追踪 Input 绑定目标示例：

`Assets/Samples/XR Interaction Toolkit/2.6.5/Starter Assets/XRI Default Input Actions.inputactions`

操作步骤：在 Project 窗口选中该文件 → **XREAL → Setup Hand Tracking**。

## 项目结构（核心）

```
Assets/
├── Editor/
│   ├── XREALAndroidLaunchHelper.cs    # adb 部署与启动
│   ├── ProjectDebugBuildSettings.cs   # Debug 构建默认选项
│   └── VoiceCommandModelSetup.cs      # Vosk 中文模型下载与清单生成
├── Plugins/
│   ├── Android/arm64-v8a/libvosk.so   # Vosk Android 原生库
│   └── Vosk/                          # Vosk C# 绑定与 asmdef
├── StreamingAssets/
│   └── VoiceCommands/                 # 口令表、Vosk 模型与模型文件清单
└── Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR/
    ├── HelloMR.unity                  # 主场景
    ├── HelloMR.cs                     # 追踪/输入/UI 总控
    ├── RGBCameraFloatingWindow.cs     # RGB 相机 + Beam Pro 日志面板
    ├── LeftEyeDisplayWindow.cs        # Beam Pro 左眼预览
    ├── ReferenceCubeSpawner.cs          # 参考立方体 / Check Plane
    ├── XREALMicrophoneStream.cs         # 眼镜麦 PCM 流
    └── VoiceCommands/                   # 离线口令识别（词表 + Vosk + Beam Pro 面板）
```

## 常见问题

**Build And Run 报 NullReferenceException**  
多为 Unity Editor 启动 APK 失败，APK 可能已生成。使用 **XREAL → Launch App On Android Device**，或手动 `adb install -r`。

**看不到双手模型**  
需在真机运行；手部 mesh 仅在追踪成功（`isTracked`）后显示。可尝试切换到 **Hands** 输入对比；确认设备支持手部追踪且手在 RGB 相机视野内。

**Setup Hand Tracking 报错**  
须先在 Project 窗口选中 `.inputactions` 资源，再执行菜单。

**RGB 相机无画面**  
确认 XREAL Eye 已连接、Camera 权限已授予，查看 Beam Pro 上 RGB Debug Log 中的 plug 状态与 capture 日志。

**语音口令面板显示模型未就绪**  
确认 `Assets/StreamingAssets/VoiceCommands/vosk-model-small-cn-0.22/` 存在，并执行 **XREAL → Regenerate Vosk Model Manifest** 后重新打包。

**语音识别误触发或漏识别**  
优先调整 `commands_zh.json` 中的 `phrases`、`minConfidence` 与 `cooldownSeconds`；短口令建议保留 2～4 个汉字并避免发音相近。

## 许可证

本项目基于 XREAL 与 Unity 官方 Sample 扩展开发；第三方 Sample 与 Plugin 遵循各自许可证。
