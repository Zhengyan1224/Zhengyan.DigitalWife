# Web PMX Bullet 物理

## 根因与替换

之前 Web 使用 `PmxPoseEvaluator.IntegrateFallbackPhysics`：给每根动态骨骼叠加受重力影响的平移，按质量缩放重力，未实现 PMX 刚体碰撞、关节或旋转惯性。骨骼链上的平移会累积，使裙子和头发变长，也不能产生正常的动作跟随与摆动。

Web Player 现在通过 `WebPmxBulletPhysics` 和 `web-physics.js` 接入官方 Ammo.js 的 Bullet WASM；带刚体的模型不再使用该平移回退。依赖与许可证在 `wwwroot/vendor/ammo`，不使用运行时 CDN。

## PC / Android 对齐

- 球、盒、胶囊形状，PMX 质量、线性/角阻尼、摩擦、弹性、additional damping 和睡眠阈值。
- 刚体初始旋转为 Z→X→Y，使用同样的 Z 反射、骨骼偏移及偏移逆矩阵。弹簧关节采用原 PMX 的位置/旋转限制和六轴刚度。
- 静态刚体作为运动学目标，只更新 motion state，让 Bullet 计算目标运动速度；动态刚体保留积分状态和惯性。
- 按刚体顺序回写骨骼，传递到子骨骼后重建局部/全局矩阵。`DynamicAndBoneMerge` 只回写旋转，保留动画的骨骼位置。
- Bullet 内部固定步长 1/120 秒，最多 10 个子步；慢帧将动画目标和物理一起分步，最终只上传一次可见姿势。
- 首帧、暂停、停止、重新播放和动作循环的启停规则保持原约定；重置移除/重新插入刚体和关节，清除旧接触对及速度。
- 预先保存子骨骼列表和回写缓冲区，避免每个刚体回写时重复扫描整副骨架。

Ammo 的标准绑定未暴露自定义 overlap-filter 回调。适配器保留全部 PMX 刚体碰撞组与掩码，并选择一个未被刚体使用的组用于地面，重现 PC 始终可碰撞的地面。若模型同时使用全部 16 个组，加载会明确报告该限制。

## 验证

```powershell
dotnet run --project tests/Zhengyan.DigitalWife.WebRuntime.Tests
node --test tests/web-physics.test.mjs tests/web-audio.test.mjs tests/web-renderer.test.mjs tests/web-runtime-input.test.mjs
dotnet publish src/Zhengyan.DigitalWife.GamePlayer.Web -c Release --no-restore -o artifacts/web-player
```

物理测试使用实际 vendored WASM。不同质量自由落体的加速度一致；关节链保持长度并随运动学目标产生旋转和滞后；碰撞掩码不屏蔽地面；重置清除旧姿势。真实 Nene/Lamb 的 97 个刚体、132 个关节运行六秒，启动收敛后关节锚点的最大额外偏差约 0.22 PMX 单位，未出现累积拉伸。C# 验证启动、暂停、停止、慢帧子步和缓存动作共享物理世界的释放行为。

最终发布版复测使用 Edge / Intel HD Graphics 520 / ANGLE D3D11，874 × 612 Canvas，关闭 CPU 分析器：实际动作和音频持续运行，裙长恢复正常且有摆动；稳定阶段帧间隔中位数约 21–26 ms，拖拽约 22 ms（P95 34.6 ms）。音频和 WebGL 错误均为 0。完整 Bullet 的求解开销高于旧回退路径，帧率随设备、分辨率和动作变化。
