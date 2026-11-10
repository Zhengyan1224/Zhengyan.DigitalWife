# 项目全局阴影质量

在 GameEditor 的 Project 面板展开 **Shadow quality**，启用 **Use global shadow settings**。
这些设置保存到 `game.project.json`，对整个项目的所有场景、摄像机和运行平台生效，编辑器预览会即时更新。
关闭全局设置，或旧项目没有 `shadows` 字段时，保留各平台原有的默认阴影效果。

| 设置 | 范围 | 含义 |
| --- | --- | --- |
| Directional shadow resolution | 256、512、1024、2048、4096、8192 | 方向光阴影贴图边长 |
| Point / spot shadow resolution | 256、512、1024、2048 | 点光源每个立方体面、聚光灯每个图集区域的边长 |
| Shadow filter samples | 1、4、9、16、25、49 | PCF 过滤位置数 |
| Shadow filter radius | 0～8 texels | 过滤采样覆盖的半径，以阴影贴图像素为单位 |

启用后的默认值为方向光 2048、局部光源 1024、9 个过滤位置、半径 1。
PC 和 Android 的局部光源使用 4×4 图集，因此 1024 的每面分辨率对应 4096 的图集边长。
Web 使用相同的图集布局。图集区域保留边距，实际绘制区域略小于设置的每面边长。

例如：

```json
"shadows": {
  "directionalResolution": 4096,
  "localLightResolution": 1024,
  "filterSamples": 25,
  "filterRadius": 2.0
}
```

分辨率越高，阴影细节越清晰，也会占用更多显存；采样次数越高，过滤结果越细腻，也会增加着色开销。
半径越大，边缘越柔和。在相同像素半径下，提高分辨率会缩小过滤在世界空间中的范围。
半径为 0 时，所有过滤位置重合；只有一个过滤位置时，半径不改变采样位置。
过滤位置数与场景 MSAA 无关。每个位置的双线性阴影比较还可能包含相邻像素的深度比较。

PC OpenGL、PC Vulkan、Android OpenGL ES、Android Vulkan 和 WebGL2 均使用这组全局参数。
设备纹理尺寸限制会降低实际分辨率；OpenGL / OpenGL ES / WebGL2 在阴影帧缓冲不可用时还会继续尝试更小的贴图。
Android 明确选择的 low / medium 质量配置仍保留分辨率和光源数量限制，auto / high 使用项目设置。
现有灯光阴影开关、模型接收阴影开关、阴影强度和 Toon 模式继续生效。
