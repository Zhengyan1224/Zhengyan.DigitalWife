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
  - Built-in skills tools
keywords:
  - llm
  - openai
  - chat
  - stream
  - function call
  - tools
  - skills
---

# LLM / OpenAI-compatible API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | LLM / OpenAI-compatible API |
| 分类 | AI |
| 主要对象 | `RuntimeLlm`, `RuntimeLlmScriptEvent`, `RuntimeLlmChatMessage`, `RuntimeLlmTool`, `RuntimeLlmScriptTool`, `Built-in skills tools` |
| C# 入口 | `Scene.Llm.ChatAsync/StreamChatAsync/StartChat/StartChatWithTools` |
| Python 入口 | `scene.llm.chat/stream_chat/start_chat/start_chat_with_tools` |
| 说明 | OpenAI-compatible 文本对话、流式输出、后台回调、function call 和项目 skills 内置工具。 |

## 基础配置

GamePlayer 通过 OpenAI-compatible `/v1/chat/completions` 调用 LLM。配置在 GameEditor 的 `Project -> LLM / OpenAI-compatible` 中，保存到 `game.project.json` 的 `llm` 节点。

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 是否启用脚本 LLM。未启用时调用会报错。 |
| `EnableSkills` | 是否给 LLM 自动启用项目内置 skills 工具。开启后，GamePlayer 会把 `skills/` 目录、项目文件读写、文本搜索和命令执行工具注册给 LLM。 |
| `Provider` | Provider 名称，当前运行时按 OpenAI-compatible 协议处理。 |
| `BaseUrl` | API 根地址，例如 `https://api.openai.com` 或私有兼容服务地址。 |
| `ApiKeyEnvironmentVariable` | API Key 环境变量名，默认 `OPENAI_API_KEY`。推荐使用环境变量，避免把密钥写入工程文件。 |
| `ApiKey` | 直接写入工程的 API Key 覆盖值。不建议提交到版本库。 |
| `Model` | 默认模型名，例如 `gpt-4o-mini`、`qwen-plus` 或私有模型名。 |
| `ChatCompletionsPath` | 默认 `/v1/chat/completions`。Provider 路径不同时可修改。 |
| `TimeoutSeconds` | 请求超时时间。 |
| `DefaultTemperature` | 默认温度；为空则不发送 `temperature` 字段。 |

## C# API

`Scene.Llm` 提供三类调用：

- `await Scene.Llm.ChatAsync(...)`：等待完整结果，适合加载脚本、短请求或不介意等待的逻辑。
- `await foreach (var update in Scene.Llm.StreamChatAsync(...))`：在当前脚本事件内同步读取流式输出。它会占用当前脚本事件，运行时 UI 更推荐用后台式 `StartChat`。
- `Scene.Llm.StartChat(...)`：后台请求，delta/completed/error 会通过 `IsLlmEvent` 回到同一个实体脚本，适合运行时 UI 和对话。
- `Scene.Llm.StartChatWithTools(...)`：后台 function call 请求，LLM 需要工具时会调用脚本工具或内置 skills 工具，再把结果继续发回模型。

| API | 说明 |
| --- | --- |
| `Scene.Llm.Enabled` | 当前项目是否启用 LLM。 |
| `Scene.Llm.Provider` | Provider 名称。 |
| `Scene.Llm.BaseUrl` | API 根地址。 |
| `Scene.Llm.Model` | 默认模型名。 |
| `Scene.Llm.ChatCompletionsPath` | Chat Completions 路径。 |
| `Scene.Llm.DefaultTemperature` | 默认温度，可能为 `null`。 |
| `Scene.Llm.SkillsEnabled` | 是否启用项目 skills 内置工具。 |
| `Scene.Llm.SkillsDirectory` | 当前项目的 skills 目录绝对路径。 |
| `ChatAsync(text, systemPrompt, model, temperature)` | 非流式返回完整文本。启用 skills 时会自动带内置工具。 |
| `StreamChatAsync(text, systemPrompt, model, temperature)` | 按文本 prompt 发起流式请求。启用 skills 时会自动带内置工具。 |
| `StreamChatAsync(messages, model, temperature)` | 按消息列表发起流式请求。启用 skills 时会自动带内置工具。 |
| `StartChat(entity, text, systemPrompt, model, temperature, requestId, onDeltaCallback, onCompletedCallback, onErrorCallback)` | 后台流式请求。启用 skills 时会自动带内置工具。 |
| `ChatWithToolsAsync(text, tools, systemPrompt, model, temperature, maxToolRounds)` | 带 function call 的完整文本请求。启用 skills 时会合并内置工具。 |
| `StreamChatWithToolsAsync(messages, tools, model, temperature, maxToolRounds)` | 带 function call 的流式请求。启用 skills 时会合并内置工具。 |
| `StartChatWithTools(entity, text, tools, systemPrompt, model, temperature, requestId, onDeltaCallback, onCompletedCallback, onErrorCallback, onToolCallCallback, onToolResultCallback, maxToolRounds)` | 后台工具调用请求，适合运行时 UI。启用 skills 时会合并内置工具。 |

### C#：后台流式输出

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    Scene.GetGuiControl("LLM Output")?.SetValue("");

    Scene.Llm.StartChat(
        Entity,
        "写一句欢迎玩家进入海边场景的台词。",
        systemPrompt: "你是游戏 NPC，回答要简短自然。",
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

### C#：自定义 function call 工具

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
```

### C# 工具对象

| 对象 / 属性 | 说明 |
| --- | --- |
| `RuntimeLlmTool(name, description, parametersJsonSchema, handler)` | C# 直接定义工具。`handler` 收到 `RuntimeLlmToolCall` 或参数 JSON，返回工具结果字符串。 |
| `RuntimeLlmScriptTool(name, description, parametersJsonSchema, callbackName)` | 把脚本回调包装成工具。适合 `.csx` 顶层脚本或 Python worker 回调。 |
| `RuntimeLlmToolCall.Id` | LLM 生成的工具调用 ID。 |
| `RuntimeLlmToolCall.Name` | LLM 要调用的工具名。 |
| `RuntimeLlmToolCall.ArgumentsJson` | LLM 传入的参数 JSON 字符串。脚本需要自行解析和校验。 |
| `LlmEventName == "tool_execute"` | 当前脚本事件是实际执行工具，脚本应 `return` JSON 字符串、普通字符串或可 JSON 序列化对象。 |
| `LlmEventName == "tool_call"` | LLM 请求调用工具，仅用于通知 UI 或日志。 |
| `LlmEventName == "tool_result"` | 工具执行完成，仅用于通知 UI 或日志。 |
| `LlmToolName` / `LlmToolArgumentsJson` / `LlmToolResult` | C# 全局便捷属性。 |

## Python API

`scene.llm` 提供：

- `scene.llm.chat(...)`：等待完整结果。
- `scene.llm.stream_chat(...)` / `stream_messages(...)`：在当前函数内同步流式迭代。注意：Python 同步调用由 Python worker 直接请求 HTTP，目前不会执行 GamePlayer 的内置 skills 工具；需要 skills 时请使用后台 `start_chat` / `start_chat_with_tools`。
- `scene.llm.start_chat(...)`：后台请求，delta/completed/error 回调到指定 Python 函数。启用 skills 时会自动带内置工具。
- `scene.llm.start_chat_with_tools(...)`：后台 function call 请求。启用 skills 时会合并内置工具。

| API | 说明 |
| --- | --- |
| `scene.llm.enabled` | 当前项目是否启用 LLM。 |
| `scene.llm.model` | 默认模型名。 |
| `scene.llm.skills_enabled` | 是否启用项目 skills 内置工具。 |
| `scene.llm.skills_directory` | 当前项目的 skills 目录绝对路径。 |
| `scene.llm.chat(text, system_prompt=None, model=None, temperature=None)` | 非流式返回完整文本。Python worker 直连 HTTP，不执行内置 skills 工具。 |
| `scene.llm.stream_chat(text, system_prompt=None, model=None, temperature=None)` | 按文本 prompt 发起同步流式请求。Python worker 直连 HTTP，不执行内置 skills 工具。 |
| `scene.llm.stream_messages(messages, model=None, temperature=None)` | 按消息列表发起同步流式请求。Python worker 直连 HTTP，不执行内置 skills 工具。 |
| `scene.llm.start_chat(text, system_prompt=None, model=None, temperature=None, request_id=None, on_delta="llm_delta", on_completed="llm_completed", on_error="llm_error")` | 后台流式请求，通过 Python 函数回调。启用 skills 时会自动带内置工具。 |
| `scene.llm.tool(name, description, parameters_json_schema, callback)` | 创建一个 function call 工具定义。`parameters_json_schema` 可以是 JSON 字符串或 Python dict。 |
| `scene.llm.start_chat_with_tools(text, tools, system_prompt=None, model=None, temperature=None, request_id=None, on_delta="llm_delta", on_completed="llm_completed", on_error="llm_error", on_tool_call="llm_tool_call", on_tool_result="llm_tool_result", max_tool_rounds=4)` | 后台工具调用请求，通过 Python 函数回调执行脚本工具。启用 skills 时会合并内置工具。 |

### Python：后台流式输出

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value("")

    scene.llm.start_chat(
        "写一句欢迎玩家进入海边场景的台词。",
        system_prompt="你是游戏 NPC，回答要简短自然。",
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

### Python：自定义 function call 工具

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

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
```

## 内置 Skills 工具

启用 `Project -> LLM / OpenAI-compatible -> Enable skills tools` 后，GamePlayer 会把一组 `skill_*` 内置工具注册给 LLM。用户可以在游戏项目目录下创建 `skills/` 目录，并按功能创建子目录。每个 skill 建议使用主流 skills 目录规范：

```text
GameProject/
  skills/
    quest_writer/
      SKILL.md
      scripts/
        generate_quest.py
      resources/
        templates.json
```

`SKILL.md` 建议包含 YAML front matter：

```markdown
---
name: quest_writer
description: Generate short quest text for NPC dialogue.
---

# Quest Writer

Use this skill when the player asks for a quest idea or NPC dialogue.
```

### 自动注入范围

| 调用 | 是否自动带内置 skills 工具 |
| --- | --- |
| C# `ChatAsync` / `StreamChatAsync` / `StartChat` | 是 |
| C# `ChatWithToolsAsync` / `StreamChatWithToolsAsync` / `StartChatWithTools` | 是，会和脚本工具合并 |
| Python `start_chat` / `start_chat_with_tools` | 是 |
| Python `chat` / `stream_chat` / `stream_messages` | 否，它们由 Python worker 直接请求 HTTP，当前不执行 GamePlayer 内置工具 |

如果脚本自定义工具和内置工具重名，脚本工具优先。建议业务工具避免使用 `skill_*` 前缀。

### 内置工具清单

| 工具名 | 作用 | 主要参数 |
| --- | --- | --- |
| `skill_list` | 列出项目 `skills/` 下的所有技能，并读取名称、描述和 markdown 路径。 | `includeContent`, `maxResults` |
| `skill_read` | 读取某个 skill 的 `SKILL.md` 或 skill 目录内指定文件。 | `name`, `path`, `maxBytes` |
| `skill_list_files` | 列出项目目录或某个 skill 目录下的文件。 | `path`, `skillName`, `recursive`, `maxResults` |
| `skill_read_file` | 读取项目目录内的 UTF-8 文本文件。 | `path`, `maxBytes` |
| `skill_write_file` | 写入项目目录内的 UTF-8 文本文件。 | `path`, `content`, `append`, `createDirectories` |
| `skill_search_files` | 在项目文件或 skills 文件中搜索文本。 | `query`, `path`, `recursive`, `maxResults` |
| `skill_run_command` | 在项目目录内执行 shell 命令并返回 stdout/stderr/exit code。 | `command`, `workingDirectory`, `timeoutSeconds` |

所有文件路径都会限制在游戏项目目录内；`skill_read` 的 `name` 只能是 `skills/` 下的直接子目录名。`skill_run_command` 的工作目录也会限制在项目目录内，但命令本身仍然是受信任本地能力，只应在你信任当前项目和模型输出时开启。

### C#：让 LLM 使用 skills

```csharp
if (IsGuiEvent && GuiEventName == "clicked")
{
    Scene.GetGuiControl("LLM Output")?.SetValue("");

    Scene.Llm.StartChat(
        Entity,
        "请检查项目里的 skills，选择合适的技能生成一个 30 字以内的支线任务。",
        systemPrompt: """
        你是游戏编剧助手。
        如果需要了解可用技能，先调用 skill_list。
        如果需要技能说明，调用 skill_read。
        如果需要读取模板或脚本，使用 skill_read_file 或 skill_list_files。
        """,
        onDeltaCallback: "skills_reply_delta",
        onCompletedCallback: "skills_reply_done",
        onErrorCallback: "skills_reply_error");
}

if (IsLlmEvent && LlmCallbackName == "skills_reply_delta")
{
    Scene.GetGuiControl("LLM Output")?.SetValue(LlmText);
}

if (IsLlmEvent && LlmCallbackName == "skills_reply_done")
{
    Entity.Speak(LlmText);
}
```

### Python：让 LLM 使用 skills

```python
def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    if event_name != "clicked":
        return

    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value("")

    scene.llm.start_chat(
        "请检查项目里的 skills，选择合适的技能生成一个 30 字以内的支线任务。",
        system_prompt=(
            "你是游戏编剧助手。"
            "如果需要了解可用技能，先调用 skill_list。"
            "如果需要技能说明，调用 skill_read。"
            "如果需要读取模板或脚本，使用 skill_read_file 或 skill_list_files。"
        ),
        on_delta="skills_reply_delta",
        on_completed="skills_reply_done",
        on_error="skills_reply_error")

def skills_reply_delta(entity, scene, input, audio, event):
    output = scene.get_gui_control("LLM Output")
    if output:
        output.set_value(event["accumulatedText"])

def skills_reply_done(entity, scene, input, audio, event):
    entity.speak(event["accumulatedText"])

def skills_reply_error(entity, scene, input, audio, event):
    print("LLM skills error:", event["error"])
```

## 注意事项

- LLM 请求是网络请求。同步 `ChatAsync` / `chat` / 当前函数内 `stream_chat` 会占用脚本事件执行时间；运行中实时 UI 建议用 `StartChat` / `start_chat`。
- function call 使用 OpenAI-compatible Chat Completions 的 `tools: [{ type: "function", function: ... }]` 格式。不同供应商如果工具调用字段兼容性不足，需要在服务端适配。
- 工具参数由 LLM 生成，脚本工具必须自行校验参数类型和范围，不要直接把参数拼接成系统命令或任意文件路径。
- `maxToolRounds` / `max_tool_rounds` 限制“模型调用工具 -> 工具结果返回模型 -> 模型再次调用工具”的最大轮数，避免无限循环。
- 内置 skills 文件工具限制在项目目录内，但 `skill_run_command` 仍然可以执行本机命令。只在受信任项目、受信任模型和明确需要自动执行命令时开启 skills。
- 当前 LLM API 面向文本 Chat Completions；图片和多模态暂未封装到脚本层。
