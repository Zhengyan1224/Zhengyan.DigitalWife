---
id: tts
title: TTS / 人物说话
category: 音频
objects:
  - RuntimeEntity
  - RuntimeVoiceOptions
keywords:
  - tts
  - speak
  - voice
---

# TTS / 人物说话

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | TTS / 人物说话 |
| 分类 | 音频 |
| 主要对象 | ``RuntimeEntity``, ``RuntimeVoiceOptions`` |
| C# 入口 | `Entity.Speak, SpeakWithCallback` |
| Python 入口 | `entity.speak` |
| 说明 | 实体说话、TTS 参数、完成回调和人物语音示例。 |

## API 内容

`Entity.Speak` 是非阻塞的：调用后会立即返回，音频和口型在运行时异步播放。需要播放结束后继续逻辑时，使用回调。

前提条件：

- 项目 `Voice / TTS` 中启用运行时 TTS。
- TTS 模型路径配置正确。
- 说话实体必须是 PMX 模型，且启用了口型字典时才会驱动口型。
- `PreloadOnSceneLoad` 开启时，GamePlayer 会在场景加载时预热 TTS，避免首次说话明显卡顿。

C#：

```csharp
Entity.Speak("你好");
Entity.Speak("你好", speakerId: 0);
Entity.Speak("语速稍快一点", speakerId: 0, speed: 1.15f);
Entity.Speak("音量小一点", speakerId: 0, speed: 1.0f, volume: 0.7f);

Entity.Speak("播放完后转身", 0, 1.0f, 1.0f, () =>
{
    Entity.RotateY(180);
});

Entity.SpeakWithCallback("播放完触发脚本事件", "after_intro");

if (IsSpeechEvent && SpeechCallbackName == "after_intro")
{
    Entity.SetPosition(0, 0, 0);
}

Entity.StopSpeaking();
```

Python：

```python
def start(entity, scene, input, audio):
    entity.speak("你好", speaker_id=0, speed=1.0, volume=1.0)
    entity.speak("播放完触发回调", speaker_id=0, speed=1.0, volume=1.0, on_completed="after_intro")

def after_intro(entity, scene, input, audio):
    entity.rotate_y(180)

def update(entity, scene, input, audio, delta_seconds):
    if input.is_key_down("Escape"):
        entity.stop_speaking()
```
