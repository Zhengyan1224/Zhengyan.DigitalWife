---
id: notes
title: 当前边界与注意事项
category: 边界
objects:
  - Limitations
keywords:
  - notes
  - limits
  - platform
---

# 当前边界与注意事项

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | 当前边界与注意事项 |
| 分类 | 边界 |
| 主要对象 | ``Limitations`` |
| C# 入口 | `runtime limitations` |
| Python 入口 | `snapshot model` |
| 说明 | 当前实现边界、脚本执行模型、平台差异和调试建议。 |

## API 内容

- Python 对象是事件快照，修改命令在函数返回后由 GamePlayer 执行。
- C# 脚本方法调用直接作用于运行时对象；但每次事件都会重新执行 `.csx` 文件，需要跨帧状态时建议使用 `static` 类型保存。
- GUI 控件使用 `absolute` 时坐标和大小是窗口像素；使用 `relative` 时会以项目窗口基准分辨率缩放。Sprite 坐标和鼠标坐标仍是窗口像素，左上角为 `(0, 0)`。
- 3D 世界坐标使用 `System.Numerics.Vector3`，通常 Y 轴向上。
- 音频脚本只控制已注册 Audio 资源的播放状态，不负责动态加载文件。
- 轻量 Collider 用于拾取、触发和简单碰撞判断，不是完整物理模拟。
- PMX 动作、PMX 材质贴图覆盖、TTS 口型、PMX 绑定关系只对 `pmx_model` 有效。
- 如果脚本绑定到 GUI 控件的目标实体为空，GUI 事件不会有实体脚本接收；建议在 GameEditor 里为控件设置 `Target entity`。
- `Scene.Physics.SampleGround(...)` 基于实体 Collider 做地面采样，C# 侧支持 box / capsule / mesh；Python 快照层当前仍只支持 box / capsule。
- `Scene.Navigation` 是轻量 NavMesh 三角图，不是完整 Recast/Unity 风格烘焙；它不会处理角色半径膨胀、动态障碍或自动避障。
