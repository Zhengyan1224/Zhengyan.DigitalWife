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
| `Scene.Asr.Enabled` | 当前项目是否启用 ASR，且当前本机麦克风输入可用。 |
| `Scene.Asr.Provider` | 当前 ASR Provider 名称。 |
| `Scene.Asr.InputDeviceIndex` | 当前输入设备索引；`null` 表示默认设备。 |
| `Scene.Asr.MicrophoneInputAvailable` | 当前本机麦克风输入是否可用。 |
| `Scene.Asr.MicrophoneUnavailableReason` | 麦克风不可用时的原因。 |
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
| `scene.asr.enabled` | 当前项目是否启用 ASR，且当前本机麦克风输入可用。 |
| `scene.asr.provider` | 当前 ASR Provider 名称。 |
| `scene.asr.input_device_index` | 当前输入设备索引。 |
| `scene.asr.microphone_input_available` | 当前本机麦克风输入是否可用。 |
| `scene.asr.microphone_unavailable_reason` | 麦克风不可用时的原因。 |
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
| `Scene.Asr.Enabled` | 当前项目是否启用 ASR，且当前本机麦克风输入可用。 |
| `Scene.Asr.Provider` | 当前 provider。 |
| `Scene.Asr.InputDeviceIndex` | 当前输入设备索引；`null` 表示默认设备。 |
| `Scene.Asr.MicrophoneInputAvailable` | 当前本机麦克风输入是否可用。 |
| `Scene.Asr.MicrophoneUnavailableReason` | 麦克风不可用时的原因。 |
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
| `scene.asr.enabled` | 当前项目是否启用 ASR，且当前本机麦克风输入可用。 |
| `scene.asr.provider` | 当前 provider。 |
| `scene.asr.input_device_index` | 当前输入设备索引。 |
| `scene.asr.microphone_input_available` | 当前本机麦克风输入是否可用。 |
| `scene.asr.microphone_unavailable_reason` | 麦克风不可用时的原因。 |
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
| `Scene.Asr.Enabled` | 当前项目是否启用 ASR，且当前本机麦克风输入可用。 |
| `Scene.Asr.Provider` | 当前 provider。 |
| `Scene.Asr.InputDeviceIndex` | 当前输入设备索引；`null` 表示默认设备。 |
| `Scene.Asr.MicrophoneInputAvailable` | 当前本机麦克风输入是否可用。 |
| `Scene.Asr.MicrophoneUnavailableReason` | 麦克风不可用时的原因。 |
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
| `scene.asr.enabled` | 当前项目是否启用 ASR，且当前本机麦克风输入可用。 |
| `scene.asr.provider` | 当前 provider。 |
| `scene.asr.input_device_index` | 当前输入设备索引。 |
| `scene.asr.microphone_input_available` | 当前本机麦克风输入是否可用。 |
| `scene.asr.microphone_unavailable_reason` | 麦克风不可用时的原因。 |
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

## 本地 ASR 唤醒词监听

除了 Realtime Voice 服务端唤醒词监听，ASR 现在也支持在脚本层启动本地唤醒词监听。这个模式使用当前项目 ASR 配置里的 provider、模型和麦克风输入设备，不依赖 Realtime Voice 服务端。脚本启动监听时传入唤醒词组，GamePlayer 会循环录制短音频片段，用本地 ASR 识别文本并匹配唤醒词。

适用模式：

- Realtime Voice 唤醒词：`Scene.RealtimeVoice.StartWakeWordMonitoring(...)`，唤醒检测由 Realtime Voice 服务端完成，后续可走 Realtime 对话。
- 本地 ASR 唤醒词：`Scene.Asr.StartWakeWordMonitoring(...)`，唤醒检测由本地 ASR 完成，后续通常走 `ASR -> LLM with skills -> TTS`。

使用注意：

- 本地 ASR 唤醒词只要求项目 ASR 已启用且麦克风可用，不要求 `Project.RealtimeVoice.WakeWord.Enabled`。
- 唤醒词组由脚本启动监听时传入，可以按角色、场景或语言动态切换。
- ASR 唤醒监听会占用本地麦克风；启动普通 ASR 流式识别时会自动停止唤醒监听，反过来启动唤醒监听也会停止当前 ASR 流式识别。
- 本地 ASR 唤醒的准确率取决于 ASR 模型、采样率、环境噪声和唤醒词长度。建议使用 2 到 5 个字以上、和普通对白不容易混淆的唤醒词。
- `chunkDurationSeconds` 是每轮监听的基础录音片段长度；`extensionDurationSeconds` 会在识别结果像是唤醒词前缀时补录一小段；`trailingSilencePaddingSeconds` 会给短音频补一点尾部静音，通常能提升短词识别稳定性。
- 当前 `StartStreamingRecognition(...)` 是持续流式录音，最终结果通常在脚本调用 `StopStreamingRecognition(...)` 后产生。无按键的语音示例可以用一个短问题窗口自动停止录音，或者在项目中另行封装 VAD 式的一次性问题采集。

### C# API 补充

| API | 说明 |
| --- | --- |
| `Scene.Asr.IsWakeWordMonitoring` | 当前是否有本地 ASR 唤醒词监听任务。 |
| `Scene.Asr.StartWakeWordMonitoring(entity, wakeWords, requestId, chunkDurationSeconds, extensionDurationSeconds, trailingSilencePaddingSeconds, onDetectedCallback, onErrorCallback)` | 启动本地 ASR 唤醒词监听。`wakeWords` 是字符串集合；检测到后触发 `onDetectedCallback`。 |
| `Scene.Asr.StopWakeWordMonitoring()` | 停止本地 ASR 唤醒词监听。 |

ASR 唤醒事件字段：

| 字段 | 说明 |
| --- | --- |
| `AsrEventName` | 检测到唤醒词时为 `wake_word_detected`；出错时为 `error`。 |
| `AsrText` | 唤醒词之后的尾句。如果只说了唤醒词，这里通常为空。 |
| `AsrWakeWord` | 命中的唤醒词。 |
| `AsrRecognizedText` | 本地 ASR 识别到的完整文本。 |
| `AsrCallbackName` | 当前命中的回调名，例如 `local_wake_detected`。 |
| `AsrError` | 出错时的错误文本。 |

### Python API 补充

| API | 说明 |
| --- | --- |
| `scene.asr.is_wake_word_monitoring` | 当前快照中是否正在本地 ASR 唤醒词监听。 |
| `scene.asr.start_wake_word_monitoring(wake_words, request_id=None, chunk_duration_seconds=None, extension_duration_seconds=None, trailing_silence_padding_seconds=None, on_detected="asr_wake_word_detected", on_error="asr_wake_word_error")` | 启动本地 ASR 唤醒词监听。`wake_words` 可以是字符串或字符串列表。 |
| `scene.asr.stop_wake_word_monitoring()` | 停止本地 ASR 唤醒词监听。 |

Python ASR 唤醒事件字段使用 camelCase：

| 字段 | 说明 |
| --- | --- |
| `event["eventName"]` | `wake_word_detected` 或 `error`。 |
| `event["text"]` | 唤醒词之后的尾句。 |
| `event["wakeWord"]` | 命中的唤醒词。 |
| `event["recognizedText"]` | 本地 ASR 识别到的完整文本。 |
| `event["callbackName"]` | 当前命中的回调名。 |
| `event["error"]` | 出错时的错误文本。 |

### C#：本地 ASR 唤醒 -> ASR -> LLM with skills -> TTS

下面示例假设：

- 脚本绑定在要说话的人物实体上。
- 项目 ASR、LLM、TTS 已启用。
- 项目里已有 `assets/motions/basic_stand.vmd` 和 `assets/motions/basic_wait.vmd`。
- 显示只使用对话气泡，不依赖任何预先创建的 GUI 控件。
- 唤醒词触发后人物从 StandMotion 过渡到 WaitMotion，说“我在，请说。”，随后打开一个短 ASR 问题窗口。窗口结束后用最终 ASR 文本请求 LLM，启用 skills 时会自动合并内置 `skill_*` 工具。
- LLM 生成和 TTS 播放期间会重新打开唤醒词监听；再次说出唤醒词会取消当前 LLM 请求、停止当前 TTS、清空待播队列，并进入下一轮语音输入。

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;

static class LocalWakeChatState
{
    public const string BubbleName = "local-asr-wake-chat";
    public const string StandMotion = "assets/motions/basic_stand.vmd";
    public const string WaitMotion = "assets/motions/basic_wait.vmd";
    public const string TtsDoneCallbackPrefix = "local_wake_tts_done_";
    public const float MotionBlendDurationSeconds = 0.35f;
    public const double QuestionCaptureSeconds = 9.0;
    public const double ConversationIdleSeconds = 30.0;
    public const int MaxSpeechSegmentLength = 70;
    public const int MaxConversationTurns = 8;

    public static readonly string[] WakeWords = new[] { "晓雨", "小雨", "小玉", "小宇", "小鱼" };
    public static bool MotionBlendReady;
    public static float WaitLayerWeight;
    public static float TargetWaitLayerWeight;
    public static bool WakeWordMonitorStarted;
    public static bool WaitingForQuestion;
    public static bool QuestionStopRequested;
    public static DateTimeOffset QuestionDeadlineUtc = DateTimeOffset.MinValue;
    public static string AsrRequestId = string.Empty;
    public static string LlmRequestId = string.Empty;
    public static string CurrentUserText = string.Empty;
    public static string ActiveLlmUserText = string.Empty;
    public static string CurrentAssistantText = string.Empty;
    public static string PendingSpeechText = string.Empty;
    public static string CurrentSpeechCallback = string.Empty;
    public static Queue<string> SpeakQueue = new();
    public static bool IsSpeaking;
    public static bool ReplyInProgress;
    public static bool ReplyCompleted;
    public static bool Interrupted;
    public static int QuestionAsrRetryCount;
    public static List<RuntimeLlmChatMessage> ConversationHistory = new();
}

void EnsureMotionBlendSetup()
{
    if (LocalWakeChatState.MotionBlendReady)
    {
        return;
    }

    Entity.SetMotionLayers(new[]
    {
        new MotionLayerDefinition(LocalWakeChatState.StandMotion, 1.0f, false),
        new MotionLayerDefinition(LocalWakeChatState.WaitMotion, 0.0f, false)
    });
    Entity.LoopMotion = true;
    Entity.PlayMotion();
    LocalWakeChatState.MotionBlendReady = true;
}

void SetStandState()
{
    EnsureMotionBlendSetup();
    LocalWakeChatState.TargetWaitLayerWeight = 0.0f;
}

void SetWaitState()
{
    EnsureMotionBlendSetup();
    LocalWakeChatState.TargetWaitLayerWeight = 1.0f;
}

void UpdateMotionBlend()
{
    if (!IsUpdate || !LocalWakeChatState.MotionBlendReady)
    {
        return;
    }

    float current = LocalWakeChatState.WaitLayerWeight;
    float target = LocalWakeChatState.TargetWaitLayerWeight;
    if (MathF.Abs(current - target) < 0.001f)
    {
        return;
    }

    float step = (float)(DeltaSeconds / LocalWakeChatState.MotionBlendDurationSeconds);
    float next = target > current
        ? MathF.Min(target, current + step)
        : MathF.Max(target, current - step);

    LocalWakeChatState.WaitLayerWeight = next;
    Entity.SetMotionLayerWeight(LocalWakeChatState.StandMotion, 1.0f - next);
    Entity.SetMotionLayerWeight(LocalWakeChatState.WaitMotion, next);
}

RuntimeDialogueBubble GetBubble()
{
    RuntimeDialogueBubble bubble = Scene.Bubble.GetOrCreate(LocalWakeChatState.BubbleName);
    bubble.AttachToEntity(Entity.Id, useModelTopAnchor: true);
    bubble.SetWorldOffset(0.0f, 0.20f, 0.0f);
    bubble.SetScreenOffset(0.0f, -16.0f);
    bubble.Width = 460.0f;
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

void ShowBubble(string userText, string assistantText, string footer)
{
    RuntimeDialogueBubble bubble = GetBubble();
    string header = string.IsNullOrWhiteSpace(userText) ? "语音助手" : $"你：{userText}";
    bubble.SetContent(assistantText, headerText: header, footerText: footer);
    bubble.Show();
}

bool IsMeaningfulText(string text)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return false;
    }

    foreach (char ch in text)
    {
        if (!char.IsWhiteSpace(ch) && !char.IsPunctuation(ch) && !char.IsSymbol(ch))
        {
            return true;
        }
    }

    return false;
}

string CreateSystemPrompt()
{
    string characterMemoryPath = Scene.Llm.GetCharacterMemoryPath(Entity);

    return string.Join(
        "\n",
        "你是一个中文语音助手，回答要自然、简洁。",
        $"当前本机时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}",
        $"项目 skills 是否启用：{Scene.Llm.SkillsEnabled}",
        $"项目 skills 目录：{Scene.Llm.SkillsDirectory}",
        $"长期记忆目录：{Scene.Llm.MemoryDirectory}",
        $"当前角色名：{Entity.Name}",
        $"当前角色长期记忆文件：{characterMemoryPath}",
        "如果用户的问题可能依赖过去告诉你的身份、称呼、偏好、关系、长期任务或重要经历，先调用 memory_search；必要时再调用 memory_read 读取 memory/index.md 或相关记忆文件。",
        "如果用户明确要求你记住、更新或忘记某件事，使用 memory_write、memory_update 或 memory_forget 执行，不要只口头答应。",
        $"如果本轮对话产生了稳定、长期、有价值的信息，可以用 memory_write 追加到合适的记忆文件；与当前角色相关的信息优先写入 {characterMemoryPath}；不要保存寒暄、临时问题、一次性天气或完整聊天原文。",
        "如果需要外部能力、实时信息、项目文件、命令执行或专门技能，请主动调用可用 skills 工具。",
        "如果工具失败，请说明失败原因，不要编造工具没有返回的信息。");
}

List<RuntimeLlmChatMessage> BuildConversationMessages(string userText)
{
    List<RuntimeLlmChatMessage> messages = new()
    {
        new RuntimeLlmChatMessage("system", CreateSystemPrompt())
    };
    messages.AddRange(LocalWakeChatState.ConversationHistory);
    messages.Add(new RuntimeLlmChatMessage("user", userText));
    return messages;
}

void AddConversationTurn(string userText, string assistantText)
{
    if (string.IsNullOrWhiteSpace(userText) || string.IsNullOrWhiteSpace(assistantText))
    {
        return;
    }

    LocalWakeChatState.ConversationHistory.Add(new RuntimeLlmChatMessage("user", userText.Trim()));
    LocalWakeChatState.ConversationHistory.Add(new RuntimeLlmChatMessage("assistant", assistantText.Trim()));

    while (LocalWakeChatState.ConversationHistory.Count > LocalWakeChatState.MaxConversationTurns * 2)
    {
        LocalWakeChatState.ConversationHistory.RemoveAt(0);
    }
}

bool IsSpeechBreak(char ch)
{
    return ch is '.' or '!' or '?' or ';' or ',' or '\n';
}

string CleanSpeechText(string text)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return string.Empty;
    }

    return text
        .Replace("**", "")
        .Replace("__", "")
        .Replace("`", "")
        .Trim();
}

void EnqueueSpeech(string text, bool flushTail)
{
    if (!string.IsNullOrEmpty(text))
    {
        LocalWakeChatState.PendingSpeechText += text;
    }

    int start = 0;
    string buffer = LocalWakeChatState.PendingSpeechText;
    for (int i = 0; i < buffer.Length; i++)
    {
        bool shouldBreak = IsSpeechBreak(buffer[i])
            || i - start + 1 >= LocalWakeChatState.MaxSpeechSegmentLength;
        if (!shouldBreak)
        {
            continue;
        }

        string sentence = CleanSpeechText(buffer[start..(i + 1)]);
        if (!string.IsNullOrWhiteSpace(sentence))
        {
            LocalWakeChatState.SpeakQueue.Enqueue(sentence);
        }

        start = i + 1;
    }

    string tail = start >= buffer.Length ? string.Empty : buffer[start..];
    if (flushTail)
    {
        string finalSentence = CleanSpeechText(tail);
        if (!string.IsNullOrWhiteSpace(finalSentence))
        {
            LocalWakeChatState.SpeakQueue.Enqueue(finalSentence);
        }

        LocalWakeChatState.PendingSpeechText = string.Empty;
    }
    else
    {
        LocalWakeChatState.PendingSpeechText = tail;
    }
}

void TrySpeakNext()
{
    if (LocalWakeChatState.Interrupted
        || LocalWakeChatState.IsSpeaking
        || LocalWakeChatState.SpeakQueue.Count == 0)
    {
        return;
    }

    string sentence = LocalWakeChatState.SpeakQueue.Dequeue();
    LocalWakeChatState.IsSpeaking = true;
    LocalWakeChatState.CurrentSpeechCallback = LocalWakeChatState.TtsDoneCallbackPrefix
        + (string.IsNullOrWhiteSpace(LocalWakeChatState.LlmRequestId)
            ? Guid.NewGuid().ToString("N")
            : LocalWakeChatState.LlmRequestId);
    ShowBubble(LocalWakeChatState.CurrentUserText, LocalWakeChatState.CurrentAssistantText, "语音回复中...");
    Entity.SpeakWithCallback(sentence, LocalWakeChatState.CurrentSpeechCallback);
}

void EnsureWakeMonitor()
{
    EnsureWakeMonitor("等待唤醒词...", "local_asr_wake_monitor", setStandState: true);
}

void EnsureWakeMonitor(string footer, string requestId, bool setStandState)
{
    if (LocalWakeChatState.WakeWordMonitorStarted)
    {
        return;
    }

    Scene.Asr.StartWakeWordMonitoring(
        Entity,
        LocalWakeChatState.WakeWords,
        requestId: requestId,
        chunkDurationSeconds: 2.0f,
        extensionDurationSeconds: 1.2f,
        trailingSilencePaddingSeconds: 0.4f,
        onDetectedCallback: "local_wake_detected",
        onErrorCallback: "local_wake_error");
    LocalWakeChatState.WakeWordMonitorStarted = true;
    if (setStandState)
    {
        SetStandState();
        ShowBubble(string.Empty, string.Empty, footer);
    }
    else
    {
        ShowBubble(LocalWakeChatState.CurrentUserText, LocalWakeChatState.CurrentAssistantText, footer);
    }
}

void EnsureInterruptWakeMonitor()
{
    EnsureWakeMonitor("回复中，可再次说唤醒词打断...", "local_asr_interrupt_monitor", setStandState: false);
}

void StartQuestionAsr()
{
    StartQuestionAsr(LocalWakeChatState.QuestionCaptureSeconds, "请说话...");
}

void StartQuestionAsr(double captureSeconds, string footer)
{
    if (LocalWakeChatState.WakeWordMonitorStarted)
    {
        Scene.Asr.StopWakeWordMonitoring();
        LocalWakeChatState.WakeWordMonitorStarted = false;
    }

    LocalWakeChatState.CurrentUserText = string.Empty;
    LocalWakeChatState.WaitingForQuestion = true;
    LocalWakeChatState.QuestionStopRequested = false;
    LocalWakeChatState.LlmRequestId = string.Empty;
    LocalWakeChatState.CurrentSpeechCallback = string.Empty;
    LocalWakeChatState.ReplyInProgress = false;
    LocalWakeChatState.ReplyCompleted = false;
    LocalWakeChatState.Interrupted = false;
    LocalWakeChatState.QuestionDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1.0, captureSeconds));
    SetWaitState();
    ShowBubble(string.Empty, string.Empty, footer);
    LocalWakeChatState.AsrRequestId = Scene.Asr.StartStreamingRecognition(
        Entity,
        requestId: "local_asr_question",
        onPartialCallback: "local_question_partial",
        onCompletedCallback: "local_question_completed",
        onErrorCallback: "local_question_error");
}

void RetryQuestionAsrAfterError()
{
    LocalWakeChatState.WaitingForQuestion = false;
    LocalWakeChatState.QuestionStopRequested = false;
    LocalWakeChatState.QuestionDeadlineUtc = DateTimeOffset.MinValue;
    LocalWakeChatState.AsrRequestId = string.Empty;
    LocalWakeChatState.LlmRequestId = string.Empty;
    LocalWakeChatState.CurrentSpeechCallback = string.Empty;
    LocalWakeChatState.SpeakQueue.Clear();
    LocalWakeChatState.PendingSpeechText = string.Empty;
    LocalWakeChatState.IsSpeaking = false;
    LocalWakeChatState.ReplyInProgress = false;
    LocalWakeChatState.ReplyCompleted = false;
    LocalWakeChatState.Interrupted = false;
    SetWaitState();
    StartQuestionAsr(LocalWakeChatState.QuestionCaptureSeconds, "ASR 出错，正在重试收音...");
}

void ReturnToWake(string footer)
{
    if (LocalWakeChatState.WakeWordMonitorStarted)
    {
        Scene.Asr.StopWakeWordMonitoring();
        LocalWakeChatState.WakeWordMonitorStarted = false;
    }

    LocalWakeChatState.WaitingForQuestion = false;
    LocalWakeChatState.QuestionStopRequested = false;
    LocalWakeChatState.QuestionDeadlineUtc = DateTimeOffset.MinValue;
    LocalWakeChatState.AsrRequestId = string.Empty;
    LocalWakeChatState.LlmRequestId = string.Empty;
    LocalWakeChatState.ActiveLlmUserText = string.Empty;
    LocalWakeChatState.SpeakQueue.Clear();
    LocalWakeChatState.PendingSpeechText = string.Empty;
    LocalWakeChatState.CurrentSpeechCallback = string.Empty;
    LocalWakeChatState.IsSpeaking = false;
    LocalWakeChatState.ReplyInProgress = false;
    LocalWakeChatState.ReplyCompleted = false;
    LocalWakeChatState.Interrupted = false;
    LocalWakeChatState.QuestionAsrRetryCount = 0;
    SetStandState();
    EnsureWakeMonitor();
    ShowBubble(string.Empty, string.Empty, footer);
}

void InterruptReplyAndListenAgain(string wakeWord)
{
    Scene.Asr.StopWakeWordMonitoring();
    LocalWakeChatState.WakeWordMonitorStarted = false;

    if (!string.IsNullOrWhiteSpace(LocalWakeChatState.LlmRequestId))
    {
        Scene.Llm.CancelRequest(LocalWakeChatState.LlmRequestId);
    }

    Entity.StopSpeaking();
    LocalWakeChatState.SpeakQueue.Clear();
    LocalWakeChatState.PendingSpeechText = string.Empty;
    LocalWakeChatState.CurrentSpeechCallback = string.Empty;
    LocalWakeChatState.IsSpeaking = false;
    LocalWakeChatState.ReplyInProgress = false;
    LocalWakeChatState.ReplyCompleted = false;
    LocalWakeChatState.Interrupted = true;
    LocalWakeChatState.QuestionAsrRetryCount = 0;
    LocalWakeChatState.LlmRequestId = string.Empty;
    StartQuestionAsr(LocalWakeChatState.QuestionCaptureSeconds, $"已打断：{wakeWord}，请说新的问题...");
}

void StartReply(string userText)
{
    LocalWakeChatState.ActiveLlmUserText = userText;
    LocalWakeChatState.CurrentUserText = userText;
    LocalWakeChatState.CurrentAssistantText = string.Empty;
    LocalWakeChatState.PendingSpeechText = string.Empty;
    LocalWakeChatState.SpeakQueue.Clear();
    LocalWakeChatState.IsSpeaking = false;
    LocalWakeChatState.ReplyInProgress = true;
    LocalWakeChatState.ReplyCompleted = false;
    LocalWakeChatState.Interrupted = false;
    LocalWakeChatState.QuestionAsrRetryCount = 0;
    LocalWakeChatState.LlmRequestId = "local_asr_llm_" + Guid.NewGuid().ToString("N");
    ShowBubble(userText, string.Empty, "思考中...");

    LocalWakeChatState.LlmRequestId = Scene.Llm.StartChatWithTools(
        Entity,
        BuildConversationMessages(userText),
        Array.Empty<RuntimeLlmTool>(),
        requestId: LocalWakeChatState.LlmRequestId,
        onDeltaCallback: "local_llm_delta",
        onCompletedCallback: "local_llm_completed",
        onErrorCallback: "local_llm_error",
        onToolCallCallback: "local_llm_tool_call",
        onToolResultCallback: "local_llm_tool_result",
        maxToolRounds: 1000);
    EnsureInterruptWakeMonitor();
}

void FinishReplyIfIdle()
{
    if (LocalWakeChatState.ReplyCompleted
        && !LocalWakeChatState.IsSpeaking
        && LocalWakeChatState.SpeakQueue.Count == 0)
    {
        StartQuestionAsr(
            LocalWakeChatState.ConversationIdleSeconds,
            "回复完成，继续说吧，我在听...");
    }
}

if (IsStart)
{
    EnsureMotionBlendSetup();
    EnsureWakeMonitor();
}

if (IsUpdate)
{
    UpdateMotionBlend();

    if (LocalWakeChatState.WaitingForQuestion
        && !LocalWakeChatState.QuestionStopRequested
        && DateTimeOffset.UtcNow >= LocalWakeChatState.QuestionDeadlineUtc)
    {
        LocalWakeChatState.QuestionStopRequested = true;
        Scene.Asr.StopStreamingRecognition(LocalWakeChatState.AsrRequestId);
        ShowBubble(LocalWakeChatState.CurrentUserText, string.Empty, "正在识别...");
    }
}

if (IsAsrEvent && AsrCallbackName == "local_wake_detected")
{
    if (LocalWakeChatState.ReplyInProgress || LocalWakeChatState.IsSpeaking || LocalWakeChatState.SpeakQueue.Count > 0)
    {
        InterruptReplyAndListenAgain(AsrWakeWord);
        return;
    }

    Scene.Asr.StopWakeWordMonitoring();
    LocalWakeChatState.WakeWordMonitorStarted = false;
    SetWaitState();
    ShowBubble(string.Empty, "我在，请说。", $"已唤醒：{AsrWakeWord}");
    Entity.SpeakWithCallback("我在，请说。", "local_wake_prompt_done");
}

if (IsSpeechEvent && SpeechCallbackName == "local_wake_prompt_done")
{
    StartQuestionAsr();
}

if (IsAsrEvent && AsrCallbackName == "local_question_partial")
{
    LocalWakeChatState.CurrentUserText = AsrText;
    ShowBubble(AsrText, string.Empty, "正在听...");
}

if (IsAsrEvent && AsrCallbackName == "local_question_completed")
{
    LocalWakeChatState.WaitingForQuestion = false;
    LocalWakeChatState.QuestionStopRequested = false;
    LocalWakeChatState.QuestionAsrRetryCount = 0;
    string userText = AsrText.Trim();
    if (!IsMeaningfulText(userText))
    {
        ReturnToWake("没有听清，等待唤醒词...");
    }
    else
    {
        StartReply(userText);
    }
}

if (IsAsrEvent && (AsrCallbackName == "local_wake_error" || AsrCallbackName == "local_question_error"))
{
    Console.Error.WriteLine(AsrError);
    if (AsrCallbackName == "local_question_error" && LocalWakeChatState.QuestionAsrRetryCount == 0)
    {
        LocalWakeChatState.QuestionAsrRetryCount = 1;
        RetryQuestionAsrAfterError();
        return;
    }
    ReturnToWake("ASR 出错，等待唤醒词...");
}

if (IsLlmEvent && LlmCallbackName == "local_llm_delta")
{
    if (LlmRequestId != LocalWakeChatState.LlmRequestId || LocalWakeChatState.Interrupted)
    {
        return;
    }

    LocalWakeChatState.CurrentAssistantText = LlmText;
    ShowBubble(LocalWakeChatState.CurrentUserText, LocalWakeChatState.CurrentAssistantText, "生成回复中，可说唤醒词打断...");
    EnqueueSpeech(LlmDelta, flushTail: false);
    TrySpeakNext();
}

if (IsLlmEvent && LlmCallbackName == "local_llm_completed")
{
    if (LlmRequestId != LocalWakeChatState.LlmRequestId || LocalWakeChatState.Interrupted)
    {
        return;
    }

    LocalWakeChatState.ReplyInProgress = false;
    LocalWakeChatState.ReplyCompleted = true;
    LocalWakeChatState.CurrentAssistantText = LlmText;
    AddConversationTurn(LocalWakeChatState.ActiveLlmUserText, LlmText);
    EnqueueSpeech(string.Empty, flushTail: true);
    ShowBubble(LocalWakeChatState.CurrentUserText, LocalWakeChatState.CurrentAssistantText, "语音回复中，可说唤醒词打断...");
    TrySpeakNext();
    FinishReplyIfIdle();
}

if (IsLlmEvent && LlmCallbackName == "local_llm_tool_call")
{
    if (LlmRequestId != LocalWakeChatState.LlmRequestId || LocalWakeChatState.Interrupted)
    {
        return;
    }

    ShowBubble(LocalWakeChatState.CurrentUserText, LocalWakeChatState.CurrentAssistantText, $"正在调用工具：{LlmToolName}");
}

if (IsLlmEvent && LlmCallbackName == "local_llm_tool_result")
{
    if (LlmRequestId != LocalWakeChatState.LlmRequestId || LocalWakeChatState.Interrupted)
    {
        return;
    }

    ShowBubble(LocalWakeChatState.CurrentUserText, LocalWakeChatState.CurrentAssistantText, $"工具返回：{LlmToolName}");
}

if (IsLlmEvent && LlmCallbackName == "local_llm_error")
{
    if (LlmRequestId != LocalWakeChatState.LlmRequestId || LocalWakeChatState.Interrupted)
    {
        return;
    }

    Console.Error.WriteLine(LlmError);
    ReturnToWake("LLM 出错，等待唤醒词...");
}

if (IsSpeechEvent && SpeechCallbackName.StartsWith(LocalWakeChatState.TtsDoneCallbackPrefix, StringComparison.Ordinal))
{
    if (LocalWakeChatState.Interrupted || SpeechCallbackName != LocalWakeChatState.CurrentSpeechCallback)
    {
        return;
    }

    LocalWakeChatState.CurrentSpeechCallback = string.Empty;
    LocalWakeChatState.IsSpeaking = false;
    TrySpeakNext();
    FinishReplyIfIdle();
}
```

### Python：本地 ASR 唤醒 -> ASR -> LLM with skills -> TTS

Python 版本使用同样的流程。`scene.llm.start_chat_with_tools(..., tools=[])` 会在项目启用 skills 时自动合并内置 skills 工具。

```python
import time

STAND_MOTION = "assets/motions/basic_stand.vmd"
WAIT_MOTION = "assets/motions/basic_wait.vmd"
WAKE_WORDS = ["小言", "你好小言"]
BUBBLE_NAME = "local-asr-wake-chat"
TTS_DONE_CALLBACK = "local_wake_tts_done"
QUESTION_CAPTURE_SECONDS = 9.0
CONVERSATION_IDLE_SECONDS = 30.0
MOTION_BLEND_DURATION_SECONDS = 0.35
MAX_CONVERSATION_TURNS = 8
MAX_SPEECH_SEGMENT_LENGTH = 70

motion_blend_ready = False
wait_layer_weight = 0.0
target_wait_layer_weight = 0.0
wake_word_monitor_started = False
waiting_for_question = False
question_stop_requested = False
question_deadline = 0.0
asr_request_id = ""
current_user_text = ""
active_llm_user_text = ""
current_assistant_text = ""
pending_speech_text = ""
speak_queue = []
is_speaking = False
reply_completed = False
question_asr_retry_count = 0
conversation_history = []

def ensure_motion_blend_setup(entity):
    global motion_blend_ready
    if motion_blend_ready:
        return
    entity.set_motion_layers([
        {"path": STAND_MOTION, "weight": 1.0, "resetPhysicsOnLoop": False},
        {"path": WAIT_MOTION, "weight": 0.0, "resetPhysicsOnLoop": False},
    ])
    entity.set_loop_motion(True)
    entity.play_motion()
    motion_blend_ready = True

def set_stand_state(entity):
    global target_wait_layer_weight
    ensure_motion_blend_setup(entity)
    target_wait_layer_weight = 0.0

def set_wait_state(entity):
    global target_wait_layer_weight
    ensure_motion_blend_setup(entity)
    target_wait_layer_weight = 1.0

def update_motion_blend(entity, delta_seconds):
    global wait_layer_weight
    if not motion_blend_ready:
        return
    current = wait_layer_weight
    target = target_wait_layer_weight
    if abs(current - target) < 0.001:
        return
    step = delta_seconds / MOTION_BLEND_DURATION_SECONDS
    if target > current:
        next_weight = min(target, current + step)
    else:
        next_weight = max(target, current - step)
    wait_layer_weight = next_weight
    entity.set_motion_layer_weight(STAND_MOTION, 1.0 - next_weight)
    entity.set_motion_layer_weight(WAIT_MOTION, next_weight)

def get_bubble(entity, scene):
    bubble = scene.bubble.get_or_create(BUBBLE_NAME)
    bubble.attach_to_entity(entity.id, use_model_top_anchor=True)
    bubble.set_world_offset(0.0, 0.20, 0.0)
    bubble.set_screen_offset(0.0, -16.0)
    bubble.set_width(460)
    bubble.set_text_alignment("left")
    bubble.set_font_size(20)
    bubble.set_header_font_size(16)
    bubble.set_footer_font_size(14)
    bubble.set_background_color(0.08, 0.10, 0.16, 0.92)
    bubble.set_border_color(0.58, 0.82, 1.0, 0.95)
    bubble.set_header_text_color(0.82, 0.90, 1.0, 1.0)
    bubble.set_text_color(1.0, 1.0, 1.0, 1.0)
    bubble.set_footer_text_color(0.76, 0.80, 0.88, 1.0)
    return bubble

def show_bubble(entity, scene, user_text, assistant_text, footer):
    header = "语音助手" if not user_text.strip() else "你：" + user_text.strip()
    get_bubble(entity, scene).show(
        text=assistant_text or "",
        header_text=header,
        footer_text=footer or "")

def is_meaningful_text(text):
    return any(not ch.isspace() for ch in (text or ""))

def create_system_prompt(entity, scene):
    character_memory_path = scene.llm.get_character_memory_path(entity)
    return "\n".join([
        "你是一个中文语音助手，回答要自然、简洁。",
        "当前本机时间：" + time.strftime("%Y-%m-%d %H:%M:%S"),
        "项目 skills 是否启用：" + str(scene.llm.skills_enabled),
        "项目 skills 目录：" + scene.llm.skills_directory,
        "长期记忆目录：" + scene.llm.memory_directory,
        "当前角色名：" + entity.name,
        "当前角色长期记忆文件：" + character_memory_path,
        "如果用户的问题可能依赖过去告诉你的身份、称呼、偏好、关系、长期任务或重要经历，先调用 memory_search；必要时再调用 memory_read 读取 memory/index.md 或相关记忆文件。",
        "如果用户明确要求你记住、更新或忘记某件事，使用 memory_write、memory_update 或 memory_forget 执行，不要只口头答应。",
        "如果本轮对话产生了稳定、长期、有价值的信息，可以用 memory_write 追加到合适的记忆文件；与当前角色相关的信息优先写入 " + character_memory_path + "；不要保存寒暄、临时问题、一次性天气或完整聊天原文。",
        "如果需要外部能力、实时信息、项目文件、命令执行或专门技能，请主动调用可用 skills 工具。",
        "如果工具失败，请说明失败原因，不要编造工具没有返回的信息。",
    ])

def build_user_prompt(user_text):
    lines = []
    if conversation_history:
        lines.append("以下是最近几轮对话历史：")
        for item in conversation_history[-MAX_CONVERSATION_TURNS:]:
            lines.append("用户：" + item["user"])
            lines.append("助手：" + item["assistant"])
        lines.append("")
    lines.append("当前用户问题：" + user_text)
    return "\n".join(lines)

def add_conversation_turn(user_text, assistant_text):
    if not user_text.strip() or not assistant_text.strip():
        return
    conversation_history.append({"user": user_text.strip(), "assistant": assistant_text.strip()})
    while len(conversation_history) > MAX_CONVERSATION_TURNS:
        conversation_history.pop(0)

def clean_speech_text(text):
    return (text or "").replace("**", "").replace("__", "").replace("`", "").strip()

def enqueue_speech(text, flush_tail=False):
    global pending_speech_text
    if text:
        pending_speech_text += text

    start = 0
    buffer = pending_speech_text
    for index, ch in enumerate(buffer):
        should_break = ch in ".!?;,\n" or index - start + 1 >= MAX_SPEECH_SEGMENT_LENGTH
        if not should_break:
            continue
        sentence = clean_speech_text(buffer[start:index + 1])
        if sentence:
            speak_queue.append(sentence)
        start = index + 1

    tail = buffer[start:]
    if flush_tail:
        sentence = clean_speech_text(tail)
        if sentence:
            speak_queue.append(sentence)
        pending_speech_text = ""
    else:
        pending_speech_text = tail

def try_speak_next(entity, scene):
    global is_speaking
    if is_speaking or not speak_queue:
        return
    sentence = speak_queue.pop(0)
    is_speaking = True
    show_bubble(entity, scene, current_user_text, current_assistant_text, "语音回复中...")
    entity.speak(sentence, on_completed=TTS_DONE_CALLBACK)

def ensure_wake_monitor(entity, scene):
    global wake_word_monitor_started
    if wake_word_monitor_started:
        return
    scene.asr.start_wake_word_monitoring(
        WAKE_WORDS,
        request_id="local_asr_wake_monitor",
        chunk_duration_seconds=2.0,
        extension_duration_seconds=1.2,
        trailing_silence_padding_seconds=0.4,
        on_detected="local_wake_detected",
        on_error="local_wake_error")
    wake_word_monitor_started = True
    set_stand_state(entity)
    show_bubble(entity, scene, "", "", "等待唤醒词...")

def start_question_asr(entity, scene, capture_seconds=QUESTION_CAPTURE_SECONDS, footer="请说话..."):
    global current_user_text, waiting_for_question, question_stop_requested, question_deadline, asr_request_id
    global reply_completed
    current_user_text = ""
    waiting_for_question = True
    question_stop_requested = False
    reply_completed = False
    question_asr_retry_count = 0
    question_asr_retry_count = 0
    question_asr_retry_count = 0
    question_deadline = time.time() + max(1.0, capture_seconds)
    set_wait_state(entity)
    show_bubble(entity, scene, "", "", footer)
    asr_request_id = "local_asr_question"
    scene.asr.start_streaming_recognition(
        request_id=asr_request_id,
        on_partial="local_question_partial",
        on_completed="local_question_completed",
        on_error="local_question_error")

def retry_question_asr_after_error(entity, scene):
    global waiting_for_question, question_stop_requested, question_deadline, asr_request_id
    global pending_speech_text, is_speaking, reply_completed, question_asr_retry_count
    waiting_for_question = False
    question_stop_requested = False
    question_deadline = 0.0
    asr_request_id = ""
    speak_queue.clear()
    pending_speech_text = ""
    is_speaking = False
    reply_completed = False
    set_wait_state(entity)
    start_question_asr(entity, scene, QUESTION_CAPTURE_SECONDS, "ASR 出错，正在重试收音...")

def return_to_wake(entity, scene, footer):
    global waiting_for_question, question_stop_requested, question_deadline, asr_request_id
    global active_llm_user_text, pending_speech_text, is_speaking, reply_completed, question_asr_retry_count
    waiting_for_question = False
    question_stop_requested = False
    question_deadline = 0.0
    asr_request_id = ""
    active_llm_user_text = ""
    speak_queue.clear()
    pending_speech_text = ""
    is_speaking = False
    reply_completed = False
    question_asr_retry_count = 0
    set_stand_state(entity)
    ensure_wake_monitor(entity, scene)
    show_bubble(entity, scene, "", "", footer)

def start_reply(entity, scene, user_text):
    global active_llm_user_text, current_user_text, current_assistant_text
    global pending_speech_text, is_speaking, reply_completed, question_asr_retry_count
    active_llm_user_text = user_text
    current_user_text = user_text
    current_assistant_text = ""
    pending_speech_text = ""
    speak_queue.clear()
    is_speaking = False
    reply_completed = False
    question_asr_retry_count = 0
    show_bubble(entity, scene, user_text, "", "思考中...")
    scene.llm.start_chat_with_tools(
        build_user_prompt(user_text),
        [],
        system_prompt=create_system_prompt(entity, scene),
        request_id="local_asr_llm",
        on_delta="local_llm_delta",
        on_completed="local_llm_completed",
        on_error="local_llm_error",
        on_tool_call="local_llm_tool_call",
        on_tool_result="local_llm_tool_result",
        max_tool_rounds=4)

def finish_reply_if_idle(entity, scene):
    if reply_completed and not is_speaking and not speak_queue:
        start_question_asr(entity, scene, CONVERSATION_IDLE_SECONDS, "回复完成，继续说吧，我在听...")

def start(entity, scene, input, audio):
    ensure_motion_blend_setup(entity)
    ensure_wake_monitor(entity, scene)

def update(entity, scene, input, audio, delta_seconds):
    global question_stop_requested
    update_motion_blend(entity, delta_seconds)
    if waiting_for_question and not question_stop_requested and time.time() >= question_deadline:
        question_stop_requested = True
        scene.asr.stop_streaming_recognition(asr_request_id)
        show_bubble(entity, scene, current_user_text, "", "正在识别...")

def local_wake_detected(entity, scene, input, audio, event):
    global wake_word_monitor_started
    scene.asr.stop_wake_word_monitoring()
    wake_word_monitor_started = False
    set_wait_state(entity)
    show_bubble(entity, scene, "", "我在，请说。", "已唤醒：" + event.get("wakeWord", ""))
    entity.speak("我在，请说。", on_completed="local_wake_prompt_done")

def local_wake_prompt_done(entity, scene, input, audio):
    start_question_asr(entity, scene)

def local_question_partial(entity, scene, input, audio, event):
    global current_user_text
    current_user_text = event.get("text", "")
    show_bubble(entity, scene, current_user_text, "", "正在听...")

def local_question_completed(entity, scene, input, audio, event):
    global waiting_for_question, question_stop_requested, question_asr_retry_count
    waiting_for_question = False
    question_stop_requested = False
    question_asr_retry_count = 0
    user_text = event.get("text", "").strip()
    if not is_meaningful_text(user_text):
        return_to_wake(entity, scene, "没有听清，等待唤醒词...")
    else:
        start_reply(entity, scene, user_text)

def local_wake_error(entity, scene, input, audio, event):
    print("ASR wake error:", event.get("error", ""))
    return_to_wake(entity, scene, "ASR 出错，等待唤醒词...")

def local_question_error(entity, scene, input, audio, event):
    global question_asr_retry_count
    print("ASR question error:", event.get("error", ""))
    if question_asr_retry_count == 0:
        question_asr_retry_count = 1
        retry_question_asr_after_error(entity, scene)
        return
    return_to_wake(entity, scene, "ASR 出错，等待唤醒词...")

def local_llm_delta(entity, scene, input, audio, event):
    global current_assistant_text
    current_assistant_text = event.get("accumulatedText", "")
    show_bubble(entity, scene, current_user_text, current_assistant_text, "生成回复中...")
    enqueue_speech(event.get("delta", ""), flush_tail=False)
    try_speak_next(entity, scene)

def local_llm_completed(entity, scene, input, audio, event):
    global current_assistant_text, reply_completed
    reply_completed = True
    current_assistant_text = event.get("accumulatedText", "")
    add_conversation_turn(active_llm_user_text, current_assistant_text)
    enqueue_speech("", flush_tail=True)
    show_bubble(entity, scene, current_user_text, current_assistant_text, "语音回复中...")
    try_speak_next(entity, scene)
    finish_reply_if_idle(entity, scene)

def local_llm_tool_call(entity, scene, input, audio, event):
    call = event.get("toolCall") or {}
    show_bubble(entity, scene, current_user_text, current_assistant_text, "正在调用工具：" + call.get("name", ""))

def local_llm_tool_result(entity, scene, input, audio, event):
    call = event.get("toolCall") or {}
    show_bubble(entity, scene, current_user_text, current_assistant_text, "工具返回：" + call.get("name", ""))

def local_llm_error(entity, scene, input, audio, event):
    print("LLM error:", event.get("error", ""))
    return_to_wake(entity, scene, "LLM 出错，等待唤醒词...")

def local_wake_tts_done(entity, scene, input, audio):
    global is_speaking
    is_speaking = False
    try_speak_next(entity, scene)
    finish_reply_if_idle(entity, scene)
```
