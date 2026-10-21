# 夜晚天空盒资源

## 暗色星空和月亮（推荐）

`dark_starry_moon.jpg` 是 8192 × 4096（2:1）的等距柱状全景 JPG。天空底色接近黑蓝色，星星保留亮度，月亮具有月面纹理和较弱的光晕，可直接由引擎的 `SkyboxComponent` 加载。

在 GameEditor 的场景 Inspector → `Skybox` → `Skybox texture` 中填写：

```text
app:Resources/Skybox/dark_starry_moon.jpg
```

建议设置 `Skybox exposure = 1.0` 和白色 Tint；不需要像较亮的月升天空那样用极低曝光压暗整张图。切回白天时填写 `app:Resources/Skybox/autumn_field_puresky.jpg`。资源会随现有的目录通配规则加入 GameEditor、GamePlayer 和 Android GamePlayer，无需逐张注册。

来源与许可：

- 星空：[Night Sky HDRI 001 — ambientCG](https://ambientcg.com/view?id=NightSkyHDRI001)，使用 [8K 资源包](https://ambientcg.com/get?file=NightSkyHDRI001_8K.zip)中的 `NightSkyHDRI001_8K_TONEMAPPED.jpg`。采用 [CC0 1.0](https://docs.ambientcg.com/license/)，允许修改和再分发。
- 月面：[CGI Moon Kit — NASA's Scientific Visualization Studio](https://svs.gsfc.nasa.gov/4720/)，使用 [LROC 月面颜色纹理](https://svs.gsfc.nasa.gov/vis/a000000/a004700/a004720/lroc_color_2k.jpg)。署名：NASA's Scientific Visualization Studio / Ernie Wright；数据：NASA/LRO/LROC。该来源通常不受美国版权保护，使用遵循 [NASA 图像和媒体使用说明](https://www.nasa.gov/nasa-brand-center/images-and-media/)，保留来源署名；此衍生贴图不表示 NASA 为引擎背书。
- 处理：从星空中分离局部星点与平滑背景，压暗背景、增强星点对比；按球面方向投影月面纹理，并添加弱光晕。月亮仰角为 28°，角直径为 2.8°，为便于游戏画面辨识而放大，并非天文角尺寸。
- 可复现脚本：[tools/Skybox/build_dark_night.py](../../../../../tools/Skybox/build_dark_night.py)，需要 Python、Pillow、numpy 和 OpenCV。
- 获取和处理日期：2026-10-21。
- 输出大小：16,389,305 字节。
- 输出 SHA-256：`a68408fb2fee0cfde4106b06e07a60983107281288a1a176ea447bb4521dd5f3`。
- 星空输入 SHA-256：`cba81a513d6ee1efa512859032a9310d0f2ca1f607ff9d67e9b2c295dc860fb1`。
- 月面输入 SHA-256：`f7130a1822681fa7512d7dcfd40db8c10b9ba4f06777910348698260ed7a2170`。

复现命令（在仓库根目录运行，输入文件先从上述来源下载）：

```text
python tools/Skybox/build_dark_night.py NightSkyHDRI001_8K_TONEMAPPED.jpg lroc_color_2k.jpg assets/mmd/engine/Resources/Skybox/dark_starry_moon.jpg
```

## 较亮的月升天空

`qwantani_moonrise_puresky.jpg` 为保留的 Poly Haven 原图，尺寸为 8192 × 4096（2:1）。它的天空底色较亮、月光晕较强。使用路径为 `app:Resources/Skybox/qwantani_moonrise_puresky.jpg`。

- 资源：[Qwantani Moonrise (Pure Sky) — Poly Haven](https://polyhaven.com/a/qwantani_moonrise_puresky)
- 作者：Greg Zaal（摄影）、Jarod Guest（处理）。
- 许可：[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)，允许商业使用、修改和再分发，详见 [Poly Haven 许可说明](https://polyhaven.com/license)。
- 下载：[官方 Tonemapped JPG](https://dl.polyhaven.org/file/ph-assets/HDRIs/extra/Tonemapped%20JPG/qwantani_moonrise_puresky.jpg)
- 下载信息：[Poly Haven 文件 API](https://api.polyhaven.com/files/qwantani_moonrise_puresky)
- 获取日期：2026-10-21。
- 处理：保留官方 JPG 原文件，未裁剪、缩放或重新编码。
- 文件大小：23,443,425 字节。
- MD5：`e4c224450bc69510254f9024fb46ab65`（与官方文件 API 一致）。
