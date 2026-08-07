using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class VeldridUnderwaterPostProcessRenderer : IUnderwaterPostProcessRenderer
{
    private readonly VulkanRenderer _renderer;
    private readonly IRenderTarget _capture;
    private readonly DeviceBuffer _vertices;
    private readonly DeviceBuffer _uniforms;
    private readonly ResourceLayout _layout;
    private readonly Sampler _sampler;
    private readonly Shader[] _shaders;
    private readonly ShaderSetDescription _shaderSet;
    private readonly List<(OutputDescription Output, Pipeline Pipeline)> _pipelines = [];
    private TextureView? _boundView;
    private ResourceSet? _set;

    public VeldridUnderwaterPostProcessRenderer(VulkanRenderer renderer, string name)
    {
        _renderer = renderer;
        _capture = renderer.CreateRenderTarget($"{name}-Capture");
        ResourceFactory factory = renderer.ResourceFactory;
        float[] vertices = { -1,-1,0,0, 1,-1,1,0, -1,1,0,1, -1,1,0,1, 1,-1,1,0, 1,1,1,1 };
        _vertices = factory.CreateBuffer(new BufferDescription((uint)(vertices.Length * sizeof(float)), BufferUsage.VertexBuffer));
        renderer.Device.UpdateBuffer(_vertices, 0, vertices);
        _uniforms = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<UniformData>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _sampler = factory.CreateSampler(SamplerDescription.Linear);
        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("PostFrame", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneColor", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("underwater.vert", VertexSource, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("underwater.frag", FragmentSource, ShaderStages.Fragment));
        _shaderSet = new ShaderSetDescription(
            [new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2),
                new VertexElementDescription("Uv", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2))], _shaders);
    }

    public void BeginCapture(int width, int height, Vector4 clearColor)
    {
        _capture.EnsureSize(width, height);
        _capture.BeginPass(clearColor);
    }

    public void ResumeCapture() => _capture.ResumePass();

    public void Draw(OrbitCamera camera, UnderwaterPostProcessSettings settings, double timeSeconds, int viewportWidth, int viewportHeight)
    {
        _ = camera; _ = viewportWidth; _ = viewportHeight;
        if (!_renderer.IsFrameOpen || _capture.NativeColorResource is not TextureView view) return;
        if (!ReferenceEquals(view, _boundView))
        {
            _set?.Dispose();
            _set = _renderer.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_layout, _uniforms, view, _sampler));
            _boundView = view;
        }
        UniformData data = new()
        {
            TintTime = new Vector4(settings.Tint, (float)timeSeconds),
            FogDensity = new Vector4(settings.FogColor, Math.Clamp(settings.FogDensity, 0, 8)),
            Effects = new Vector4(
                Math.Clamp(settings.DistortionStrength, 0, .12f),
                Math.Clamp(settings.CausticsStrength, 0, 2),
                Math.Clamp(settings.BubbleStrength, 0, 2),
                Math.Clamp(settings.SurfaceDepth, 0, 20))
        };
        CommandList commands = _renderer.CommandList;
        commands.UpdateBuffer(_uniforms, 0, data);
        commands.SetPipeline(GetPipeline(_renderer.CurrentOutputDescription));
        commands.SetVertexBuffer(0, _vertices);
        commands.SetGraphicsResourceSet(0, _set!);
        commands.Draw(6);
    }

    public void Dispose()
    {
        _set?.Dispose();
        foreach ((_, Pipeline pipeline) in _pipelines) pipeline.Dispose();
        foreach (Shader shader in _shaders) shader.Dispose();
        _layout.Dispose(); _sampler.Dispose(); _uniforms.Dispose(); _vertices.Dispose(); _capture.Dispose();
    }

    private Pipeline GetPipeline(OutputDescription output)
    {
        foreach ((OutputDescription candidate, Pipeline pipeline) in _pipelines) if (candidate.Equals(output)) return pipeline;
        Pipeline created = _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend, DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone, PrimitiveTopology.TriangleList, _shaderSet, [_layout], output));
        _pipelines.Add((output, created)); return created;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniformData { public Vector4 TintTime; public Vector4 FogDensity; public Vector4 Effects; }
    private const string VertexSource = """
        layout(location=0) in vec2 in_Pos; layout(location=1) in vec2 in_Uv; layout(location=0) out vec2 vs_Uv;
        void main(){ gl_Position=vec4(in_Pos,0,1); vs_Uv=in_Uv; }
        """;
    private const string FragmentSource = """
        layout(set=0,binding=0,std140) uniform PostFrame { vec4 tintTime; vec4 fogDensity; vec4 effects; } frame;
        layout(set=0,binding=1) uniform texture2D sceneColor; layout(set=0,binding=2) uniform sampler sceneSampler;
        layout(location=0) in vec2 vs_Uv; layout(location=0) out vec4 out_Color;
        float hash(vec2 p){ return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
        void main(){
            float entry=smoothstep(0.0,.45,frame.effects.w);
            vec2 wave=vec2(sin(vs_Uv.y*32.0+frame.tintTime.w*.9),cos(vs_Uv.x*28.0-frame.tintTime.w*.75));
            vec2 uv=clamp(vs_Uv+wave*frame.effects.x*entry,.001,.999);
            vec4 source=texture(sampler2D(sceneColor,sceneSampler),uv);
            float fog=clamp((.12+frame.effects.w*.045)*frame.fogDensity.w*entry,0.0,.82);
            vec3 color=mix(source.rgb*mix(vec3(1),frame.tintTime.rgb,.34*entry),frame.fogDensity.rgb,fog);
            float caustic=pow(max(sin((uv.x+uv.y)*45.0+frame.tintTime.w*1.4),0.0),8.0)*frame.effects.y*entry;
            color+=vec3(.12,.2,.18)*caustic;
            float bubble=step(.992,hash(floor((uv+vec2(0,frame.tintTime.w*.02))*vec2(64,40))))*frame.effects.z*entry;
            color=mix(color,vec3(.72,.92,.95),bubble*.25);
            out_Color=vec4(clamp(color,0.0,1.0),source.a);
        }
        """;
}
