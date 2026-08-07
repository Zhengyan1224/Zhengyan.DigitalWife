namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal sealed class OpenGlRenderBackendServices(OpenGlRenderer renderer) : IRenderBackendServices
{
    public RenderBackendCapabilities Capabilities => new(true, false, false, false);

    public IUnderwaterPostProcessRenderer CreateUnderwaterPostProcessRenderer(string name)
        => new UnderwaterPostProcessRenderer(renderer.Gl, name);

    public ILineRenderer CreateLineRenderer(int initialCapacityBytes = 4096)
        => new OpenGlLineRenderer(renderer.Gl, initialCapacityBytes);

    public IImGuiBackendController CreateImGuiController(Game game, Action? configureFonts = null)
        => new OpenGlImGuiBackendController(game, renderer, configureFonts);

    public ISkyboxPassRenderer? CreateSkyboxPassRenderer() => null;
    public IWaterPassRenderer? CreateWaterPassRenderer(uint vertexCapacityBytes, ReadOnlySpan<uint> indices) => null;
    public IParticlePassRenderer? CreateParticlePassRenderer(uint initialCapacityBytes) => null;
    public ILoadingScreenPassRenderer? CreateLoadingScreenPassRenderer() => null;
    public ITexturedPlanePassRenderer? CreateTexturedPlanePassRenderer(IGpuBuffer vertexBuffer, ITexture2D fallbackTexture) => null;
    public IShadowMapTarget CreateShadowMapTarget(string name) => new OpenGlShadowMapTarget(renderer.Gl, name);
}

internal sealed class VulkanRenderBackendServices(VulkanRenderer renderer) : IRenderBackendServices
{
    public RenderBackendCapabilities Capabilities => new(false, true, true, true);

    public IUnderwaterPostProcessRenderer CreateUnderwaterPostProcessRenderer(string name)
        => new VeldridUnderwaterPostProcessRenderer(renderer, name);

    public ILineRenderer CreateLineRenderer(int initialCapacityBytes = 4096)
        => new VeldridLineRenderer(renderer, (uint)Math.Max(initialCapacityBytes, 1));

    public IImGuiBackendController CreateImGuiController(Game game, Action? configureFonts = null)
        => new VulkanImGuiBackendController(game, renderer, configureFonts);

    public ISkyboxPassRenderer CreateSkyboxPassRenderer() => new VeldridSkyboxRenderer(renderer);

    public IWaterPassRenderer CreateWaterPassRenderer(uint vertexCapacityBytes, ReadOnlySpan<uint> indices)
        => new VeldridWaterRenderer(renderer, vertexCapacityBytes, indices);

    public IParticlePassRenderer CreateParticlePassRenderer(uint initialCapacityBytes)
        => new VeldridParticleRenderer(renderer, initialCapacityBytes);

    public ILoadingScreenPassRenderer CreateLoadingScreenPassRenderer()
        => new VeldridLoadingScreenRenderer(renderer);

    public ITexturedPlanePassRenderer CreateTexturedPlanePassRenderer(IGpuBuffer vertexBuffer, ITexture2D fallbackTexture)
        => new Components.VeldridTexturedPlanePassRenderer(renderer, vertexBuffer, fallbackTexture);

    public IShadowMapTarget CreateShadowMapTarget(string name) => new VeldridShadowMapTarget(renderer, name);
}
