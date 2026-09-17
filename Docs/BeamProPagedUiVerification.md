# Beam Pro 分页界面验证记录

日期：2026-09-16

Unity：6000.0.83f1

目标平台：Android ARM64 / IL2CPP

## 2026-09-17 悬停/跟随增量

- Beam Pro 监看页新增“内容悬停”和“内容跟随”互斥按钮，默认跟随。
- “内容悬停”会让 HUD 与三维模型保留切换瞬间的世界姿态；“内容跟随”会重新应用各自配置的头部相对位置，并恢复模型的头部相对缩放。
- 新增竖屏和横屏按钮布局检查，保证两个按钮高度不小于 56，且不与显隐按钮或资产状态重叠。
- 新增 `DentalDisplayLayoutAnchorTests`：默认跟随、非法枚举忽略、重复设置不重复通知、不写 PlayerPrefs、不影响 `ControlVersion`、远端布局不能覆盖本地锚点。
- 已还原误改的 `ProjectSettings` 预加载列表和 Image Tracking 参考图库缓存。
- 按用户安排，本次增量的 Unity 编译、测试结果导出、截图、APK 和真机部署由用户执行。下方 58 / 58、截图及 APK 信息属于 2026-09-16 分页界面基线，不包含本次增量。EditMode 在原 58 项之外新增布局 1 项与锚点 7 项，重跑后应变为 66。

## 实施结果

- `BeamProPagedController` 统一绘制监看、连接和工程调试三页；顶部状态栏与底部页签固定，正文独立滚动。
- `BeamProPageLayoutCalculator` 按 540 逻辑单位短边计算安全区、软键盘、竖屏、横屏、动态文本和滚动条预留。
- 旧 `HelloMR` 按钮列、导航仪表、左眼预览和日志窗口在分页控制器激活时停止独立绘制，但继续提供状态和操作接口。
- 连接页保留输入草稿，IPv4 与端口仍在提交时校验；连接期间锁定重复提交，并把成功、失败或超时写入操作结果。
- 调试页只在工程模式出现，按 Inspector 配置收起未启用的控制组；“本地 RGB 预览”与导航端 RGB 回传保持区分。
- XR 预览只接受 250ms 内的真实 XR 左眼帧。分页界面激活时不执行主相机替代渲染，也不把旧帧显示为实时画面。

## 自动化与编辑器验证

Unity Test Runner EditMode：**58 / 58 通过，0 失败**。其中 Beam Pro 分页布局测试 11 项，覆盖：

- 1080×2400 与 720×1600 的统一逻辑几何。
- 2400×1080 横屏预览和指标左右排列。
- 安全区域、极端软键盘高度和提交按钮滚动可达。
- 顶部超长告警限高，正文与页签不重叠。
- 动态长文本扩展、竖向滚动条预留且不产生横向滚动。
- 所有可见触控项高度不小于 56，指标卡、连接控件和调试控件互不覆盖。
- 非工程模式无法进入调试页，未启用的工程控件会收起。

测试结果原始文件：[BeamProPagedUiTestResults.xml](BeamProPagedUiTestResults.xml)。

编辑器 Play Mode 使用正式 IMGUI 代码生成 1080×2400 截图：

- [监看页](BeamProScreenshots/monitor-editor.png)
- [连接页](BeamProScreenshots/connection-editor.png)
- [调试页](BeamProScreenshots/debug-editor.png)

编辑器没有 XREAL 左眼输出和 Android MediaPipe runtime，因此截图按设计显示 XR 等待状态；编辑器 gRPC HTTP/2 不可用状态已转换为面向操作者的中文提示，完整异常仍保留在工程日志中。

## Android 构建

- 结果：成功。
- APK：`Builds/First XR.apk`
- 大小：234,872,678 bytes。
- SHA-256：`638c2580698a5b0c7e01f03ca27f1ec4d1c01fd6cda5d6963b901fc3e72101e5`
- 构建日志仍报告现有 XREAL 原生库的 16 KB page-size alignment 警告；本次构建未因此失败。

## 待真机验证

本记录没有把编辑器结果写成真机完成。仍需在 Beam Pro + XREAL 上验证触摸滚动、软键盘、系统安全区、横竖屏切换、真实 XR 左眼帧、RGB/手势并发、导航端回传以及切页期间的持续运行。悬停/跟随增量还需验证：转头和移动时跟随模式保持相对视野位置，悬停模式保持世界姿态，隐藏状态下切换仍有效，重新跟随后内容可立即回到可见范围。
