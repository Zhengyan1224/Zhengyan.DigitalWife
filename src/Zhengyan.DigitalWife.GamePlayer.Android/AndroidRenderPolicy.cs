using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>Shared, non-destructive quality limits for the Android render hosts.</summary>
internal sealed record AndroidRenderPolicy(
    int ShadowMapSize,
    int LocalShadowMapSize,
    int PointShadowCount,
    int SpotShadowCount,
    int ReflectionCount,
    int ParticleCount,
    int Samples)
{
    private const float PmxEdgeWidthMultiplier = 2.0f;

    // PMX edge sizes are desktop pixels. Scale them like relative GUI fonts so
    // a high-resolution phone preserves their width in the project's layout.
    // The additional multiplier is calibrated from phone/desktop visual comparison.
    public static float ResolvePmxEdgeWidthScale(int width, int height, int referenceWidth, int referenceHeight)
        => PmxEdgeWidthMultiplier * Math.Min(Math.Max(width, 1) / (float)Math.Max(referenceWidth, 1),
            Math.Max(height, 1) / (float)Math.Max(referenceHeight, 1));

    // Default/high profiles keep the PC renderer's native viewport resolution.
    // Explicit reduced-quality profiles retain their smaller targets.
    public static (int Width, int Height) ResolveReflectionSize(int width, int height, string? profile)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        string mode = (profile ?? "auto").Trim().ToLowerInvariant();
        if (mode is not ("low" or "medium")) return (width, height);
        int limit = mode == "low" ? 512 : 1024;
        float scale = Math.Min(1f, limit / (float)Math.Max(width, height));
        return (Math.Max(1, (int)MathF.Round(width * scale)), Math.Max(1, (int)MathF.Round(height * scale)));
    }

    public static AndroidRenderPolicy Resolve(AndroidQualitySettings quality, int requestedSamples, GameProjectShadowSettings? shadows = null)
    {
        string profile = (quality.Profile ?? "auto").Trim().ToLowerInvariant();
        bool low = profile == "low";
        bool medium = profile == "medium";
        int samples = requestedSamples <= 1 ? 1 : requestedSamples <= 2 ? 2
            : requestedSamples <= 4 ? 4 : requestedSamples <= 8 ? 8 : 16;
        return new AndroidRenderPolicy(
            Math.Clamp(shadows?.NormalizedDirectionalResolution ?? quality.MaxShadowMapSize, 256,
                low ? 512 : medium ? 1024 : shadows is null ? 2048 : 8192),
            Math.Clamp(shadows?.NormalizedLocalLightResolution ?? quality.MaxLocalShadowMapSize, 256,
                low ? 256 : medium ? 512 : 2048),
            Math.Clamp(quality.MaxPointShadowMaps, 0, low ? 1 : 2),
            Math.Clamp(quality.MaxSpotShadowMaps, 0, low ? 1 : 4),
            Math.Clamp(quality.MaxReflectionSurfaces, 0, low ? 1 : medium ? 2 : 16),
            Math.Clamp(quality.MaxParticleCount, 1, low ? 600 : medium ? 1200 : 100000),
            low ? 1 : medium ? Math.Min(samples, 2) : samples);
    }
}
