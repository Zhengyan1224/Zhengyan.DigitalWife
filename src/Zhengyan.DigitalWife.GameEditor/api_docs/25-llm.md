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
| `StartChatWithTools(entity, messages, tools, model, temperature, requestId, onDeltaCallback, onCompletedCallback, onErrorCallback, onToolCallCallback, onToolResultCallback, maxToolRounds)` | 按消息列表发起后台工具调用请求，适合多轮对话。启用 skills 时会合并内置工具。 |

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

## 完整示例：ASR -> LLM -> TTS

下面示例展示一个完整的语音对话链路：

1. 玩家按住 `RecordButton` 开始录音。
2. 松开按钮后停止 ASR 流式识别。
3. 把识别结果写入 `PromptInput`，玩家也可以手动编辑后点击 `SendButton`。
4. 使用 `StartChatWithTools` 调用 LLM；如果项目启用了 skills，LLM 可自动调用内置 skills 工具。
5. LLM 流式输出写入对话气泡，并按短句排队调用 TTS。
6. 每段 TTS 播完后再播放下一段，避免多段语音重叠。

示例假设场景里有：

- 一个按钮：`RecordButton`
- 一个按钮：`SendButton`
- 一个输入框：`PromptInput`
- 脚本绑定在要说话的 PMX 实体上

### C#：ASR -> LLM with skills -> TTS

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;

static class VoiceChatState
{
    public const string BubbleName = "voice-chat-bubble";
    public const string TtsDoneCallback = "voice_chat_tts_done";
    public const string LlmToolCallCallback = "voice_chat_llm_tool_call";
    public const string LlmToolResultCallback = "voice_chat_llm_tool_result";
    public const int MaxSpeechSegmentLength = 70;
    public const int MaxConversationTurns = 8;
    public const int MaxConversationCharacters = 6000;

    public static string AsrRequestId = string.Empty;
    public static string LlmRequestId = string.Empty;
    public static string CurrentUserText = string.Empty;
    public static string ActiveLlmUserText = string.Empty;
    public static string CurrentAssistantText = string.Empty;
    public static string PendingSentenceBuffer = string.Empty;
    public static List<RuntimeLlmChatMessage> ConversationHistory = new();
    public static Queue<string> SpeakQueue = new();
    public static bool IsRecording;
    public static bool IsSpeaking;
    public static bool ReplyCompleted;
}

RuntimeDialogueBubble GetChatBubble()
{
    RuntimeDialogueBubble bubble = Scene.Bubble.GetOrCreate(VoiceChatState.BubbleName);
    bubble.AttachToEntity(Entity.Id, useModelTopAnchor: true);
    bubble.SetWorldOffset(0.0f, 0.20f, 0.0f);
    bubble.SetScreenOffset(0.0f, -16.0f);
    bubble.Width = 440.0f;
    bubble.TextAlignment = "left";
    bubble.BackgroundColor = new Vector4(0.9f, 0.0f, 1.0f, 0.50f);
    bubble.TextColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
    bubble.FooterTextColor = new Vector4(0.76f, 0.80f, 0.88f, 1.0f);
    return bubble;
}

void ShowBubble(string userText, string assistantText, string footer)
{
    RuntimeDialogueBubble bubble = GetChatBubble();
    bubble.SetContent(
        assistantText,
        headerText: string.IsNullOrWhiteSpace(userText) ? "语音助手" : $"你：{userText}",
        footerText: footer);
    bubble.Show();
}

void ResetReplyState(bool stopSpeaking)
{
    VoiceChatState.CurrentAssistantText = string.Empty;
    VoiceChatState.PendingSentenceBuffer = string.Empty;
    VoiceChatState.SpeakQueue.Clear();
    VoiceChatState.ReplyCompleted = false;
    VoiceChatState.IsSpeaking = false;

    if (stopSpeaking)
    {
        Entity.StopSpeaking();
    }
}

bool IsSpeechBreak(char ch)
{
    return ch is '。' or '！' or '？' or '；' or '，' or '、' or '.' or '!' or '?' or ';' or ',' or '\n';
}

string CleanSpeechText(string rawText)
{
    if (string.IsNullOrWhiteSpace(rawText))
    {
        return string.Empty;
    }

    string text = rawText
        .Replace("**", "")
        .Replace("__", "")
        .Replace("`", "")
        .Replace("（", "，")
        .Replace("）", "，")
        .Replace("(", "，")
        .Replace(")", "，")
        .Replace("——", "，")
        .Replace("-", "，");
    text = Regex.Replace(text, @"https?://\S+", "网页链接");
    text = Regex.Replace(text, @"[^\u4e00-\u9fa5a-zA-Z0-9\s，,。.!?！？；;、]", "");
    text = Regex.Replace(text, @"\s+", " ");
    return text.Trim();
}

IEnumerable<string> DrainCompletedSentences(ref string buffer, bool flushTail)
{
    List<string> result = [];
    int sentenceStart = 0;

    for (int i = 0; i < buffer.Length; i++)
    {
        bool shouldBreak = IsSpeechBreak(buffer[i])
            || i - sentenceStart + 1 >= VoiceChatState.MaxSpeechSegmentLength;
        if (!shouldBreak)
        {
            continue;
        }

        string sentence = buffer[sentenceStart..(i + 1)].Trim();
        if (!string.IsNullOrWhiteSpace(sentence))
        {
            result.Add(sentence);
        }

        sentenceStart = i + 1;
    }

    string tail = sentenceStart >= buffer.Length ? string.Empty : buffer[sentenceStart..];
    if (flushTail)
    {
        string finalSentence = tail.Trim();
        if (!string.IsNullOrWhiteSpace(finalSentence))
        {
            result.Add(finalSentence);
        }

        buffer = string.Empty;
    }
    else
    {
        buffer = tail;
    }

    return result;
}

void EnqueueAssistantSpeech(string text, bool flushTail)
{
    if (!string.IsNullOrEmpty(text))
    {
        VoiceChatState.PendingSentenceBuffer += text;
    }

    foreach (string sentence in DrainCompletedSentences(ref VoiceChatState.PendingSentenceBuffer, flushTail))
    {
        string cleanSentence = CleanSpeechText(sentence);
        if (!string.IsNullOrWhiteSpace(cleanSentence))
        {
            VoiceChatState.SpeakQueue.Enqueue(cleanSentence);
        }
    }
}

void TrySpeakNext()
{
    if (VoiceChatState.IsSpeaking || VoiceChatState.SpeakQueue.Count == 0)
    {
        return;
    }

    string sentence = VoiceChatState.SpeakQueue.Dequeue();
    VoiceChatState.IsSpeaking = true;
    ShowBubble(VoiceChatState.CurrentUserText, VoiceChatState.CurrentAssistantText, "语音回复中...");

    try
    {
        Entity.SpeakWithCallback(sentence, VoiceChatState.TtsDoneCallback);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"TTS start failed: {ex.Message}");
        VoiceChatState.IsSpeaking = false;
        TrySpeakNext();
    }
}

string GetPromptInputText()
{
    return Scene.GetGuiControl("PromptInput")?.Value.Trim() ?? string.Empty;
}

void SetPromptInputText(string text)
{
    Scene.GetGuiControl("PromptInput")?.SetValue(text ?? string.Empty);
}

string CreateLlmSystemPrompt()
{
    return string.Join(
        "\n",
        "你是一个中文语音助手，回答要自然、简洁。",
        $"当前本机时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}。",
        $"项目 skills 是否启用：{Scene.Llm.SkillsEnabled}。",
        $"项目 skills 目录：{Scene.Llm.SkillsDirectory}。",
        "遇到需要外部能力、实时信息、项目文件、命令执行、计算或联网查询的问题时，主动调用工具，不要假装已经完成。",
        "如果不确定项目里有哪些能力，先调用 skill_list；需要了解某个 skill 的使用方法时，调用 skill_read；需要执行 skill 脚本时，按说明调用 skill_run_command。",
        "如果工具调用失败，请直接说明失败原因，不要编造工具没有返回的信息。");
}

int CountConversationHistoryCharacters()
{
    int total = 0;
    foreach (RuntimeLlmChatMessage message in VoiceChatState.ConversationHistory)
    {
        total += message.Content.Length;
    }

    return total;
}

void RemoveOldestConversationTurn()
{
    if (VoiceChatState.ConversationHistory.Count == 0)
    {
        return;
    }

    VoiceChatState.ConversationHistory.RemoveAt(0);
    if (VoiceChatState.ConversationHistory.Count > 0
        && string.Equals(VoiceChatState.ConversationHistory[0].Role, "assistant", StringComparison.OrdinalIgnoreCase))
    {
        VoiceChatState.ConversationHistory.RemoveAt(0);
    }
}

void TrimConversationHistory()
{
    int maxMessages = VoiceChatState.MaxConversationTurns * 2;
    while (VoiceChatState.ConversationHistory.Count > maxMessages)
    {
        RemoveOldestConversationTurn();
    }

    while (CountConversationHistoryCharacters() > VoiceChatState.MaxConversationCharacters)
    {
        RemoveOldestConversationTurn();
    }
}

List<RuntimeLlmChatMessage> BuildConversationMessages(string prompt)
{
    TrimConversationHistory();

    List<RuntimeLlmChatMessage> messages =
    [
        new RuntimeLlmChatMessage("system", CreateLlmSystemPrompt())
    ];

    foreach (RuntimeLlmChatMessage message in VoiceChatState.ConversationHistory)
    {
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            messages.Add(message);
        }
    }

    messages.Add(new RuntimeLlmChatMessage("user", prompt));
    return messages;
}

void AddConversationTurn(string userText, string assistantText)
{
    if (string.IsNullOrWhiteSpace(userText) || string.IsNullOrWhiteSpace(assistantText))
    {
        return;
    }

    VoiceChatState.ConversationHistory.Add(new RuntimeLlmChatMessage("user", userText.Trim()));
    VoiceChatState.ConversationHistory.Add(new RuntimeLlmChatMessage("assistant", assistantText.Trim()));
    TrimConversationHistory();
}

void StartReplyFromPromptInput()
{
    string prompt = GetPromptInputText();
    if (string.IsNullOrWhiteSpace(prompt))
    {
        return;
    }

    VoiceChatState.CurrentUserText = prompt;
    VoiceChatState.ActiveLlmUserText = prompt;
    ResetReplyState(stopSpeaking: true);
    ShowBubble(prompt, string.Empty, "思考中...");

    List<RuntimeLlmChatMessage> messages = BuildConversationMessages(prompt);
    VoiceChatState.LlmRequestId = Scene.Llm.StartChatWithTools(
        Entity,
        messages,
        Array.Empty<RuntimeLlmTool>(),
        onDeltaCallback: "voice_chat_llm_delta",
        onCompletedCallback: "voice_chat_llm_done",
        onErrorCallback: "voice_chat_llm_error",
        onToolCallCallback: VoiceChatState.LlmToolCallCallback,
        onToolResultCallback: VoiceChatState.LlmToolResultCallback,
        maxToolRounds: 8);
}

if (IsStart)
{
    Console.WriteLine($"LLM skills enabled: {Scene.Llm.SkillsEnabled}, skills directory: {Scene.Llm.SkillsDirectory}");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "pressed")
{
    if (!Scene.Asr.Enabled || VoiceChatState.IsRecording)
    {
        return;
    }

    VoiceChatState.IsRecording = true;
    VoiceChatState.CurrentUserText = string.Empty;
    ResetReplyState(stopSpeaking: true);
    SetPromptInputText(string.Empty);
    ShowBubble(string.Empty, string.Empty, "请说话，松开按钮后停止录音");

    VoiceChatState.AsrRequestId = Scene.Asr.StartStreamingRecognition(
        Entity,
        onPartialCallback: "voice_chat_asr_partial",
        onCompletedCallback: "voice_chat_asr_completed",
        onErrorCallback: "voice_chat_asr_error");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "released")
{
    if (!VoiceChatState.IsRecording)
    {
        return;
    }

    VoiceChatState.IsRecording = false;
    Scene.Asr.StopStreamingRecognition(VoiceChatState.AsrRequestId);
    ShowBubble(VoiceChatState.CurrentUserText, string.Empty, "录音已结束，可编辑后点击发送");
}

if (IsGuiEvent && GuiControlName == "SendButton" && GuiEventName == "clicked")
{
    if (VoiceChatState.IsRecording)
    {
        VoiceChatState.IsRecording = false;
        Scene.Asr.StopStreamingRecognition(VoiceChatState.AsrRequestId);
    }

    StartReplyFromPromptInput();
}

if (IsAsrEvent && AsrCallbackName == "voice_chat_asr_partial")
{
    if (!string.Equals(AsrRequestId, VoiceChatState.AsrRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.CurrentUserText = AsrText;
    SetPromptInputText(AsrText);
    ShowBubble(AsrText, string.Empty, "请继续说，松开按钮后停止录音");
}

if (IsAsrEvent && AsrCallbackName == "voice_chat_asr_completed")
{
    if (!string.Equals(AsrRequestId, VoiceChatState.AsrRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.IsRecording = false;
    VoiceChatState.CurrentUserText = AsrText.Trim();
    SetPromptInputText(VoiceChatState.CurrentUserText);
    ShowBubble(VoiceChatState.CurrentUserText, string.Empty, "可编辑文本框后点击发送");
}

if (IsAsrEvent && AsrCallbackName == "voice_chat_asr_error")
{
    if (!string.Equals(AsrRequestId, VoiceChatState.AsrRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.IsRecording = false;
    ShowBubble(VoiceChatState.CurrentUserText, string.Empty, "ASR 出错");
    Console.Error.WriteLine(AsrError);
}

if (IsLlmEvent && LlmCallbackName == "voice_chat_llm_delta")
{
    if (!string.Equals(LlmRequestId, VoiceChatState.LlmRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.CurrentAssistantText = LlmText;
    ShowBubble(
        VoiceChatState.CurrentUserText,
        VoiceChatState.CurrentAssistantText,
        VoiceChatState.IsSpeaking ? "语音回复中..." : "正在生成回复...");

    EnqueueAssistantSpeech(LlmDelta, flushTail: false);
    TrySpeakNext();
}

if (IsLlmEvent && LlmCallbackName == "voice_chat_llm_done")
{
    if (!string.Equals(LlmRequestId, VoiceChatState.LlmRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.ReplyCompleted = true;
    VoiceChatState.CurrentAssistantText = LlmText;
    AddConversationTurn(VoiceChatState.ActiveLlmUserText, VoiceChatState.CurrentAssistantText);
    EnqueueAssistantSpeech(string.Empty, flushTail: true);
    ShowBubble(
        VoiceChatState.CurrentUserText,
        VoiceChatState.CurrentAssistantText,
        VoiceChatState.SpeakQueue.Count > 0 || VoiceChatState.IsSpeaking ? "语音回复中..." : "回复完成");

    TrySpeakNext();
}

if (IsLlmEvent && LlmCallbackName == VoiceChatState.LlmToolCallCallback)
{
    if (!string.Equals(LlmRequestId, VoiceChatState.LlmRequestId, StringComparison.Ordinal))
    {
        return;
    }

    Console.WriteLine($"LLM tool call: {LlmToolName} {LlmToolArgumentsJson}");
    ShowBubble(VoiceChatState.CurrentUserText, VoiceChatState.CurrentAssistantText, $"正在调用工具：{LlmToolName}");
}

if (IsLlmEvent && LlmCallbackName == VoiceChatState.LlmToolResultCallback)
{
    if (!string.Equals(LlmRequestId, VoiceChatState.LlmRequestId, StringComparison.Ordinal))
    {
        return;
    }

    Console.WriteLine($"LLM tool result: {LlmToolName} {LlmToolResult}");
    ShowBubble(VoiceChatState.CurrentUserText, VoiceChatState.CurrentAssistantText, $"工具返回结果：{LlmToolName}");
}

if (IsSpeechEvent && SpeechCallbackName == VoiceChatState.TtsDoneCallback)
{
    VoiceChatState.IsSpeaking = false;
    TrySpeakNext();

    if (!VoiceChatState.IsSpeaking && VoiceChatState.SpeakQueue.Count == 0 && VoiceChatState.ReplyCompleted)
    {
        ShowBubble(VoiceChatState.CurrentUserText, VoiceChatState.CurrentAssistantText, "回复完成");
    }
}

if (IsLlmEvent && LlmCallbackName == "voice_chat_llm_error")
{
    if (!string.Equals(LlmRequestId, VoiceChatState.LlmRequestId, StringComparison.Ordinal))
    {
        return;
    }

    ShowBubble(VoiceChatState.CurrentUserText, VoiceChatState.CurrentAssistantText, "LLM 出错");
    Console.Error.WriteLine(LlmError);
}
```

### Python：ASR -> LLM with skills -> TTS

```python
import datetime
import re

BUBBLE_NAME = "voice-chat-bubble"
TTS_DONE_CALLBACK = "voice_chat_tts_done"
LLM_TOOL_CALL_CALLBACK = "voice_chat_llm_tool_call"
LLM_TOOL_RESULT_CALLBACK = "voice_chat_llm_tool_result"
MAX_SPEECH_SEGMENT_LENGTH = 70

asr_request_id = ""
llm_request_id = ""
current_user_text = ""
current_assistant_text = ""
pending_sentence_buffer = ""
speak_queue = []
is_recording = False
is_speaking = False
reply_completed = False

def get_chat_bubble(scene, entity):
    bubble = scene.bubble.get_or_create(BUBBLE_NAME)
    bubble.attach_to_entity(entity.id, use_model_top_anchor=True)
    bubble.set_world_offset(0.0, 0.20, 0.0)
    bubble.set_screen_offset(0.0, -16.0)
    bubble.set_width(440.0)
    bubble.set_text_alignment("left")
    bubble.set_background_color(0.9, 0.0, 1.0, 0.50)
    bubble.set_text_color(1.0, 1.0, 1.0, 1.0)
    bubble.set_footer_text_color(0.76, 0.80, 0.88, 1.0)
    return bubble

def show_bubble(entity, scene, user_text, assistant_text, footer):
    bubble = get_chat_bubble(scene, entity)
    bubble.show(
        assistant_text,
        header_text="语音助手" if not user_text.strip() else f"你：{user_text}",
        footer_text=footer)

def reset_reply_state(entity, stop_speaking):
    global current_assistant_text, pending_sentence_buffer, speak_queue
    global reply_completed, is_speaking

    current_assistant_text = ""
    pending_sentence_buffer = ""
    speak_queue = []
    reply_completed = False
    is_speaking = False

    if stop_speaking:
        entity.stop_speaking()

def is_speech_break(ch):
    return ch in "。！？；，、.!?;,\n"

def clean_speech_text(raw_text):
    if not raw_text or not raw_text.strip():
        return ""

    text = raw_text
    for old, new in [
        ("**", ""), ("__", ""), ("`", ""),
        ("（", "，"), ("）", "，"), ("(", "，"), (")", "，"),
        ("——", "，"), ("-", "，"),
    ]:
        text = text.replace(old, new)
    text = re.sub(r"https?://\S+", "网页链接", text)
    text = re.sub(r"[^\u4e00-\u9fa5a-zA-Z0-9\s，,。.!?！？；;、]", "", text)
    text = re.sub(r"\s+", " ", text)
    return text.strip()

def drain_completed_sentences(flush_tail):
    global pending_sentence_buffer

    result = []
    sentence_start = 0
    text = pending_sentence_buffer
    for i, ch in enumerate(text):
        should_break = is_speech_break(ch) or i - sentence_start + 1 >= MAX_SPEECH_SEGMENT_LENGTH
        if not should_break:
            continue

        sentence = text[sentence_start:i + 1].strip()
        if sentence:
            result.append(sentence)
        sentence_start = i + 1

    tail = "" if sentence_start >= len(text) else text[sentence_start:]
    if flush_tail:
        final_sentence = tail.strip()
        if final_sentence:
            result.append(final_sentence)
        pending_sentence_buffer = ""
    else:
        pending_sentence_buffer = tail

    return result

def enqueue_assistant_speech(text, flush_tail):
    global pending_sentence_buffer, speak_queue

    if text:
        pending_sentence_buffer += text

    for sentence in drain_completed_sentences(flush_tail):
        clean_sentence = clean_speech_text(sentence)
        if clean_sentence:
            speak_queue.append(clean_sentence)

def try_speak_next(entity, scene):
    global is_speaking

    if is_speaking or not speak_queue:
        return

    sentence = speak_queue.pop(0)
    is_speaking = True
    show_bubble(entity, scene, current_user_text, current_assistant_text, "语音回复中...")
    entity.speak(sentence, on_completed=TTS_DONE_CALLBACK)

def get_prompt_input_text(scene):
    control = scene.get_gui_control("PromptInput")
    return (control.value.strip() if control and control.value else "")

def set_prompt_input_text(scene, text):
    control = scene.get_gui_control("PromptInput")
    if control:
        control.set_value(text or "")

def create_llm_system_prompt(scene):
    return "\n".join([
        "你是一个中文语音助手，回答要自然、简洁。",
        f"当前本机时间：{datetime.datetime.now():%Y-%m-%d %H:%M:%S}。",
        f"项目 skills 是否启用：{scene.llm.skills_enabled}。",
        f"项目 skills 目录：{scene.llm.skills_directory}。",
        "遇到需要外部能力、实时信息、项目文件、命令执行、计算或联网查询的问题时，主动调用工具，不要假装已经完成。",
        "如果不确定项目里有哪些能力，先调用 skill_list；需要了解某个 skill 的使用方法时，调用 skill_read；需要执行 skill 脚本时，按说明调用 skill_run_command。",
        "如果工具调用失败，请直接说明失败原因，不要编造工具没有返回的信息。",
    ])

def start_reply_from_prompt_input(entity, scene):
    global current_user_text, llm_request_id

    prompt = get_prompt_input_text(scene)
    if not prompt.strip():
        return

    current_user_text = prompt
    reset_reply_state(entity, stop_speaking=True)
    show_bubble(entity, scene, prompt, "", "思考中...")

    request_id = "voice_chat_llm"
    llm_request_id = request_id
    scene.llm.start_chat_with_tools(
        prompt,
        [],
        system_prompt=create_llm_system_prompt(scene),
        request_id=request_id,
        on_delta="voice_chat_llm_delta",
        on_completed="voice_chat_llm_done",
        on_error="voice_chat_llm_error",
        on_tool_call=LLM_TOOL_CALL_CALLBACK,
        on_tool_result=LLM_TOOL_RESULT_CALLBACK,
        max_tool_rounds=8)

def start(entity, scene, input, audio):
    print("LLM skills enabled:", scene.llm.skills_enabled, "skills directory:", scene.llm.skills_directory)

def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
    global asr_request_id, current_user_text, is_recording

    if control_name == "RecordButton" and event_name == "pressed":
        if not scene.asr.enabled or is_recording:
            return

        is_recording = True
        current_user_text = ""
        reset_reply_state(entity, stop_speaking=True)
        set_prompt_input_text(scene, "")
        show_bubble(entity, scene, "", "", "请说话，松开按钮后停止录音")

        asr_request_id = "voice_chat_asr"
        scene.asr.start_streaming_recognition(
            request_id=asr_request_id,
            on_partial="voice_chat_asr_partial",
            on_completed="voice_chat_asr_completed",
            on_error="voice_chat_asr_error")
        return

    if control_name == "RecordButton" and event_name == "released":
        if not is_recording:
            return

        is_recording = False
        scene.asr.stop_streaming_recognition(asr_request_id)
        show_bubble(entity, scene, current_user_text, "", "录音已结束，可编辑后点击发送")
        return

    if control_name == "SendButton" and event_name == "clicked":
        if is_recording:
            is_recording = False
            scene.asr.stop_streaming_recognition(asr_request_id)
        start_reply_from_prompt_input(entity, scene)

def voice_chat_asr_partial(entity, scene, input, audio, event):
    global current_user_text

    if event.get("requestId") != asr_request_id:
        return

    text = event.get("text", "")
    current_user_text = text
    set_prompt_input_text(scene, text)
    show_bubble(entity, scene, text, "", "请继续说，松开按钮后停止录音")

def voice_chat_asr_completed(entity, scene, input, audio, event):
    global current_user_text, is_recording

    if event.get("requestId") != asr_request_id:
        return

    is_recording = False
    current_user_text = event.get("text", "").strip()
    set_prompt_input_text(scene, current_user_text)
    show_bubble(entity, scene, current_user_text, "", "可编辑文本框后点击发送")

def voice_chat_asr_error(entity, scene, input, audio, event):
    global is_recording

    if event.get("requestId") != asr_request_id:
        return

    is_recording = False
    show_bubble(entity, scene, current_user_text, "", "ASR 出错")
    print("ASR error:", event.get("error", ""))

def voice_chat_llm_delta(entity, scene, input, audio, event):
    global current_assistant_text

    if event.get("requestId") != llm_request_id:
        return

    current_assistant_text = event.get("accumulatedText", "")
    show_bubble(
        entity,
        scene,
        current_user_text,
        current_assistant_text,
        "语音回复中..." if is_speaking else "正在生成回复...")

    enqueue_assistant_speech(event.get("delta", ""), flush_tail=False)
    try_speak_next(entity, scene)

def voice_chat_llm_done(entity, scene, input, audio, event):
    global reply_completed, current_assistant_text

    if event.get("requestId") != llm_request_id:
        return

    reply_completed = True
    current_assistant_text = event.get("accumulatedText", "")
    enqueue_assistant_speech("", flush_tail=True)
    show_bubble(
        entity,
        scene,
        current_user_text,
        current_assistant_text,
        "语音回复中..." if speak_queue or is_speaking else "回复完成")
    try_speak_next(entity, scene)

def voice_chat_llm_tool_call(entity, scene, input, audio, event):
    call = event.get("toolCall") or {}
    print("LLM tool call:", call.get("name", ""), call.get("argumentsJson", ""))
    show_bubble(entity, scene, current_user_text, current_assistant_text, f"正在调用工具：{call.get('name', '')}")

def voice_chat_llm_tool_result(entity, scene, input, audio, event):
    call = event.get("toolCall") or {}
    print("LLM tool result:", call.get("name", ""), event.get("toolResult", ""))
    show_bubble(entity, scene, current_user_text, current_assistant_text, f"工具返回结果：{call.get('name', '')}")

def voice_chat_tts_done(entity, scene, input, audio):
    global is_speaking

    is_speaking = False
    try_speak_next(entity, scene)

    if not is_speaking and not speak_queue and reply_completed:
        show_bubble(entity, scene, current_user_text, current_assistant_text, "回复完成")

def voice_chat_llm_error(entity, scene, input, audio, event):
    if event.get("requestId") != llm_request_id:
        return

    show_bubble(entity, scene, current_user_text, current_assistant_text, "LLM 出错")
    print("LLM error:", event.get("error", ""))
```

这个示例的关键点是：

- ASR 录音和 LLM 请求都是后台式回调，避免阻塞主脚本事件。
- `StartChatWithTools` 即使传入空工具列表，也会在项目启用 skills 时自动合并内置 `skill_*` 工具。
- TTS 使用队列和 `SpeakWithCallback` / `entity.speak(..., on_completed=...)` 串行播放，避免多段语音重叠。
- 送入 TTS 前先清理 Markdown、链接和不稳定符号，并把长回复切成短片段。

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
