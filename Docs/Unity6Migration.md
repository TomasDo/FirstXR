# Unity 6 开发环境迁移记录

2026-09-16 在本机 macOS 27.0 / Apple Silicon 上完成，从 Unity `2022.3.62f3c1` 迁移到 `6000.0.83f1`。Unity Hub 中的 First XR 已关联新版本，Android 为当前开发平台。

## 当前环境

| 组件 | 配置 |
|---|---|
| Unity Hub | 3.21.3 |
| Unity | 6.0 LTS / 6000.0.83f1 / Apple Silicon |
| XREAL XR Plugin | 3.1.0，使用仓库根目录的本地 SDK 压缩包 |
| AR Foundation / XR Interaction Toolkit / XR Hands | 6.0.8 / 2.6.5 / 1.5.1 |
| Android | ARM64、IL2CPP、OpenGLES3、最低 API 29、当前目标 API 36 |
| Unity 自带工具链 | OpenJDK 17.0.18、NDK 27.2.12479018、Build Tools 36.0.0、Gradle 9.1.0、AGP 9.0.0 |
| 脚本编辑器 | Visual Studio Code |
| Mac 兼容组件 | Rosetta 已安装，Intel 程序执行检查通过 |

## 兼容性调整

- 更新 Unity 包和项目序列化设置；保留与现有 Samples 匹配的 XR Interaction Toolkit、XR Hands 版本。
- 导入 Unity 6 配套的 TMP Essential Resources，修正旧 TMP 示例代码对 UV 数据类型的假设。
- 重建 Visual Scripting 节点库，更新旧编辑器生成的节点缓存。
- 主 Gradle 模板更新为新编辑器模板，保留固定的 MediaPipe `tasks-vision:1.0.0` 依赖。
- 相机权限 Android 库明确声明 Gradle 命名空间；MediaPipe 库使用当前 Gradle DSL 和本机已有的 Build Tools。
- XREAL 3.1.0 的 `nr_common` 与 `nr_loader` 都使用 `nrsdk.pack`。启用自定义 Gradle Properties，并设置 `android.uniquePackageNames=false`，保持旧版工具的兼容行为。后续升级 XREAL SDK、确认其库命名空间已分离后再移除此项。参见 [AGP 9.0 变更](https://developer.android.com/build/releases/agp-9-0-0-release-notes)。
- 构建辅助菜单在失败时抛出异常，保证命令行构建能返回失败状态。

## 验证结果

- HelloMR 可在正常编辑模式打开；Project Validation 为 12 项检查、0 个问题。
- 现有 EditMode 测试 **47 / 47 通过**，无失败、无跳过。结果：`Logs/unity6-editmode-results.xml`。
- Android 完整构建成功：`Builds/First XR.apk`，约 204 MiB；构建日志：`Logs/unity6-android-build.log`。
- APK 签名校验通过。包名 `com.DefaultCompany.FirstXR`，启动 Activity 为 `ai.nreal.activitylife.NRXRActivity`，仅包含 `arm64-v8a`。
- APK SHA-256：`470d21deefd4927613560d175eb463594aa6f2479fdf3de15bc6d1621313176f`。
- 旧 Samples 中仍有 API 弃用等非阻断警告；本次已通过脚本编译及完整构建。
- 本次未连接 Beam Pro / XREAL 真机验证；设备运行、追踪、相机与显示效果需在真机复核。

## 备份与继续开发

升级前源码及本地依赖归档：`Builds/BeforeUnity6-20260916/project-before-unity6.tar.gz`。包含 Assets、Packages、ProjectSettings、UserSettings、README 及本地 XREAL SDK 压缩包；不包含可重新生成的 Library。原 APK 在同目录的 `First XR-before-unity6.apk`，原 Git 提交记录在 `original-head.txt`。这些本地备份和构建产物由 Git 忽略。

日常从 Unity Hub 打开 First XR。主场景为 `Assets/Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR/HelloMR.unity`。使用 **XREAL → Build → Android APK (Build Only, No Unity Launch)** 打包，需要真机时再通过 **XREAL → Launch App On Android Device** 安装启动。

如需回看旧版本，将备份解压到另一个目录后使用其原编辑器版本；不要直接在已迁移的工程上切回 Unity 2022。此次改动保留在本地工作区，未提交或推送。
