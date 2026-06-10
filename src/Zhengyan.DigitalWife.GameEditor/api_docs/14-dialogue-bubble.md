---
id: dialogue-bubble
title: Dialogue Bubble API
category: GUI
objects:
  - RuntimeDialogueBubbleManager
  - RuntimeDialogueBubble
  - scene.bubble
keywords:
  - bubble
  - dialogue
  - overlay
  - chat
---

# Dialogue Bubble API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | Dialogue Bubble API |
| 分类 | GUI |
| 主要对象 | ``RuntimeDialogueBubbleManager``, ``RuntimeDialogueBubble``, ``scene.bubble`` |
| C# 入口 | `Scene.Bubble.GetOrCreate` |
| Python 入口 | `scene.bubble.get_or_create` |
| 说明 | 运行时对话气泡、屏幕/世界定位、样式和完整语音聊天示例。 |

## API 内容

`Scene.Bubble` / `scene.bubble` 用于在运行时创建、显示、隐藏和删除对话气泡。它和 GUI 控件不同，不需要预先在 GameEditor 里摆一个控件；脚本里按名字创建即可。

适合的场景：

- 给 NPC 头顶挂台词气泡
- 做屏幕角落的系统提示、任务提示
- 复刻 `DigitalHuman` 那种 `header + text + footer` 的三段式气泡

管理器 API：

| C# | Python | 说明 |
| --- | --- | --- |
| `Scene.Bubble.Count` | `scene.bubble.count` | 当前运行时气泡数量。 |
| `Scene.Bubble.Names` | `scene.bubble.names` | 当前气泡名称列表。 |
| `Scene.Bubble.VisibleNames` | `scene.bubble.visible_names` | 当前可见气泡名称列表。 |
| `Scene.Bubble.Contains(name)` | `scene.bubble.contains(name)` | 是否已存在指定名字的气泡。 |
| `Scene.Bubble.GetOrCreate(name)` | `scene.bubble.get_or_create(name)` | 获取或创建一个命名气泡。 |
| `Scene.Bubble.Create(name)` | `scene.bubble.create(name)` | `GetOrCreate` 的别名。 |
| `Scene.Bubble.ShowText(name, text, headerText, footerText)` | `scene.bubble.show(name, text="", header_text="", footer_text="")` | 快速显示一条气泡。 |
| `Scene.Bubble.HideAll()` | `scene.bubble.hide_all()` | 隐藏全部气泡，但保留对象。 |
| `Scene.Bubble.Remove(name)` | `scene.bubble.remove(name)` | 删除指定气泡。 |
| `Scene.Bubble.Clear()` | `scene.bubble.clear()` | 删除全部气泡。 |

单个气泡常用属性 / 方法：

| C# | Python | 说明 |
| --- | --- | --- |
| `Visible` / `Show()` / `Hide()` | `set_visible(value)` / `show(...)` / `hide()` | 显示或隐藏气泡。 |
| `HeaderText` / `Text` / `FooterText` | `set_header_text(...)` / `set_text(...)` / `set_footer_text(...)` | 三段文本内容。都为空时不会绘制。 |
| `LayoutMode` | `set_layout_mode("absolute" / "relative")` | `relative` 会按项目窗口基准分辨率缩放位置、宽度、字号和边距。 |
| `AnchorMode` | `set_anchor_mode("screen" / "world" / "entity")` | 屏幕坐标、世界坐标或绑定实体。 |
| `AttachToEntity(name, useModelTopAnchor)` | `attach_to_entity(name, use_model_top_anchor=True)` | 把气泡挂到实体上；PMX 默认取模型包围盒顶部中心点，更接近数字人气泡。 |
| `UseScreenSpace(x, y, layoutMode)` / `SetScreenPosition(x, y)` | `set_screen_position(x, y, layout_mode=None)` | 直接设置屏幕锚点位置。 |
| `SetScreenOffset(x, y)` | `set_screen_offset(x, y)` | 在最终投影位置上再叠加一个 2D 偏移。 |
| `UseWorldSpace(x, y, z)` / `SetWorldPosition(x, y, z)` | `set_world_position(x, y, z)` | 把气泡锚到一个世界坐标点。 |
| `SetWorldOffset(x, y, z)` | `set_world_offset(x, y, z)` | 世界坐标偏移，常用于把气泡抬高一点。 |
| `Width` | `set_width(width)` | 文本换行宽度。 |
| `SetPadding(x, y)` | `set_padding(x, y)` | 气泡内边距。 |
| `SetPivot(x, y)` | `set_pivot(x, y)` | 锚点枢轴，范围 `0.0 ~ 1.0`。例如 `(0.5, 1.0)` 表示底边中心对齐锚点。 |
| `TextAlignment` | `set_text_alignment("left" / "center" / "right")` | 三段文本统一的水平对齐方式。 |
| `FontSize` / `HeaderFontSize` / `FooterFontSize` | `set_font_size(...)` / `set_header_font_size(...)` / `set_footer_font_size(...)` | 三段字号。 |
| `BackgroundColor` / `BorderColor` | `set_background_color(r, g, b, a)` / `set_border_color(...)` | 气泡背景和边框颜色。 |
| `TextColor` / `HeaderTextColor` / `FooterTextColor` | `set_text_color(...)` / `set_header_text_color(...)` / `set_footer_text_color(...)` | 三段文字颜色。 |
| `Rounding` | `set_rounding(value)` | 圆角半径。 |
| `BorderThickness` | `set_border_thickness(value)` | 边框粗细。 |
| `DrawOrder` | `set_draw_order(value)` | 绘制顺序；值越大越靠后绘制。 |

### C#：挂在 NPC 头顶的对话气泡

```csharp
RuntimeDialogueBubble bubble = Scene.Bubble.GetOrCreate("shopkeeper");
bubble.AttachToEntity("Shopkeeper", useModelTopAnchor: true);
bubble.SetWorldOffset(0.0f, 0.18f, 0.0f);
bubble.SetScreenOffset(0.0f, -18.0f);
bubble.Width = 420.0f;
bubble.TextAlignment = "left";
bubble.FontSize = 22.0f;
bubble.HeaderFontSize = 18.0f;
bubble.FooterFontSize = 16.0f;
bubble.BackgroundColor = new Vector4(0.08f, 0.10f, 0.16f, 0.92f);
bubble.BorderColor = new Vector4(0.62f, 0.84f, 1.0f, 0.95f);
bubble.HeaderTextColor = new Vector4(0.78f, 0.88f, 1.0f, 1.0f);
bubble.TextColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
bubble.FooterTextColor = new Vector4(0.76f, 0.80f, 0.88f, 1.0f);
bubble.SetContent(
    "欢迎来到海边集市，今天的新货刚到。",
    headerText: "商店老板",
    footerText: "按 E 继续");
bubble.Show();
```

### Python：屏幕右上角任务提示

```python
tip = scene.bubble.get_or_create("quest-tip")
tip.set_layout_mode("relative")
tip.set_screen_position(1180, 48, layout_mode="relative")
tip.set_pivot(1.0, 0.0)
tip.set_width(360)
tip.set_padding(16, 12)
tip.set_text_alignment("left")
tip.set_font_size(20)
tip.set_header_font_size(16)
tip.set_footer_font_size(15)
tip.set_background_color(0.08, 0.11, 0.18, 0.92)
tip.set_border_color(0.54, 0.80, 1.00, 0.95)
tip.set_header_text_color(0.80, 0.90, 1.00, 1.00)
tip.set_footer_text_color(0.74, 0.78, 0.86, 1.00)
tip.show(
    text="去码头找渔夫，打听暴风雨前的异常声响。",
    header_text="支线任务",
    footer_text="任务日志已更新")
scene.flush()
```

### Python：更接近 DigitalHuman 的三段式气泡

```python
reply = scene.bubble.get_or_create("assistant-reply")
reply.attach_to_entity("DigitalHumanBody", use_model_top_anchor=True)
reply.set_world_offset(0.0, 0.20, 0.0)
reply.set_screen_offset(0.0, -16.0)
reply.set_width(440)
reply.set_text_alignment("left")
reply.show(
    text="当然可以，我已经帮你整理好了今天的待办。",
    header_text="你：今天有什么安排？",
    footer_text="语音回复中...")
```

说明：

- C# 可以直接保留 `RuntimeDialogueBubble` 引用跨帧复用；Python 通常每次通过 `scene.bubble.get_or_create(name)` 取同名气泡即可。
- Python 里的 `scene.bubble.names` / `visible_names` 是当前脚本事件上下文的快照；如果你在同一事件里更新后想立刻刷新 UI，记得调用 `scene.flush()`。
- `entity` 模式下如果目标是 PMX 模型，会优先用模型顶部中心作为锚点；不是 PMX 时回退到实体当前 `Position`。

### 完整示例：按住录音 ASR -> 可编辑文本框 -> 点击发送 -> 气泡流式回复 + 句子级顺序 Speak

假设：

- 这个脚本绑定在要说话的角色实体上
- 场景里有一个录音按钮：`RecordButton`
- 场景里有一个输入文本框：`PromptInput`
- 场景里有一个发送按钮：`SendButton`
- `RecordButton` 使用按钮默认事件里的 `pressed` / `released`

这个示例实现的行为是：

1. 按住 `RecordButton` 时启动本地麦克风流式 ASR
2. ASR 的部分结果会实时填充到 `PromptInput`
3. 松开按钮时停止录音
4. 用户可以手动修改 `PromptInput` 中的文本
5. 点击 `SendButton` 后，把文本框内容发给 `Scene.Llm.StartChat(...)`
6. LLM 流式返回时，一边更新角色头顶气泡，一边按“句子”切分回复
7. 每次只调用一句 `SpeakWithCallback(...)`
8. 必须等上一句播完，才会播下一句，不会重叠

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;

static class VoiceChatState
{
    public const string BubbleName = "voice-chat-bubble";
    public const string TtsDoneCallback = "voice_chat_tts_done";

    public static string AsrRequestId = string.Empty;
    public static string LlmRequestId = string.Empty;
    public static string CurrentUserText = string.Empty;
    public static string CurrentAssistantText = string.Empty;
    public static string PendingSentenceBuffer = string.Empty;
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
    bubble.FontSize = 20.0f;
    bubble.HeaderFontSize = 16.0f;
    bubble.FooterFontSize = 14.0f;
    bubble.BackgroundColor = new Vector4(0.08f, 0.10f, 0.16f, 0.92f);
    bubble.BorderColor = new Vector4(0.58f, 0.82f, 1.0f, 0.95f);
    bubble.HeaderTextColor = new Vector4(0.82f, 0.90f, 1.0f, 1.0f);
    bubble.TextColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
    bubble.FooterTextColor = new Vector4(0.76f, 0.80f, 0.88f, 1.0f);
    return bubble;
}

void ShowListeningBubble(string userText, string footer)
{
    RuntimeDialogueBubble bubble = GetChatBubble();
    bubble.SetContent(
        userText,
        headerText: "正在听你说",
        footerText: footer);
    bubble.Show();
}

void ShowReplyBubble(string userText, string assistantText, string footer)
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

bool IsSentenceEnding(char ch)
{
    return ch is '。' or '！' or '？' or '；' or '.' or '!' or '?' or ';' or '\n';
}

IEnumerable<string> DrainCompletedSentences(ref string buffer, bool flushTail)
{
    List<string> result = [];
    int sentenceStart = 0;

    for (int i = 0; i < buffer.Length; i++)
    {
        if (!IsSentenceEnding(buffer[i]))
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
        VoiceChatState.SpeakQueue.Enqueue(sentence);
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
    ShowReplyBubble(VoiceChatState.CurrentUserText, VoiceChatState.CurrentAssistantText, "语音回复中...");
    Entity.SpeakWithCallback(sentence, VoiceChatState.TtsDoneCallback);
}

string GetPromptInputText()
{
    return Scene.GetGuiControl("PromptInput")?.Value.Trim() ?? string.Empty;
}

void SetPromptInputText(string text)
{
    Scene.GetGuiControl("PromptInput")?.SetValue(text ?? string.Empty);
}

void StartReplyFromPromptInput()
{
    string prompt = GetPromptInputText();
    if (string.IsNullOrWhiteSpace(prompt))
    {
        return;
    }

    VoiceChatState.CurrentUserText = prompt;
    ResetReplyState(stopSpeaking: true);

    ShowReplyBubble(prompt, string.Empty, "思考中...");

    VoiceChatState.LlmRequestId = Scene.Llm.StartChat(
        Entity,
        prompt,
        systemPrompt: "你是一个中文语音助手，请简洁、自然地回答用户的问题。",
        onDeltaCallback: "voice_chat_llm_delta",
        onCompletedCallback: "voice_chat_llm_done",
        onErrorCallback: "voice_chat_llm_error");
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
    ShowListeningBubble(string.Empty, "请说话，松开按钮后停止录音");

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
    ShowListeningBubble(VoiceChatState.CurrentUserText, "录音已结束，可编辑后点击发送");
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
    ShowListeningBubble(AsrText, "请继续说，松开按钮后停止录音");
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
    ShowReplyBubble(VoiceChatState.CurrentUserText, string.Empty, "可编辑文本框后点击发送");
}

if (IsAsrEvent && AsrCallbackName == "voice_chat_asr_error")
{
    if (!string.Equals(AsrRequestId, VoiceChatState.AsrRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.IsRecording = false;
    ShowReplyBubble(VoiceChatState.CurrentUserText, string.Empty, "ASR 出错");
    Console.Error.WriteLine(AsrError);
}

if (IsLlmEvent && LlmCallbackName == "voice_chat_llm_delta")
{
    if (!string.Equals(LlmRequestId, VoiceChatState.LlmRequestId, StringComparison.Ordinal))
    {
        return;
    }

    VoiceChatState.CurrentAssistantText = LlmText;
    ShowReplyBubble(
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
    EnqueueAssistantSpeech(string.Empty, flushTail: true);

    ShowReplyBubble(
        VoiceChatState.CurrentUserText,
        VoiceChatState.CurrentAssistantText,
        VoiceChatState.SpeakQueue.Count > 0 || VoiceChatState.IsSpeaking
            ? "语音回复中..."
            : "回复完成");

    TrySpeakNext();
}

if (IsSpeechEvent && SpeechCallbackName == VoiceChatState.TtsDoneCallback)
{
    VoiceChatState.IsSpeaking = false;
    TrySpeakNext();

    if (!VoiceChatState.IsSpeaking && VoiceChatState.SpeakQueue.Count == 0 && VoiceChatState.ReplyCompleted)
    {
        ShowReplyBubble(VoiceChatState.CurrentUserText, VoiceChatState.CurrentAssistantText, "回复完成");
    }
}

if (IsLlmEvent && LlmCallbackName == "voice_chat_llm_error")
{
    if (!string.Equals(LlmRequestId, VoiceChatState.LlmRequestId, StringComparison.Ordinal))
    {
        return;
    }

    ShowReplyBubble(VoiceChatState.CurrentUserText, VoiceChatState.CurrentAssistantText, "LLM 出错");
    Console.Error.WriteLine(LlmError);
}
```

这个例子的关键点：

- `RecordButton` 的 `pressed` / `released` 只负责开始和停止 ASR，不会自动发给 LLM
- `PromptInput` 始终保留为最终可编辑输入源，所以用户可以修正 ASR 结果
- 发送时走已经实现好的 `Scene.Llm.StartChat(...)`
- 对话气泡每次都按“用户输入 + 助手回复 + 状态 footer”更新，比较接近 `DigitalHuman` 的呈现方式
- `LlmDelta` 只用来做“句子切分入队”；真正显示给用户的完整文本始终用 `LlmText`
- `SpeakWithCallback(...)` + `Queue<string>` 保证一句播完以后才播下一句，不会出现上一句没播完就插播下一句

如果你想写 Python 版，核心 API 对应关系就是：

- `Scene.Asr.StartStreamingRecognition(...)` -> `scene.asr.start_streaming_recognition(...)`
- `Scene.Llm.StartChat(...)` -> `scene.llm.start_chat(...)`
- `Scene.Bubble.GetOrCreate(...)` -> `scene.bubble.get_or_create(...)`
- `Entity.SpeakWithCallback(...)` -> `entity.speak(..., on_completed="callback_name")`
