using System.Numerics;
using System.Text.Json.Serialization;
using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.GameProjects;

public sealed class GameProject
{
    public string Name { get; set; } = "Untitled Game";

    public string Version { get; set; } = "0.1.0";

    public string DefaultScene { get; set; } = "scenes/main.scene.json";

    public string EditorScene { get; set; } = "scenes/main.scene.json";

    public List<string> Scenes { get; set; } = ["scenes/main.scene.json"];

    public GameProjectScriptRuntime ScriptRuntime { get; set; } = new();

    public GameProjectVoiceSettings Voice { get; set; } = new();

    public GameProjectMicrophoneSettings Microphone { get; set; } = new();

    public GameProjectAsrSettings Asr { get; set; } = new();

    public GameProjectRealtimeVoiceSettings RealtimeVoice { get; set; } = new();

    public GameProjectLlmSettings Llm { get; set; } = new();

    public GameWindowSettings Window { get; set; } = new();

    public GameRuntimeSettings Runtime { get; set; } = new();

    /// <summary>Android 运行时质量、资源预算和自适应降级策略。</summary>
    public AndroidQualitySettings AndroidQuality { get; set; } = new();

    [JsonIgnore]
    public GameProjectScene Scene { get; set; } = new();
}

public sealed class GameRuntimeSettings
{
    public string GraphicsBackend { get; set; } = "Auto";

    public bool UseOpenCL { get; set; } = true;

    public bool UseVulkanCompute { get; set; }
}

public sealed class GameProjectScriptRuntime
{
    public string PreferredLanguage { get; set; } = "csharp";

    public List<string> ScriptSearchPaths { get; set; } = ["scripts"];
}

public sealed class GameProjectVoiceSettings
{
    public bool Enabled { get; set; }

    public AudioPlaybackBackend PlaybackBackend { get; set; } = AudioPlaybackBackend.OpenAL;

    public int? OutputDeviceIndex { get; set; }

    public string TtsProvider { get; set; } = "sherpa-onnx";

    public string ModelKind { get; set; } = "vits";

    public string ModelPath { get; set; } = string.Empty;

    public string TokensPath { get; set; } = string.Empty;

    public string? LexiconPath { get; set; }

    public string? DataDirectory { get; set; }

    public string? DictDirectory { get; set; }

    public string? VocoderPath { get; set; }

    public string? RuleFars { get; set; }

    public string? RuleFsts { get; set; }

    public string InferenceProvider { get; set; } = "cpu";

    public int Threads { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    public int DefaultSpeakerId { get; set; }

    public float DefaultSpeed { get; set; } = 1.0f;

    public float DefaultVolume { get; set; } = 1.0f;

    public bool PreloadOnSceneLoad { get; set; } = true;

    public string WarmUpText { get; set; } = "你好";

    public GameProjectLipSyncSettings LipSync { get; set; } = new();
}

public sealed class GameProjectMicrophoneSettings
{
    public bool AutoDetectOnPlayerLoad { get; set; }
}

public sealed class GameProjectRealtimeVoiceSettings
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://127.0.0.1:5000";

    public string RealtimePath { get; set; } = "/v1/realtime";

    public string AudioSpeechPath { get; set; } = "/v1/audio/speech";

    public string ApiKey { get; set; } = string.Empty;

    public string ApiKeyEnvironmentVariable { get; set; } = "OPENAI_API_KEY";

    public string Model { get; set; } = "zhengyan-realtime-voice";

    public string Instructions { get; set; } = "你是晓雨，一个温柔、简洁、自然的中文语音助手。请直接回答用户问题，避免冗长。";

    public string Voice { get; set; } = "0";

    public int ConnectTimeoutSeconds { get; set; } = 30;

    public int OutboundAudioChunkSamples { get; set; } = 4096;

    public int InputAudioSampleRate { get; set; } = 24000;

    public int OutputAudioSampleRate { get; set; } = 24000;

    public string InputTranscriptionModel { get; set; } = "whisper-1";

    public string InputTranscriptionLanguage { get; set; } = "zh";

    public string InputTranscriptionPrompt { get; set; } = string.Empty;

    public int? MaxOutputTokens { get; set; } = 1024;

    public float? Temperature { get; set; } = 0.7f;

    public int? InputDeviceIndex { get; set; }

    public float OutputVolume { get; set; } = 1.0f;

    public float PromptSpeed { get; set; } = 1.0f;

    public GameProjectVoiceActivityCaptureSettings UserCapture { get; set; } = new();

    public GameProjectRealtimeVoiceWakeWordSettings WakeWord { get; set; } = new();
}

public sealed class GameProjectAsrSettings
{
    public bool Enabled { get; set; }

    public string Provider { get; set; } = "sherpa";

    public bool PreloadOnSceneLoad { get; set; } = true;

    public int? InputDeviceIndex { get; set; }

    public float PartialResultIntervalSeconds { get; set; } = 0.75f;

    public GameProjectAudioCaptureSettings Capture { get; set; } = new()
    {
        SampleRate = 16000,
        Channels = 1,
        FramesPerBuffer = 512
    };

    public GameProjectSherpaAsrSettings Sherpa { get; set; } = new();

    public GameProjectWhisperAsrSettings Whisper { get; set; } = new();
}

public sealed class GameProjectSherpaAsrSettings
{
    public string ModelKind { get; set; } = "OnlineTransducer";

    public string TokensPath { get; set; } = string.Empty;

    public string? EncoderPath { get; set; }

    public string? DecoderPath { get; set; }

    public string? JoinerPath { get; set; }

    public string? ModelPath { get; set; }

    public string Language { get; set; } = "zh";

    public string Provider { get; set; } = "cpu";

    public int SampleRate { get; set; } = 16000;

    public int FeatureDim { get; set; } = 80;

    public int Threads { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    public string DecodingMethod { get; set; } = "greedy_search";
}

public sealed class GameProjectWhisperAsrSettings
{
    public string ModelPath { get; set; } = string.Empty;

    public string Language { get; set; } = "auto";

    public bool TranslateToEnglish { get; set; }

    public bool UseGpu { get; set; }

    public int Threads { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    public int SampleRate { get; set; } = 16000;
}

public class GameProjectAudioCaptureSettings
{
    public int SampleRate { get; set; } = 16000;

    public int Channels { get; set; } = 1;

    public int FramesPerBuffer { get; set; } = 512;
}

public sealed class GameProjectVoiceActivityCaptureSettings : GameProjectAudioCaptureSettings
{
    public float PreRollSeconds { get; set; } = 0.25f;

    public float MinDurationSeconds { get; set; } = 0.8f;

    public float MaxDurationSeconds { get; set; } = 20.0f;

    public float SilenceTimeoutSeconds { get; set; } = 0.9f;

    public float SilenceThreshold { get; set; } = 0.015f;
}

public sealed class GameProjectRealtimeVoiceWakeWordSettings
{
    public bool Enabled { get; set; }

    public List<string> Keywords { get; set; } = [];

    public float ChunkDurationSeconds { get; set; } = 2.0f;

    public float ExtensionDurationSeconds { get; set; } = 1.2f;

    public float TrailingSilencePaddingSeconds { get; set; } = 0.4f;

    public GameProjectAudioCaptureSettings Capture { get; set; } = new();
}

public sealed class GameProjectLlmSettings
{
    public bool Enabled { get; set; }

    public bool EnableSkills { get; set; }

    public bool EnableMemory { get; set; }

    public string Provider { get; set; } = "openai-compatible";

    public string BaseUrl { get; set; } = "https://api.openai.com";

    public string ApiKey { get; set; } = string.Empty;

    public string ApiKeyEnvironmentVariable { get; set; } = "OPENAI_API_KEY";

    public string Model { get; set; } = "gpt-4o-mini";

    public string ChatCompletionsPath { get; set; } = "/v1/chat/completions";

    public int TimeoutSeconds { get; set; } = 300;

    public float? DefaultTemperature { get; set; }
}

public sealed class GameWindowSettings
{
    public string Title { get; set; } = "Demo Game";

    public string IconPath { get; set; } = string.Empty;

    public bool DesktopSpriteMode { get; set; }

    public bool DesktopSpriteClickThrough { get; set; }

    public string DesktopSpriteDragButton { get; set; } = "none";

    public bool DesktopSpriteTrayEnabled { get; set; }

    public string DesktopSpriteTrayIconPath { get; set; } = string.Empty;

    public string DesktopSpriteTrayWindowsIconPath { get; set; } = string.Empty;

    public string DesktopSpriteTrayLinuxIconPath { get; set; } = string.Empty;

    public string DesktopSpriteTrayMacOSIconPath { get; set; } = string.Empty;

    public List<DesktopSpriteTrayMenuItemSettings>? DesktopSpriteTrayMenuItems { get; set; } =
    [
        new DesktopSpriteTrayMenuItemSettings
        {
            Id = "toggle_visibility",
            Text = "Show / Hide",
            BuiltInAction = "toggle_visibility",
            EventName = "tray_toggle_visibility"
        },
        new DesktopSpriteTrayMenuItemSettings
        {
            Id = "exit",
            Text = "Exit",
            BuiltInAction = "exit",
            EventName = "tray_exit"
        }
    ];

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public bool Fullscreen { get; set; }

    public bool Resizable { get; set; } = true;

    public string TimingMode { get; set; } = "time_synchronized";

    public int AntiAliasingSamples { get; set; } = 4;
}

public sealed class AndroidQualitySettings
{
    /// <summary>auto、low、medium 或 high。</summary>
    public string Profile { get; set; } = "auto";

    public int TargetFrameRate { get; set; } = 60;

    public int MaxShadowMapSize { get; set; } = 1024;

    public int MaxLocalShadowMapSize { get; set; } = 512;

    public int MaxPointShadowMaps { get; set; } = 2;

    public int MaxSpotShadowMaps { get; set; } = 2;

    public int MaxReflectionSurfaces { get; set; } = 4;

    public int MaxParticleCount { get; set; } = 2000;

    public int TextureMemoryBudgetMb { get; set; } = 256;

    public int RenderTargetMemoryBudgetMb { get; set; } = 96;

    public int DrawCallBudget { get; set; } = 3500;

    public bool DynamicDegradation { get; set; } = true;

    public float DynamicFrameBudgetMs { get; set; } = 16.67f;
}

public sealed class DesktopSpriteTrayMenuItemSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text { get; set; } = "Menu Item";

    public bool Enabled { get; set; } = true;

    public string BuiltInAction { get; set; } = "none";

    public string EventName { get; set; } = string.Empty;
}

public sealed class GameProjectLipSyncSettings
{
    public bool Enabled { get; set; } = true;

    public string DictionaryDirectory { get; set; } = "Resources/SpeechLipSyncDictionaries";

    public string DictionaryLanguage { get; set; } = "Chinese";

    public List<string> DictionaryLanguages { get; set; } = [];

    public float MinFramePeriodMilliseconds { get; set; } = 70.0f;

    public float MaxFramePeriodMilliseconds { get; set; } = 320.0f;

    public bool UseFallbackVowelOnNoMatch { get; set; }

    public string NoMatchFallbackVowel { get; set; } = "\u3042";

    public Dictionary<string, string> VowelMorphMap { get; set; } = new()
    {
        ["あ"] = "あ",
        ["い"] = "い",
        ["う"] = "う",
        ["え"] = "え",
        ["お"] = "お"
    };

    public IReadOnlyList<string> GetEffectiveDictionaryLanguages()
    {
        List<string> languages = [];
        AddNormalizedLanguageValues(DictionaryLanguages, languages);
        if (languages.Count == 0)
        {
            AddNormalizedLanguageValues(DictionaryLanguage, languages);
        }

        if (languages.Count == 0)
        {
            languages.Add("Chinese");
        }

        return languages;
    }

    public void SetEffectiveDictionaryLanguages(IEnumerable<string> languages)
    {
        ArgumentNullException.ThrowIfNull(languages);

        List<string> normalized = [];
        AddNormalizedLanguageValues(languages, normalized);
        if (normalized.Count == 0)
        {
            normalized.Add("Chinese");
        }

        DictionaryLanguages = normalized;
        DictionaryLanguage = normalized[0];
    }

    public string GetEffectiveNoMatchFallbackVowel()
    {
        return NormalizeJapaneseVowel(NoMatchFallbackVowel) ?? "\u3042";
    }

    private static void AddNormalizedLanguageValues(IEnumerable<string>? source, IList<string> target)
    {
        if (source is null)
        {
            return;
        }

        foreach (string value in source)
        {
            AddNormalizedLanguageValues(value, target);
        }
    }

    private static void AddNormalizedLanguageValues(string? source, IList<string> target)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        string[] values = source.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0)
        {
            values = [source.Trim()];
        }

        foreach (string value in values)
        {
            string? normalized = NormalizeDictionaryLanguageName(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            bool alreadyExists = false;
            foreach (string existing in target)
            {
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyExists = true;
                    break;
                }
            }

            if (!alreadyExists)
            {
                target.Add(normalized);
            }
        }
    }

    private static string? NormalizeDictionaryLanguageName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "zh-hans" or "chinese" => "Chinese",
            "ja" or "ja-jp" or "jp" or "japanese" => "Japanese",
            "en" or "en-us" or "en-gb" or "english" => "English",
            _ => null
        };
    }

    private static string? NormalizeJapaneseVowel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim() switch
        {
            "\u3042" => "\u3042",
            "\u3044" => "\u3044",
            "\u3046" => "\u3046",
            "\u3048" => "\u3048",
            "\u304A" => "\u304A",
            _ => null
        };
    }
}

public sealed class GameProjectScene
{
    public string Name { get; set; } = "Main Scene";

    public CameraSettings Camera { get; set; } = new();

    public string MainCamera { get; set; } = "Main Camera";

    public List<SceneCameraSettings> Cameras { get; set; } =
    [
        new SceneCameraSettings
        {
            Name = "Main Camera",
            IsMain = true
        }
    ];

    public List<RenderTextureSettings> RenderTextures { get; set; } = [];

    public LightingSettings Lighting { get; set; } = new();

    public SkyboxSettings Skybox { get; set; } = new();

    public LoadingScreenSettings LoadingScreen { get; set; } = new();

    public List<ScriptBinding> LoadingScripts { get; set; } = [];

    public List<GuiControlSettings> GuiControls { get; set; } = [];

    public List<ContextMenuSettings> ContextMenus { get; set; } = [];

    public List<SpriteSettings> Sprites { get; set; } = [];

    public List<GameEntity> Entities { get; set; } = [];

    public List<AudioAsset> Audio { get; set; } = [];

    public List<MotionAsset> Motions { get; set; } = [];
}

public sealed class LoadingScreenSettings
{
    public Vector4Dto BackgroundColor { get; set; } = new(0.0f, 0.0f, 0.0f, 1.0f);

    public string BackgroundImagePath { get; set; } = string.Empty;

    public float BackgroundImageOpacity { get; set; } = 1.0f;

    public LoadingProgressBarSettings ProgressBar { get; set; } = new();
}

public sealed class LoadingProgressBarSettings
{
    public bool Visible { get; set; } = true;

    public string LayoutMode { get; set; } = "relative";

    public float X { get; set; } = 346.0f;

    public float Y { get; set; } = 342.0f;

    public float Width { get; set; } = 588.0f;

    public float Height { get; set; } = 36.0f;

    public Vector4Dto BackgroundColor { get; set; } = new(0.10f, 0.15f, 0.22f, 0.92f);

    public Vector4Dto TrackColor { get; set; } = new(0.02f, 0.04f, 0.07f, 1.0f);

    public Vector4Dto FillColor { get; set; } = new(0.30f, 0.62f, 1.0f, 1.0f);

    public Vector4Dto BorderColor { get; set; } = new(0.14f, 0.20f, 0.30f, 0.95f);

    public float BorderThickness { get; set; } = 2.0f;

    public float Rounding { get; set; } = 0.0f;

    public float Padding { get; set; } = 10.0f;
}

public sealed class CameraSettings
{
    public Vector3Dto Position { get; set; } = new(0.0f, 2.0f, 8.0f);

    public Vector3Dto Target { get; set; } = new(0.0f, 1.2f, 0.0f);

    public string ControlMode { get; set; } = "editor";

    public string TargetEntity { get; set; } = string.Empty;

    public string SubjectEntity { get; set; } = string.Empty;

    public float Distance { get; set; } = 5.0f;

    public float Height { get; set; } = 1.5f;

    public float ShoulderOffset { get; set; }

    public float Smoothing { get; set; } = 12.0f;

    public float MoveSpeed { get; set; } = 5.0f;

    public float MouseSensitivity { get; set; } = 0.15f;

    public float SafeRadius { get; set; } = 0.25f;

    public float AutoOrbitSpeed { get; set; } = 30.0f;

    public bool EnableMouseLook { get; set; } = true;

    public bool RequireRightMouseForMouseLook { get; set; } = true;

    public string ProjectionMode { get; set; } = "perspective";

    public float Fov { get; set; } = 45.0f;

    public float OrthographicSize { get; set; } = 5.0f;

    public float NearClipPlane { get; set; } = 0.1f;

    public float FarClipPlane { get; set; } = 1000.0f;

    public VmdPlaybackSettings Vmd { get; set; } = new();

    // VMD camera roll is transient animation state and is not part of the project file.
    [JsonIgnore]
    public Vector3Dto VmdUp { get; set; } = new(0.0f, 1.0f, 0.0f);

    [JsonIgnore]
    public bool VmdHasUp { get; set; }
}

public sealed class SceneCameraSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Camera";

    public bool IsMain { get; set; }

    public bool Enabled { get; set; } = true;

    public CameraSettings Camera { get; set; } = new();

    public CameraViewportSettings Viewport { get; set; } = new();
}

public sealed class CameraViewportSettings
{
    public bool Enabled { get; set; }

    public string LayoutMode { get; set; } = "relative";

    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; } = 1280.0f;

    public float Height { get; set; } = 720.0f;
}

public sealed class RenderTextureSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "RenderTexture";

    public bool Enabled { get; set; } = true;

    public string Camera { get; set; } = "Main Camera";

    public int Width { get; set; } = 512;

    public int Height { get; set; } = 512;

    public Vector4Dto ClearColor { get; set; } = new(0.08f, 0.09f, 0.12f, 1.0f);

    public string RefreshMode { get; set; } = "every_frame";

    public float RefreshIntervalSeconds { get; set; } = 0.1f;
}

public sealed class LightingSettings
{
    public Vector3Dto LightColor { get; set; } = new(1.0f, 1.0f, 1.0f);

    public Vector3Dto LightDirection { get; set; } = new(-0.5f, -1.0f, -0.5f);

    public Vector3Dto AmbientColor { get; set; } = new(0.65f, 0.65f, 0.65f);

    public float AmbientStrength { get; set; } = 0.25f;

    public Vector4Dto ShadowColor { get; set; } = new(0.17f, 0.17f, 0.17f, 0.7f);

    public Vector4Dto ClearColor { get; set; } = new(0.08f, 0.09f, 0.12f, 1.0f);

    public VmdPlaybackSettings Vmd { get; set; } = new();
}

public sealed class VmdPlaybackSettings
{
    public string Path { get; set; } = string.Empty;

    public bool IsPlaying { get; set; }

    public bool Loop { get; set; } = true;

    public float PlaybackSpeed { get; set; } = 1.0f;

    public float Frame { get; set; }
}

public sealed class PointLightSettings
{
    public bool Enabled { get; set; } = true;

    public Vector3Dto Color { get; set; } = Vector3Dto.One;

    public float Intensity { get; set; } = 1.0f;

    public float Range { get; set; } = 8.0f;

    // Enables real-time PMX shadow rendering for this light, subject to the
    // local-light shadow budget.
    public bool CastShadows { get; set; }
}

public sealed class SpotLightSettings
{
    public bool Enabled { get; set; } = true;

    public Vector3Dto Color { get; set; } = Vector3Dto.One;

    public float Intensity { get; set; } = 1.0f;

    public float Range { get; set; } = 12.0f;

    public float InnerConeAngleDegrees { get; set; } = 18.0f;

    public float OuterConeAngleDegrees { get; set; } = 28.0f;

    // Enables real-time PMX shadow rendering for this light, subject to the
    // local-light shadow budget.
    public bool CastShadows { get; set; }
}

public sealed class SkyboxSettings
{
    public bool Enabled { get; set; }

    public string TexturePath { get; set; } = "app:Resources/Skybox/autumn_field_puresky.jpg";

    public float Exposure { get; set; } = 1.0f;

    public Vector3Dto Tint { get; set; } = Vector3Dto.One;
}

public sealed class GameEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Entity";

    public string Type { get; set; } = "pmx_model";

    public string AssetPath { get; set; } = string.Empty;

    public TransformSettings Transform { get; set; } = new();

    public bool IsPlaying { get; set; } = true;

    public bool EnableEdge { get; set; } = true;

    public bool EnableShadow { get; set; } = true;

    public bool ReceiveShadow { get; set; } = true;

    /// <summary>
    /// Controls how shadows received by this PMX are quantized in the main material.
    /// Legacy projects omit this field and therefore keep the smooth PCF behavior.
    /// </summary>
    public string ReceiveShadowMode { get; set; } = "smooth";

    public bool DrawShadowInMainPass { get; set; }

    public float PlaybackSpeed { get; set; } = 1.0f;

    public bool LoopMotion { get; set; } = true;

    public bool ResetPhysicsOnMotionLoop { get; set; } = true;

    public bool EnablePhysics { get; set; } = true;

    public Vector3Dto PhysicsGravityDirection { get; set; } = new(0.0f, -1.0f, 0.0f);

    public float PhysicsGravityMagnitude { get; set; } = 98.0f;

    [JsonIgnore]
    public Vector3 PhysicsGravity
    {
        get
        {
            Vector3 direction = PhysicsGravityDirection.ToVector3();
            if (!float.IsFinite(direction.X)
                || !float.IsFinite(direction.Y)
                || !float.IsFinite(direction.Z)
                || direction.LengthSquared() <= 1e-12f)
            {
                direction = -Vector3.UnitY;
            }

            float magnitude = float.IsFinite(PhysicsGravityMagnitude)
                ? MathF.Max(0.0f, PhysicsGravityMagnitude)
                : 98.0f;
            return Vector3.Normalize(direction) * magnitude;
        }
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Physics gravity components must be finite.");
            }

            float magnitude = value.Length();
            PhysicsGravityMagnitude = magnitude;
            if (magnitude > 1e-6f)
            {
                PhysicsGravityDirection = Vector3Dto.FromVector3(value / magnitude);
            }
        }
    }

    public ParticleEntitySettings Particle { get; set; } = new();

    public WaterSurfaceSettings Water { get; set; } = new();

    public TexturedPlaneSettings Plane { get; set; } = new();

    public PointLightSettings PointLight { get; set; } = new();

    public SpotLightSettings SpotLight { get; set; } = new();

    public PmxRelationSettings Relation { get; set; } = new();

    public CollisionSettings Collision { get; set; } = new();

    public List<ColliderSettings> Colliders { get; set; } = [];

    public List<MotionLayerSettings> MotionLayers { get; set; } = [];

    public List<ScriptBinding> Scripts { get; set; } = [];
}

public sealed class GuiControlSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Button";

    public string Type { get; set; } = "button";

    public string Text { get; set; } = "Button";

    public bool Multiline { get; set; }

    public float X { get; set; } = 24.0f;

    public float Y { get; set; } = 24.0f;

    public float Width { get; set; } = 160.0f;

    public float Height { get; set; } = 36.0f;

    public string LayoutMode { get; set; } = "absolute";

    public bool Visible { get; set; } = true;

    public string TargetEntity { get; set; } = string.Empty;

    public string EventName { get; set; } = "clicked";

    public bool WordWrap { get; set; } = true;

    public bool Checked { get; set; }

    public float Progress { get; set; }

    public List<string> Items { get; set; } = [];

    public int SelectedIndex { get; set; }

    [JsonIgnore]
    public int CursorPosition { get; set; }

    [JsonIgnore]
    public int SelectionStart { get; set; }

    [JsonIgnore]
    public int SelectionEnd { get; set; }

    public GuiControlStyleSettings Style { get; set; } = new();
}

public sealed class ContextMenuSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Context Menu";

    public bool Enabled { get; set; } = true;

    public string TargetType { get; set; } = "window";

    public string TargetId { get; set; } = string.Empty;

    public string TargetCollider { get; set; } = string.Empty;

    public string LayoutMode { get; set; } = "absolute";

    public float Width { get; set; } = 180.0f;

    public float ItemHeight { get; set; } = 28.0f;

    public float PaddingX { get; set; } = 8.0f;

    public float PaddingY { get; set; } = 6.0f;

    public GuiControlStyleSettings Style { get; set; } = new()
    {
        BackgroundColor = new Vector4Dto(0.06f, 0.08f, 0.11f, 0.96f),
        HoverColor = new Vector4Dto(0.18f, 0.36f, 0.58f, 0.96f),
        ActiveColor = new Vector4Dto(0.12f, 0.27f, 0.45f, 1.0f),
        BorderColor = new Vector4Dto(0.34f, 0.52f, 0.70f, 0.90f),
        Rounding = 8.0f
    };

    public List<ContextMenuItemSettings> Items { get; set; } =
    [
        new ContextMenuItemSettings
        {
            Text = "Menu Item",
            EventName = "context_menu_clicked"
        }
    ];
}

public sealed class ContextMenuItemSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text { get; set; } = "Menu Item";

    public bool Enabled { get; set; } = true;

    public string EventName { get; set; } = "context_menu_clicked";
}

public sealed class SpriteSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Sprite";

    public string Path { get; set; } = string.Empty;

    public string LayoutMode { get; set; } = "absolute";

    public string TargetEntity { get; set; } = string.Empty;

    public float X { get; set; } = 0.0f;

    public float Y { get; set; } = 0.0f;

    public float Width { get; set; } = 128.0f;

    public float Height { get; set; } = 128.0f;

    public float RotationDegrees { get; set; }

    public float Opacity { get; set; } = 1.0f;

    public bool Visible { get; set; } = true;

    public int DrawOrder { get; set; } = 500;
}

public sealed class GuiControlStyleSettings
{
    public Vector4Dto BackgroundColor { get; set; } = new(0.10f, 0.31f, 0.58f, 0.88f);

    public Vector4Dto HoverColor { get; set; } = new(0.14f, 0.40f, 0.74f, 0.95f);

    public Vector4Dto ActiveColor { get; set; } = new(0.08f, 0.24f, 0.46f, 1.0f);

    public Vector4Dto TextColor { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);

    public Vector4Dto BorderColor { get; set; } = new(0.54f, 0.77f, 1.0f, 0.95f);

    public float BorderThickness { get; set; } = 1.5f;

    public float Rounding { get; set; } = 6.0f;

    public float FontSize { get; set; } = 18.0f;

    public string HorizontalAlignment { get; set; } = "center";

    public string VerticalAlignment { get; set; } = "middle";
}

public sealed class PmxRelationSettings
{
    public bool Enabled { get; set; }

    public string RelationEntity { get; set; } = string.Empty;

    public bool BindComponentTransform { get; set; } = true;

    public bool BindLighting { get; set; }
}

public sealed class CollisionSettings
{
    public bool Enabled { get; set; }

    public string Shape { get; set; } = "capsule";

    public Vector3Dto Center { get; set; } = new(0.0f, 1.0f, 0.0f);

    public float Radius { get; set; } = 0.5f;

    public float Height { get; set; } = 2.0f;

    public string Axis { get; set; } = "y";
}

public sealed class ColliderSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Collider";

    public bool Enabled { get; set; } = true;

    public string Shape { get; set; } = "capsule";

    public string BoundBoneName { get; set; } = string.Empty;

    public Vector3Dto Position { get; set; } = new(0.0f, 1.0f, 0.0f);

    public Vector3Dto RotationDegrees { get; set; } = Vector3Dto.Zero;

    public Vector3Dto Size { get; set; } = Vector3Dto.One;

    public float Radius { get; set; } = 0.5f;

    public float Height { get; set; } = 2.0f;

    public string Axis { get; set; } = "y";

    public bool Walkable { get; set; }

    public float MaxSlopeDegrees { get; set; } = 55.0f;
}

public sealed class MotionLayerSettings
{
    public string Path { get; set; } = string.Empty;

    public float Weight { get; set; } = 1.0f;

    public bool ResetPhysicsOnLoop { get; set; } = true;
}

public sealed class WaterSurfaceSettings
{
    public float Size { get; set; } = 20.0f;

    public float Alpha { get; set; } = 0.55f;

    public float AnimationSpeed { get; set; } = 0.03f;

    public float NormalTiling { get; set; } = 100.0f;

    public bool GerstnerWavesEnabled { get; set; } = true;

    public int GerstnerMeshResolution { get; set; } = 96;

    public int GerstnerWaveCount { get; set; } = 4;

    public float GerstnerAmplitude { get; set; } = 0.18f;

    public float GerstnerWavelength { get; set; } = 8.0f;

    public float GerstnerSpeed { get; set; } = 1.1f;

    public float GerstnerSteepness { get; set; } = 0.45f;

    public float GerstnerDirectionDegrees { get; set; } = 35.0f;

    public Vector3Dto DeepColor { get; set; } = new(0.02f, 0.10f, 0.22f);

    public Vector3Dto ReflectionTint { get; set; } = new(0.56f, 0.70f, 0.90f);

    public float SkyReflectionStrength { get; set; } = 0.85f;

    public bool MirrorReflectionEnabled { get; set; } = true;

    public bool UnderwaterEffectEnabled { get; set; } = true;

    public Vector3Dto UnderwaterTint { get; set; } = new(0.58f, 0.88f, 0.95f);

    public Vector3Dto UnderwaterFogColor { get; set; } = new(0.02f, 0.20f, 0.28f);

    public float UnderwaterFogDensity { get; set; } = 0.75f;

    public float UnderwaterVisibilityDistance { get; set; } = 18.0f;

    public float UnderwaterDistortionStrength { get; set; } = 0.010f;

    public float UnderwaterCausticsStrength { get; set; } = 0.28f;

    public float UnderwaterBubbleStrength { get; set; } = 0.18f;

    public bool EnableInteraction { get; set; }

    public float InteractionRadius { get; set; } = 0.8f;

    public float InteractionStrength { get; set; } = 0.8f;

    public float ParticleRippleMinIntervalSeconds { get; set; } = 0.12f;

    public float ParticleRippleMergeDistance { get; set; } = 0.6f;

    public float RippleLifetimeSeconds { get; set; } = 2.8f;

    public float RippleWaveSpeed { get; set; } = 12.0f;

    public float RippleFrequency { get; set; } = 16.0f;

    public float RippleNormalStrength { get; set; } = 0.65f;
}

public sealed class TexturedPlaneSettings
{
    public string TexturePath { get; set; } = string.Empty;

    public float Width { get; set; } = 2.0f;

    public float Height { get; set; } = 2.0f;

    public bool Billboard { get; set; }

    public float Opacity { get; set; } = 1.0f;

    public Vector4Dto Tint { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);

    public bool ReceiveShadow { get; set; } = true;

    public bool MirrorReflectionEnabled { get; set; }

    public float MirrorReflectionStrength { get; set; } = 1.0f;
}

public sealed class ParticleEntitySettings
{
    public string Preset { get; set; } = "sakura";

    public bool CastShadows { get; set; }

    public bool EnableWaterInteraction { get; set; }

    public bool KillOnWaterContact { get; set; }

    /// <summary>粒子与场景 Collider 的碰撞开关。默认关闭以保持旧工程行为。</summary>
    public bool EnableColliderCollision { get; set; }

    /// <summary>碰撞后沿法线反射的速度比例。</summary>
    public float CollisionBounce { get; set; } = 0.25f;

    /// <summary>碰撞后切向速度保留比例。</summary>
    public float CollisionDamping { get; set; } = 0.85f;

    /// <summary>粒子碰撞后是否立即销毁。</summary>
    public bool KillOnColliderContact { get; set; }

    public int ParticleCount { get; set; } = 420;

    public Vector3Dto SpawnBoxHalfExtents { get; set; } = new(20.0f, 4.0f, 20.0f);

    public Vector3Dto BaseVelocity { get; set; } = new(-0.25f, -1.15f, 0.1f);

    public Vector3Dto VelocityJitter { get; set; } = new(0.75f, 0.4f, 0.75f);

    public Vector3Dto Acceleration { get; set; } = new(0.0f, -0.1f, 0.0f);

    public float MinLifetime { get; set; } = 6.0f;

    public float MaxLifetime { get; set; } = 10.0f;

    public float MinSize { get; set; } = 0.16f;

    public float MaxSize { get; set; } = 0.34f;

    public float StartSizeScale { get; set; } = 1.0f;

    public float EndSizeScale { get; set; } = 1.0f;

    public float WidthScale { get; set; } = 1.0f;

    public float HeightScale { get; set; } = 1.0f;

    public float MinRotationSpeedRadians { get; set; } = -2.5f;

    public float MaxRotationSpeedRadians { get; set; } = 2.5f;

    public Vector4Dto StartColor { get; set; } = new(1.0f, 0.88f, 0.94f, 0.88f);

    public Vector4Dto EndColor { get; set; } = new(1.0f, 0.72f, 0.84f, 0.22f);

    public bool RandomizeInitialAge { get; set; } = true;

    public string BlendMode { get; set; } = "alpha";

    public string OrientationMode { get; set; } = "billboard";

    public string TexturePreset { get; set; } = "softCircle";

    public string? TexturePath { get; set; }

    public bool UseTextureColor { get; set; } = true;

    public bool PreventDarkening { get; set; }

    public float SimulationSpeed { get; set; } = 1.0f;

    public float Opacity { get; set; } = 1.0f;
}

public sealed class TransformSettings
{
    public Vector3Dto Position { get; set; } = Vector3Dto.Zero;

    public Vector3Dto RotationDegrees { get; set; } = Vector3Dto.Zero;

    public Vector3Dto Scale { get; set; } = Vector3Dto.One;
}

public sealed class ScriptBinding
{
    public string Language { get; set; } = "csharp";

    public string Path { get; set; } = "scripts/main.csx";

    public bool Enabled { get; set; } = true;
}

public sealed class AudioAsset
{
    public string Name { get; set; } = "Audio";

    public string Path { get; set; } = string.Empty;

    public bool Loop { get; set; } = true;

    public float Volume { get; set; } = 0.8f;

    public bool PlayOnStart { get; set; }
}

public sealed class MotionAsset
{
    public string Name { get; set; } = "Motion";

    public string Path { get; set; } = string.Empty;
}

public readonly record struct Vector3Dto(float X, float Y, float Z)
{
    [JsonIgnore]
    public static Vector3Dto Zero => new(0.0f, 0.0f, 0.0f);

    [JsonIgnore]
    public static Vector3Dto One => new(1.0f, 1.0f, 1.0f);

    public Vector3 ToVector3() => new(X, Y, Z);

    public static Vector3Dto FromVector3(Vector3 value) => new(value.X, value.Y, value.Z);
}

public readonly record struct Vector4Dto(float X, float Y, float Z, float W)
{
    public Vector4 ToVector4() => new(X, Y, Z, W);

    public static Vector4Dto FromVector4(Vector4 value) => new(value.X, value.Y, value.Z, value.W);
}
