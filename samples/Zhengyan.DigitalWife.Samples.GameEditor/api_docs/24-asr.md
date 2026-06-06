---
id: asr
title: ASR API
category: 语音
objects:
  - RuntimeAsr
  - RuntimeAsrScriptEvent
  - scene.asr
keywords:
  - asr
  - speech recognition
  - start_streaming_recognition
---

# ASR API

## 结构化索引

| 项 | 内容 |
| --- | --- |
| 模块 | ASR API |
| 分类 | 语音 |
| 主要对象 | ``RuntimeAsr``, ``RuntimeAsrScriptEvent``, ``scene.asr`` |
| C# 入口 | `Scene.Asr.StartStreamingRecognition` |
| Python 入口 | `scene.asr.start_streaming_recognition` |
| 说明 | ASR 流式识别、回调事件字段、按住录音和发送 LLM 示例。 |

## API 内容

## ASR API

GamePlayer 支持从脚本调用本地麦克风录音和 ASR 识别。配置在 GameEditor 的 `Project` 标签页 `ASR` 中，也会保存到 `game.project.json` 的 `asr` 节点。

当前支持：

- `SherpaOnnx`
- `Whisper.net`

主要场景是：

- 按下按钮开始录音
- 一边录音一边拿到部分识别结果
- 松开按钮后拿到最终文本
- 再把最终文本交给 `Scene.Llm` 做后续对话逻辑

配置字段：

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 是否启用脚本 ASR。 |
| `Provider` | `sherpa` 或 `whisper`。 |
| `InputDeviceIndex` | 本地 `PortAudio` 输入设备索引；为空时使用默认输入设备。 |
| `PartialResultIntervalSeconds` | 流式部分结果推送间隔。对 Whisper 更明显；Sherpa 在线模型会更频繁地产生 partial。 |
| `Capture.SampleRate` / `Channels` / `FramesPerBuffer` | 麦克风打开参数。 |
| `Sherpa.*` | Sherpa 模型路径、线程、采样率、语言、provider 等。 |
| `Whisper.*` | Whisper 模型路径、语言、线程、是否用 GPU 等。 |

### C# ASR

`Scene.Asr` 采用后台录音 + 后台识别 + 脚本事件回调模式。

C# API：

| API | 说明 |
| --- | --- |
| `Scene.Asr.Enabled` | 当前项目是否启用 ASR。 |
| `Scene.Asr.Provider` | 当前 ASR Provider 名称。 |
| `Scene.Asr.InputDeviceIndex` | 当前输入设备索引；`null` 表示默认设备。 |
| `Scene.Asr.PartialResultIntervalSeconds` | 当前部分结果间隔秒数。 |
| `Scene.Asr.IsRecording` | 当前是否在录音。 |
| `StartStreamingRecognition(entity, requestId, onPartialCallback, onCompletedCallback, onErrorCallback)` | 开始后台录音，并持续推送部分识别结果。 |
| `StopStreamingRecognition(requestId)` | 停止指定流式识别；如果不传 requestId，则停止当前录音。 |
| `StopAllStreamingRecognitions()` | 停止全部流式识别。 |

ASR 回调事件字段：

| 字段 | 说明 |
| --- | --- |
| `AsrRequestId` | 识别请求 Id。 |
| `AsrEventName` | `partial`、`completed`、`error`。 |
| `AsrText` | 当前识别文本。 |
| `AsrIsFinal` | 当前是否最终结果。 |
| `AsrError` | 错误文本。 |
| `AsrCallbackName` | 当前命中的回调名。 |
| `AsrOffsetSeconds` | 当前结果对应的音频偏移秒数。 |

完整示例：按下按钮开始录音，录音时实时显示 ASR 结果；松开按钮后把最终文本发送给 LLM，流式接收回复，同时按句子顺序调用 `Speak(...)`。

假设场景里有：

- 一个按钮：`Record Button`
- 一个文本框：`Asr Box`
- 一个标签：`Reply Label`

```csharp
using System.Text;

static class AsrChatState
{
    public static bool Recording;
    public static string AsrRequestId = string.Empty;
    public static string AsrText = string.Empty;
    public static string LlmReplyText = string.Empty;
    public static StringBuilder PendingSentenceBuffer = new();
    public static Queue<string> SpeakQueue = new();
    public static bool Speaking;
}

void ClearReplyUi()
{
    Scene.GetGuiControl("Reply Label")?.SetValue(string.Empty);
    AsrChatState.LlmReplyText = string.Empty;
    AsrChatState.PendingSentenceBuffer.Clear();
    AsrChatState.SpeakQueue.Clear();
    AsrChatState.Speaking = false;
}

void TrySpeakNext()
{
    if (AsrChatState.Speaking || AsrChatState.SpeakQueue.Count == 0)
    {
        return;
    }

    string sentence = AsrChatState.SpeakQueue.Dequeue();
    AsrChatState.Speaking = true;
    Entity.Speak(sentence, () =>
    {
        AsrChatState.Speaking = false;
        TrySpeakNext();
    });
}

void EnqueueCompletedSentences(string text, bool flushTail)
{
    AsrChatState.PendingSentenceBuffer.Append(text);
    string buffer = AsrChatState.PendingSentenceBuffer.ToString();
    int lastCut = 0;
    for (int i = 0; i < buffer.Length; i++)
    {
        char ch = buffer[i];
        if (ch is '。' or '！' or '？' or '!' or '?' or ';' or '；')
        {
            string sentence = buffer[lastCut..(i + 1)].Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                AsrChatState.SpeakQueue.Enqueue(sentence);
            }

            lastCut = i + 1;
        }
    }

    if (flushTail)
    {
        string tail = buffer[lastCut..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            AsrChatState.SpeakQueue.Enqueue(tail);
        }

        AsrChatState.PendingSentenceBuffer.Clear();
    }
    else
    {
        AsrChatState.PendingSentenceBuffer.Clear();
        AsrChatState.PendingSentenceBuffer.Append(buffer[lastCut..]);
    }

    TrySpeakNext();
}

if (IsGuiEvent && GuiControlName == "Record Button" && GuiEventName == "pressed")
{
    if (!Scene.Asr.Enabled || AsrChatState.Recording)
    {
        return;
    }

    AsrChatState.Recording = true;
    AsrChatState.AsrText = string.Empty;
    Scene.GetGuiControl("Asr Box")?.SetValue(string.Empty);
    ClearReplyUi();

    AsrChatState.AsrRequestId = Scene.Asr.StartStreamingRecognition(
        Entity,
        onPartialCallback: "asr_partial",
        onCompletedCallback: "asr_completed",
        onErrorCallback: "asr_error");
}

if (IsGuiEvent && GuiControlName == "Record Button" && GuiEventName == "released")
{
    if (!AsrChatState.Recording)
    {
        return;
    }

    AsrChatState.Recording = false;
    Scene.Asr.StopStreamingRecognition(AsrChatState.AsrRequestId);
}

if (IsAsrEvent && AsrCallbackName == "asr_partial")
{
    AsrChatState.AsrText = AsrText;
    Scene.GetGuiControl("Asr Box")?.SetValue(AsrText);
}

if (IsAsrEvent && AsrCallbackName == "asr_completed")
{
    AsrChatState.Recording = false;
    AsrChatState.AsrText = AsrText.Trim();
    Scene.GetGuiControl("Asr Box")?.SetValue(AsrChatState.AsrText);

    if (!string.IsNullOrWhiteSpace(AsrChatState.AsrText))
    {
        ClearReplyUi();
        Scene.Llm.StartChat(
            Entity,
            AsrChatState.AsrText,
            systemPrompt: "你是一个中文语音助手，请简洁、自然地回答用户的问题。",
            onDeltaCallback: "reply_delta",
            onCompletedCallback: "reply_done",
            onErrorCallback: "reply_error");
    }
}

if (IsAsrEvent && AsrCallbackName == "asr_error")
{
    AsrChatState.Recording = false;
    Console.Error.WriteLine(AsrError);
}

if (IsLlmEvent && LlmCallbackName == "reply_delta")
{
    AsrChatState.LlmReplyText = LlmText;
    Scene.GetGuiControl("Reply Label")?.SetValue(AsrChatState.LlmReplyText);
    EnqueueCompletedSentences(LlmDelta, flushTail: false);
}

if (IsLlmEvent && LlmCallbackName == "reply_done")
{
    AsrChatState.LlmReplyText = LlmText;
    Scene.GetGuiControl("Reply Label")?.SetValue(AsrChatState.LlmReplyText);

    string tail = AsrChatState.PendingSentenceBuffer.ToString();
    AsrChatState.PendingSentenceBuffer.Clear();
    if (!string.IsNullOrWhiteSpace(tail))
    {
        EnqueueCompletedSentences(tail, flushTail: true);
    }
}

if (IsLlmEvent && LlmCallbackName == "reply_error")
{
    Console.Error.WriteLine(LlmError);
}
```

### Python ASR

Python API：

| API | 说明 |
| --- | --- |
| `scene.asr.enabled` | 当前项目是否启用 ASR。 |
| `scene.asr.provider` | 当前 ASR Provider 名称。 |
| `scene.asr.input_device_index` | 当前输入设备索引。 |
| `scene.asr.partial_result_interval_seconds` | 当前部分结果间隔秒数。 |
| `scene.asr.is_recording` | 当前是否在录音。 |
| `scene.asr.start_streaming_recognition(request_id=None, on_partial="asr_partial", on_completed="asr_completed", on_error="asr_error")` | 开始后台录音并持续识别。 |
| `scene.asr.stop_streaming_recognition(request_id=None)` | 停止流式识别。 |

Python 通用回调事件字典字段包括：

- `requestId`
- `eventName`
- `text`
- `isFinal`
- `error`
- `callbackName`
- `offsetSeconds`

---

## ASR API

GamePlayer 支持从脚本调用本地麦克风和 ASR 识别器。配置在 GameEditor 的 `Project` 标签页 `ASR` 中，也会保存到 `game.project.json` 的 `asr` 节点。

当前支持：

- `sherpa`
- `whisper`

常见用法是：

- 按钮 `pressed` 时启动流式录音识别
- 一边录音一边更新识别文本
- 按钮 `released` 时停止录音并拿到最终文本
- 再把最终文本交给 `Scene.Llm` 继续做对话逻辑

项目配置重点：

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 是否启用脚本层 ASR。 |
| `Provider` | `sherpa` 或 `whisper`。 |
| `InputDeviceIndex` | 本地 `PortAudio` 输入设备索引；为空表示默认输入设备。 |
| `PartialResultIntervalSeconds` | 流式部分结果刷新间隔。 |
| `Capture` | 麦克风打开参数：`SampleRate`、`Channels`、`FramesPerBuffer`。 |
| `Sherpa.*` | SherpaOnnx 模型与推理参数。 |
| `Whisper.*` | Whisper 模型与推理参数。 |

### C# ASR

C# API：

| API | 说明 |
| --- | --- |
| `Scene.Asr.Enabled` | 当前项目是否启用 ASR。 |
| `Scene.Asr.Provider` | 当前 provider。 |
| `Scene.Asr.InputDeviceIndex` | 当前输入设备索引；`null` 表示默认设备。 |
| `Scene.Asr.PartialResultIntervalSeconds` | 当前部分结果刷新间隔。 |
| `Scene.Asr.IsRecording` | 当前是否正在录音识别。 |
| `StartStreamingRecognition(entity, requestId, onPartialCallback, onCompletedCallback, onErrorCallback)` | 开始后台录音，并推送部分识别结果。 |
| `StopStreamingRecognition(requestId)` | 停止指定流式识别；为空则停止当前录音。 |
| `StopAllStreamingRecognitions()` | 停止所有流式识别任务。 |

ASR 回调字段：

| 字段 | 说明 |
| --- | --- |
| `AsrRequestId` | 当前请求 Id。 |
| `AsrEventName` | `partial`、`completed`、`error`。 |
| `AsrText` | 当前识别文本。 |
| `AsrIsFinal` | 是否最终结果。 |
| `AsrError` | 错误文本。 |
| `AsrCallbackName` | 当前回调名。 |
| `AsrOffsetSeconds` | 当前识别文本对应的音频时长偏移。 |

### Python ASR

Python API：

| API | 说明 |
| --- | --- |
| `scene.asr.enabled` | 当前项目是否启用 ASR。 |
| `scene.asr.provider` | 当前 provider。 |
| `scene.asr.input_device_index` | 当前输入设备索引。 |
| `scene.asr.partial_result_interval_seconds` | 当前部分结果刷新间隔。 |
| `scene.asr.is_recording` | 当前是否正在录音识别。 |
| `scene.asr.start_streaming_recognition(request_id=None, on_partial="asr_partial", on_completed="asr_completed", on_error="asr_error")` | 开始后台录音识别。 |
| `scene.asr.stop_streaming_recognition(request_id=None)` | 停止流式识别。 |

Python 通用事件函数：

```python
def asr_event(entity, scene, input, audio, event):
    print(event["eventName"], event["text"])
```

### 完整示例：按住录音 -> 流式 ASR -> 松开发给 LLM -> 句子级顺序 Speak

假设场景中有：

- 一个按钮：`RecordButton`
- 一个文本框：`AsrInput`
- 一个标签：`Reply`

```csharp
using System.Text;

static class DemoState
{
    public static string AsrRequestId = string.Empty;
    public static string CurrentAsrText = string.Empty;
    public static string LlmAccumulatedText = string.Empty;
    public static string PendingSentenceBuffer = string.Empty;
    public static Queue<string> SpeakQueue = new();
    public static bool IsSpeaking;
}

IEnumerable<string> ExtractCompletedSentences(ref string buffer)
{
    List<string> result = [];
    int start = 0;
    for (int i = 0; i < buffer.Length; i++)
    {
        char ch = buffer[i];
        if (ch is '。' or '！' or '？' or '.' or '!' or '?')
        {
            string sentence = buffer[start..(i + 1)].Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                result.Add(sentence);
            }

            start = i + 1;
        }
    }

    buffer = start >= buffer.Length ? string.Empty : buffer[start..];
    return result;
}

void TrySpeakNext()
{
    if (DemoState.IsSpeaking || DemoState.SpeakQueue.Count == 0)
    {
        return;
    }

    string sentence = DemoState.SpeakQueue.Dequeue();
    DemoState.IsSpeaking = true;
    Entity.SpeakWithCallback(sentence, "llm_sentence_done");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "pressed")
{
    DemoState.CurrentAsrText = string.Empty;
    DemoState.LlmAccumulatedText = string.Empty;
    DemoState.PendingSentenceBuffer = string.Empty;
    DemoState.SpeakQueue.Clear();
    DemoState.IsSpeaking = false;

    Scene.GetGuiControl("AsrInput")?.SetValue("");
    Scene.GetGuiControl("Reply")?.SetValue("");

    DemoState.AsrRequestId = Scene.Asr.StartStreamingRecognition(
        Entity,
        requestId: "push_to_talk",
        onPartialCallback: "asr_partial",
        onCompletedCallback: "asr_completed",
        onErrorCallback: "asr_error");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "released")
{
    Scene.Asr.StopStreamingRecognition(DemoState.AsrRequestId);
}

if (IsAsrEvent && AsrCallbackName == "asr_partial")
{
    DemoState.CurrentAsrText = AsrText;
    Scene.GetGuiControl("AsrInput")?.SetValue(AsrText);
}

if (IsAsrEvent && AsrCallbackName == "asr_completed")
{
    DemoState.CurrentAsrText = AsrText.Trim();
    Scene.GetGuiControl("AsrInput")?.SetValue(DemoState.CurrentAsrText);

    if (!string.IsNullOrWhiteSpace(DemoState.CurrentAsrText))
    {
        Scene.Llm.StartChat(
            Entity,
            DemoState.CurrentAsrText,
            systemPrompt: "你是一个中文语音助手，请简洁、自然地回答用户的问题。",
            onDeltaCallback: "reply_delta",
            onCompletedCallback: "reply_done",
            onErrorCallback: "reply_error");
    }
}

if (IsAsrEvent && AsrCallbackName == "asr_error")
{
    Console.Error.WriteLine(AsrError);
}

if (IsLlmEvent && LlmCallbackName == "reply_delta")
{
    DemoState.LlmAccumulatedText = LlmText;
    Scene.GetGuiControl("Reply")?.SetValue(DemoState.LlmAccumulatedText);

    string delta = LlmDelta;
    if (!string.IsNullOrEmpty(delta))
    {
        DemoState.PendingSentenceBuffer += delta;
        foreach (string sentence in ExtractCompletedSentences(ref DemoState.PendingSentenceBuffer))
        {
            DemoState.SpeakQueue.Enqueue(sentence);
        }

        TrySpeakNext();
    }
}

if (IsLlmEvent && LlmCallbackName == "reply_done")
{
    Scene.GetGuiControl("Reply")?.SetValue(LlmText);

    string tail = DemoState.PendingSentenceBuffer.Trim();
    if (!string.IsNullOrWhiteSpace(tail))
    {
        DemoState.SpeakQueue.Enqueue(tail);
        DemoState.PendingSentenceBuffer = string.Empty;
    }

    TrySpeakNext();
}

if (IsSpeechEvent && SpeechCallbackName == "llm_sentence_done")
{
    DemoState.IsSpeaking = false;
    TrySpeakNext();
}

if (IsLlmEvent && LlmCallbackName == "reply_error")
{
    Console.Error.WriteLine(LlmError);
}
```

这个示例的关键点是：

- ASR 用 `pressed` / `released` 做 push-to-talk
- LLM 仍然走已经有的 `Scene.Llm.StartChat(...)`
- `SpeakWithCallback(...)` 保证一句说完后再说下一句，不会重叠播放

---

## ASR API

GamePlayer 支持从脚本调用本地麦克风和 ASR 识别器。配置在 GameEditor 的 `Project` 标签页 `ASR` 中，也会保存到 `game.project.json` 的 `asr` 节点。

当前支持：

- `sherpa`
- `whisper`

常见用法是：

- 按钮 `pressed` 时启动流式录音识别
- 一边录音一边更新识别文本
- 按钮 `released` 时停止录音并拿到最终文本
- 再把最终文本交给 `Scene.Llm` 继续做对话逻辑

项目配置重点：

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 是否启用脚本层 ASR。 |
| `Provider` | `sherpa` 或 `whisper`。 |
| `InputDeviceIndex` | 本地 `PortAudio` 输入设备索引；为空表示默认输入设备。 |
| `PartialResultIntervalSeconds` | 流式部分结果刷新间隔。 |
| `Capture` | 麦克风打开参数：`SampleRate`、`Channels`、`FramesPerBuffer`。 |
| `Sherpa.*` | SherpaOnnx 模型与推理参数。 |
| `Whisper.*` | Whisper 模型与推理参数。 |

### C# ASR

C# API：

| API | 说明 |
| --- | --- |
| `Scene.Asr.Enabled` | 当前项目是否启用 ASR。 |
| `Scene.Asr.Provider` | 当前 provider。 |
| `Scene.Asr.InputDeviceIndex` | 当前输入设备索引；`null` 表示默认设备。 |
| `Scene.Asr.PartialResultIntervalSeconds` | 当前部分结果刷新间隔。 |
| `Scene.Asr.IsRecording` | 当前是否正在录音识别。 |
| `StartStreamingRecognition(entity, requestId, onPartialCallback, onCompletedCallback, onErrorCallback)` | 开始后台录音，并推送部分识别结果。 |
| `StopStreamingRecognition(requestId)` | 停止指定流式识别；为空则停止当前录音。 |
| `StopAllStreamingRecognitions()` | 停止所有流式识别任务。 |

ASR 回调字段：

| 字段 | 说明 |
| --- | --- |
| `AsrRequestId` | 当前请求 Id。 |
| `AsrEventName` | `partial`、`completed`、`error`。 |
| `AsrText` | 当前识别文本。 |
| `AsrIsFinal` | 是否最终结果。 |
| `AsrError` | 错误文本。 |
| `AsrCallbackName` | 当前回调名。 |
| `AsrOffsetSeconds` | 当前识别文本对应的音频时长偏移。 |

### Python ASR

Python API：

| API | 说明 |
| --- | --- |
| `scene.asr.enabled` | 当前项目是否启用 ASR。 |
| `scene.asr.provider` | 当前 provider。 |
| `scene.asr.input_device_index` | 当前输入设备索引。 |
| `scene.asr.partial_result_interval_seconds` | 当前部分结果刷新间隔。 |
| `scene.asr.is_recording` | 当前是否正在录音识别。 |
| `scene.asr.start_streaming_recognition(request_id=None, on_partial="asr_partial", on_completed="asr_completed", on_error="asr_error")` | 开始后台录音识别。 |
| `scene.asr.stop_streaming_recognition(request_id=None)` | 停止流式识别。 |

Python 通用事件函数：

```python
def asr_event(entity, scene, input, audio, event):
    print(event["eventName"], event["text"])
```

### 完整示例：按住录音 -> 流式 ASR -> 松开发给 LLM -> 句子级顺序 Speak

假设场景中有：

- 一个按钮：`RecordButton`
- 一个文本框：`AsrInput`
- 一个标签：`Reply`

```csharp
using System.Text;

static class DemoState
{
    public static string AsrRequestId = string.Empty;
    public static string CurrentAsrText = string.Empty;
    public static string LlmAccumulatedText = string.Empty;
    public static string PendingSentenceBuffer = string.Empty;
    public static Queue<string> SpeakQueue = new();
    public static bool IsSpeaking;
}

IEnumerable<string> ExtractCompletedSentences(ref string buffer)
{
    List<string> result = [];
    int start = 0;
    for (int i = 0; i < buffer.Length; i++)
    {
        char ch = buffer[i];
        if (ch is '。' or '！' or '？' or '.' or '!' or '?')
        {
            string sentence = buffer[start..(i + 1)].Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                result.Add(sentence);
            }

            start = i + 1;
        }
    }

    buffer = start >= buffer.Length ? string.Empty : buffer[start..];
    return result;
}

void TrySpeakNext()
{
    if (DemoState.IsSpeaking || DemoState.SpeakQueue.Count == 0)
    {
        return;
    }

    string sentence = DemoState.SpeakQueue.Dequeue();
    DemoState.IsSpeaking = true;
    Entity.SpeakWithCallback(sentence, "llm_sentence_done");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "pressed")
{
    DemoState.CurrentAsrText = string.Empty;
    DemoState.LlmAccumulatedText = string.Empty;
    DemoState.PendingSentenceBuffer = string.Empty;
    DemoState.SpeakQueue.Clear();
    DemoState.IsSpeaking = false;

    Scene.GetGuiControl("AsrInput")?.SetValue("");
    Scene.GetGuiControl("Reply")?.SetValue("");

    DemoState.AsrRequestId = Scene.Asr.StartStreamingRecognition(
        Entity,
        requestId: "push_to_talk",
        onPartialCallback: "asr_partial",
        onCompletedCallback: "asr_completed",
        onErrorCallback: "asr_error");
}

if (IsGuiEvent && GuiControlName == "RecordButton" && GuiEventName == "released")
{
    Scene.Asr.StopStreamingRecognition(DemoState.AsrRequestId);
}

if (IsAsrEvent && AsrCallbackName == "asr_partial")
{
    DemoState.CurrentAsrText = AsrText;
    Scene.GetGuiControl("AsrInput")?.SetValue(AsrText);
}

if (IsAsrEvent && AsrCallbackName == "asr_completed")
{
    DemoState.CurrentAsrText = AsrText.Trim();
    Scene.GetGuiControl("AsrInput")?.SetValue(DemoState.CurrentAsrText);

    if (!string.IsNullOrWhiteSpace(DemoState.CurrentAsrText))
    {
        Scene.Llm.StartChat(
            Entity,
            DemoState.CurrentAsrText,
            systemPrompt: "你是一个中文语音助手，请简洁、自然地回答用户的问题。",
            onDeltaCallback: "reply_delta",
            onCompletedCallback: "reply_done",
            onErrorCallback: "reply_error");
    }
}

if (IsAsrEvent && AsrCallbackName == "asr_error")
{
    Console.Error.WriteLine(AsrError);
}

if (IsLlmEvent && LlmCallbackName == "reply_delta")
{
    DemoState.LlmAccumulatedText = LlmText;
    Scene.GetGuiControl("Reply")?.SetValue(DemoState.LlmAccumulatedText);

    string delta = LlmDelta;
    if (!string.IsNullOrEmpty(delta))
    {
        DemoState.PendingSentenceBuffer += delta;
        foreach (string sentence in ExtractCompletedSentences(ref DemoState.PendingSentenceBuffer))
        {
            DemoState.SpeakQueue.Enqueue(sentence);
        }

        TrySpeakNext();
    }
}

if (IsLlmEvent && LlmCallbackName == "reply_done")
{
    Scene.GetGuiControl("Reply")?.SetValue(LlmText);

    string tail = DemoState.PendingSentenceBuffer.Trim();
    if (!string.IsNullOrWhiteSpace(tail))
    {
        DemoState.SpeakQueue.Enqueue(tail);
        DemoState.PendingSentenceBuffer = string.Empty;
    }

    TrySpeakNext();
}

if (IsSpeechEvent && SpeechCallbackName == "llm_sentence_done")
{
    DemoState.IsSpeaking = false;
    TrySpeakNext();
}

if (IsLlmEvent && LlmCallbackName == "reply_error")
{
    Console.Error.WriteLine(LlmError);
}
```

这个示例的关键点是：

- ASR 用按钮 `pressed` / `released` 做 push-to-talk
- LLM 仍然走已经有的 `Scene.Llm.StartChat(...)`
- `SpeakWithCallback(...)` 保证一句说完后再说下一句
