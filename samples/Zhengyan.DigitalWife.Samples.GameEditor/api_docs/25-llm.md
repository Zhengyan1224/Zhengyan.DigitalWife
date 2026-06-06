---
id: llm
title: LLM / OpenAI-compatible API
category: AI
objects:
  - RuntimeLlm
  - RuntimeLlmScriptEvent
  - RuntimeLlmChatMessage
keywords:
  - llm
  - openai
  - chat
  - stream
---

# LLM / OpenAI-compatible API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | LLM / OpenAI-compatible API |
| 分类 | AI |
| 主要对象 | ``RuntimeLlm``, ``RuntimeLlmScriptEvent``, ``RuntimeLlmChatMessage`` |
| C# 入口 | `Scene.Llm.ChatAsync/StreamChatAsync/StartChat` |
| Python 入口 | `scene.llm.chat/stream_chat/start_chat` |
| 说明 | OpenAI-compatible LLM 完整请求、流式请求、后台回调和消息列表。 |

## API 内容

GamePlayer 支持从脚本调用 OpenAI-compatible `/v1/chat/completions` 接口，并支持流式输出。配置在 GameEditor 的 `Project` 标签页 `LLM / OpenAI-compatible` 中，也会保存到 `game.project.json` 的 `llm` 节点。

配置字段：

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 是否启用脚本 LLM。未启用时调用会报错。 |
| `BaseUrl` | API 根地址，例如 `https://api.openai.com` 或私有供应商地址。 |
| `ApiKeyEnvironmentVariable` | API Key 环境变量名，默认 `OPENAI_API_KEY`。推荐使用它，避免把密钥写入工程文件。 |
| `ApiKey` | 直接写入工程的 API Key 覆盖值。不建议提交到版本库。 |
| `Model` | 默认模型名，例如 `gpt-4o-mini`、`qwen-plus`、私有模型名等。 |
| `ChatCompletionsPath` | 默认 `/v1/chat/completions`。如果供应商路径不同可在这里改。 |
| `TimeoutSeconds` | 请求超时时间。 |
| `DefaultTemperature` | 默认温度；为空则不发送 `temperature` 字段。 |

### C# LLM

`Scene.Llm` 提供三类调用：

- `await Scene.Llm.ChatAsync(...)`：等待完整结果，适合加载脚本、短请求或不介意等待的逻辑。
- `await foreach (var update in Scene.Llm.StreamChatAsync(...))`：当前脚本事件内同步流式读取。注意如果在 GUI 事件里直接等待，会阻塞这次脚本事件，画面要等事件结束后才继续刷新。
- `Scene.Llm.StartChat(...)`：后台请求，流式 delta 会通过 `IsLlmEvent` 回到同一个实体脚本，适合运行时 UI 和对话。

C# LLM API：

| API | 说明 |
| --- | --- |
| `Scene.Llm.Enabled` | 当前项目是否启用 LLM。 |
| `Scene.Llm.Provider` | Provider 名称。 |
| `Scene.Llm.BaseUrl` | API 根地址。 |
| `Scene.Llm.Model` | 默认模型名。 |
| `Scene.Llm.ChatCompletionsPath` | Chat Completions 路径。 |
| `Scene.Llm.DefaultTemperature` | 默认温度，可能为 `null`。 |
| `ChatAsync(text, systemPrompt, model, temperature)` | 非流式返回完整文本。 |
| `StreamChatAsync(text, systemPrompt, model, temperature)` | 按文本 prompt 发起流式请求。 |
| `StreamChatAsync(messages, model, temperature)` | 按消息列表发起流式请求。 |
| `StartChat(entity, text, systemPrompt, model, temperature, requestId, onDeltaCallback, onCompletedCallback, onErrorCallback)` | 后台流式请求，通过脚本事件回调。 |

C# 阻塞式完整结果：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    string answer = await Scene.Llm.ChatAsync(
        "用一句话介绍这片海。",
        systemPrompt: "你是游戏中的旁白，回答要短。");

    Scene.GetGuiControl("LLM Output")?.SetValue(answer);
}
```

C# 事件内同步流式读取：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    RuntimeGuiControl? output = Scene.GetGuiControl("LLM Output");
    output?.SetValue("");

    await foreach (RuntimeLlmStreamUpdate update in Scene.Llm.StreamChatAsync(
        "写一句欢迎玩家进入海边场景的台词。",
        systemPrompt: "你是游戏 NPC。",
        temperature: 0.7f))
    {
        output?.SetValue(update.AccumulatedText);
    }
}
```

C# 后台流式输出，不阻塞渲染：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    Scene.GetGuiControl("LLM Output")?.SetValue("");

    Scene.Llm.StartChat(
        Entity,
        "写一句欢迎玩家进入海边场景的台词。",
        systemPrompt: "你是游戏 NPC。",
        onDeltaCallback: "npc_reply_delta",
        onCompletedCallback: "npc_reply_done",
        onErrorCallback: "npc_reply_error");
}

if (IsLlmEvent && LlmCallbackName == "npc_reply_delta")
{
    Scene.GetGuiControl("LLM Output")?.SetValue(LlmText);
}

if (IsLlmEvent && LlmCallbackName == "npc_reply_done")
{
    Entity.Speak(LlmText);
}

if (IsLlmEvent && LlmCallbackName == "npc_reply_error")
{
    Console.Error.WriteLine(LlmError);
}
```

C# 消息列表调用：

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    var messages = new[]
    {
        new RuntimeLlmChatMessage("system", "你是游戏任务设计助手。"),
        new RuntimeLlmChatMessage("user", "给玩家一个 20 字以内的探索任务。")
    };

    await foreach (RuntimeLlmStreamUpdate update in Scene.Llm.StreamChatAsync(messages))
    {
        Scene.GetGuiControl("Quest Text")?.SetValue(update.AccumulatedText);
    }
}
```

### Python LLM

`scene.llm` 提供三类调用：

- `scene.llm.chat(...)`：等待完整结果。
- `scene.llm.stream_chat(...)` / `stream_messages(...)`：在当前函数里同步流式迭代。适合加载脚本或测试；运行中 UI 建议用 `start_chat`，否则当前脚本事件会占用主循环。
- `scene.llm.start_chat(...)`：后台请求，delta / completed / error 会回调到指定 Python 函数，不阻塞当前事件。

Python LLM API：

| API | 说明 |
| --- | --- |
| `scene.llm.enabled` | 当前项目是否启用 LLM。 |
| `scene.llm.model` | 默认模型名。 |
| `scene.llm.chat(text, system_prompt=None, model=None, temperature=None)` | 非流式返回完整文本。 |
| `scene.llm.stream_chat(text, system_prompt=None, model=None, temperature=None)` | 按文本 prompt 发起流式请求。 |
| `scene.llm.stream_messages(messages, model=None, temperature=None)` | 按消息列表发起流式请求。 |
| `scene.llm.start_chat(text, system_prompt=None, model=None, temperature=None, request_id=None, on_delta="llm_delta", on_completed="llm_completed", on_error="llm_error")` | 后台流式请求，通过 Python 函数回调。 |

Python 完整结果：

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    answer = scene.llm.chat(
        "用一句话介绍这片海。",
        system_prompt="你是游戏中的旁白，回答要短。")

    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value(answer)
```

Python 当前函数内同步流式读取：

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value("")
        scene.flush()

    for update in scene.llm.stream_chat(
        "写一句欢迎玩家进入海边场景的台词。",
        system_prompt="你是游戏 NPC。",
        temperature=0.7):
        if output:
            output.set_value(update["accumulated_text"])
            scene.flush()
```

Python 后台流式输出，不阻塞渲染：

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value("")

    scene.llm.start_chat(
        "写一句欢迎玩家进入海边场景的台词。",
        system_prompt="你是游戏 NPC。",
        on_delta="npc_reply_delta",
        on_completed="npc_reply_done",
        on_error="npc_reply_error")

def npc_reply_delta(entity, scene, input, audio, event):
    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value(event["accumulatedText"])

def npc_reply_done(entity, scene, input, audio, event):
    entity.speak(event["accumulatedText"])

def npc_reply_error(entity, scene, input, audio, event):
    print("LLM error:", event["error"])
```

Python 消息列表调用：

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    messages = [
        {"role": "system", "content": "你是游戏任务设计助手。"},
        {"role": "user", "content": "给玩家一个 20 字以内的探索任务。"},
    ]

    output = scene.get_gui_control("Quest Text")
    for update in scene.llm.stream_messages(messages):
        if output:
            output.set_value(update["accumulated_text"])
            scene.flush()
```

注意事项：

- LLM 请求是网络请求。同步 `ChatAsync` / `chat` / 当前函数内 `stream_chat` 会占用脚本事件执行时间；运行中实时 UI 建议用 `StartChat` / `start_chat`。
- Python 的 `scene.flush()` 只会提交当前已累计的引擎命令，例如 GUI 文本变化、实体移动等；不会重新读取新的输入快照，也不会让被阻塞的主循环提前渲染新帧。
- `start_chat` 的回调事件字段包括 `requestId`、`eventName`、`delta`、`accumulatedText`、`isFinal`、`error`、`callbackName`。
- 当前 LLM API 面向文本 Chat Completions；图片、多模态、工具调用等还没有封装到脚本层。
