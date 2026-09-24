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
    public static AndroidRenderPolicy Resolve(AndroidQualitySettings quality, int requestedSamples)
    {
        string profile = (quality.Profile ?? "auto").Trim().ToLowerInvariant();
        bool low = profile == "low";
        bool medium = profile == "medium";
        int samples = requestedSamples <= 1 ? 1 : requestedSamples <= 2 ? 2
            : requestedSamples <= 4 ? 4 : requestedSamples <= 8 ? 8 : 16;
        return new AndroidRenderPolicy(
            Math.Clamp(quality.MaxShadowMapSize, 256, low ? 512 : medium ? 1024 : 2048),
            Math.Clamp(quality.MaxLocalShadowMapSize, 256, low ? 256 : medium ? 512 : 2048),
            Math.Clamp(quality.MaxPointShadowMaps, 0, low ? 1 : 2),
            Math.Clamp(quality.MaxSpotShadowMaps, 0, low ? 1 : 4),
            Math.Clamp(quality.MaxReflectionSurfaces, 0, low ? 1 : medium ? 2 : 16),
            Math.Clamp(quality.MaxParticleCount, 1, low ? 600 : medium ? 1200 : 100000),
            low || profile == "auto" ? 1 : medium ? Math.Min(samples, 2) : samples);
    }
}
