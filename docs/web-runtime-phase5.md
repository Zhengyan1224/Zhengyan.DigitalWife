# Web Runtime 阶段 5：预编译 Web 脚本【已实现】

> 状态：**已实现**
>
> 对应设计文档：`docs/web-runtime-design.md` 的阶段 5

## 1. 交付范围

阶段 5 建立了独立于桌面和 Android 的 Web 脚本 ABI。GameEditor 在导出准备阶段读取 `.csx`，尝试生成 `compiled/web/*.dll`；Web GamePlayer 只加载已经生成的托管程序集，不在浏览器中运行 Roslyn。

共享 ABI 位于 `GamePlayer.Runtime.Core`：

- `IWebGameScript.Start`；
- `IWebGameScript.Update`；
- `IWebGameScript.OnEvent`；
- `WebScriptContext`，提供场景、实体、输入和计时；
- `WebScriptEvent`，为 GUI、加载和平台事件保留统一事件入口。

## 2. GameEditor 预编译

`WebCSharpScriptPrecompiler` 为每个工程生成：

```text
compiled/web/
  <script-path>.dll
  manifest.json
```

清单记录 API 版本、目标框架、源脚本、程序集和 SHA-256。脚本不能使用 Web ABI 时，预编译器把 Roslyn 错误写入 `compiled/web/manifest.json`，并继续完成 PC/Android `.dwgame` 导出；Web 包不会把不兼容脚本伪装成可运行脚本。

## 3. Web 运行时

`WebScriptRuntime` 完成以下工作：

1. 读取并验证脚本清单；
2. 校验 API 版本、程序集 SHA-256 和程序集引用；
3. 按 `dependencies` 拓扑排序加载脚本；
4. 查找清单指定类型/工厂，或查找第一个 `IWebGameScript` 类型；
5. 调用 `Start`、每帧调用 `Update`，并隔离脚本异常；
6. 通过 `DispatchEvent` 分发统一事件；
7. 将循环依赖、缺失依赖、平台程序集引用和反射错误转换为诊断信息。

`WebScriptCompatibility` 会拒绝 Android、桌面窗口、Veldrid、Silk.NET、OpenTK 和 `Microsoft.Win32` 等 Web 不可用引用。程序集仍属于受信任游戏代码，ABI 校验不是安全沙箱。

## 4. 验证

已验证：

```text
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj --no-restore -p:NuGetAudit=false -v minimal
dotnet build src/Zhengyan.DigitalWife.GameEditor/Zhengyan.DigitalWife.GameEditor.csproj --no-restore -p:NuGetAudit=false -v minimal
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests --no-restore
node tests/web-runtime-smoke.mjs
```

GameEditor 的多场景导出回归继续验证 Android 和 desktop 脚本集合；Web 脚本则按 Web API 兼容性生成程序集或保留逐脚本错误诊断。

## 5. 阶段状态

阶段 5 已完成。后续阶段可以在现有 ABI 上增加更多 Web 平台服务，例如 Web Audio、存档、网络和语音，但不应让脚本直接依赖浏览器 API 或桌面播放器类型。
