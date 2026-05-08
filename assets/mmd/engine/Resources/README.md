# MMD 运行时资源目录说明

本目录存放 `Zhengyan.DigitalWife.Mmd.Game` 的内置资源。示例项目在构建时会把这里的内容复制到输出目录下的 `Resources/`。

## 子目录说明

### `Shader/`

PMX 模型渲染所需的 GLSL Shader：

- `pmx_model.vert`
- `pmx_model.frag`
- `pmx_edge.vert`
- `pmx_edge.frag`
- `pmx_ground_shadow.vert`
- `pmx_ground_shadow.frag`

供以下类型使用：

- `PmxShader`
- `PmxEdgeShader`
- `PmxGroundShadowShader`

### `MMD/`

内置 Toon 贴图：

- `toon01.bmp` 到 `toon10.bmp`

供以下场景使用：

- PMX Common Toon 回退
- `EmbeddedToonTextureLibrary`

### `Water/`

水面效果纹理：

- `Ocean0_N.dds` 到 `Ocean3_N.dds`
- `Sky.dds`

供以下类型使用：

- `WaterSurfaceComponent`

### `Particles/`

粒子预设默认贴图：

- `Snow.dds`
- `Sakura.dds`
- `Waterfall.png`
- `Stream.png`
- `Fire.png`

供以下类型和预设使用：

- `ParticleSystemComponent`
- `ParticleSystemPresets`

如果这些贴图缺失，部分粒子预设仍然可以回退到程序化纹理，但视觉效果会变差。

### `SpeechLipSyncDictionaries/`

口型驱动字典：

- `kanadic.txt`
- `zh_kanadic.txt`
- `voweldic.txt`

供以下类型使用：

- `SpeechDictionarySet`
- `KanaDictionary`
- `VowelDictionary`
- `SpeechTransformUpdater`

其中：

- `kanadic.txt`
  日语 / 日文汉字到假名映射
- `zh_kanadic.txt`
  中文到日语假名近似映射
- `voweldic.txt`
  假名到 `あ / い / う / え / お` 的元音映射

### `ParticlePresets/`

用于存放运行期保存的粒子参数 `.json` 预设文件。

## 输出目录规则

示例项目会把本目录复制为：

```text
bin/Debug/net9.0/Resources/
```

运行时资源查找只依赖输出目录，不再通过查找解决方案文件名推断仓库根路径。
