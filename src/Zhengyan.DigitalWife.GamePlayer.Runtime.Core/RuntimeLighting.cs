using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed class RuntimeLighting
{
    internal RuntimeLighting(LightingSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public LightingSettings Settings { get; }

    public Vector3 DirectionalColor
    {
        get => Settings.LightColor.ToVector3();
        set => Settings.LightColor = Vector3Dto.FromVector3(ValidateColor(value));
    }

    public Vector3 DirectionalDirection
    {
        get => Settings.LightDirection.ToVector3();
        set
        {
            ValidateFinite(value);
            if (value.LengthSquared() < 1e-8f) throw new ArgumentOutOfRangeException(nameof(value));
            Settings.LightDirection = Vector3Dto.FromVector3(Vector3.Normalize(value));
        }
    }

    public Vector3 AmbientColor
    {
        get => Settings.AmbientColor.ToVector3();
        set => Settings.AmbientColor = Vector3Dto.FromVector3(ValidateColor(value));
    }

    public float AmbientStrength
    {
        get => Settings.AmbientStrength;
        set
        {
            if (!float.IsFinite(value) || value < 0.0f) throw new ArgumentOutOfRangeException(nameof(value));
            Settings.AmbientStrength = value;
        }
    }

    public string VmdPath => Settings.Vmd.Path;
    public bool VmdIsPlaying => Settings.Vmd.IsPlaying;
    public float VmdFrame => Settings.Vmd.Frame;

    public void SetVmd(string path, bool loop = true, float playbackSpeed = 1.0f, bool play = true)
    {
        Settings.Vmd.Path = path ?? string.Empty;
        Settings.Vmd.Loop = loop;
        Settings.Vmd.PlaybackSpeed = Math.Max(playbackSpeed, 0.0f);
        Settings.Vmd.Frame = 0.0f;
        Settings.Vmd.IsPlaying = play;
    }

    public void PlayVmd(bool restart = false)
    {
        if (restart) Settings.Vmd.Frame = 0.0f;
        Settings.Vmd.IsPlaying = true;
    }

    public void PauseVmd() => Settings.Vmd.IsPlaying = false;

    public void SeekVmd(float frame) => Settings.Vmd.Frame = Math.Max(frame, 0.0f);

    private static Vector3 ValidateColor(Vector3 value)
    {
        ValidateFinite(value);
        return Vector3.Max(value, Vector3.Zero);
    }

    private static void ValidateFinite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}
