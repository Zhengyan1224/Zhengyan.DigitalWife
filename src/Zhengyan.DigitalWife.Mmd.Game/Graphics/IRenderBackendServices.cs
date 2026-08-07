using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public readonly record struct RenderBackendCapabilities(
    bool UsesLegacyOpenGlCompatibility,
    bool SupportsSpirv,
    bool SupportsCompute,
    bool SupportsAsynchronousReadback);

/// <summary>
/// Creates backend-specific render passes without exposing the concrete renderer to scene code.
/// Adding a backend should be implemented here instead of adding renderer type checks to components.
/// </summary>
public interface IRenderBackendServices
{
    RenderBackendCapabilities Capabilities { get; }

    IUnderwaterPostProcessRenderer CreateUnderwaterPostProcessRenderer(string name);

    ILineRenderer CreateLineRenderer(int initialCapacityBytes = 4096);

    IImGuiBackendController CreateImGuiController(Game game, Action? configureFonts = null);

    ISkyboxPassRenderer? CreateSkyboxPassRenderer();

    IWaterPassRenderer? CreateWaterPassRenderer(uint vertexCapacityBytes, ReadOnlySpan<uint> indices);

    IParticlePassRenderer? CreateParticlePassRenderer(uint initialCapacityBytes);

    ILoadingScreenPassRenderer? CreateLoadingScreenPassRenderer();

    ITexturedPlanePassRenderer? CreateTexturedPlanePassRenderer(IGpuBuffer vertexBuffer, ITexture2D fallbackTexture);

    IShadowMapTarget CreateShadowMapTarget(string name);
}

public interface ISkyboxPassRenderer : IDisposable
{
    void Draw(ITexture2D texture, Matrix4x4 inverseViewProjection, Vector3 tint, float exposure);
}

public interface IParticlePassRenderer : IDisposable
{
    void Draw<T>(ReadOnlySpan<T> vertices, int vertexCount, ITexture2D fallbackTexture,
        RuntimeTextureHandle? runtimeTexture, Matrix4x4 viewProjection, float opacity,
        Vector4 startColor, Vector4 endColor, bool useTextureColor, bool additive) where T : unmanaged;
}

public interface IWaterPassRenderer : IDisposable
{
    void Draw<T>(ReadOnlySpan<T> vertices, uint indexCount, ITexture2D normalA, ITexture2D normalB,
        ITexture2D sky, RuntimeTextureHandle? reflection, ReadOnlySpan<Vector4> ripples,
        Matrix4x4 world, Matrix4x4 view, Matrix4x4 projection,
        Matrix4x4 reflectionViewProjection, Vector3 eye, Vector3 deepColor, Vector3 reflectionTint,
        float time, float textureLerp, float alpha, float normalTiling, float skyStrength, bool mirrorEnabled)
        where T : unmanaged;
}

public interface ILoadingScreenPassRenderer : IDisposable
{
    void DrawRect(Vector4 clipRect, Vector4 color, ITexture2D? texture = null, float opacity = 1.0f);
}

public interface ITexturedPlanePassRenderer : IDisposable
{
    void SetCustomShaders(string vertexSpirvPath, string fragmentSpirvPath);
    void ClearCustomShaders();
    void Draw(
        ITexture2D baseTexture,
        RuntimeTextureHandle? runtimeBaseTexture,
        Vector4 tint,
        bool flipV,
        Matrix4x4 world,
        Matrix4x4 view,
        Matrix4x4 projection,
        bool receiveShadow,
        ShadowMapBinding? shadowMap,
        RuntimeTextureHandle? reflectionTexture,
        Matrix4x4 reflectionViewProjection,
        float reflectionStrength);
}

public interface ILineRenderer : IDisposable
{
    void Draw(ReadOnlySpan<float> vertices, int vertexCount, Matrix4x4 worldViewProjection, bool depth = false);
}
