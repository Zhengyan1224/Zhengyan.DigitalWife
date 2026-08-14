using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal sealed class VeldridParticleRenderer : IParticlePassRenderer
{
    private readonly VulkanRenderer _renderer;
    private DeviceBuffer _vertices;
    private uint _vertexCapacity;
    private readonly DeviceBuffer _uniforms;
    private readonly ResourceLayout _layout;
    private readonly Sampler _sampler;
    private readonly Shader[] _shaders;
    private readonly ShaderSetDescription _shaderSet;
    private readonly Shader[] _shadowShaders;
    private readonly ShaderSetDescription _shadowShaderSet;
    private readonly Dictionary<TextureView, ResourceSet> _sets = [];
    private readonly List<(OutputDescription Output, bool Additive, Pipeline Pipeline)> _pipelines = [];
    private readonly List<(OutputDescription Output, Pipeline Pipeline)> _shadowPipelines = [];

    public VeldridParticleRenderer(VulkanRenderer renderer, uint initialCapacityBytes)
    {
        _renderer = renderer;
        ResourceFactory factory = renderer.ResourceFactory;
        _vertexCapacity = Math.Max(initialCapacityBytes, 256);
        _vertices = factory.CreateBuffer(new BufferDescription(_vertexCapacity, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _uniforms = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<UniformData>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _sampler = factory.CreateSampler(SamplerDescription.Linear);
        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ParticleFrame", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ParticleTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ParticleSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("particle.vert", VertexSource, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("particle.frag", FragmentSource, ShaderStages.Fragment));
        _shaderSet = new ShaderSetDescription(
            [new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
                new VertexElementDescription("Uv", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Life", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1))],
            _shaders);
        _shadowShaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("particle_shadow.vert", ShadowVertexSource, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("particle_shadow.frag", ShadowFragmentSource, ShaderStages.Fragment));
        _shadowShaderSet = new ShaderSetDescription(_shaderSet.VertexLayouts, _shadowShaders);
    }

    public void Draw<T>(ReadOnlySpan<T> vertices, int vertexCount, ITexture2D fallbackTexture,
        RuntimeTextureHandle? runtimeTexture, Matrix4x4 viewProjection, float opacity,
        Vector4 startColor, Vector4 endColor, bool useTextureColor, bool additive) where T : unmanaged
    {
        if (!_renderer.IsFrameOpen || vertexCount <= 0) return;
        TextureView? view = runtimeTexture?.NativeResource as TextureView ?? fallbackTexture.NativeResource as TextureView;
        if (view is null) return;
        uint bytes = checked((uint)(vertexCount * Marshal.SizeOf<T>()));
        EnsureCapacity(bytes);
        if (!_sets.TryGetValue(view, out ResourceSet? set))
        {
            set = _renderer.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_layout, _uniforms, view, _sampler));
            _sets.Add(view, set);
        }

        UniformData data = new()
        {
            ViewProjection = viewProjection,
            StartColor = startColor,
            EndColor = endColor,
            Parameters = new Vector4(Math.Clamp(opacity, 0, 1), useTextureColor ? 1 : 0, 0, 0)
        };
        CommandList commands = _renderer.CommandList;
        commands.UpdateBuffer(_vertices, 0, vertices[..vertexCount]);
        commands.UpdateBuffer(_uniforms, 0, data);
        commands.SetPipeline(GetPipeline(_renderer.CurrentOutputDescription, additive));
        commands.SetVertexBuffer(0, _vertices);
        commands.SetGraphicsResourceSet(0, set);
        commands.Draw((uint)vertexCount);
    }

    public void DrawShadow<T>(ReadOnlySpan<T> vertices, int vertexCount, ITexture2D fallbackTexture,
        RuntimeTextureHandle? runtimeTexture, Matrix4x4 lightViewProjection, float opacity,
        Vector4 startColor, Vector4 endColor, float depthBias) where T : unmanaged
    {
        if (!_renderer.IsFrameOpen || vertexCount <= 0) return;
        TextureView? view = runtimeTexture?.NativeResource as TextureView ?? fallbackTexture.NativeResource as TextureView;
        if (view is null) return;
        uint bytes = checked((uint)(vertexCount * Marshal.SizeOf<T>()));
        EnsureCapacity(bytes);
        if (!_sets.TryGetValue(view, out ResourceSet? set))
        {
            set = _renderer.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_layout, _uniforms, view, _sampler));
            _sets.Add(view, set);
        }

        UniformData data = new()
        {
            ViewProjection = lightViewProjection,
            StartColor = startColor,
            EndColor = endColor,
            Parameters = new Vector4(Math.Clamp(opacity, 0, 1), 0, Math.Max(depthBias, 0), 0)
        };
        CommandList commands = _renderer.CommandList;
        commands.UpdateBuffer(_vertices, 0, vertices[..vertexCount]);
        commands.UpdateBuffer(_uniforms, 0, data);
        commands.SetPipeline(GetShadowPipeline(_renderer.CurrentOutputDescription));
        commands.SetVertexBuffer(0, _vertices);
        commands.SetGraphicsResourceSet(0, set);
        commands.Draw((uint)vertexCount);
    }

    public void Dispose()
    {
        foreach (ResourceSet set in _sets.Values) set.Dispose();
        foreach ((_, _, Pipeline pipeline) in _pipelines) pipeline.Dispose();
        foreach ((_, Pipeline pipeline) in _shadowPipelines) pipeline.Dispose();
        foreach (Shader shader in _shaders) shader.Dispose();
        foreach (Shader shader in _shadowShaders) shader.Dispose();
        _layout.Dispose();
        _sampler.Dispose();
        _uniforms.Dispose();
        _vertices.Dispose();
    }

    private void EnsureCapacity(uint bytes)
    {
        if (bytes <= _vertexCapacity) return;
        _vertexCapacity = Math.Max(bytes, _vertexCapacity * 2);
        _vertices.Dispose();
        _vertices = _renderer.ResourceFactory.CreateBuffer(new BufferDescription(_vertexCapacity, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
    }

    private Pipeline GetPipeline(OutputDescription output, bool additive)
    {
        foreach ((OutputDescription candidate, bool candidateAdditive, Pipeline pipeline) in _pipelines)
            if (candidate.Equals(output) && candidateAdditive == additive) return pipeline;
        BlendStateDescription blend = additive
            ? new BlendStateDescription(RgbaFloat.Black, new BlendAttachmentDescription(
                true, BlendFactor.SourceAlpha, BlendFactor.One, BlendFunction.Add,
                BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFunction.Add))
            : BlendStateDescription.SingleAlphaBlend;
        Pipeline created = _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            blend,
            new DepthStencilStateDescription(true, false, ComparisonKind.LessEqual),
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            _shaderSet,
            [_layout],
            output));
        _pipelines.Add((output, additive, created));
        return created;
    }

    private Pipeline GetShadowPipeline(OutputDescription output)
    {
        foreach ((OutputDescription candidate, Pipeline pipeline) in _shadowPipelines)
            if (candidate.Equals(output)) return pipeline;
        Pipeline created = _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled,
            new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            _shadowShaderSet,
            [_layout],
            output));
        _shadowPipelines.Add((output, created));
        return created;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniformData
    {
        public Matrix4x4 ViewProjection;
        public Vector4 StartColor;
        public Vector4 EndColor;
        public Vector4 Parameters;
    }

    private const string VertexSource = """
        layout(set=0,binding=0,std140) uniform ParticleFrame { mat4 viewProjection; vec4 startColor; vec4 endColor; vec4 parameters; } frame;
        layout(location=0) in vec3 in_Pos;
        layout(location=1) in vec2 in_Uv;
        layout(location=2) in float in_Life;
        layout(location=0) out vec2 vs_Uv;
        layout(location=1) out float vs_Life;
        void main(){ gl_Position=frame.viewProjection*vec4(in_Pos,1.0); vs_Uv=in_Uv; vs_Life=in_Life; }
        """;
    private const string FragmentSource = """
        layout(set=0,binding=0,std140) uniform ParticleFrame { mat4 viewProjection; vec4 startColor; vec4 endColor; vec4 parameters; } frame;
        layout(set=0,binding=1) uniform texture2D particleTexture;
        layout(set=0,binding=2) uniform sampler particleSampler;
        layout(location=0) in vec2 vs_Uv;
        layout(location=1) in float vs_Life;
        layout(location=0) out vec4 out_Color;
        void main(){
            vec4 sampled=texture(sampler2D(particleTexture,particleSampler),vs_Uv);
            vec4 color=mix(frame.startColor,frame.endColor,clamp(vs_Life,0.0,1.0));
            if(frame.parameters.y>0.5) color.rgb*=sampled.rgb;
            color.a*=sampled.a*frame.parameters.x;
            if(color.a<=0.001) discard;
            out_Color=color;
        }
        """;

    private const string ShadowVertexSource = """
        layout(set=0,binding=0,std140) uniform ParticleFrame { mat4 viewProjection; vec4 startColor; vec4 endColor; vec4 parameters; } frame;
        layout(location=0) in vec3 in_Pos;
        layout(location=1) in vec2 in_Uv;
        layout(location=2) in float in_Life;
        layout(location=0) out vec2 vs_Uv;
        layout(location=1) out float vs_Life;
        void main(){ gl_Position=frame.viewProjection*vec4(in_Pos,1.0); vs_Uv=in_Uv; vs_Life=in_Life; }
        """;

    private const string ShadowFragmentSource = """
        layout(set=0,binding=0,std140) uniform ParticleFrame { mat4 viewProjection; vec4 startColor; vec4 endColor; vec4 parameters; } frame;
        layout(set=0,binding=1) uniform texture2D particleTexture;
        layout(set=0,binding=2) uniform sampler particleSampler;
        layout(location=0) in vec2 vs_Uv;
        layout(location=1) in float vs_Life;
        void main(){
            float particleAlpha=mix(frame.startColor.a,frame.endColor.a,clamp(vs_Life,0.0,1.0));
            float alpha=texture(sampler2D(particleTexture,particleSampler),vs_Uv).a*particleAlpha*frame.parameters.x;
            if(alpha<=0.05) discard;
            float slope=max(abs(dFdx(gl_FragCoord.z)),abs(dFdy(gl_FragCoord.z)));
            gl_FragDepth=gl_FragCoord.z+slope*1.5+frame.parameters.z*2.0;
        }
        """;
}
