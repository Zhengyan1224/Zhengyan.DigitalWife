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

### `Skybox/`

场景天空盒贴图：

- `autumn_field_puresky.jpg`：白天天空。
- `dark_starry_moon.jpg`：暗色星空和带月面细节的月亮，8192 × 4096，建议曝光 `1.0`。
- `qwantani_moonrise_puresky.jpg`：较亮的月升天空和强月光晕，8192 × 4096。

供以下类型使用：

- `SkyboxComponent`
- `GameEditor` / `GamePlayer` 场景 `Skybox` 设置

默认路径为：

```text
app:Resources/Skybox/autumn_field_puresky.jpg
```

在 GameEditor 的场景 Inspector → `Skybox` → `Skybox texture` 中，修改贴图路径即可切换天空：

| 天空 | 贴图路径 |
| --- | --- |
| 白天 | `app:Resources/Skybox/autumn_field_puresky.jpg` |
| 夜晚（暗色星空和月亮，推荐） | `app:Resources/Skybox/dark_starry_moon.jpg` |
| 较亮的月升天空 | `app:Resources/Skybox/qwantani_moonrise_puresky.jpg` |

`app:` 表示从程序输出目录开始查找。现有项目资源规则会自动将这些贴图复制到 GameEditor / GamePlayer 输出目录下的 `Resources/Skybox/`，Android GamePlayer 也会将其打包为内置资源。暗色夜空建议使用 `Skybox exposure = 1.0` 和白色 Tint，其天空底色已单独压暗，星星仍保持明亮。各贴图的来源、处理和许可信息见 [Skybox/README.md](Skybox/README.md)。

天空贴图只改变背景；如需场景物体也呈现夜间亮度，可在 `Lighting` 中调整平行光和环境光。

### `Fonts/`

ImGui 界面使用的内置中文字体：

- `NotoSansCJKsc-Regular.otf`

供以下示例界面使用：

- `GameEditor`
- `GamePlayer`
- `DigitalHuman`
- `MmdDemo`

示例项目启动时会优先从输出目录读取：

```text
Resources/Fonts/NotoSansCJKsc-Regular.otf
```

如果该文件不存在，才会回退到系统字体。这样 Linux 发行版即使没有安装 CJK 字体包，也不会导致 ImGui 中文乱码。字体来自 Google Noto CJK，采用 SIL Open Font License 1.1。

### `Particles/`

粒子预设默认贴图：

- `Snow.dds`
- `Sakura.dds`
- `Snow.png`
- `Sakura.png`
- `Waterfall.png`
- `Stream.png`
- `Fire.png`

供以下类型和预设使用：

- `ParticleSystemComponent`
- `ParticleSystemPresets`

如果这些贴图缺失，部分粒子预设仍然可以回退到程序化纹理，但视觉效果会变差。

其中：

- `Snow.png`
- `Sakura.png`
- `Fire.png`
- `Stream.png`
- `Waterfall.png`

当前版本基于 Kenney `Particle Pack` 的 CC0 透明粒子素材重新整理和适配，保留了现有文件名和尺寸，以便直接兼容 `ParticleSystemPresets.Fire/Stream/Waterfall`。

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
