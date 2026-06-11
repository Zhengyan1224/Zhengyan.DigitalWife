using System.Numerics;
using System.Text.Json;

namespace Zhengyan.DigitalWife.Mmd.Game.Components;

public static class ParticleSystemPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void Save(string filePath, ParticleSystemSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(settings);

        settings.Validate();
        ParticlePresetDocument document = ParticlePresetDocument.FromSettings(settings);
        string fullPath = Path.GetFullPath(filePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(fullPath, json);
    }

    public static ParticleSystemSettings Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string fullPath = Path.GetFullPath(filePath);
        string json = File.ReadAllText(fullPath);
        ParticlePresetDocument? document = JsonSerializer.Deserialize<ParticlePresetDocument>(json, JsonOptions);
        if (document is null)
        {
            throw new InvalidDataException($"Particle preset is invalid: {fullPath}");
        }

        ParticleSystemSettings settings = document.ToSettings();
        settings.Validate();
        return settings;
    }

    private sealed class ParticlePresetDocument
    {
        public int Version { get; set; } = 1;

        public string Name { get; set; } = "Particles";

        public int ParticleCount { get; set; } = 512;

        public float[] SpawnBoxHalfExtents { get; set; } = [8.0f, 4.0f, 8.0f];

        public float[] BaseVelocity { get; set; } = [0.0f, -2.0f, 0.0f];

        public float[] VelocityJitter { get; set; } = [1.0f, 0.5f, 1.0f];

        public float[] Acceleration { get; set; } = [0.0f, 0.0f, 0.0f];

        public float MinLifetime { get; set; } = 1.0f;

        public float MaxLifetime { get; set; } = 3.0f;

        public float MinSize { get; set; } = 0.1f;

        public float MaxSize { get; set; } = 0.5f;

        public float StartSizeScale { get; set; } = 1.0f;

        public float EndSizeScale { get; set; } = 1.0f;

        public float WidthScale { get; set; } = 1.0f;

        public float HeightScale { get; set; } = 1.0f;

        public float MinRotationSpeedRadians { get; set; } = -1.2f;

        public float MaxRotationSpeedRadians { get; set; } = 1.2f;

        public float[] StartColor { get; set; } = [1.0f, 1.0f, 1.0f, 1.0f];

        public float[] EndColor { get; set; } = [1.0f, 1.0f, 1.0f, 1.0f];

        public bool RandomizeInitialAge { get; set; } = true;

        public ParticleBlendMode BlendMode { get; set; } = ParticleBlendMode.Alpha;

        public ParticleOrientationMode OrientationMode { get; set; } = ParticleOrientationMode.Billboard;

        public ParticleTexturePreset TexturePreset { get; set; } = ParticleTexturePreset.SoftCircle;

        public string? TexturePath { get; set; }

        public bool UseTextureColor { get; set; } = true;

        public bool PreventDarkening { get; set; }

        public static ParticlePresetDocument FromSettings(ParticleSystemSettings settings)
        {
            return new ParticlePresetDocument
            {
                Name = settings.Name,
                ParticleCount = settings.ParticleCount,
                SpawnBoxHalfExtents = [settings.SpawnBoxHalfExtents.X, settings.SpawnBoxHalfExtents.Y, settings.SpawnBoxHalfExtents.Z],
                BaseVelocity = [settings.BaseVelocity.X, settings.BaseVelocity.Y, settings.BaseVelocity.Z],
                VelocityJitter = [settings.VelocityJitter.X, settings.VelocityJitter.Y, settings.VelocityJitter.Z],
                Acceleration = [settings.Acceleration.X, settings.Acceleration.Y, settings.Acceleration.Z],
                MinLifetime = settings.MinLifetime,
                MaxLifetime = settings.MaxLifetime,
                MinSize = settings.MinSize,
                MaxSize = settings.MaxSize,
                StartSizeScale = settings.StartSizeScale,
                EndSizeScale = settings.EndSizeScale,
                WidthScale = settings.WidthScale,
                HeightScale = settings.HeightScale,
                MinRotationSpeedRadians = settings.MinRotationSpeedRadians,
                MaxRotationSpeedRadians = settings.MaxRotationSpeedRadians,
                StartColor = [settings.StartColor.X, settings.StartColor.Y, settings.StartColor.Z, settings.StartColor.W],
                EndColor = [settings.EndColor.X, settings.EndColor.Y, settings.EndColor.Z, settings.EndColor.W],
                RandomizeInitialAge = settings.RandomizeInitialAge,
                BlendMode = settings.BlendMode,
                OrientationMode = settings.OrientationMode,
                TexturePreset = settings.TexturePreset,
                TexturePath = settings.TexturePath,
                UseTextureColor = settings.UseTextureColor,
                PreventDarkening = settings.PreventDarkening
            };
        }

        public ParticleSystemSettings ToSettings()
        {
            return new ParticleSystemSettings
            {
                Name = string.IsNullOrWhiteSpace(Name) ? "Particles" : Name,
                ParticleCount = ParticleCount,
                SpawnBoxHalfExtents = ToVector3(SpawnBoxHalfExtents, new Vector3(8.0f, 4.0f, 8.0f)),
                BaseVelocity = ToVector3(BaseVelocity, new Vector3(0.0f, -2.0f, 0.0f)),
                VelocityJitter = ToVector3(VelocityJitter, new Vector3(1.0f, 0.5f, 1.0f)),
                Acceleration = ToVector3(Acceleration, Vector3.Zero),
                MinLifetime = MinLifetime,
                MaxLifetime = MaxLifetime,
                MinSize = MinSize,
                MaxSize = MaxSize,
                StartSizeScale = StartSizeScale,
                EndSizeScale = EndSizeScale,
                WidthScale = WidthScale,
                HeightScale = HeightScale,
                MinRotationSpeedRadians = MinRotationSpeedRadians,
                MaxRotationSpeedRadians = MaxRotationSpeedRadians,
                StartColor = ToVector4(StartColor, Vector4.One),
                EndColor = ToVector4(EndColor, Vector4.One),
                RandomizeInitialAge = RandomizeInitialAge,
                BlendMode = BlendMode,
                OrientationMode = OrientationMode,
                TexturePreset = TexturePreset,
                TexturePath = string.IsNullOrWhiteSpace(TexturePath) ? null : TexturePath.Trim(),
                UseTextureColor = UseTextureColor,
                PreventDarkening = PreventDarkening
            };
        }
    }

    private static Vector3 ToVector3(float[]? values, Vector3 fallback)
    {
        if (values is not null && values.Length >= 3)
        {
            return new Vector3(values[0], values[1], values[2]);
        }

        return fallback;
    }

    private static Vector4 ToVector4(float[]? values, Vector4 fallback)
    {
        if (values is not null && values.Length >= 4)
        {
            return new Vector4(values[0], values[1], values[2], values[3]);
        }

        return fallback;
    }
}

