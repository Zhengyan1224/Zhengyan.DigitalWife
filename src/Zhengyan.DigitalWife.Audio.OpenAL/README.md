# Zhengyan.DigitalWife.Audio.OpenAL

`Zhengyan.DigitalWife.Audio.OpenAL` 提供基于 `Silk.NET.OpenAL` 的 `IAudioPlayer` 实现。

它当前实现了：

- `IAudioPlayer`
- `IAudioPlaybackTiming`

适合：

- 需要跨平台扬声器播放
- 希望把录音和播放后端拆开，保留 `PortAudio` 录音，同时切换成 `OpenAL` 播放
- 需要整段 `AudioData` 和流式 `IAsyncEnumerable<AudioChunk>` 两种播放模式
