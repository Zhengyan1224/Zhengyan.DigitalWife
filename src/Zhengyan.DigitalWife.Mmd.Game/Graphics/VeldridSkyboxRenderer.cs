using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal sealed class VeldridSkyboxRenderer : ISkyboxPassRenderer
{
    private readonly VulkanRenderer _renderer;
    private readonly DeviceBuffer _vertices;
    private readonly DeviceBuffer _uniforms;
    private readonly ResourceLayout _layout;
    private readonly Sampler _sampler;
    private readonly Shader[] _shaders;
    private readonly ShaderSetDescription _shaderSet;
    private readonly Dictionary<TextureView, ResourceSet> _sets = [];
    private readonly List<(OutputDescription Output, Pipeline Pipeline)> _pipelines = [];

    public VeldridSkyboxRenderer(VulkanRenderer renderer)
    {
        _renderer = renderer;
        ResourceFactory factory = renderer.ResourceFactory;
        _vertices = factory.CreateBuffer(new BufferDescription(12u * sizeof(float), BufferUsage.VertexBuffer));
        renderer.Device.UpdateBuffer(_vertices, 0, new float[] { -1, -1, 1, -1, -1, 1, -1, 1, 1, -1, 1, 1 });
        _uniforms = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<UniformData>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _sampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Wrap, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear, null, 0, 0, uint.MaxValue, 0, SamplerBorderColor.TransparentBlack));
        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SkyFrame", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SkyTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SkySampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("skybox.vert", VertexSource, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("skybox.frag", FragmentSource, ShaderStages.Fragment));
        _shaderSet = new ShaderSetDescription(
            [new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2))],
            _shaders);
    }

    public void Draw(ITexture2D texture, Matrix4x4 inverseViewProjection, Vector3 tint, float exposure)
    {
        if (!_renderer.IsFrameOpen || texture.NativeResource is not TextureView view) return;
        if (!_sets.TryGetValue(view, out ResourceSet? set))
        {
            set = _renderer.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_layout, _uniforms, view, _sampler));
            _sets.Add(view, set);
        }

        UniformData data = new()
        {
            InverseViewProjection = inverseViewProjection,
            TintExposure = new Vector4(tint, Math.Max(0, exposure))
        };
        CommandList commands = _renderer.CommandList;
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
        _layout.Dispose();
        _sampler.Dispose();
        _uniforms.Dispose();
        _vertices.Dispose();
    }

    private Pipeline GetPipeline(OutputDescription output)
    {
        foreach ((OutputDescription candidate, Pipeline pipeline) in _pipelines)
            if (candidate.Equals(output)) return pipeline;
        Pipeline created = _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            _shaderSet,
            [_layout],
            output));
        _pipelines.Add((output, created));
        return created;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniformData
    {
        public Matrix4x4 InverseViewProjection;
        public Vector4 TintExposure;
    }

    private const string VertexSource = """
        layout(location=0) in vec2 in_Pos;
        layout(location=0) out vec2 vs_Pos;
        void main() { vs_Pos=in_Pos; gl_Position=vec4(in_Pos,1.0,1.0); }
        """;

    private const string FragmentSource = """
        layout(set=0,binding=0,std140) uniform SkyFrame { mat4 inverseVP; vec4 tintExposure; } frame;
        layout(set=0,binding=1) uniform texture2D skyTexture;
        layout(set=0,binding=2) uniform sampler skySampler;
        layout(location=0) in vec2 vs_Pos;
        layout(location=0) out vec4 out_Color;
        const float PI=3.14159265359;
        void main() {
            vec4 farPoint=frame.inverseVP*vec4(vs_Pos,1.0,1.0);
            vec3 dir=normalize(farPoint.xyz/farPoint.w);
            vec2 uv=vec2(fract(atan(dir.z,dir.x)/(2.0*PI)+0.5),0.5-asin(clamp(dir.y,-1.0,1.0))/PI);
            vec3 color=texture(sampler2D(skyTexture,skySampler),uv).rgb*frame.tintExposure.rgb*frame.tintExposure.a;
            out_Color=vec4(color,1.0);
        }
        """;
}
