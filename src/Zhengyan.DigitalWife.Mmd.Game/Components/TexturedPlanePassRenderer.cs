using System.Numerics;
using System.Runtime.InteropServices;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Veldrid;
using Veldrid.SPIRV;
using VeldridSampler = Veldrid.Sampler;
using VeldridShader = Veldrid.Shader;
using EngineGraphicsBackend = Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend;

namespace Zhengyan.DigitalWife.Mmd.Game.Components;

internal sealed class VeldridTexturedPlanePassRenderer : IDisposable
{
    private readonly VulkanRenderer _renderer;
    private readonly IGpuBuffer _vertexBuffer;
    private readonly IGpuBuffer _uniformBuffer;
    private readonly ResourceLayout _layout;
    private readonly ResourceFactory _factory;
    private readonly ResourceSet _fallbackSet;
    private VeldridShader[] _shaders = [];
    private ShaderSetDescription _shaderSet;
    private readonly List<PipelineBundle> _pipelines = [];
    private readonly Dictionary<TextureSetKey, ResourceSet> _resourceSets = [];
    private readonly TextureView _fallbackTexture;
    private readonly VeldridSampler _fallbackSampler;
    private bool _disposed;

    public VeldridTexturedPlanePassRenderer(
        VulkanRenderer renderer,
        IGpuBuffer vertexBuffer,
        ITexture2D fallbackTexture)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _vertexBuffer = vertexBuffer ?? throw new ArgumentNullException(nameof(vertexBuffer));
        _factory = renderer.ResourceFactory;
        _uniformBuffer = new VeldridGpuBufferAdapter(renderer, Marshal.SizeOf<UniformData>());
        _fallbackTexture = RequireTextureView(fallbackTexture);
        _fallbackSampler = _factory.CreateSampler(SamplerDescription.Linear);

        _layout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("PlaneFrame", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PlaneBaseTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PlaneBaseSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PlaneShadowTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PlaneShadowSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PlaneReflectionTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PlaneReflectionSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _fallbackSet = CreateResourceSet(_fallbackTexture, _fallbackSampler, _fallbackTexture, _fallbackSampler, _fallbackTexture, _fallbackSampler);
        _resourceSets[new TextureSetKey(_fallbackTexture, _fallbackSampler, _fallbackTexture, _fallbackSampler, _fallbackTexture, _fallbackSampler)] = _fallbackSet;

        SetShaderProgram(null, null);
    }

    public void SetCustomShaders(string vertexSpirvPath, string fragmentSpirvPath)
        => SetShaderProgram(vertexSpirvPath, fragmentSpirvPath);

    public void ClearCustomShaders() => SetShaderProgram(null, null);

    public void Draw(
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
        float reflectionStrength)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_renderer.IsFrameOpen)
        {
            return;
        }

        TextureView baseView = ResolveTextureView(baseTexture, runtimeBaseTexture) ?? _fallbackTexture;
        VeldridSampler baseSampler = _fallbackSampler;
        TextureView shadowView = shadowMap?.NativeTexture as TextureView ?? _fallbackTexture;
        VeldridSampler shadowSampler = shadowMap?.NativeSampler as VeldridSampler ?? _fallbackSampler;
        TextureView reflectionView = reflectionTexture?.NativeResource as TextureView ?? _fallbackTexture;
        VeldridSampler reflectionSampler = _fallbackSampler;
        ResourceSet resources = GetResourceSet(baseView, baseSampler, shadowView, shadowSampler, reflectionView, reflectionSampler);

        bool shadowEnabled = receiveShadow
            && shadowMap?.NativeTexture is TextureView
            && shadowMap.Value.NativeSampler is VeldridSampler;
        bool reflectionEnabled = reflectionTexture?.NativeResource is TextureView && reflectionStrength > 0.001f;
        Matrix4x4 worldViewProjection = world * view * projection;
        UniformData data = new()
        {
            World = world,
            WorldViewProjection = worldViewProjection,
            LightViewProjection = shadowEnabled ? shadowMap!.Value.LightViewProjection : Matrix4x4.Identity,
            ReflectionViewProjection = reflectionEnabled ? reflectionViewProjection : Matrix4x4.Identity,
            Tint = tint,
            ShadowParameters = new Vector4(
                shadowEnabled ? 1.0f : 0.0f,
                shadowEnabled ? Math.Clamp(shadowMap!.Value.Strength, 0.0f, 1.0f) : 0.0f,
                shadowEnabled ? Math.Max(0.0f, shadowMap!.Value.Bias) : 0.0f,
                flipV ? 1.0f : 0.0f),
            ReflectionParameters = new Vector4(
                reflectionEnabled ? 1.0f : 0.0f,
                Math.Clamp(reflectionStrength, 0.0f, 1.0f),
                0.0f,
                0.0f)
        };

        CommandList commands = _renderer.CommandList;
        commands.UpdateBuffer(RequireDeviceBuffer(_uniformBuffer), 0, data);
        commands.SetPipeline(GetPipeline(_renderer.CurrentOutputDescription));
        commands.SetVertexBuffer(0, RequireDeviceBuffer(_vertexBuffer));
        commands.SetGraphicsResourceSet(0, resources);
        commands.Draw(6);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (ResourceSet set in _resourceSets.Values) set.Dispose();
        _resourceSets.Clear();
        foreach (PipelineBundle pipeline in _pipelines) pipeline.Pipeline.Dispose();
        foreach (VeldridShader shader in _shaders) shader.Dispose();
        _layout.Dispose();
        _uniformBuffer.Dispose();
        _fallbackSampler.Dispose();
        GC.SuppressFinalize(this);
    }

    private ResourceSet GetResourceSet(
        TextureView baseTexture,
        VeldridSampler baseSampler,
        TextureView shadowTexture,
        VeldridSampler shadowSampler,
        TextureView reflectionTexture,
        VeldridSampler reflectionSampler)
    {
        TextureSetKey key = new(baseTexture, baseSampler, shadowTexture, shadowSampler, reflectionTexture, reflectionSampler);
        if (_resourceSets.TryGetValue(key, out ResourceSet? resourceSet)) return resourceSet;
        resourceSet = CreateResourceSet(baseTexture, baseSampler, shadowTexture, shadowSampler, reflectionTexture, reflectionSampler);
        _resourceSets[key] = resourceSet;
        return resourceSet;
    }

    private ResourceSet CreateResourceSet(
        TextureView baseTexture,
        VeldridSampler baseSampler,
        TextureView shadowTexture,
        VeldridSampler shadowSampler,
        TextureView reflectionTexture,
        VeldridSampler reflectionSampler)
    {
        return _factory.CreateResourceSet(new ResourceSetDescription(
            _layout,
            RequireDeviceBuffer(_uniformBuffer),
            baseTexture,
            baseSampler,
            shadowTexture,
            shadowSampler,
            reflectionTexture,
            reflectionSampler));
    }

    private Pipeline GetPipeline(OutputDescription output)
    {
        PipelineBundle? existing = _pipelines.FirstOrDefault(item => item.Output.Equals(output));
        if (existing is not null) return existing.Pipeline;
        Pipeline pipeline = CreatePipeline(output, _shaderSet);
        _pipelines.Add(new PipelineBundle(output, pipeline));
        return pipeline;
    }

    private Pipeline CreatePipeline(OutputDescription output, ShaderSetDescription shaderSet)
    {
        return _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            shaderSet,
            [_layout],
            output));
    }

    private void SetShaderProgram(string? vertexSpirvPath, string? fragmentSpirvPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool customSpirv = !string.IsNullOrWhiteSpace(vertexSpirvPath)
            || !string.IsNullOrWhiteSpace(fragmentSpirvPath);
        if (customSpirv && (string.IsNullOrWhiteSpace(vertexSpirvPath) || string.IsNullOrWhiteSpace(fragmentSpirvPath)))
        {
            throw new ArgumentException("Both Vulkan vertex and fragment SPIR-V paths are required.");
        }

        ShaderDescription vertexShader = customSpirv
            ? VulkanShaderCompiler.LoadSpirvFile(vertexSpirvPath!, ShaderStages.Vertex)
            : VulkanShaderCompiler.CompileSource("textured_plane.vert", VertexShaderSource, ShaderStages.Vertex);
        ShaderDescription fragmentShader = customSpirv
            ? VulkanShaderCompiler.LoadSpirvFile(fragmentSpirvPath!, ShaderStages.Fragment)
            : VulkanShaderCompiler.CompileSource("textured_plane.frag", FragmentShaderSource, ShaderStages.Fragment);
        VeldridShader[] nextShaders = _factory.CreateFromSpirv(vertexShader, fragmentShader);
        ShaderSetDescription nextShaderSet = new(
            [new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2))],
            nextShaders);

        Pipeline nextPipeline;
        try
        {
            nextPipeline = CreatePipeline(_renderer.Device.SwapchainFramebuffer.OutputDescription, nextShaderSet);
        }
        catch
        {
            foreach (VeldridShader shader in nextShaders) shader.Dispose();
            throw;
        }

        foreach (PipelineBundle bundle in _pipelines) bundle.Pipeline.Dispose();
        _pipelines.Clear();
        foreach (VeldridShader shader in _shaders) shader.Dispose();
        _shaders = nextShaders;
        _shaderSet = nextShaderSet;
        _pipelines.Add(new PipelineBundle(_renderer.Device.SwapchainFramebuffer.OutputDescription, nextPipeline));
    }

    private static TextureView? ResolveTextureView(ITexture2D texture, RuntimeTextureHandle? runtimeTexture)
    {
        return runtimeTexture?.NativeResource as TextureView ?? texture.NativeResource as TextureView;
    }

    private static DeviceBuffer RequireDeviceBuffer(IGpuBuffer buffer)
    {
        return buffer.NativeResource as DeviceBuffer
            ?? throw new InvalidOperationException("Vulkan textured plane requires a Veldrid device buffer.");
    }

    private static TextureView RequireTextureView(ITexture2D texture)
    {
        return texture.NativeResource as TextureView
            ?? throw new InvalidOperationException("Vulkan textured plane requires a Veldrid texture view.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniformData
    {
        public Matrix4x4 World;
        public Matrix4x4 WorldViewProjection;
        public Matrix4x4 LightViewProjection;
        public Matrix4x4 ReflectionViewProjection;
        public Vector4 Tint;
        public Vector4 ShadowParameters;
        public Vector4 ReflectionParameters;
    }

    private sealed record PipelineBundle(OutputDescription Output, Pipeline Pipeline);
    private readonly record struct TextureSetKey(
        TextureView BaseTexture,
        VeldridSampler BaseSampler,
        TextureView ShadowTexture,
        VeldridSampler ShadowSampler,
        TextureView ReflectionTexture,
        VeldridSampler ReflectionSampler);

    private sealed class VeldridGpuBufferAdapter : IGpuBuffer
    {
        private readonly DeviceBuffer _buffer;

        public VeldridGpuBufferAdapter(VulkanRenderer renderer, int size)
        {
            _buffer = renderer.ResourceFactory.CreateBuffer(new BufferDescription((uint)size, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        }

        public EngineGraphicsBackend Backend => EngineGraphicsBackend.Vulkan;
        public GpuBufferKind Kind => GpuBufferKind.Uniform;
        public uint SizeInBytes => (uint)_buffer.SizeInBytes;
        public uint LegacyBufferId => 0;
        public object NativeResource => _buffer;
        public void Update<T>(ReadOnlySpan<T> data, uint offsetInBytes = 0) where T : unmanaged => throw new NotSupportedException();
        public void Dispose() => _buffer.Dispose();
    }

    private const string VertexShaderSource = """
        layout(set = 0, binding = 0, std140) uniform PlaneFrame
        {
            mat4 u_World;
            mat4 u_WVP;
            mat4 u_LightWVP;
            mat4 u_ReflectionWVP;
            vec4 u_Tint;
            vec4 u_ShadowParameters;
            vec4 u_ReflectionParameters;
        } u_Frame;
        layout(location = 0) in vec3 in_Pos;
        layout(location = 1) in vec2 in_Uv;
        layout(location = 0) out vec2 vs_Uv;
        layout(location = 1) out vec3 vs_WorldPos;
        layout(location = 2) out vec4 vs_ShadowPos;
        layout(location = 3) out vec4 vs_ReflectionPos;
        void main()
        {
            vec4 worldPos = u_Frame.u_World * vec4(in_Pos, 1.0);
            gl_Position = u_Frame.u_WVP * vec4(in_Pos, 1.0);
            vs_Uv = in_Uv;
            vs_WorldPos = worldPos.xyz;
            vs_ShadowPos = u_Frame.u_LightWVP * worldPos;
            vs_ReflectionPos = u_Frame.u_ReflectionWVP * worldPos;
        }
        """;

    private const string FragmentShaderSource = """
        layout(set = 0, binding = 0, std140) uniform PlaneFrame
        {
            mat4 u_World;
            mat4 u_WVP;
            mat4 u_LightWVP;
            mat4 u_ReflectionWVP;
            vec4 u_Tint;
            vec4 u_ShadowParameters;
            vec4 u_ReflectionParameters;
        } u_Frame;
        layout(set = 0, binding = 1) uniform texture2D u_Texture;
        layout(set = 0, binding = 2) uniform sampler u_TextureSampler;
        layout(set = 0, binding = 3) uniform texture2D u_ShadowMap;
        layout(set = 0, binding = 4) uniform sampler u_ShadowSampler;
        layout(set = 0, binding = 5) uniform texture2D u_ReflectionTex;
        layout(set = 0, binding = 6) uniform sampler u_ReflectionSampler;
        layout(location = 0) in vec2 vs_Uv;
        layout(location = 1) in vec3 vs_WorldPos;
        layout(location = 2) in vec4 vs_ShadowPos;
        layout(location = 3) in vec4 vs_ReflectionPos;
        layout(location = 0) out vec4 out_Color;
        float SampleShadow()
        {
            if (u_Frame.u_ShadowParameters.x < 0.5) return 1.0;
            vec3 ndc = vs_ShadowPos.xyz / max(abs(vs_ShadowPos.w), 0.0001);
            vec2 uv = ndc.xy * 0.5 + 0.5;
            if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || ndc.z < -1.0 || ndc.z > 1.0) return 1.0;
            float depth = (ndc.z * 0.5 + 0.5) - u_Frame.u_ShadowParameters.z;
            float visibility = texture(sampler2D(u_ShadowMap, u_ShadowSampler), uv).r >= depth ? 1.0 : 0.0;
            return mix(1.0 - clamp(u_Frame.u_ShadowParameters.y, 0.0, 1.0), 1.0, visibility);
        }
        void main()
        {
            vec2 uv = vs_Uv;
            if (u_Frame.u_ShadowParameters.w > 0.5) uv.y = 1.0 - uv.y;
            vec4 color = texture(sampler2D(u_Texture, u_TextureSampler), uv) * u_Frame.u_Tint;
            color.rgb *= SampleShadow();
            vec3 reflectionNdc = vs_ReflectionPos.xyz / max(abs(vs_ReflectionPos.w), 0.0001);
            vec2 reflectionUv = reflectionNdc.xy * 0.5 + 0.5;
            float inside = step(0.0, reflectionUv.x) * step(reflectionUv.x, 1.0) * step(0.0, reflectionUv.y) * step(reflectionUv.y, 1.0);
            vec3 reflection = texture(sampler2D(u_ReflectionTex, u_ReflectionSampler), clamp(reflectionUv, 0.001, 0.999)).rgb;
            float amount = clamp(u_Frame.u_ReflectionParameters.x, 0.0, 1.0) * inside * clamp(u_Frame.u_ReflectionParameters.y, 0.0, 1.0);
            color.rgb = mix(color.rgb, reflection, amount);
            if (color.a <= 0.001) discard;
            out_Color = color;
        }
        """;
}
