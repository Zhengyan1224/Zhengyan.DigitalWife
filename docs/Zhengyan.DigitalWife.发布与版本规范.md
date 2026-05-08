# 发布与版本规范

本文档用于说明 `Zhengyan.DigitalWife` 语音侧类库的版本管理、NuGet 打包和发布建议。

## 建议发布为 NuGet 的项目

建议发布以下主包：

1. `Zhengyan.DigitalWife.Abstractions`
2. `Zhengyan.DigitalWife.Assistant`
3. `Zhengyan.DigitalWife.Audio.PortAudio`
4. `Zhengyan.DigitalWife.Llm.OpenAI`
5. `Zhengyan.DigitalWife.Speech.SherpaOnnx`
6. `Zhengyan.DigitalWife.Speech.WhisperNet`

## 不建议发布为 NuGet 的项目

- `Zhengyan.DigitalWife.Samples.AssistantConsole`
- `Zhengyan.DigitalWife.Tools.*`
- `Zhengyan.DigitalWife.Mmd`
- `Zhengyan.DigitalWife.Mmd.Game`
- `Zhengyan.DigitalWife.Samples.Mmd*`

原因：

- `samples/` 更适合做示例与调试入口
- `tools/` 更适合做仓库维护工具
- `Mmd` 侧当前更偏源码仓库与 Demo 形态，而不是稳定 NuGet 组件交付形态

## 打包准备

当前主类库已经具备基础打包字段：

- `IsPackable`
- `PackageId`
- `PackageReadmeFile`
- `Description`

并且每个主类库都带有单独的 `README.md`，可以直接进入 NuGet 包说明页。

## 版本号建议

推荐使用语义化版本：

```text
MAJOR.MINOR.PATCH
```

规则：

- `MAJOR`
  破坏性变更
- `MINOR`
  向后兼容的新功能
- `PATCH`
  向后兼容的修复

示例：

- `1.0.0`
  首个稳定版本
- `1.1.0`
  增加新 Provider、扩展接口或增加新能力
- `1.1.1`
  Bug 修复、文档修正、打包修正

## 版本同步策略

如果你希望语音侧这些包保持同一发布线，建议统一版本号：

- `Zhengyan.DigitalWife.Abstractions`
- `Zhengyan.DigitalWife.Assistant`
- `Zhengyan.DigitalWife.Audio.PortAudio`
- `Zhengyan.DigitalWife.Llm.OpenAI`
- `Zhengyan.DigitalWife.Speech.SherpaOnnx`
- `Zhengyan.DigitalWife.Speech.WhisperNet`

优点：

- 依赖关系更容易对齐
- 文档、示例和问题排查更简单

## 打包命令

### 打包单个项目

```powershell
dotnet pack src/Zhengyan.DigitalWife.Abstractions/Zhengyan.DigitalWife.Abstractions.csproj -c Release -o artifacts/packages
```

### 打包全部语音侧主类库

```powershell
dotnet pack src/Zhengyan.DigitalWife.Abstractions/Zhengyan.DigitalWife.Abstractions.csproj -c Release -o artifacts/packages
dotnet pack src/Zhengyan.DigitalWife.Assistant/Zhengyan.DigitalWife.Assistant.csproj -c Release -o artifacts/packages
dotnet pack src/Zhengyan.DigitalWife.Audio.PortAudio/Zhengyan.DigitalWife.Audio.PortAudio.csproj -c Release -o artifacts/packages
dotnet pack src/Zhengyan.DigitalWife.Llm.OpenAI/Zhengyan.DigitalWife.Llm.OpenAI.csproj -c Release -o artifacts/packages
dotnet pack src/Zhengyan.DigitalWife.Speech.SherpaOnnx/Zhengyan.DigitalWife.Speech.SherpaOnnx.csproj -c Release -o artifacts/packages
dotnet pack src/Zhengyan.DigitalWife.Speech.WhisperNet/Zhengyan.DigitalWife.Speech.WhisperNet.csproj -c Release -o artifacts/packages
```

## 发布前检查清单

- 所有 `README.md` 已更新
- `PackageId` 与命名空间前缀一致
- 依赖版本已确认
- 示例代码与文档中的项目路径已同步
- `dotnet build Zhengyan.DigitalWife.slnx` 已通过
- 如需发布包，先本地执行 `dotnet pack`
