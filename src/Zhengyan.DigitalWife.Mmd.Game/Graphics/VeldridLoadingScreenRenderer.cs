using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class VeldridLoadingScreenRenderer : ILoadingScreenPassRenderer
{
    private readonly VulkanRenderer _renderer;
    private readonly DeviceBuffer _vertices;
    private readonly DeviceBuffer _uniforms;
    private readonly ResourceLayout _layout;
    private readonly Sampler _sampler;
    private readonly ITexture2D _fallbackTexture;
    private readonly Shader[] _shaders;
    private readonly ShaderSetDescription _shaderSet;
    private readonly Dictionary<TextureView, ResourceSet> _sets = [];
    private readonly List<(OutputDescription Output, Pipeline Pipeline)> _pipelines = [];

    public VeldridLoadingScreenRenderer(VulkanRenderer renderer)
    {
        _renderer = renderer;
        ResourceFactory factory = renderer.ResourceFactory;
        _vertices = factory.CreateBuffer(new BufferDescription(24u * sizeof(float), BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _uniforms = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<UniformData>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _sampler = factory.CreateSampler(SamplerDescription.Linear);
        _fallbackTexture = renderer.CreateTexture2D();
        _fallbackTexture.Fill(255, 255, 255, 255);
        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("LoadingFrame", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("LoadingTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("LoadingSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("loading.vert", VertexSource, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("loading.frag", FragmentSource, ShaderStages.Fragment));
        _shaderSet = new ShaderSetDescription(
            [new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2),
                new VertexElementDescription("Uv", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2))], _shaders);
    }

    public void DrawRect(Vector4 clipRect, Vector4 color, ITexture2D? texture = null, float opacity = 1)
    {
        if (!_renderer.IsFrameOpen) return;
        TextureView view = (texture?.NativeResource as TextureView) ?? (_fallbackTexture.NativeResource as TextureView)!;
        if (!_sets.TryGetValue(view, out ResourceSet? set))
        {
            set = _renderer.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_layout, _uniforms, view, _sampler));
            _sets.Add(view, set);
        }
        float[] vertices =
        [
            clipRect.X, clipRect.Y, 0, 1, clipRect.Z, clipRect.Y, 1, 1, clipRect.X, clipRect.W, 0, 0,
            clipRect.X, clipRect.W, 0, 0, clipRect.Z, clipRect.Y, 1, 1, clipRect.Z, clipRect.W, 1, 0
        ];
        UniformData data = new() { Color = new Vector4(color.X, color.Y, color.Z, color.W * Math.Clamp(opacity, 0, 1)), UseTexture = texture is null ? 0 : 1 };
        CommandList commands = _renderer.CommandList;
        commands.UpdateBuffer(_vertices, 0, vertices);
        commands.UpdateBuffer(_uniforms, 0, data);
        commands.SetPipeline(GetPipeline(_renderer.CurrentOutputDescription));
        commands.SetVertexBuffer(0, _vertices);
        commands.SetGraphicsResourceSet(0, set);
        commands.Draw(6);
    }

    public void Dispose()
    {
        foreach (ResourceSet set in _sets.Values) set.Dispose();
        foreach ((_, Pipeline pipeline) in _pipelines) pipeline.Dispose();
        foreach (Shader shader in _shaders) shader.Dispose();
        _layout.Dispose(); _sampler.Dispose(); _uniforms.Dispose(); _vertices.Dispose(); _fallbackTexture.Dispose();
    }

    private Pipeline GetPipeline(OutputDescription output)
    {
        foreach ((OutputDescription candidate, Pipeline pipeline) in _pipelines) if (candidate.Equals(output)) return pipeline;
        Pipeline created = _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend, DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone, PrimitiveTopology.TriangleList, _shaderSet, [_layout], output));
        _pipelines.Add((output, created)); return created;
    }

    [StructLayout(LayoutKind.Sequential)] private struct UniformData { public Vector4 Color; public int UseTexture; public Vector3 Padding; }
    private const string VertexSource = """
        layout(location=0) in vec2 in_Pos; layout(location=1) in vec2 in_Uv; layout(location=0) out vec2 vs_Uv;
        void main(){ gl_Position=vec4(in_Pos,0,1); vs_Uv=in_Uv; }
        """;
    private const string FragmentSource = """
        layout(set=0,binding=0,std140) uniform LoadingFrame { vec4 color; int useTexture; vec3 padding; } frame;
        layout(set=0,binding=1) uniform texture2D loadingTexture; layout(set=0,binding=2) uniform sampler loadingSampler;
        layout(location=0) in vec2 vs_Uv; layout(location=0) out vec4 out_Color;
        void main(){ vec4 color=frame.color; if(frame.useTexture!=0) color*=texture(sampler2D(loadingTexture,loadingSampler),vs_Uv); if(color.a<=.001) discard; out_Color=color; }
        """;
}
