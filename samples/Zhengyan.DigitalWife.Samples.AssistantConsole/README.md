# Zhengyan.DigitalWife.Samples.AssistantConsole

`Zhengyan.DigitalWife.Samples.AssistantConsole` 是控制台语音助手示例，演示如何把 `Assistant`、音频 Provider、识别器、LLM 和 TTS 组合成完整链路。

## 功能

- 录音并自动静音截断
- 识别器回退
- OpenAI 协议兼容 LLM 流式输出
- 句子级 TTS 与播放
- 可选唤醒词检测
- 设备枚举与单文件转写

## 配置文件

- `appsettings.json`
- `appsettings.Local.json`
- `appsettings.Local.example.json`

环境变量前缀：

- `DIGITALWIFE_`
- `SPEECHBRIDGE_`（兼容旧配置）

## 快速开始

### 1. 复制本地配置

```powershell
Copy-Item appsettings.Local.example.json appsettings.Local.json
```

### 2. 填写 LLM 参数

编辑 `appsettings.Local.json`：

- `Demo:Audio:PlaybackBackend`
- `Demo:Llm:BaseUrl`
- `Demo:Llm:ApiKey`

### 3. 查看设备

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole/Zhengyan.DigitalWife.Samples.AssistantConsole.csproj -- --list-devices
```

### 4. 运行单轮

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole/Zhengyan.DigitalWife.Samples.AssistantConsole.csproj -- --run-once
```

### 5. 指定设备

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole/Zhengyan.DigitalWife.Samples.AssistantConsole.csproj -- --run-once --input-device 1 --output-device 3
```

`Demo:Audio:PlaybackBackend` 支持：

- `PortAudio`：录音和播放都走 `PortAudio`，`--output-device` 有效。
- `OpenAL`：录音继续走 `PortAudio`，TTS 播放改走 `OpenAL`，`--output-device` 会被忽略。

### 6. 转写文件

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole/Zhengyan.DigitalWife.Samples.AssistantConsole.csproj -- --transcribe-file test.wav
```

## Linux 麦克风输入排查

`AssistantConsole` 的录音也走 `PortAudio`，所以 Linux 下会遇到和 `DigitalHuman` 类似的问题：默认输入设备不对、采样率不被硬件支持、设备被占用。

建议顺序：

1. 先看系统设备：

```bash
arecord -l
arecord -L
```

2. 再看 `PortAudio` 设备索引：

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole/Zhengyan.DigitalWife.Samples.AssistantConsole.csproj -- --list-devices
```

3. 如果录音打开失败，优先调整这些配置：

- `Demo:Audio:InputDeviceIndex`
- `Demo:Capture:SampleRate`
- `Demo:WakeWord:CaptureOptions:SampleRate`

4. Linux 上优先试 `48000`，不行再试 `44100`。

常见现象：

- `paInvalidSampleRate`：采样率不被设备支持
- 没有录音但程序能跑：输入设备索引不对
- `Device or resource busy`：麦克风被别的进程占用

## 代码入口

入口文件：

- `Program.cs`

它会：

1. 读取配置
2. 注册 `AddDigitalWifeAssistantCore()`
3. 注册音频、LLM、ASR、TTS、唤醒词 Provider
4. 启动 `DemoHostedService`

如果你要做自己的控制台应用，可以直接参考它的 DI 和配置装配方式。
