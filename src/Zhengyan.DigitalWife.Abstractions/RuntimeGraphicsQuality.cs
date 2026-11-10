namespace Zhengyan.DigitalWife.Scripting;

public readonly record struct RuntimeGraphicsSnapshot(long Revision, int SceneSamples, bool OverrideSceneSamples,
    bool ShadowsEnabled, int DirectionalResolution, int LocalLightResolution, int FilterSamples, float FilterRadius);

/// <summary>Project-lifetime script requests. Renderers consume one snapshot at a frame boundary.</summary>
public sealed class RuntimeGraphicsQuality
{
    private readonly object _gate = new();
    private RuntimeGraphicsSnapshot _requested;
    private long _appliedRevision = -1;
    private int _actualSceneSamples = 1, _actualDirectionalResolution, _actualLocalResolution;

    public RuntimeGraphicsQuality(int sceneSamples = 4, bool shadowsEnabled = false,
        int directionalResolution = 2048, int localLightResolution = 1024, int filterSamples = 9, float filterRadius = 1)
    {
        _requested = new(0, NormalizeSamples(sceneSamples), false, shadowsEnabled,
            NormalizeResolution(directionalResolution,8192), NormalizeResolution(localLightResolution,2048),
            NormalizeFilterSamples(filterSamples), NormalizeRadius(filterRadius));
        Shadows = new(this);
    }

    public RuntimeShadowQuality Shadows { get; }
    public int SceneSamples
    {
        get { lock(_gate) return _requested.SceneSamples; }
        set
        {
            value = NormalizeSamples(value);
            lock(_gate)
            {
                if (_requested.OverrideSceneSamples && _requested.SceneSamples == value) return;
                _requested = _requested with { Revision = _requested.Revision + 1, SceneSamples = value, OverrideSceneSamples = true };
            }
        }
    }
    public int RequestedSceneSamples => SceneSamples;
    public int ActualSceneSamples { get { lock(_gate) return _actualSceneSamples; } }
    public bool Pending { get { lock(_gate) return _appliedRevision != _requested.Revision; } }
    public RuntimeGraphicsSnapshot Capture() { lock(_gate) return _requested; }

    // The public renderer reporting API is deliberately separate from script setters.
    public void ReportApplied(long revision, int actualSceneSamples, int directionalResolution, int localLightResolution)
    {
        lock(_gate)
        {
            if (revision < _appliedRevision) return;
            _appliedRevision = revision;
            _actualSceneSamples = Math.Max(1,actualSceneSamples);
            _actualDirectionalResolution = Math.Max(0,directionalResolution);
            _actualLocalResolution = Math.Max(0,localLightResolution);
        }
    }
    internal int ActualDirectionalResolution { get { lock(_gate) return _actualDirectionalResolution; } }
    internal int ActualLocalResolution { get { lock(_gate) return _actualLocalResolution; } }
    internal void SetShadows(bool? enabled = null, int? directional = null, int? local = null, int? samples = null, float? radius = null)
    {
        lock(_gate)
        {
            RuntimeGraphicsSnapshot next = _requested with {
                ShadowsEnabled = enabled ?? true,
                DirectionalResolution = directional is { } d ? NormalizeResolution(d,8192) : _requested.DirectionalResolution,
                LocalLightResolution = local is { } l ? NormalizeResolution(l,2048) : _requested.LocalLightResolution,
                FilterSamples = samples is { } s ? NormalizeFilterSamples(s) : _requested.FilterSamples,
                FilterRadius = radius is { } r ? NormalizeRadius(r) : _requested.FilterRadius
            };
            if (next == _requested) return;
            _requested = next with { Revision = _requested.Revision + 1 };
        }
    }
    public static int NormalizeSamples(int value) => value <= 1 ? 1 : value <= 2 ? 2 : value <= 4 ? 4 : value <= 8 ? 8 : 16;
    public static int NormalizeResolution(int value,int maximum)
    {
        int result=256;
        for(int candidate=512;candidate<=8192 && candidate<=value && candidate<=maximum;candidate*=2) result=candidate;
        return result;
    }
    public static int NormalizeFilterSamples(int value) => value >= 49 ? 49 : value >= 25 ? 25 : value >= 16 ? 16 : value >= 9 ? 9 : value >= 4 ? 4 : 1;
    public static float NormalizeRadius(float value) => float.IsFinite(value) ? Math.Clamp(value,0,8) : 1;
}

public sealed class RuntimeShadowQuality
{
    private readonly RuntimeGraphicsQuality _owner;
    internal RuntimeShadowQuality(RuntimeGraphicsQuality owner) => _owner=owner;
    public bool UseGlobalSettings { get => _owner.Capture().ShadowsEnabled; set => _owner.SetShadows(enabled:value); }
    public int DirectionalResolution { get => _owner.Capture().DirectionalResolution; set => _owner.SetShadows(directional:value); }
    public int LocalLightResolution { get => _owner.Capture().LocalLightResolution; set => _owner.SetShadows(local:value); }
    public int FilterSamples { get => _owner.Capture().FilterSamples; set => _owner.SetShadows(samples:value); }
    public float FilterRadius { get => _owner.Capture().FilterRadius; set => _owner.SetShadows(radius:value); }
    public int ActualDirectionalResolution => _owner.ActualDirectionalResolution;
    public int ActualLocalLightResolution => _owner.ActualLocalResolution;
}
