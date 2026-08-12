using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeLighting
{
    private readonly LightingSettings _settings;
    private readonly Action _changed;

    internal RuntimeLighting(LightingSettings settings, Action changed)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public Vector3 DirectionalColor
    {
        get => _settings.LightColor.ToVector3();
        set
        {
            _settings.LightColor = Vector3Dto.FromVector3(ValidateColor(value, nameof(value)));
            _changed();
        }
    }

    public Vector3 DirectionalDirection
    {
        get => _settings.LightDirection.ToVector3();
        set
        {
            ValidateFinite(value, nameof(value));
            if (value.LengthSquared() <= 1e-12f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Directional-light direction must not be zero.");
            }

            _settings.LightDirection = Vector3Dto.FromVector3(Vector3.Normalize(value));
            _changed();
        }
    }

    public Vector3 AmbientColor
    {
        get => _settings.AmbientColor.ToVector3();
        set
        {
            _settings.AmbientColor = Vector3Dto.FromVector3(ValidateColor(value, nameof(value)));
            _changed();
        }
    }

    public float AmbientStrength
    {
        get => _settings.AmbientStrength;
        set
        {
            if (!float.IsFinite(value) || value < 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Ambient strength must be finite and non-negative.");
            }

            _settings.AmbientStrength = value;
            _changed();
        }
    }

    public Vector3 LightColor
    {
        get => DirectionalColor;
        set => DirectionalColor = value;
    }

    public Vector3 LightDirection
    {
        get => DirectionalDirection;
        set => DirectionalDirection = value;
    }

    public void SetDirectionalColor(float red, float green, float blue)
    {
        DirectionalColor = new Vector3(red, green, blue);
    }

    public void SetDirectionalDirection(float x, float y, float z)
    {
        DirectionalDirection = new Vector3(x, y, z);
    }

    public void SetAmbientColor(float red, float green, float blue)
    {
        AmbientColor = new Vector3(red, green, blue);
    }

    public string VmdPath => _settings.Vmd.Path;

    public bool VmdIsPlaying => _settings.Vmd.IsPlaying;

    public float VmdFrame => _settings.Vmd.Frame;

    public void SetVmd(string path, bool loop = true, float playbackSpeed = 1.0f, bool play = true)
    {
        _settings.Vmd.Path = path ?? string.Empty;
        _settings.Vmd.Loop = loop;
        _settings.Vmd.PlaybackSpeed = Math.Max(0.0f, playbackSpeed);
        _settings.Vmd.Frame = 0.0f;
        _settings.Vmd.IsPlaying = play;
    }

    public void PlayVmd(bool restart = false)
    {
        if (restart) _settings.Vmd.Frame = 0.0f;
        _settings.Vmd.IsPlaying = true;
    }

    public void PauseVmd() => _settings.Vmd.IsPlaying = false;

    public void SeekVmd(float frame) => _settings.Vmd.Frame = Math.Max(0.0f, frame);

    public void SetVmdLoop(bool loop) => _settings.Vmd.Loop = loop;

    public void SetVmdPlaybackSpeed(float playbackSpeed) => _settings.Vmd.PlaybackSpeed = Math.Max(0.0f, playbackSpeed);

    public void ClearVmd()
    {
        _settings.Vmd.Path = string.Empty;
        _settings.Vmd.IsPlaying = false;
        _settings.Vmd.Frame = 0.0f;
    }

    private static Vector3 ValidateColor(Vector3 color, string parameterName)
    {
        ValidateFinite(color, parameterName);
        return Vector3.Max(color, Vector3.Zero);
    }

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Vector components must be finite.");
        }
    }
}
