using Zhengyan.DigitalWife.Mmd.Game.Components;

namespace Zhengyan.DigitalWife.GameProjects;

public static class ParticleEntitySettingsMapper
{
    public static ParticleEntitySettings FromPreset(string preset)
    {
        ParticleSystemSettings settings = CreatePresetSettings(preset);
        ParticleEntitySettings result = FromSettings(settings);
        result.Preset = NormalizePreset(preset);
        return result;
    }

    public static ParticleEntitySettings FromSettings(ParticleSystemSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ParticleEntitySettings
        {
            ParticleCount = settings.ParticleCount,
            SpawnBoxHalfExtents = Vector3Dto.FromVector3(settings.SpawnBoxHalfExtents),
            BaseVelocity = Vector3Dto.FromVector3(settings.BaseVelocity),
            VelocityJitter = Vector3Dto.FromVector3(settings.VelocityJitter),
            Acceleration = Vector3Dto.FromVector3(settings.Acceleration),
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
            StartColor = Vector4Dto.FromVector4(settings.StartColor),
            EndColor = Vector4Dto.FromVector4(settings.EndColor),
            RandomizeInitialAge = settings.RandomizeInitialAge,
            BlendMode = ToWireName(settings.BlendMode),
            OrientationMode = ToWireName(settings.OrientationMode),
            TexturePreset = ToWireName(settings.TexturePreset),
            TexturePath = settings.TexturePath,
            UseTextureColor = settings.UseTextureColor,
            PreventDarkening = settings.PreventDarkening,
            SimulationSpeed = 1.0f,
            Opacity = 1.0f
        };
    }

    public static ParticleSystemSettings ToSettings(ParticleEntitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ParticleSystemSettings result = new()
        {
            Name = string.IsNullOrWhiteSpace(settings.Preset) ? "Particles" : settings.Preset,
            ParticleCount = settings.ParticleCount,
            SpawnBoxHalfExtents = settings.SpawnBoxHalfExtents.ToVector3(),
            BaseVelocity = settings.BaseVelocity.ToVector3(),
            VelocityJitter = settings.VelocityJitter.ToVector3(),
            Acceleration = settings.Acceleration.ToVector3(),
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
            StartColor = settings.StartColor.ToVector4(),
            EndColor = settings.EndColor.ToVector4(),
            RandomizeInitialAge = settings.RandomizeInitialAge,
            BlendMode = ParseBlendMode(settings.BlendMode),
            OrientationMode = ParseOrientationMode(settings.OrientationMode),
            TexturePreset = ParseTexturePreset(settings.TexturePreset),
            TexturePath = string.IsNullOrWhiteSpace(settings.TexturePath) ? null : settings.TexturePath.Trim(),
            UseTextureColor = settings.UseTextureColor,
            PreventDarkening = settings.PreventDarkening
        };
        result.Validate();
        return result;
    }

    public static string NormalizePreset(string preset)
    {
        return string.IsNullOrWhiteSpace(preset)
            ? "sakura"
            : preset.Trim().Replace(" ", string.Empty).ToLowerInvariant();
    }

    private static ParticleSystemSettings CreatePresetSettings(string preset)
    {
        return NormalizePreset(preset) switch
        {
            "rain" => ParticleSystemPresets.Rain(),
            "snow" => ParticleSystemPresets.Snow(),
            "cloud" => ParticleSystemPresets.Cloud(),
            "waterfall" => ParticleSystemPresets.Waterfall(),
            "stream" => ParticleSystemPresets.Stream(),
            "fire" => ParticleSystemPresets.Fire(),
            _ => ParticleSystemPresets.Sakura()
        };
    }

    private static ParticleBlendMode ParseBlendMode(string value)
    {
        return value.Trim().Equals("additive", StringComparison.OrdinalIgnoreCase)
            ? ParticleBlendMode.Additive
            : ParticleBlendMode.Alpha;
    }

    private static ParticleOrientationMode ParseOrientationMode(string value)
    {
        return value.Trim().Equals("velocityAligned", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("velocity_aligned", StringComparison.OrdinalIgnoreCase)
            ? ParticleOrientationMode.VelocityAligned
            : ParticleOrientationMode.Billboard;
    }

    private static ParticleTexturePreset ParseTexturePreset(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "streak" => ParticleTexturePreset.Streak,
            "flame" => ParticleTexturePreset.Flame,
            _ => ParticleTexturePreset.SoftCircle
        };
    }

    private static string ToWireName(ParticleBlendMode value)
    {
        return value == ParticleBlendMode.Additive ? "additive" : "alpha";
    }

    private static string ToWireName(ParticleOrientationMode value)
    {
        return value == ParticleOrientationMode.VelocityAligned ? "velocityAligned" : "billboard";
    }

    private static string ToWireName(ParticleTexturePreset value)
    {
        return value switch
        {
            ParticleTexturePreset.Streak => "streak",
            ParticleTexturePreset.Flame => "flame",
            _ => "softCircle"
        };
    }
}
