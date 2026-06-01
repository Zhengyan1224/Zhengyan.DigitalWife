using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Llm.OpenAI;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;
using Zhengyan.DigitalWife.Speech.WhisperNet;

namespace Zhengyan.DigitalWife.Samples.AssistantConsole;

public sealed class DemoOptions
{
    public string RecognitionProvider { get; init; } = "sherpa";

    public DemoAudioOptions Audio { get; init; } = new();

    public required OpenAiCompatibleLlmOptions Llm { get; init; }

    public required SherpaOnnxTtsOptions Tts { get; init; }

    public SherpaOnnxRecognizerOptions? SherpaRecognizer { get; init; }

    public WhisperNetRecognizerOptions? WhisperRecognizer { get; init; }

    public SherpaOnnxWakeWordOptions? WakeWord { get; init; }

    public VoiceActivityCaptureOptions Capture { get; init; } = new();

    public string SystemPrompt { get; init; } = "你是一个中文语音助手，请简洁、自然地回答用户的问题。";

    public string LlmModel { get; init; } = "qwen_2.5_14b";

    public string? CapturedAudioDirectory { get; init; }
}

public sealed class DemoAudioOptions
{
    public AudioPlaybackBackend PlaybackBackend { get; init; } = AudioPlaybackBackend.PortAudio;

    public int? InputDeviceIndex { get; init; }

    public int? OutputDeviceIndex { get; init; }
}

