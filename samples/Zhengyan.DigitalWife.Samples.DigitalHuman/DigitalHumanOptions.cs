using System.Numerics;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.Audio.PortAudio;
using Zhengyan.DigitalWife.Mmd.Game.Speech;
using Zhengyan.DigitalWife.Realtime.OpenAI;

namespace Zhengyan.DigitalWife.Samples.DigitalHuman;

public sealed class DigitalHumanAppOptions
{
    public string? CapturedAudioDirectory { get; init; } = "artifacts/digital-human/captured";

    public string? WindowIconPath { get; init; } = "Resources/Logo/logo.png";

    public bool DeleteCapturedAudioAfterRecognition { get; init; }

    public DigitalHumanAudioOptions Audio { get; init; } = new();

    public DigitalHumanRealtimeOptions Realtime { get; init; } = new();

    public DigitalHumanSpeechOutputOptions SpeechOutput { get; init; } = new();

    public DigitalHumanConversationOptions Conversation { get; init; } = new();

    public DigitalHumanSceneOptions Scene { get; init; } = new();

    public DigitalHumanCharacterOptions Character { get; init; } = new();
}

public sealed class DigitalHumanAudioOptions
{
    public AudioPlaybackBackend PlaybackBackend { get; init; } = AudioPlaybackBackend.PortAudio;

    public int? InputDeviceIndex { get; init; }

    public int? OutputDeviceIndex { get; init; }
}

public sealed class DigitalHumanSpeechOutputOptions
{
    public float Volume { get; init; } = 1.0f;

    public float Speed { get; init; } = 1.0f;

    public int SpeakerId { get; init; }
}

public sealed class DigitalHumanRealtimeOptions
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:5058";

    public string RealtimePath { get; init; } = "/v1/realtime";

    public string AudioSpeechPath { get; init; } = "/v1/audio/speech";

    public string? ApiKey { get; init; }

    public string Model { get; init; } = "zhengyan-realtime-voice";

    public string Instructions { get; init; } = "\u4f60\u662f\u6653\u96e8\uff0c\u4e00\u4e2a\u6e29\u67d4\u3001\u7b80\u6d01\u3001\u81ea\u7136\u7684\u4e2d\u6587\u8bed\u97f3\u52a9\u624b\u3002\u8bf7\u76f4\u63a5\u56de\u7b54\u7528\u6237\u95ee\u9898\uff0c\u907f\u514d\u5197\u957f\u3002";

    public string[] OutputModalities { get; init; } = ["audio"];

    public string Voice { get; init; } = "0";

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public bool SendOpenAiBetaHeader { get; init; } = true;

    public int OutboundAudioChunkSamples { get; init; } = 4_096;

    public int InputAudioSampleRate { get; init; } = 24_000;

    public int OutputAudioSampleRate { get; init; } = 24_000;

    public string InputTranscriptionModel { get; init; } = "whisper-1";

    public string InputTranscriptionLanguage { get; init; } = "zh";

    public string? InputTranscriptionPrompt { get; init; }

    public int? MaxOutputTokens { get; init; } = 1_024;

    public float? Temperature { get; init; } = 0.7f;

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class DigitalHumanConversationOptions
{
    public IReadOnlyList<string> WakeWords { get; init; } = [];

    public TimeSpan WakeWordListeningTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public bool UseFallbackRecognizersForWakeWord { get; init; }

    public TimeSpan WakeWordChunkDuration { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan WakeWordExtensionDuration { get; init; } = TimeSpan.FromMilliseconds(1200);

    public TimeSpan WakeWordTrailingSilencePadding { get; init; } = TimeSpan.FromMilliseconds(400);

    public TimeSpan PostResponseIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ReturnToStandTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public string ReturnToStandPromptText { get; init; } = "\u5982\u679c\u4f60\u540e\u9762\u8fd8\u60f3\u7ee7\u7eed\u804a\uff0c\u968f\u65f6\u518d\u8f7b\u8f7b\u53eb\u6211\u4e00\u58f0\uff0c\u6211\u4f1a\u5728\u7684\u3002";

    public TimeSpan MotionTransitionDuration { get; init; } = TimeSpan.FromSeconds(1);

    public IReadOnlyList<DigitalHumanMotionTransitionOptions> MotionTransitions { get; init; } = [];

    public DigitalHumanResponseChunkingOptions ResponseChunking { get; init; } = new();

    public string SpeechDictionaryDirectory { get; init; } = "Resources/SpeechLipSyncDictionaries";

    public SpeechDictionaryLanguage SpeechDictionaryLanguage { get; init; } = SpeechDictionaryLanguage.Chinese;

    public int HistoryMaxMessages { get; init; } = 12;

    public string WakeAcknowledgementText { get; init; } = "\u6211\u5728\uff0c\u8bf7\u8bf4\u3002";

    public string ListeningPromptText { get; init; } = "\u6211\u5728\u542c\u3002";

    public string ThinkingText { get; init; } = "\u6211\u60f3\u60f3\u2026\u2026";

    public VoiceActivityCaptureOptions WakeWordCapture { get; init; } = new()
    {
        SampleRate = 16_000,
        Channels = 1,
        FramesPerBuffer = 512,
        PreRoll = TimeSpan.FromMilliseconds(250),
        MinDuration = TimeSpan.FromMilliseconds(300),
        MaxDuration = TimeSpan.FromSeconds(6),
        SilenceTimeout = TimeSpan.FromMilliseconds(700),
        SilenceThreshold = 0.015f
    };

    public VoiceActivityCaptureOptions UserCapture { get; init; } = new()
    {
        SampleRate = 16_000,
        Channels = 1,
        FramesPerBuffer = 512,
        PreRoll = TimeSpan.FromMilliseconds(250),
        MinDuration = TimeSpan.FromMilliseconds(800),
        MaxDuration = TimeSpan.FromSeconds(20),
        SilenceTimeout = TimeSpan.FromMilliseconds(900),
        SilenceThreshold = 0.015f
    };
}

public sealed class DigitalHumanSceneOptions
{
    public DigitalHumanCameraOptions Camera { get; init; } = new();

    public DigitalHumanLightingOptions Lighting { get; init; } = new();

    public IReadOnlyList<DigitalHumanSceneModelOptions> Models { get; init; } = [];

    public DigitalHumanBackgroundMusicOptions BackgroundMusic { get; init; } = new();
}

public sealed class DigitalHumanCameraOptions
{
    public Float3Options Position { get; init; } = new(0.0f, 2.2f, 7.2f);

    public Float3Options Target { get; init; } = new(0.0f, 1.3f, 1.6f);

    public float Fov { get; init; } = 45.0f;
}

public sealed class DigitalHumanBackgroundMusicOptions
{
    public string? Path { get; init; } = string.Empty;

    public float Volume { get; init; } = 0.35f;

    public bool Loop { get; init; } = true;
}

public sealed class DigitalHumanLightingOptions
{
    public Float3Options DirectionalLightColor { get; init; } = Float3Options.One;

    public Float3Options DirectionalLightDirection { get; init; } = new(-0.5f, -1.0f, -0.5f);

    public Float3Options AmbientLightColor { get; init; } = new(0.68f, 0.68f, 0.68f);

    public float AmbientLightStrength { get; init; } = 0.35f;

    public Float4Options ShadowColor { get; init; } = new(0.17f, 0.17f, 0.17f, 0.45f);

    public float GroundShadowPlaneHeight { get; init; }
}

public sealed class DigitalHumanCharacterOptions
{
    public DigitalHumanBodyOptions Body { get; init; } = new();

    public IReadOnlyList<DigitalHumanWearableOptions> Wearables { get; init; } = [];

    public DigitalHumanActionOptions Actions { get; init; } = new();

    public DigitalHumanSpeechBubbleOptions SpeechBubble { get; init; } = new();
}

public sealed class DigitalHumanMotionTransitionOptions
{
    public required string Source { get; init; }

    public required string Target { get; init; }

    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(1);
}

public sealed class DigitalHumanResponseChunkingOptions
{
    public bool EnableClauseBoundaries { get; init; } = true;

    public int MinClauseCharacters { get; init; } = 12;

    public int MaxBufferedCharacters { get; init; } = 320;
}

public sealed class DigitalHumanBodyOptions : DigitalHumanModelOptions
{
    public DigitalHumanBodyOptions()
    {
        Name = "Body";
        Path = "GameData/Character/Body/Body.pmx";
        Scale = new Float3Options(0.2f, 0.2f, 0.2f);
        Position = new Float3Options(0.0f, 0.0f, 1.6f);
        EnablePhysical = true;
        EnableEdge = true;
        EnableShadow = true;
        DrawShadowInMainPass = true;
    }
}

public sealed class DigitalHumanWearableOptions : DigitalHumanModelOptions
{
    public bool BindComponentTransform { get; init; } = true;

    public bool BindLighting { get; init; } = true;
}

public class DigitalHumanSceneModelOptions : DigitalHumanModelOptions
{
    public bool IsPlaying { get; init; }

    public bool ReceivesGroundShadow { get; init; } = true;
}

public class DigitalHumanModelOptions
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public Float3Options Position { get; init; } = Float3Options.Zero;

    public Float3Options Scale { get; init; } = Float3Options.One;

    public Float3Options RotationDegrees { get; init; } = Float3Options.Zero;

    public bool EnablePhysical { get; init; }

    public bool EnableEdge { get; init; } = true;

    public bool EnableShadow { get; init; } = true;

    public bool DrawShadowInMainPass { get; init; } = true;
}

public sealed class DigitalHumanActionOptions
{
    public bool EnableDebugMotionHotkeys { get; init; }

    public IReadOnlyList<DigitalHumanMotionClipOptions> Stand { get; init; } = [];

    public IReadOnlyList<DigitalHumanMotionClipOptions> Wait { get; init; } = [];

    public IReadOnlyList<DigitalHumanMotionClipOptions> Walk { get; init; } = [];

    public IReadOnlyList<DigitalHumanMotionClipOptions> Run { get; init; } = [];
}

public sealed record DigitalHumanMotionClipOptions(string Path, bool ResetPhysicsOnLoop = false);

public sealed class DigitalHumanSpeechBubbleOptions
{
    public bool Enabled { get; init; } = true;

    public float Width { get; init; } = 360.0f;

    public Float3Options WorldOffset { get; init; } = new(0.0f, 0.45f, 0.0f);

    public Float2Options ScreenOffset { get; init; } = new(0.0f, -12.0f);

    public bool ShowUserText { get; init; } = true;
}

public readonly record struct Float2Options(float X, float Y)
{
    public static Float2Options Zero => new(0.0f, 0.0f);

    public Vector2 ToVector2() => new(X, Y);
}

public readonly record struct Float3Options(float X, float Y, float Z)
{
    public static Float3Options Zero => new(0.0f, 0.0f, 0.0f);

    public static Float3Options One => new(1.0f, 1.0f, 1.0f);

    public Vector3 ToVector3() => new(X, Y, Z);
}

public readonly record struct Float4Options(float X, float Y, float Z, float W)
{
    public Vector4 ToVector4() => new(X, Y, Z, W);
}

internal sealed class ResolvedDigitalHumanOptions
{
    public string? CapturedAudioDirectory { get; init; }

    public string? WindowIconPath { get; init; }

    public required bool DeleteCapturedAudioAfterRecognition { get; init; }

    public required ResolvedDigitalHumanAudioOptions Audio { get; init; }

    public required OpenAiRealtimeClientOptions RealtimeClient { get; init; }

    public required OpenAiRealtimeSession RealtimeSession { get; init; }

    public required DigitalHumanSpeechOutputOptions SpeechOutput { get; init; }

    public required ResolvedConversationOptions Conversation { get; init; }

    public required ResolvedSceneOptions Scene { get; init; }

    public required ResolvedCharacterOptions Character { get; init; }
}

internal sealed class ResolvedDigitalHumanAudioOptions
{
    public required AudioPlaybackBackend PlaybackBackend { get; init; }

    public required PortAudioRuntimeOptions PortAudio { get; init; }
}

internal sealed class ResolvedConversationOptions
{
    public required IReadOnlyList<string> WakeWords { get; init; }

    public required TimeSpan WakeWordListeningTimeout { get; init; }

    public required bool UseFallbackRecognizersForWakeWord { get; init; }

    public required TimeSpan WakeWordChunkDuration { get; init; }

    public required TimeSpan WakeWordExtensionDuration { get; init; }

    public required TimeSpan WakeWordTrailingSilencePadding { get; init; }

    public required TimeSpan PostResponseIdleTimeout { get; init; }

    public required TimeSpan ReturnToStandTimeout { get; init; }

    public required string ReturnToStandPromptText { get; init; }

    public required TimeSpan MotionTransitionDuration { get; init; }

    public required IReadOnlyList<ResolvedMotionTransitionOptions> MotionTransitions { get; init; }

    public required ResolvedResponseChunkingOptions ResponseChunking { get; init; }

    public required string SpeechDictionaryDirectory { get; init; }

    public required SpeechDictionaryLanguage SpeechDictionaryLanguage { get; init; }

    public required int HistoryMaxMessages { get; init; }

    public required string WakeAcknowledgementText { get; init; }

    public required string ListeningPromptText { get; init; }

    public required string ThinkingText { get; init; }

    public required VoiceActivityCaptureOptions WakeWordCapture { get; init; }

    public required VoiceActivityCaptureOptions UserCapture { get; init; }
}

internal sealed class ResolvedSceneOptions
{
    public required ResolvedCameraOptions Camera { get; init; }

    public required ResolvedLightingOptions Lighting { get; init; }

    public required IReadOnlyList<ResolvedSceneModelOptions> Models { get; init; }

    public required ResolvedBackgroundMusicOptions BackgroundMusic { get; init; }
}

internal sealed class ResolvedCameraOptions
{
    public required Vector3 Position { get; init; }

    public required Vector3 Target { get; init; }

    public required float Fov { get; init; }
}

internal sealed class ResolvedBackgroundMusicOptions
{
    public string? Path { get; init; }

    public required float Volume { get; init; }

    public required bool Loop { get; init; }
}

internal sealed class ResolvedLightingOptions
{
    public required Vector3 DirectionalLightColor { get; init; }

    public required Vector3 DirectionalLightDirection { get; init; }

    public required Vector3 AmbientLightColor { get; init; }

    public required float AmbientLightStrength { get; init; }

    public required Vector4 ShadowColor { get; init; }

    public required float GroundShadowPlaneHeight { get; init; }
}

internal sealed class ResolvedCharacterOptions
{
    public required ResolvedBodyOptions Body { get; init; }

    public required IReadOnlyList<ResolvedWearableOptions> Wearables { get; init; }

    public required ResolvedActionOptions Actions { get; init; }

    public required DigitalHumanSpeechBubbleOptions SpeechBubble { get; init; }
}

internal sealed class ResolvedBodyOptions : ResolvedModelOptions
{
}

internal sealed class ResolvedWearableOptions : ResolvedModelOptions
{
    public required bool BindComponentTransform { get; init; }

    public required bool BindLighting { get; init; }
}

internal sealed class ResolvedSceneModelOptions : ResolvedModelOptions
{
    public required bool IsPlaying { get; init; }

    public required bool ReceivesGroundShadow { get; init; }
}

internal class ResolvedModelOptions
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public required Vector3 Position { get; init; }

    public required Vector3 Scale { get; init; }

    public required Quaternion Rotation { get; init; }

    public required bool EnablePhysical { get; init; }

    public required bool EnableEdge { get; init; }

    public required bool EnableShadow { get; init; }

    public required bool DrawShadowInMainPass { get; init; }
}

internal sealed class ResolvedActionOptions
{
    public required bool EnableDebugMotionHotkeys { get; init; }

    public required IReadOnlyList<ResolvedMotionClipOptions> Stand { get; init; }

    public required IReadOnlyList<ResolvedMotionClipOptions> Wait { get; init; }

    public required IReadOnlyList<ResolvedMotionClipOptions> Walk { get; init; }

    public required IReadOnlyList<ResolvedMotionClipOptions> Run { get; init; }
}

internal sealed class ResolvedMotionClipOptions
{
    public required string Path { get; init; }

    public required bool ResetPhysicsOnLoop { get; init; }
}

internal sealed class ResolvedMotionTransitionOptions
{
    public required CharacterMotionGroup Source { get; init; }

    public required CharacterMotionGroup Target { get; init; }

    public required TimeSpan Duration { get; init; }
}

internal sealed class ResolvedResponseChunkingOptions
{
    public required bool EnableClauseBoundaries { get; init; }

    public required int MinClauseCharacters { get; init; }

    public required int MaxBufferedCharacters { get; init; }
}

internal static class DigitalHumanOptionsResolver
{
    private static readonly string[] DefaultWakeWords =
    [
        "\u6653\u96e8",
        "\u5c0f\u96e8",
        "\u5c0f\u5b87",
        "\u5c0f\u7389",
        "\u5c0f\u9c7c"
    ];

    public static ResolvedDigitalHumanOptions Resolve(DigitalHumanAppOptions options, SamplePathResolver paths)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);

        Dictionary<string, string> realtimeHeaders = options.Realtime.Headers
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase);

        OpenAiRealtimeClientOptions realtimeClient = new()
        {
            BaseUrl = options.Realtime.BaseUrl,
            RealtimePath = options.Realtime.RealtimePath,
            AudioSpeechPath = options.Realtime.AudioSpeechPath,
            ApiKey = string.IsNullOrWhiteSpace(options.Realtime.ApiKey) ? null : options.Realtime.ApiKey.Trim(),
            Model = options.Realtime.Model,
            ConnectTimeout = options.Realtime.ConnectTimeout,
            OutboundAudioChunkSamples = Math.Max(512, options.Realtime.OutboundAudioChunkSamples),
            SendOpenAiBetaHeader = options.Realtime.SendOpenAiBetaHeader,
            Headers = realtimeHeaders
        };

        OpenAiRealtimeSession realtimeSession = new()
        {
            Model = options.Realtime.Model,
            Instructions = options.Realtime.Instructions,
            OutputModalities = options.Realtime.OutputModalities.Length > 0 ? options.Realtime.OutputModalities : ["audio"],
            Audio = new OpenAiRealtimeSessionAudioOptions
            {
                Input = new OpenAiRealtimeSessionInputAudioOptions
                {
                    Format = OpenAiRealtimeAudioFormat.Pcm16(options.Realtime.InputAudioSampleRate),
                    Transcription = new OpenAiRealtimeInputAudioTranscription
                    {
                        Model = options.Realtime.InputTranscriptionModel,
                        Language = options.Realtime.InputTranscriptionLanguage,
                        Prompt = options.Realtime.InputTranscriptionPrompt
                    },
                    TurnDetection = null
                },
                Output = new OpenAiRealtimeSessionOutputAudioOptions
                {
                    Format = OpenAiRealtimeAudioFormat.Pcm16(options.Realtime.OutputAudioSampleRate),
                    Voice = options.Realtime.Voice
                }
            },
            MaxOutputTokens = options.Realtime.MaxOutputTokens,
            Temperature = options.Realtime.Temperature
        };

        return new ResolvedDigitalHumanOptions
        {
            CapturedAudioDirectory = string.IsNullOrWhiteSpace(options.CapturedAudioDirectory) ? null : paths.ResolveOptionalDirectory(options.CapturedAudioDirectory),
            WindowIconPath = paths.ResolveOptionalFile(options.WindowIconPath),
            Audio = new ResolvedDigitalHumanAudioOptions
            {
                PlaybackBackend = options.Audio.PlaybackBackend,
                PortAudio = new PortAudioRuntimeOptions
                {
                    InputDeviceIndex = options.Audio.InputDeviceIndex,
                    OutputDeviceIndex = options.Audio.OutputDeviceIndex
                }
            },
            RealtimeClient = realtimeClient,
            RealtimeSession = realtimeSession,
            SpeechOutput = options.SpeechOutput,
            DeleteCapturedAudioAfterRecognition = options.DeleteCapturedAudioAfterRecognition,
            Conversation = ResolveConversation(options.Conversation, paths),
            Scene = ResolveScene(options.Scene, paths),
            Character = ResolveCharacter(options.Character, paths)
        };
    }

    private static ResolvedConversationOptions ResolveConversation(DigitalHumanConversationOptions options, SamplePathResolver paths)
    {
        IReadOnlyList<string> wakeWords = options.WakeWords.Count > 0 ? options.WakeWords : DefaultWakeWords;

        return new ResolvedConversationOptions
        {
            WakeWords = wakeWords.Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            WakeWordListeningTimeout = options.WakeWordListeningTimeout,
            UseFallbackRecognizersForWakeWord = options.UseFallbackRecognizersForWakeWord,
            WakeWordChunkDuration = options.WakeWordChunkDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : options.WakeWordChunkDuration,
            WakeWordExtensionDuration = options.WakeWordExtensionDuration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1200) : options.WakeWordExtensionDuration,
            WakeWordTrailingSilencePadding = options.WakeWordTrailingSilencePadding < TimeSpan.Zero ? TimeSpan.Zero : options.WakeWordTrailingSilencePadding,
            PostResponseIdleTimeout = options.PostResponseIdleTimeout,
            ReturnToStandTimeout = options.ReturnToStandTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : options.ReturnToStandTimeout,
            ReturnToStandPromptText = options.ReturnToStandPromptText,
            MotionTransitionDuration = options.MotionTransitionDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : options.MotionTransitionDuration,
            MotionTransitions = ResolveMotionTransitions(options.MotionTransitions),
            ResponseChunking = ResolveResponseChunking(options.ResponseChunking),
            SpeechDictionaryDirectory = paths.ResolveRequiredDirectory(options.SpeechDictionaryDirectory),
            SpeechDictionaryLanguage = options.SpeechDictionaryLanguage,
            HistoryMaxMessages = Math.Max(0, options.HistoryMaxMessages),
            WakeAcknowledgementText = options.WakeAcknowledgementText,
            ListeningPromptText = options.ListeningPromptText,
            ThinkingText = options.ThinkingText,
            WakeWordCapture = options.WakeWordCapture,
            UserCapture = options.UserCapture
        };
    }

    private static IReadOnlyList<ResolvedMotionTransitionOptions> ResolveMotionTransitions(IReadOnlyList<DigitalHumanMotionTransitionOptions> sourceTransitions)
    {
        if (sourceTransitions.Count == 0)
        {
            return [];
        }

        return sourceTransitions
            .Select(item => new ResolvedMotionTransitionOptions
            {
                Source = ParseMotionGroup(item.Source),
                Target = ParseMotionGroup(item.Target),
                Duration = item.Duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : item.Duration
            })
            .ToArray();
    }

    private static ResolvedResponseChunkingOptions ResolveResponseChunking(DigitalHumanResponseChunkingOptions options)
    {
        return new ResolvedResponseChunkingOptions
        {
            EnableClauseBoundaries = options.EnableClauseBoundaries,
            MinClauseCharacters = Math.Max(1, options.MinClauseCharacters),
            MaxBufferedCharacters = Math.Max(1, options.MaxBufferedCharacters)
        };
    }

    private static CharacterMotionGroup ParseMotionGroup(string value)
    {
        if (Enum.TryParse<CharacterMotionGroup>(value, ignoreCase: true, out CharacterMotionGroup group))
        {
            return group;
        }

        throw new InvalidOperationException($"Unsupported motion group '{value}'.");
    }

    private static ResolvedSceneOptions ResolveScene(DigitalHumanSceneOptions options, SamplePathResolver paths)
    {
        IReadOnlyList<DigitalHumanSceneModelOptions> sourceModels = options.Models;

        return new ResolvedSceneOptions
        {
            Camera = new ResolvedCameraOptions
            {
                Position = options.Camera.Position.ToVector3(),
                Target = options.Camera.Target.ToVector3(),
                Fov = options.Camera.Fov
            },
            Lighting = new ResolvedLightingOptions
            {
                DirectionalLightColor = options.Lighting.DirectionalLightColor.ToVector3(),
                DirectionalLightDirection = options.Lighting.DirectionalLightDirection.ToVector3(),
                AmbientLightColor = options.Lighting.AmbientLightColor.ToVector3(),
                AmbientLightStrength = options.Lighting.AmbientLightStrength,
                ShadowColor = options.Lighting.ShadowColor.ToVector4(),
                GroundShadowPlaneHeight = options.Lighting.GroundShadowPlaneHeight
            },
            Models = sourceModels.Select(item => new ResolvedSceneModelOptions
            {
                Name = string.IsNullOrWhiteSpace(item.Name) ? Path.GetFileNameWithoutExtension(item.Path) : item.Name,
                Path = paths.ResolveRequiredFile(item.Path),
                Position = item.Position.ToVector3(),
                Scale = item.Scale.ToVector3(),
                Rotation = ToQuaternion(item.RotationDegrees.ToVector3()),
                EnablePhysical = item.EnablePhysical,
                EnableEdge = item.EnableEdge,
                EnableShadow = item.EnableShadow,
                DrawShadowInMainPass = item.DrawShadowInMainPass,
                IsPlaying = item.IsPlaying,
                ReceivesGroundShadow = item.ReceivesGroundShadow
            }).ToArray(),
            BackgroundMusic = new ResolvedBackgroundMusicOptions
            {
                Path = paths.ResolveOptionalFile(options.BackgroundMusic.Path),
                Volume = Math.Clamp(options.BackgroundMusic.Volume, 0.0f, 4.0f),
                Loop = options.BackgroundMusic.Loop
            }
        };
    }

    private static ResolvedCharacterOptions ResolveCharacter(DigitalHumanCharacterOptions options, SamplePathResolver paths)
    {
        IReadOnlyList<DigitalHumanWearableOptions> sourceWearables = options.Wearables.Count > 0
            ? options.Wearables
            :
            [
                new DigitalHumanWearableOptions
                {
                    Name = "MaidOutfit",
                    Path = "GameData/Character/MaidOutfit/MaidOutfit.pmx",
                    Scale = new Float3Options(0.2f, 0.2f, 0.2f),
                    Position = new Float3Options(0.0f, 0.0f, 1.6f),
                    BindComponentTransform = true,
                    BindLighting = true,
                    EnablePhysical = true,
                    EnableEdge = true,
                    EnableShadow = true,
                    DrawShadowInMainPass = true
                }
            ];

        return new ResolvedCharacterOptions
        {
            Body = new ResolvedBodyOptions
            {
                Name = string.IsNullOrWhiteSpace(options.Body.Name) ? "Body" : options.Body.Name,
                Path = paths.ResolveRequiredFile(options.Body.Path),
                Position = options.Body.Position.ToVector3(),
                Scale = options.Body.Scale.ToVector3(),
                Rotation = ToQuaternion(options.Body.RotationDegrees.ToVector3()),
                EnablePhysical = options.Body.EnablePhysical,
                EnableEdge = options.Body.EnableEdge,
                EnableShadow = options.Body.EnableShadow,
                DrawShadowInMainPass = options.Body.DrawShadowInMainPass
            },
            Wearables = sourceWearables.Select(item => new ResolvedWearableOptions
            {
                Name = string.IsNullOrWhiteSpace(item.Name) ? Path.GetFileNameWithoutExtension(item.Path) : item.Name,
                Path = paths.ResolveRequiredFile(item.Path),
                Position = item.Position.ToVector3(),
                Scale = item.Scale.ToVector3(),
                Rotation = ToQuaternion(item.RotationDegrees.ToVector3()),
                EnablePhysical = item.EnablePhysical,
                EnableEdge = item.EnableEdge,
                EnableShadow = item.EnableShadow,
                DrawShadowInMainPass = item.DrawShadowInMainPass,
                BindComponentTransform = item.BindComponentTransform,
                BindLighting = item.BindLighting
            }).ToArray(),
            Actions = new ResolvedActionOptions
            {
                EnableDebugMotionHotkeys = options.Actions.EnableDebugMotionHotkeys,
                Stand = ResolveMotionPaths(
                    options.Actions.Stand.Count > 0 ? options.Actions.Stand : [new DigitalHumanMotionClipOptions("GameData/Motion/Basic/basic_stand.vmd", false)],
                    paths),
                Wait = ResolveMotionPaths(
                    options.Actions.Wait.Count > 0 ? options.Actions.Wait : [new DigitalHumanMotionClipOptions("GameData/Motion/Basic/basic_wait.vmd", false)],
                    paths),
                Walk = ResolveMotionPaths(
                    options.Actions.Walk.Count > 0 ? options.Actions.Walk : [new DigitalHumanMotionClipOptions("GameData/Motion/Basic/basic_walk.vmd", false)],
                    paths),
                Run = ResolveMotionPaths(
                    options.Actions.Run.Count > 0 ? options.Actions.Run : [new DigitalHumanMotionClipOptions("GameData/Motion/Basic/basic_run.vmd", false)],
                    paths)
            },
            SpeechBubble = options.SpeechBubble
        };
    }

    private static IReadOnlyList<ResolvedMotionClipOptions> ResolveMotionPaths(IEnumerable<DigitalHumanMotionClipOptions> values, SamplePathResolver paths)
    {
        return values
            .Where(static item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .Select(item => new ResolvedMotionClipOptions
            {
                Path = paths.ResolveRequiredFile(item.Path),
                ResetPhysicsOnLoop = item.ResetPhysicsOnLoop
            })
            .ToArray();
    }

    private static Quaternion ToQuaternion(Vector3 degrees)
    {
        Vector3 radians = degrees * (MathF.PI / 180.0f);
        return Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
    }
}
