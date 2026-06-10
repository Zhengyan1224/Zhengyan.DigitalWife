---
id: llm
title: LLM / OpenAI-compatible API
category: AI
objects:
  - RuntimeLlm
  - RuntimeLlmScriptEvent
  - RuntimeLlmChatMessage
  - RuntimeLlmTool
  - RuntimeLlmScriptTool
keywords:
  - llm
  - openai
  - chat
  - stream
  - function call
  - tools
---

# LLM / OpenAI-compatible API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | LLM / OpenAI-compatible API |
| 分类 | AI |
| 主要对象 | ``RuntimeLlm``, ``RuntimeLlmScriptEvent``, ``RuntimeLlmChatMessage``, ``RuntimeLlmTool``, ``RuntimeLlmScriptTool`` |
| C# 入口 | `Scene.Llm.ChatAsync/StreamChatAsync/StartChat/StartChatWithTools` |
| Python 入口 | `scene.llm.chat/stream_chat/start_chat/start_chat_with_tools` |
| 说明 | OpenAI-compatible LLM 完整请求、流式请求、后台回调、消息列表和 function call 工具调用。 |

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
| `ChatWithToolsAsync(text, tools, systemPrompt, model, temperature, maxToolRounds)` | 带工具调用的完整文本请求。LLM 请求工具时会执行脚本或 C# 工具并把结果继续发回模型。 |
| `StreamChatWithToolsAsync(messages, tools, model, temperature, maxToolRounds)` | 带工具调用的流式请求。工具调用完成后继续输出最终回复。 |
| `StartChatWithTools(entity, text, tools, systemPrompt, model, temperature, requestId, onDeltaCallback, onCompletedCallback, onErrorCallback, onToolCallCallback, onToolResultCallback, maxToolRounds)` | 后台工具调用请求，适合运行时 UI。 |

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

C# function call 工具调用：

```csharp
using System.Text.Json;

RuntimeLlmTool[] tools =
[
    new RuntimeLlmScriptTool(
        "get_player_status",
        "读取当前玩家状态，包括 HP、金币和当前位置。",
        """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """,
        "get_player_status_tool").ToTool(Entity, Scene)
];

if (IsGuiEvent && GuiEventName == "clicked")
{
    Scene.GetGuiControl("LLM Output")?.SetValue("");

    Scene.Llm.StartChatWithTools(
        Entity,
        "根据我的当前状态，给我一个下一步行动建议。",
        tools,
        systemPrompt: "你是游戏 NPC。需要状态数据时调用工具，不要编造。",
        onDeltaCallback: "npc_tool_reply_delta",
        onCompletedCallback: "npc_tool_reply_done",
        onErrorCallback: "npc_tool_reply_error",
        onToolCallCallback: "npc_tool_call",
        onToolResultCallback: "npc_tool_result",
        maxToolRounds: 4);
}

if (IsLlmEvent && LlmEventName == "tool_execute" && LlmCallbackName == "get_player_status_tool")
{
    return JsonSerializer.Serialize(new
    {
        hp = 80,
        gold = 12,
        position = new { x = Entity.Position.X, y = Entity.Position.Y, z = Entity.Position.Z }
    });
}

if (IsLlmEvent && LlmCallbackName == "npc_tool_reply_delta")
{
    Scene.GetGuiControl("LLM Output")?.SetValue(LlmText);
}

if (IsLlmEvent && LlmCallbackName == "npc_tool_call")
{
    Console.WriteLine($"LLM wants tool: {LlmToolName}, args={LlmToolArgumentsJson}");
}

if (IsLlmEvent && LlmCallbackName == "npc_tool_result")
{
    Console.WriteLine($"Tool result: {LlmToolResult}");
}

if (IsLlmEvent && LlmCallbackName == "npc_tool_reply_done")
{
    Entity.Speak(LlmText);
}

if (IsLlmEvent && LlmCallbackName == "npc_tool_reply_error")
{
    Console.Error.WriteLine(LlmError);
}
```

C# 工具相关对象：

| 对象 / 属性 | 说明 |
| --- | --- |
| `RuntimeLlmTool(name, description, parametersJsonSchema, handler)` | C# 直接定义工具。`handler` 收到 `RuntimeLlmToolCall` 或参数 JSON，返回工具结果字符串。 |
| `RuntimeLlmScriptTool(name, description, parametersJsonSchema, callbackName)` | 把脚本回调包装成工具。适合 `.csx` 顶层脚本或 Python worker 回调。 |
| `RuntimeLlmToolCall.Id` | LLM 生成的工具调用 ID。 |
| `RuntimeLlmToolCall.Name` | LLM 要调用的工具名。 |
| `RuntimeLlmToolCall.ArgumentsJson` | LLM 传入的参数 JSON 字符串。脚本需要自行解析和校验。 |
| `LlmEventName == "tool_execute"` | 当前脚本事件是实际执行工具，脚本应 `return` JSON 字符串、普通字符串或可被 JSON 序列化的对象。 |
| `LlmEventName == "tool_call"` | LLM 请求调用工具，仅用于通知 UI 或日志。 |
| `LlmEventName == "tool_result"` | 工具执行完成，仅用于通知 UI 或日志。 |
| `LlmToolName` / `LlmToolArgumentsJson` / `LlmToolResult` | C# 全局便捷属性。 |

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
- `scene.llm.start_chat_with_tools(...)`：后台工具调用请求。LLM 需要额外信息时会触发 Python 或 C# 脚本工具回调，再把结果继续发回模型。

Python LLM API：

| API | 说明 |
| --- | --- |
| `scene.llm.enabled` | 当前项目是否启用 LLM。 |
| `scene.llm.model` | 默认模型名。 |
| `scene.llm.chat(text, system_prompt=None, model=None, temperature=None)` | 非流式返回完整文本。 |
| `scene.llm.stream_chat(text, system_prompt=None, model=None, temperature=None)` | 按文本 prompt 发起流式请求。 |
| `scene.llm.stream_messages(messages, model=None, temperature=None)` | 按消息列表发起流式请求。 |
| `scene.llm.start_chat(text, system_prompt=None, model=None, temperature=None, request_id=None, on_delta="llm_delta", on_completed="llm_completed", on_error="llm_error")` | 后台流式请求，通过 Python 函数回调。 |
| `scene.llm.tool(name, description, parameters_json_schema, callback)` | 创建一个 function call 工具定义。`parameters_json_schema` 可以是 JSON 字符串或 Python dict。 |
| `scene.llm.start_chat_with_tools(text, tools, system_prompt=None, model=None, temperature=None, request_id=None, on_delta="llm_delta", on_completed="llm_completed", on_error="llm_error", on_tool_call="llm_tool_call", on_tool_result="llm_tool_result", max_tool_rounds=4)` | 后台工具调用请求，通过 Python 函数回调执行工具。 |

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

Python function call 工具调用：

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value("")

    tools = [
        scene.llm.tool(
            "get_player_status",
            "读取当前玩家状态，包括 HP、金币和当前位置。",
            {
                "type": "object",
                "properties": {},
                "additionalProperties": False,
            },
            "get_player_status_tool")
    ]

    scene.llm.start_chat_with_tools(
        "根据我的当前状态，给我一个下一步行动建议。",
        tools,
        system_prompt="你是游戏 NPC。需要状态数据时调用工具，不要编造。",
        on_delta="npc_tool_reply_delta",
        on_completed="npc_tool_reply_done",
        on_error="npc_tool_reply_error",
        on_tool_call="npc_tool_call",
        on_tool_result="npc_tool_result",
        max_tool_rounds=4)

def get_player_status_tool(entity, scene, input, audio, event):
    return {
        "hp": 80,
        "gold": 12,
        "position": {
            "x": entity.position[0],
            "y": entity.position[1],
            "z": entity.position[2],
        }
    }

def npc_tool_reply_delta(entity, scene, input, audio, event):
    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value(event["accumulatedText"])

def npc_tool_call(entity, scene, input, audio, event):
    call = event.get("toolCall") or {}
    print("LLM wants tool:", call.get("name", ""), call.get("argumentsJson", ""))

def npc_tool_result(entity, scene, input, audio, event):
    print("Tool result:", event.get("toolResult", ""))

def npc_tool_reply_done(entity, scene, input, audio, event):
    entity.speak(event["accumulatedText"])

def npc_tool_reply_error(entity, scene, input, audio, event):
    print("LLM error:", event["error"])
```

Python 工具事件字段：

| 字段 | 说明 |
| --- | --- |
| `event["eventName"] == "tool_execute"` | 当前回调正在执行工具，需要返回工具结果。 |
| `event["toolCall"]["id"]` | 工具调用 ID。 |
| `event["toolCall"]["name"]` | 工具名。 |
| `event["toolCall"]["argumentsJson"]` | 工具参数 JSON 字符串。需要脚本自行 `json.loads(...)` 并校验。 |
| `event["toolResult"]` | `tool_result` 通知事件中的工具返回文本。 |
| 工具回调返回值 | 可以返回 `str`、`dict`、`list`、数字或布尔值；非字符串会自动序列化为 JSON 文本。 |

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
- `start_chat` / `start_chat_with_tools` 的回调事件字段包括 `requestId`、`eventName`、`delta`、`accumulatedText`、`isFinal`、`error`、`callbackName`、`toolCall`、`toolResult`。
- function call 使用 OpenAI-compatible Chat Completions 的 `tools: [{ type: "function", function: ... }]` 格式。不同供应商如果对工具调用字段兼容性不足，需要在服务端适配。
- 工具参数由 LLM 生成，脚本必须自行校验参数类型和范围，不要直接把参数拼接成系统命令或文件路径。
- `maxToolRounds` / `max_tool_rounds` 用于限制“模型调用工具 -> 工具结果返回模型 -> 模型再次调用工具”的最大轮数，避免模型无限循环调用工具。
- 当前 LLM API 面向文本 Chat Completions；图片和多模态暂未封装到脚本层。
