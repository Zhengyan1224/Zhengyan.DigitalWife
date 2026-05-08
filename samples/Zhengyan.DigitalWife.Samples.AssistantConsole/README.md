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

### 6. 转写文件

```powershell
dotnet run --project samples/Zhengyan.DigitalWife.Samples.AssistantConsole/Zhengyan.DigitalWife.Samples.AssistantConsole.csproj -- --transcribe-file test.wav
```

## 代码入口

入口文件：

- `Program.cs`

它会：

1. 读取配置
2. 注册 `AddDigitalWifeAssistantCore()`
3. 注册音频、LLM、ASR、TTS、唤醒词 Provider
4. 启动 `DemoHostedService`

如果你要做自己的控制台应用，可以直接参考它的 DI 和配置装配方式。
