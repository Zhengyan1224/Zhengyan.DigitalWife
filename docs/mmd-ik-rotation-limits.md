# Nene 视线 IK 限位修复与回归

`Nene_r2328_kihon_natsu_e511.pmx` 播放 `Lamb.vmd` 时，PC 与 Android 偶尔出现眼球向上翻转。PC/Android 的现代路径共用 `MMDIkSolver`，其中多轴限位原本没有实现；Web 的 `PmxPoseEvaluator` 已执行多轴限位。

`視線IK` 的链接骨骼 `視線` 在渲染坐标下应满足 X ∈ [-8°, 16°]、Y ∈ [-30°, 30°]、Z = 0。超限旋转通过 `両目4視線 → 両目 → 左右目 → 目戻` 的附加旋转传递到眼球。

修复包括：

- [MMDIkSolver](../src/Zhengyan.DigitalWife.Mmd/MMDIkSolver.cs) 按 Web 相同的 yaw/pitch/roll 约定限制链接骨骼的总旋转，再还原 IK 增量。单轴识别要求另外两轴的上下界都为零。
- [PmxModel](../src/Zhengyan.DigitalWife.Mmd/PmxModel.cs) 的 PMX Z 坐标反射只反转 X/Y 旋转范围，Z 范围保持原符号。Web 单轴 Z 分支也使用这一规则。
- [PmxPoseEvaluator](../src/Zhengyan.DigitalWife.Mmd/PmxRuntime/PmxPoseEvaluator.cs) 保留相邻关键帧之间的上一帧姿态，但在恰好到达下一关键帧时返回该关键帧。旧边界行为会在 Lamb 后段使用不同的头部姿态，干扰视线 IK 对照。

回归入口：

```text
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "MMD IK"
dotnet run --project tests/Zhengyan.DigitalWife.WebRuntime.Tests
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests
```

IK 回归覆盖 X/Y/Z 正反向单轴、单侧范围、多轴、三轴锁定、带动画基准旋转的限位，以及通过 PMX 加载器验证 X/Y 反射和单轴/多轴正向 Z 范围。小型 PMX 用例由测试直接生成。

真实模型用例读取 `src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/web-game/assets/models/Nene_r2328_kihon_natsu_e511/Nene_r2328_kihon_natsu_e511.pmx` 和同一 Web 包的 `assets/motions/Lamb.vmd`；Nene 模型不随 Git 提交，运行前需要本地资源。

关闭物理、使用单一 Lamb 动作层并按 PC/Android 的混合入口归一化动画旋转后，0–7191 帧的 14,383 个整数帧/半帧采样全部符合视线限制。眼部局部旋转与 Web 的最大差异约 0.279167°，回归容差为 0.3°。这验证骨骼求解行为，手机与浏览器实际画面仍需运行播放器确认。

另外用提交 `faef45850900bab2365efd3d18936eded239bf17` 中修复前的 `MMDIkSolver.cs`、`PmxModel.cs` 构建隔离基线，在相同全程采样点比较左右腿、膝盖、脚踝和脚尖共 8 根骨骼，全局矩阵的最大变化为零。PC 与 Web 的既有腿部求解差异没有因此改变。

2026-11-06 验证结果：IK 回归 3/3、Web C# 回归、31 项 Web JavaScript 检查、PMX/VMD 自检均通过；PC/Web Debug 与 Android ARM64 Release 构建成功。Android 构建有 4 条 SDL 原生库页面大小和重复打包警告（XA0141、XA4301）。

Android SDK 的 Java 互操作后处理会向 NuGet 缓存写入 `.jlo.xml` 元数据；第一次构建被沙箱目录权限阻止，获得沙箱外构建授权后已成功。尚未安装到手机进行画面对照。
