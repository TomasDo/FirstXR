# XREAL 口腔种植导航 v2 验证记录

更新时间：2026-09-11

## 已自动验证

| 范围 | 方法 | 当前结果 |
|---|---|---|
| proto 语法与字段生成 | `protoc --descriptor_set_out`，重新生成 C# | 通过 |
| Unity runtime C# | Mono `msbuild Unity.XR.XREAL.Samples.InteractionBasics.csproj` | 通过；仅有仓库原依赖版本警告 |
| EditMode 测试程序集编译 | Mono `msbuild DentalNavigation.Dicom.EditModeTests.csproj` | 通过 |
| 模拟服务 | `cd Tools/navigation-simulator && npm run smoke` | 通过：上下文、阈值、连续帧、版本切片闭环 |
| DICOM 核心 | 程序生成显式 VR 样本，乱序块/重复块/续传/SHA/像素空间/体构建测试 | 通过（非图形核心 7 项） |
| 手势状态机 | 确定性关键点与时间序列 | 通过：OK 保持、方向、限速、失帧、头动、接管 |
| 源码格式 | `git diff --check` | 通过 |

Unity Test Runner 本次尝试在启动阶段因本机 `LicensingClient` IPC 60 秒超时以 exit 199 退出，没有生成 XML。因此上表中的程序集测试“编译通过”和独立核心测试不能写成 Unity Test Runner 全套已执行；日志为 `/tmp/first-xr-editmode.log`。

## 必须在设备或配套系统验证

以下项目无法由当前仓库和本机替代，发布或临床使用前必须逐项留存证据：

- Beam Pro + 目标 XREAL 眼镜安装、权限、60fps XR 渲染与头锁定位置。
- 真实导航软件 v2 双流；不同牙位及颊/舌/近中/远中正反组合的坐标对照。
- 真实完整 CT 与导航端逐体素位置、方向和窗口显示对照；Android DCMTK 原生解析和 ARM64 ABI。
- 实际术用手套、口腔灯、器械、遮挡与头动下的 OK 成功率、误触和停止时间。
- XREAL VideoEncoder 实际编码格式；FFmpeg RTP 接收、左右分窗和 P95 端到端延迟。
- 导航端按需开关 RGB 时，手势采集和导航延迟不被打断。
- 断网、导航服务退出、换规划/钻针、相机拔出、重连和旧版本延迟命令。
- 同时运行完整 CT、实时导航、手势和媒体 60 分钟的崩溃、内存增长和队列情况。
- 医生佩戴后确认数字可读、方向无歧义、默认位置不遮挡术野，并在牙模上完成整流程。

## 设备验收记录模板

| 项目 | 记录 |
|---|---|
| APK SHA-256 / Git commit | 待填 |
| Beam Pro / XREAL 型号与系统版本 | 待填 |
| 导航软件版本 / 协议版本 | 待填 |
| DICOM 样本标识 / 实例与帧数 | 待填 |
| 导航显示延迟 P50 / P95 / 最大值 | 待填 |
| 远端观察延迟 P50 / P95 / 最大值 | 待填 |
| OK 操作成功次数 / 总次数 | 待填 |
| 30 分钟非操作误切层次数 | 待填 |
| 松开/识别丢失后的最大停止时间 | 待填 |
| 60 分钟内存起点 / 终点 / 峰值 | 待填 |
| 异常恢复结果 | 待填 |
| 医生可读性与遮挡确认 | 待填 |
| 结论、问题与签字 | 待填 |
