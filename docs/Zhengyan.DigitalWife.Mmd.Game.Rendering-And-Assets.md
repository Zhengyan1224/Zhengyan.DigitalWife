# 资源、渲染与跨平台说明

本文说明 `Zhengyan.DigitalWife.Mmd.Game` 的资源目录规则、运行时资源解析方式，以及跨平台使用时需要注意的事项。

## 仓库中的资源来源

### 引擎内置资源

```text
assets/mmd/engine/Resources/
├─ MMD/
├─ Particles/
├─ ParticlePresets/
├─ Shader/
├─ SpeechLipSyncDictionaries/
└─ Water/
```

### 示例业务资源

```text
assets/mmd/samples/GameData/
├─ BGM/
├─ Character/
├─ Motion/
└─ Scene/
```

## 构建时的复制规则

示例项目会把资源复制到输出目录：

```xml
<ItemGroup>
  <None Include="$(DigitalWifeMmdSampleDataDir)**\*">
    <Link>GameData\%(RecursiveDir)%(Filename)%(Extension)</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="$(DigitalWifeMmdEngineAssetsDir)**\*">
    <Link>Resources\%(RecursiveDir)%(Filename)%(Extension)</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

最终输出目录通常类似：

```text
bin/Debug/net9.0/
├─ GameData/
└─ Resources/
```

## 运行时资源解析规则

当前 `Zhengyan.DigitalWife.Mmd.Game` 运行时只依赖输出目录下的：

- `Resources/`
- `GameData/`

资源查找以 `AppContext.BaseDirectory` 为起点，不再通过查找 `*.sln` 文件名推断仓库根路径。

## PMX 模型相关资源

### 模型文件

通常包括：

- `.pmx`
- 模型贴图 `.dds` / `.bmp` / `.png` / `.jpg`
- 球面贴图 `.sph` / `.spa`
- 动作文件 `.vmd`

### 相对路径规则

`PmxModelComponent` 加载 PMX 时，会以 PMX 文件所在目录作为模型资源基础目录。也就是说：

- PMX 内部如果写的是相对贴图路径
- 这些贴图应与模型放在相对匹配的位置

示例：

```text
Content/Model/MyCharacter/
├─ MyCharacter.pmx
├─ body.dds
├─ face.bmp
└─ hair.sph
```

## 内置 Toon 贴图

内置 Toon 贴图位于：

- `Resources/MMD/toon01.bmp`
- ...
- `Resources/MMD/toon10.bmp`

它们会在 PMX Common Toon 模式下作为默认回退资源使用。

## Shader 资源

内置 Shader 位于：

- `Resources/Shader/pmx_model.vert`
- `Resources/Shader/pmx_model.frag`
- `Resources/Shader/pmx_edge.vert`
- `Resources/Shader/pmx_edge.frag`
- `Resources/Shader/pmx_ground_shadow.vert`
- `Resources/Shader/pmx_ground_shadow.frag`

如果这些文件未复制到输出目录，`PmxModelComponent`、描边和地面阴影都无法正常工作。

## 水面资源

`WaterSurfaceComponent` 默认依赖：

- `Resources/Water/Ocean0_N.dds`
- `Resources/Water/Ocean1_N.dds`
- `Resources/Water/Ocean2_N.dds`
- `Resources/Water/Ocean3_N.dds`
- `Resources/Water/Sky.dds`

如果你想替换水面贴图，可以在构造时显式传入：

- `normalMapPaths`
- `skyTexturePath`

## 粒子资源

`ParticleSystemPresets` 默认可能使用：

- `Resources/Particles/Snow.dds`
- `Resources/Particles/Sakura.dds`
- `Resources/Particles/Waterfall.png`
- `Resources/Particles/Stream.png`
- `Resources/Particles/Fire.png`

如果某些纹理缺失，部分预设会回退到程序化纹理。

## 口型字典资源

`SpeechDictionarySet.LoadFromDirectory()` 默认需要：

- `kanadic.txt`
- `voweldic.txt`

仓库提供的默认目录：

- `Resources/SpeechLipSyncDictionaries/`

## 跨平台注意事项

### 图形

- 当前窗口上下文基线是 `OpenGL ES 3.0`
- 这是为了提高 Windows / WGL 下的兼容性

### 音频

- `EnableAudio = true` 时会尝试初始化 OpenAL
- 如果本机没有可用 OpenAL，`Game.Audio` 会是 `null`
- 游戏仍可继续运行

### OpenCL

- `GameOptions.UseOpenCL` 控制是否启用 OpenCL 路径
- 如果目标机器没有稳定的 OpenCL 环境，可以关闭它

### 路径

- 始终使用 `Path.Combine`
- 不要硬编码 Windows 分隔符
- 不要依赖当前工作目录

## 建议

- 把“业务资源”和“引擎资源”分开管理
- 构建时复制到输出目录，而不是在运行时回源查仓库
- 对外发布时，把 `Resources/` 与 `GameData/` 一起打包
