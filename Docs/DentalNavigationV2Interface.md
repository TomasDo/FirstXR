# XREAL 口腔种植导航接口 v2

本文描述本仓库已经实现的客户端接口。线格式以根目录 `dental_model_transfer.proto` 为唯一准则；导航端不得根据本文文字猜测字段编号。

## 1. 通道与生命周期

服务 `dentalmodeltransfer.DentalModelTransfer` 提供三个双向流：

| RPC | 用途 | 生命周期 |
|---|---|---|
| `StreamSession` | v2 上下文、实时导航帧、阈值、切片、布局、观察控制 | 会话长连接；只由 `SessionEnd` 或传输断开结束 |
| `StreamAssets` | 完整 DICOM 与 STL 的清单、分块、续传和校验 | 独立连接；`AssetTransferComplete` 不结束导航 |
| `StreamDentalModel` | v1 兼容 | 仅在服务端对 `StreamSession` 返回 `UNIMPLEMENTED` 时使用 |

客户端首次请求 `protocol_version=2`，同时声明 DICOM、切片、布局、XR 镜像和 RGB 能力。服务端应先发送 `NavigationContext`，再发送同一 `session_id/context_version` 的其他数据。

所有 v1 字段号保持不变。v1 标量只能显示为兼容数据；客户端不会从总偏移量推导颊舌/近远中方向，也不会使用本地临床阈值。

## 2. 坐标、方向与单位

- v2 距离只接受 `DISTANCE_UNIT_MILLIMETER`，角度只接受 `ANGLE_UNIT_DEGREE`。单位不匹配的导航帧会作为无效帧处理。
- 矩阵均为 16 个 `double` 的行主序 4×4。字段名 `target_from_source` 表示把 source 中的点变换到 target。
- `NavigationContext.patient_from_dicom` 把 DICOM Patient 坐标变换到导航 patient/plan 坐标。客户端集中求逆，把规划或切片物理平面转换回 DICOM Patient 坐标后采样。
- `buccal_axis`、`mesial_axis` 属于当前牙位的导航 patient/plan 坐标。界面固定上颊、下舌、左近中、右远中，随头部转动不旋转。
- `lateral_buccal_mm > 0` 表示钻尖偏颊，`< 0` 表示偏舌；`lateral_mesial_mm > 0` 表示偏近中，`< 0` 表示偏远中。
- 倾斜分量使用同一符号定义。钻针轴统一按尾端指向尖端定义。
- `current_depth_mm` 是钻尖相对规划入点沿规划轴的投影；`remaining_depth_mm = target_depth_mm - current_depth_mm`。负值必须原样发送，客户端显示“超深”。

导航端负责计算权威指标。客户端只做版本检查、显示和状态判定，不把偏移方向转换为自动用力指令。

## 3. 实时会话顺序

推荐服务端按下列顺序开始一次上下文：

1. `NavigationContext`：病例、CT、规划、牙位、钻针、步骤、规划几何和版本。
2. `ToleranceConfig`：与上下文版本一致的阈值、滞回、边界规则和配置版本。
3. `SliceState`：当前同步状态、物理切片和控制版本。
4. `DisplayLayout`：HUD/模型显隐、头锁定局部位置和控制版本。
5. `NavigationStatus(ACTIVE)`。
6. 按采集顺序持续发送 `NavigationFrame`。

每个接收项都有会话/上下文版本检查。切换规划、牙位、钻针或步骤时，导航端必须递增 `context_version` 并重新发送上下文、阈值和切片状态。客户端收到新上下文后清空旧动态帧、阈值及控制状态。

`NavigationFrame.sequence` 在一个上下文中严格递增。客户端使用一个 latest-only 槽，主线程只消费最新帧，资产或视频不会把历史导航帧排队到屏幕。

失效规则：

| 条件 | 客户端行为 |
|---|---|
| 帧显式 `valid=false` | 立即撤下动态标记和数字，显示 `invalid_reason` |
| 距最后有效帧 >200ms | 撤下绿/琥珀/红有效色，显示延迟 |
| 距最后有效帧 >500ms | 隐藏动态标记和数值，显示“导航数据中断” |
| `NavigationStatus(STOPPED)`、会话断开或上下文不匹配 | 立即失效，不保留旧绿色 |

CT 的静态切片可以保留，但会与导航失效状态同时显示。

## 4. 阈值

`ToleranceConfig` 必须与当前 `context_version` 匹配，`config_version` 单调递增；同版本同内容允许幂等重发。客户端没有后备临床数值。

位置和角度分别计算绿色、琥珀色和红色，并各自应用滞回。深度配置包含接近目标距离、到位容差、超深红色阈值和滞回。显示保留一位小数，所有比较使用未舍入的原始值。

`THRESHOLD_BOUNDARY_RULE_UPPER_BOUNDS_INCLUSIVE` 的位置/角度规则为：

- `value <= green_max`：绿色
- `green_max < value <= red_min`：琥珀色
- `value > red_min`：红色

`UPPER_BOUNDS_EXCLUSIVE` 则把两个等号归入更高一级。深度负值不会被截断。

## 5. DICOM 与 STL 资产

导航端用 `StreamAssets` 发送当前 `dataset_id/context_version` 的完整清单。DICOM 首版只接受未压缩 Explicit VR Little Endian，UID 为 `1.2.840.10008.1.2.1`；压缩源数据需由导航端无损解压后发送。

每个 `AssetDescriptor` 必须提供稳定的 `asset_id`、字节数和 32 字节 SHA-256。分块可以乱序或重复；块不得越过声明大小。CRC-32 不为零时客户端逐块验证。

客户端行为：

1. 创建有上限的随机访问 `.part` 文件并持久化已覆盖区间。
2. 块成功落盘后才发 `AssetChunkAck(accepted=true)`；失败返回拒绝和下一缺口位置。
3. 重连时通过 `AssetResumeRequest` 报告每个资产的 `next_offset`。
4. `AssetTransferComplete` 后逐文件核对完整覆盖和 SHA-256，再解码 DICOM。
5. 全部实例/帧成功组成体数据后才启用 CT；失败时保留明确错误，不启用半套数据。

解析使用 Image Position Patient、Image Orientation Patient、Pixel Spacing、Slice Thickness/相邻位置、Bits Stored/Pixel Representation 和 Rescale Slope/Intercept 建体。完整 HU/原始强度数据保存在 CPU；GPU 仅缓存有限数量的显示切片。

当前仓库包含可测试的 Explicit VR Little Endian 托管解析器和 `IDicomDecoder` 替换边界。Android DCMTK 原生库尚未随仓库交付；接入真实临床样本前必须完成 DCMTK adapter、ABI 打包与样本对照验证，详见验证记录。

## 6. 切片与导航端优先级

没有远端 `SliceState` 时，客户端在 CT 就绪后显示经过规划入点、垂直规划轴的物理切片，`buccal_axis` 指向画面上方。若下发 `SlicePlane`，客户端按 origin/normal/up/offset 采样。

- 同步开启时，连接/开启瞬间以导航端状态为准。本地 OK 手势以 `SliceCommand` 发送变化，导航端应用后广播新的 `SliceState`。
- 同步关闭时，两端可分别浏览；导航端仍可用明确命令发送 `source=NAVIGATION_SOFTWARE` 的眼镜切片。
- 每个本地命令携带 `base_control_version`。导航端接管时递增版本并拒绝旧基线命令；客户端同时终止正在进行的 OK 手势，要求松手后重新开始。

## 7. 布局与观察控制

`DisplayLayout` 分别控制 HUD/CT 组和三维模型的位置与显隐。导航端版本优先；应用后的布局存入设备。`reset_to_default=true` 恢复 HUD `(0,-0.13,1.80)m`、模型 `(0.32,-0.024,1.80)m`，模型默认隐藏。

`ObservationControl` 可独立请求 XR 左眼镜像和 RGB 医生视角。任一画面开启时，客户端使用 XREAL 单例编码器输出一路固定布局 RTP：

- 默认 1280×720、15fps、无音频；左半为实际 XR 左眼，右半为可选 RGB。
- 未请求的区域为黑色。真实 XR 帧不可用时不得用 Unity 其他相机冒充，状态会回报不可用。
- RGB 关闭只释放视频消费者，相机仍可被手势消费者持有。
- 编码格式、RTP 负载类型和导航端 FFmpeg 参数必须在首轮真机联调中固定，不能只凭编辑器结果确认。

客户端用 `ObservationStatus` 回报运行时可用性和错误。

## 8. 模拟服务

`Tools/navigation-simulator` 使用动态 proto 加载，不维护另一份生成代码。它每秒发送 30 帧确定性方向/深度轨迹，并实现带版本的切片接管；可选发送真实 DICOM/STL 文件。

```bash
cd Tools/navigation-simulator
npm install
npm run smoke
npm start -- --port 50051 --dicom-dir /absolute/dicom --teeth /absolute/teeth.stl --drill /absolute/drill.stl
```

`--faults` 每 30 秒插入约 3 秒显式 tracker loss，用于检查动态标记撤下和恢复。模拟阈值只属于模拟上下文，不能作为临床默认值。
