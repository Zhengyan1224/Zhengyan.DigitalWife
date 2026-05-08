# Zhengyan.DigitalWife.Mmd

`Zhengyan.DigitalWife.Mmd` 是底层 PMX / VMD 运行时。它负责：

- PMX 解析
- VMD 解析与动画评估
- 骨骼层级、Morph、IK
- Bullet 物理集成

如果你只需要“开箱即用”的跨平台渲染和交互层，请优先看 [Zhengyan.DigitalWife.Mmd.Game](../Zhengyan.DigitalWife.Mmd.Game/README.md)。本项目更适合做底层数据处理、工具链或自定义渲染集成。

## 主要 API

### 解析层

- `PmxParsing`
- `VmdParsing`

用于直接解析原始 PMX / VMD 文件结构。

### 运行时模型

- `MMDModel`
  抽象基类。
- `PmxModel`
  PMX 模型运行时实现。
- `MMDNode`
- `MMDMorph`
- `MMDMaterial`
- `MMDMesh`
- `MMDIkSolver`

### 动画

- `VmdAnimation`
- `VmdNodeController`
- `VmdMorphController`
- `VmdIkController`

### 物理

- `MMDPhysics`
- `MMDPhysicsManager`
- `MMDRigidBody`
- `MMDJoint`

## 典型使用方式

### 1. 加载 PMX 模型

```csharp
using Zhengyan.DigitalWife.Mmd;

PmxModel model = new();
bool loaded = model.Load(
    path: "Character/Body/Body.pmx",
    mmdDataDir: "Resources/MMD");

if (!loaded)
{
    throw new InvalidOperationException("Failed to load PMX model.");
}

Console.WriteLine(model.GetNodes().Length);
Console.WriteLine(model.GetMorphs().Length);
Console.WriteLine(model.GetMaterials().Length);
```

### 2. 加载 VMD 并推进动画

```csharp
using Zhengyan.DigitalWife.Mmd;

PmxModel model = new();
model.Load("Character/Body/Body.pmx", "Resources/MMD");

VmdAnimation animation = new();
if (!animation.Load("Motion/basic_walk.vmd", model))
{
    throw new InvalidOperationException("Failed to load VMD.");
}

model.BeginAnimation();
model.UpdateAllAnimation(animation, vmdFrame: 30.0f, physicsElapsed: 1.0f / 60.0f);
model.EndAnimation();
model.Update();
```

### 3. 查找骨骼和 Morph

```csharp
MMDNode? head = model.FindNode(node => node.Name == "頭");
MMDMorph? smile = model.FindMorph(morph => morph.Name == "笑い");

if (smile is not null)
{
    smile.Weight = 1.0f;
    model.UpdateMorphAnimation();
}
```

## 适合什么场景

- 你要自己实现渲染器，不想依赖 `Mmd.Game`。
- 你要做 PMX / VMD 处理工具。
- 你要在上层引擎中接入 MMD 运行时，而不是直接使用现成 Demo。
