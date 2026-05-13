using Zhengyan.DigitalWife.Assistant.Text;
using Zhengyan.DigitalWife.Llm.OpenAI;
using Zhengyan.DigitalWife.Realtime.OpenAI;
using Zhengyan.DigitalWife.Speech;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;
using Zhengyan.DigitalWife.Speech.WhisperNet;

namespace Zhengyan.DigitalWife.Samples.RealtimeVoice;

public sealed class RealtimeVoiceAppOptions
{
    public RealtimeVoiceAppOptions()
    {
        Llm = new OpenAiCompatibleLlmOptions
        {
            BaseUrl = "http://127.0.0.1:10001",
            ApiKey = "__SET_ME__",
            Model = "qwen_2.5_14b",
            ChatCompletionsPath = "/v1/chat/completions",
            Timeout = TimeSpan.FromMinutes(5)
        };

        SherpaRecognizer = new SherpaOnnxRecognizerOptions
        {
            ModelKind = SherpaOnnxRecognizerModelKind.OnlineTransducer,
            TokensPath = "models/asr/sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30/tokens.txt",
            EncoderPath = "models/asr/sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30/encoder.int8.onnx",
            DecoderPath = "models/asr/sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30/decoder.onnx",
            JoinerPath = "models/asr/sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30/joiner.int8.onnx",
            Language = "zh",
            Provider = "cpu",
            SampleRate = 16_000,
            FeatureDim = 80,
            Threads = 4,
            DecodingMethod = "greedy_search"
        };

        WhisperRecognizer = new WhisperNetRecognizerOptions
        {
            ModelPath = "models/whisper/ggml-base.bin",
            Language = "auto",
            TranslateToEnglish = false,
            UseGpu = false,
            Threads = 4,
            SampleRate = 16_000
        };

        Tts = new SherpaOnnxTtsOptions
        {
            ModelKind = SpeechSynthesisModelKind.Matcha,
            ModelPath = "models/tts/matcha-icefall-zh-en/model-steps-3.onnx",
            TokensPath = "models/tts/matcha-icefall-zh-en/tokens.txt",
            LexiconPath = "models/tts/matcha-icefall-zh-en/lexicon.txt",
            DataDirectory = "models/tts/matcha-icefall-zh-en/espeak-ng-data",
            Provider = "cpu",
            Threads = 4,
            NoiseScale = 0.667f,
            NoiseScaleW = 0.8f,
            LengthScale = 1.0f
        };
    }

    public string? ApiKey { get; init; }

    public string RecognitionProvider { get; init; } = "sherpa";

    public string[] RecognitionPriority { get; init; } = [];

    public bool UseFallbackRecognizersForTranscription { get; init; } = true;

    public int HistoryMaxMessages { get; init; } = 12;

    public OpenAiCompatibleLlmOptions Llm { get; init; }

    public SherpaOnnxRecognizerOptions SherpaRecognizer { get; init; }

    public WhisperNetRecognizerOptions WhisperRecognizer { get; init; }

    public SherpaOnnxTtsOptions Tts { get; init; }

    public RealtimeVoiceSynthesisOptions Synthesis { get; init; } = new();

    public RealtimeVoiceSessionDefaultsOptions Session { get; init; } = new();

    public RealtimeVoiceResponseChunkingOptions ResponseChunking { get; init; } = new();
}

public sealed class RealtimeVoiceSynthesisOptions
{
    public float Speed { get; init; } = 1.0f;

    public int SpeakerId { get; init; }
}

public sealed class RealtimeVoiceSessionDefaultsOptions
{
    public string Model { get; init; } = "zhengyan-realtime-voice";

    public string Instructions { get; init; } = "\u4f60\u662f\u6653\u96e8\uff0c\u4e00\u4e2a\u6e29\u67d4\u3001\u7b80\u6d01\u3001\u81ea\u7136\u7684\u4e2d\u6587\u8bed\u97f3\u52a9\u624b\u3002\u8bf7\u76f4\u63a5\u56de\u7b54\u7528\u6237\u95ee\u9898\uff0c\u907f\u514d\u5197\u957f\u3002";

    public string[] OutputModalities { get; init; } = ["audio"];

    public string Voice { get; init; } = "0";

    public int InputAudioSampleRate { get; init; } = 24_000;

    public int OutputAudioSampleRate { get; init; } = 24_000;

    public string InputTranscriptionModel { get; init; } = "whisper-1";

    public string InputTranscriptionLanguage { get; init; } = "zh";

    public string? InputTranscriptionPrompt { get; init; }

    public int? MaxOutputTokens { get; init; } = 1_024;

    public float? Temperature { get; init; } = 0.7f;
}

public sealed class RealtimeVoiceResponseChunkingOptions
{
    public bool EnableClauseBoundaries { get; init; } = true;

    public int MinClauseCharacters { get; init; } = 12;

    public int MaxBufferedCharacters { get; init; } = 320;
}

internal sealed class ResolvedRealtimeVoiceOptions
{
    public required string RecognitionProvider { get; init; }

    public required IReadOnlyList<string> RecognitionPriority { get; init; }

    public required bool UseFallbackRecognizersForTranscription { get; init; }

    public required int HistoryMaxMessages { get; init; }

    public required OpenAiCompatibleLlmOptions Llm { get; init; }

    public SherpaOnnxRecognizerOptions? SherpaRecognizer { get; init; }

    public WhisperNetRecognizerOptions? WhisperRecognizer { get; init; }

    public required SherpaOnnxTtsOptions Tts { get; init; }

    public required RealtimeVoiceSynthesisOptions Synthesis { get; init; }

    public required SentenceChunkerOptions ResponseChunking { get; init; }

    public required OpenAiRealtimeSession Session { get; init; }

    public string? ApiKey { get; init; }
}

internal static class RealtimeVoiceOptionsResolver
{
    public static ResolvedRealtimeVoiceOptions Resolve(RealtimeVoiceAppOptions options, SamplePathResolver paths)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);

        SherpaOnnxRecognizerOptions? sherpaRecognizer = null;
        if (options.SherpaRecognizer is not null)
        {
            sherpaRecognizer = new SherpaOnnxRecognizerOptions
            {
                ModelKind = options.SherpaRecognizer.ModelKind,
                TokensPath = paths.ResolveRequiredFile(options.SherpaRecognizer.TokensPath),
                EncoderPath = string.IsNullOrWhiteSpace(options.SherpaRecognizer.EncoderPath) ? null : paths.ResolveRequiredFile(options.SherpaRecognizer.EncoderPath),
                DecoderPath = string.IsNullOrWhiteSpace(options.SherpaRecognizer.DecoderPath) ? null : paths.ResolveRequiredFile(options.SherpaRecognizer.DecoderPath),
                JoinerPath = string.IsNullOrWhiteSpace(options.SherpaRecognizer.JoinerPath) ? null : paths.ResolveRequiredFile(options.SherpaRecognizer.JoinerPath),
                ModelPath = string.IsNullOrWhiteSpace(options.SherpaRecognizer.ModelPath) ? null : paths.ResolveRequiredFile(options.SherpaRecognizer.ModelPath),
                Language = options.SherpaRecognizer.Language,
                Provider = options.SherpaRecognizer.Provider,
                SampleRate = options.SherpaRecognizer.SampleRate,
                FeatureDim = options.SherpaRecognizer.FeatureDim,
                Threads = options.SherpaRecognizer.Threads,
                DecodingMethod = options.SherpaRecognizer.DecodingMethod,
                HotwordsScore = options.SherpaRecognizer.HotwordsScore,
                HotwordsFile = string.IsNullOrWhiteSpace(options.SherpaRecognizer.HotwordsFile) ? null : paths.ResolveRequiredFile(options.SherpaRecognizer.HotwordsFile)
            };
        }

        WhisperNetRecognizerOptions? whisperRecognizer = null;
        if (options.WhisperRecognizer is not null)
        {
            whisperRecognizer = new WhisperNetRecognizerOptions
            {
                ModelPath = paths.ResolveRequiredFile(options.WhisperRecognizer.ModelPath),
                Language = options.WhisperRecognizer.Language,
                TranslateToEnglish = options.WhisperRecognizer.TranslateToEnglish,
                UseGpu = options.WhisperRecognizer.UseGpu,
                Threads = options.WhisperRecognizer.Threads,
                SampleRate = options.WhisperRecognizer.SampleRate
            };
        }

        SherpaOnnxTtsOptions tts = new()
        {
            ModelKind = options.Tts.ModelKind,
            ModelPath = paths.ResolveRequiredFile(options.Tts.ModelPath),
            TokensPath = paths.ResolveRequiredFile(options.Tts.TokensPath),
            LexiconPath = string.IsNullOrWhiteSpace(options.Tts.LexiconPath) ? null : paths.ResolveOptionalFile(options.Tts.LexiconPath),
            DataDirectory = string.IsNullOrWhiteSpace(options.Tts.DataDirectory) ? null : paths.ResolveOptionalDirectory(options.Tts.DataDirectory),
            DictDirectory = string.IsNullOrWhiteSpace(options.Tts.DictDirectory) ? null : paths.ResolveOptionalDirectory(options.Tts.DictDirectory),
            Provider = options.Tts.Provider,
            Threads = options.Tts.Threads,
            NoiseScale = options.Tts.NoiseScale,
            NoiseScaleW = options.Tts.NoiseScaleW,
            LengthScale = options.Tts.LengthScale,
            VocoderPath = string.IsNullOrWhiteSpace(options.Tts.VocoderPath) ? null : paths.ResolveOptionalFile(options.Tts.VocoderPath),
            RuleFars = options.Tts.RuleFars,
            RuleFsts = options.Tts.RuleFsts
        };

        OpenAiCompatibleLlmOptions llm = new()
        {
            BaseUrl = options.Llm.BaseUrl,
            ApiKey = options.Llm.ApiKey,
            Model = !string.IsNullOrWhiteSpace(options.Llm.Model)
                ? options.Llm.Model
                : throw new InvalidOperationException("RealtimeVoice.Llm.Model is required."),
            ChatCompletionsPath = options.Llm.ChatCompletionsPath,
            Timeout = options.Llm.Timeout
        };

        return new ResolvedRealtimeVoiceOptions
        {
            RecognitionProvider = options.RecognitionProvider.Trim(),
            RecognitionPriority = ResolveRecognitionPriority(options),
            UseFallbackRecognizersForTranscription = options.UseFallbackRecognizersForTranscription,
            HistoryMaxMessages = Math.Max(0, options.HistoryMaxMessages),
            Llm = llm,
            SherpaRecognizer = sherpaRecognizer,
            WhisperRecognizer = whisperRecognizer,
            Tts = tts,
            Synthesis = options.Synthesis,
            ResponseChunking = new SentenceChunkerOptions
            {
                EnableClauseBoundaries = options.ResponseChunking.EnableClauseBoundaries,
                MinClauseCharacters = Math.Max(1, options.ResponseChunking.MinClauseCharacters),
                MaxBufferedCharacters = Math.Max(1, options.ResponseChunking.MaxBufferedCharacters)
            },
            Session = new OpenAiRealtimeSession
            {
                Model = options.Session.Model,
                Instructions = options.Session.Instructions,
                OutputModalities = options.Session.OutputModalities.Length > 0 ? options.Session.OutputModalities : ["audio"],
                Audio = new OpenAiRealtimeSessionAudioOptions
                {
                    Input = new OpenAiRealtimeSessionInputAudioOptions
                    {
                        Format = OpenAiRealtimeAudioFormat.Pcm16(options.Session.InputAudioSampleRate),
                        Transcription = new OpenAiRealtimeInputAudioTranscription
                        {
                            Model = options.Session.InputTranscriptionModel,
                            Language = options.Session.InputTranscriptionLanguage,
                            Prompt = options.Session.InputTranscriptionPrompt
                        },
                        TurnDetection = null
                    },
                    Output = new OpenAiRealtimeSessionOutputAudioOptions
                    {
                        Format = OpenAiRealtimeAudioFormat.Pcm16(options.Session.OutputAudioSampleRate),
                        Voice = options.Session.Voice
                    }
                },
                MaxOutputTokens = options.Session.MaxOutputTokens,
                Temperature = options.Session.Temperature
            },
            ApiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey.Trim()
        };
    }

    public static OpenAiRealtimeSession CloneSession(OpenAiRealtimeSession source, string? modelOverride = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        IReadOnlyList<string> outputModalities = source.OutputModalities is { Count: > 0 }
            ? source.OutputModalities
            : ["audio"];
        OpenAiRealtimeSessionAudioOptions audio = source.Audio ?? new OpenAiRealtimeSessionAudioOptions();
        OpenAiRealtimeSessionInputAudioOptions input = audio.Input ?? new OpenAiRealtimeSessionInputAudioOptions();
        OpenAiRealtimeSessionOutputAudioOptions output = audio.Output ?? new OpenAiRealtimeSessionOutputAudioOptions();
        OpenAiRealtimeAudioFormat inputFormat = input.Format ?? OpenAiRealtimeAudioFormat.Pcm16();
        OpenAiRealtimeAudioFormat outputFormat = output.Format ?? OpenAiRealtimeAudioFormat.Pcm16();

        return new OpenAiRealtimeSession
        {
            Id = source.Id,
            Model = string.IsNullOrWhiteSpace(modelOverride) ? source.Model : modelOverride,
            Instructions = source.Instructions,
            OutputModalities = outputModalities.ToArray(),
            Audio = new OpenAiRealtimeSessionAudioOptions
            {
                Input = new OpenAiRealtimeSessionInputAudioOptions
                {
                    Format = new OpenAiRealtimeAudioFormat(inputFormat.Type, inputFormat.Rate),
                    TurnDetection = input.TurnDetection is null
                        ? null
                        : new OpenAiRealtimeTurnDetection
                        {
                            Type = input.TurnDetection.Type,
                            Threshold = input.TurnDetection.Threshold,
                            PrefixPaddingMilliseconds = input.TurnDetection.PrefixPaddingMilliseconds,
                            SilenceDurationMilliseconds = input.TurnDetection.SilenceDurationMilliseconds,
                            IdleTimeoutMilliseconds = input.TurnDetection.IdleTimeoutMilliseconds
                        },
                    Transcription = input.Transcription is null
                        ? null
                        : new OpenAiRealtimeInputAudioTranscription
                        {
                            Model = input.Transcription.Model,
                            Language = input.Transcription.Language,
                            Prompt = input.Transcription.Prompt
                        }
                },
                Output = new OpenAiRealtimeSessionOutputAudioOptions
                {
                    Format = new OpenAiRealtimeAudioFormat(outputFormat.Type, outputFormat.Rate),
                    Voice = output.Voice
                }
            },
            MaxOutputTokens = source.MaxOutputTokens,
            Temperature = source.Temperature
        };
    }

    private static IReadOnlyList<string> ResolveRecognitionPriority(RealtimeVoiceAppOptions options)
    {
        IReadOnlyList<string> source = options.RecognitionPriority.Length > 0
            ? options.RecognitionPriority
            : ResolveLegacyRecognitionPriority(options.RecognitionProvider);

        return source
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ResolveLegacyRecognitionPriority(string recognitionProvider)
    {
        return recognitionProvider.Trim().ToLowerInvariant() switch
        {
            "whisper" => ["whisper", "sherpa"],
            "sherpa" => ["sherpa", "whisper"],
            _ => []
        };
    }
}
