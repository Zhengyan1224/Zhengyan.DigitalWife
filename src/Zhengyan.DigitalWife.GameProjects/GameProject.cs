using System.Numerics;
using System.Text.Json.Serialization;

namespace Zhengyan.DigitalWife.GameProjects;

public sealed class GameProject
{
    public string Name { get; set; } = "Untitled Game";

    public string Version { get; set; } = "0.1.0";

    public string DefaultScene { get; set; } = "scenes/main.scene.json";

    public List<string> Scenes { get; set; } = ["scenes/main.scene.json"];

    public GameProjectScriptRuntime ScriptRuntime { get; set; } = new();

    public GameProjectVoiceSettings Voice { get; set; } = new();

    public GameWindowSettings Window { get; set; } = new();

    [JsonIgnore]
    public GameProjectScene Scene { get; set; } = new();
}

public sealed class GameProjectScriptRuntime
{
    public string PreferredLanguage { get; set; } = "csharp";

    public List<string> ScriptSearchPaths { get; set; } = ["scripts"];
}

public sealed class GameProjectVoiceSettings
{
    public bool Enabled { get; set; }

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

public sealed class GameWindowSettings
{
    public string Title { get; set; } = "Demo Game";

    public string IconPath { get; set; } = string.Empty;

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public bool Fullscreen { get; set; }

    public bool Resizable { get; set; } = true;

    public string TimingMode { get; set; } = "time_synchronized";
}

public sealed class GameProjectLipSyncSettings
{
    public bool Enabled { get; set; } = true;

    public string DictionaryDirectory { get; set; } = "Resources/SpeechLipSyncDictionaries";

    public string DictionaryLanguage { get; set; } = "Chinese";

    public float MinFramePeriodMilliseconds { get; set; } = 70.0f;

    public float MaxFramePeriodMilliseconds { get; set; } = 320.0f;

    public Dictionary<string, string> VowelMorphMap { get; set; } = new()
    {
        ["あ"] = "あ",
        ["い"] = "い",
        ["う"] = "う",
        ["え"] = "え",
        ["お"] = "お"
    };
}

public sealed class GameProjectScene
{
    public string Name { get; set; } = "Main Scene";

    public CameraSettings Camera { get; set; } = new();

    public LightingSettings Lighting { get; set; } = new();

    public LoadingScreenSettings LoadingScreen { get; set; } = new();

    public List<ScriptBinding> LoadingScripts { get; set; } = [];

    public List<GuiControlSettings> GuiControls { get; set; } = [];

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
}

public sealed class CameraSettings
{
    public Vector3Dto Position { get; set; } = new(0.0f, 2.0f, 8.0f);

    public Vector3Dto Target { get; set; } = new(0.0f, 1.2f, 0.0f);

    public string ProjectionMode { get; set; } = "perspective";

    public float Fov { get; set; } = 45.0f;

    public float OrthographicSize { get; set; } = 5.0f;

    public float NearClipPlane { get; set; } = 0.1f;

    public float FarClipPlane { get; set; } = 1000.0f;
}

public sealed class LightingSettings
{
    public Vector3Dto LightColor { get; set; } = new(1.0f, 1.0f, 1.0f);

    public Vector3Dto LightDirection { get; set; } = new(-0.5f, -1.0f, -0.5f);

    public Vector3Dto AmbientColor { get; set; } = new(0.65f, 0.65f, 0.65f);

    public float AmbientStrength { get; set; } = 0.25f;

    public Vector4Dto ShadowColor { get; set; } = new(0.17f, 0.17f, 0.17f, 0.7f);

    public Vector4Dto ClearColor { get; set; } = new(0.08f, 0.09f, 0.12f, 1.0f);
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

    public bool DrawShadowInMainPass { get; set; }

    public float PlaybackSpeed { get; set; } = 1.0f;

    public bool LoopMotion { get; set; } = true;

    public bool ResetPhysicsOnMotionLoop { get; set; } = true;

    public ParticleEntitySettings Particle { get; set; } = new();

    public WaterSurfaceSettings Water { get; set; } = new();

    public PmxRelationSettings Relation { get; set; } = new();

    public List<MotionLayerSettings> MotionLayers { get; set; } = [];

    public List<ScriptBinding> Scripts { get; set; } = [];
}

public sealed class GuiControlSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Button";

    public string Type { get; set; } = "button";

    public string Text { get; set; } = "Button";

    public float X { get; set; } = 24.0f;

    public float Y { get; set; } = 24.0f;

    public float Width { get; set; } = 160.0f;

    public float Height { get; set; } = 36.0f;

    public bool Visible { get; set; } = true;

    public string TargetEntity { get; set; } = string.Empty;

    public string EventName { get; set; } = "clicked";

    public bool WordWrap { get; set; } = true;

    public bool Checked { get; set; }

    public List<string> Items { get; set; } = [];

    public int SelectedIndex { get; set; }

    public GuiControlStyleSettings Style { get; set; } = new();
}

public sealed class SpriteSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Sprite";

    public string Path { get; set; } = string.Empty;

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

    public Vector3Dto DeepColor { get; set; } = new(0.02f, 0.10f, 0.22f);

    public Vector3Dto ReflectionTint { get; set; } = new(0.56f, 0.70f, 0.90f);

    public float SkyReflectionStrength { get; set; } = 0.85f;
}

public sealed class ParticleEntitySettings
{
    public string Preset { get; set; } = "sakura";

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
