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
    private TextureView? _boundDepthView;
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
            new ResourceLayoutElementDescription("SceneSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneDepth", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("DepthSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
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
        _ = viewportWidth; _ = viewportHeight;
        if (!_renderer.IsFrameOpen
            || _capture.NativeColorResource is not TextureView view
            || _capture.NativeDepthResource is not TextureView depthView) return;
        if (!ReferenceEquals(view, _boundView) || !ReferenceEquals(depthView, _boundDepthView))
        {
            _set?.Dispose();
            _set = _renderer.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _layout, _uniforms, view, _sampler, depthView, _sampler));
            _boundView = view;
            _boundDepthView = depthView;
        }
        UniformData data = new()
        {
            TintTime = new Vector4(settings.Tint, (float)timeSeconds),
            FogDensity = new Vector4(settings.FogColor, Math.Clamp(settings.FogDensity, 0, 8)),
            Effects = new Vector4(
                Math.Clamp(settings.DistortionStrength, 0, .12f),
                Math.Clamp(settings.CausticsStrength, 0, 2),
                Math.Clamp(settings.BubbleStrength, 0, 2),
                Math.Max(settings.SurfaceDepth, 0)),
            DepthParameters = new Vector4(
                Math.Max(camera.NearClipPlane, .0001f),
                Math.Max(camera.FarClipPlane, camera.NearClipPlane + .001f),
                camera.ProjectionMode == CameraProjectionMode.Orthographic ? 1 : 0,
                Math.Max(settings.VisibilityDistance, .001f))
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
    private struct UniformData
    {
        public Vector4 TintTime;
        public Vector4 FogDensity;
        public Vector4 Effects;
        public Vector4 DepthParameters;
    }
    private const string VertexSource = """
        layout(location=0) in vec2 in_Pos; layout(location=1) in vec2 in_Uv; layout(location=0) out vec2 vs_Uv;
        void main(){ gl_Position=vec4(in_Pos,0,1); vs_Uv=in_Uv; }
        """;
    private const string FragmentSource = """
        layout(set=0,binding=0,std140) uniform PostFrame { vec4 tintTime; vec4 fogDensity; vec4 effects; vec4 depthParameters; } frame;
        layout(set=0,binding=1) uniform texture2D sceneColor; layout(set=0,binding=2) uniform sampler sceneSampler;
        layout(set=0,binding=3) uniform texture2D sceneDepth; layout(set=0,binding=4) uniform sampler depthSampler;
        layout(location=0) in vec2 vs_Uv; layout(location=0) out vec4 out_Color;
        float hash(vec2 p){ vec3 p3=fract(vec3(p.xyx)*.1031); p3+=dot(p3,p3.yzx+33.33); return fract((p3.x+p3.y)*p3.z); }
        float noise(vec2 p){ vec2 i=floor(p),f=fract(p),u=f*f*(3.0-2.0*f); return mix(mix(hash(i),hash(i+vec2(1,0)),u.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),u.x),u.y); }
        float linearDepth(float depth){ float near=frame.depthParameters.x,far=frame.depthParameters.y; if(frame.depthParameters.z>.5) return mix(near,far,depth); return near*far/max(far-depth*(far-near),.0001); }
        float caustics(vec2 uv,float time){ vec2 p=uv*vec2(18,13); float a=sin(p.x+sin(p.y*1.7+time*.85)+time*.65); float b=sin(p.x*1.35-p.y*.75+time*1.15); float c=sin(length(p-vec2(9,6))*1.35-time*1.8); return smoothstep(.73,1.0,(a+b+c)*.333+.5); }
        float bubbleLayer(vec2 uv,float time,float scale,float speed,float seed){ vec2 p=uv*scale; p.y-=time*speed; vec2 cell=floor(p),f=fract(p); float rnd=hash(cell+seed); vec2 center=vec2(hash(cell+seed+17),hash(cell+seed+31)); center.y=fract(center.y+time*speed*.13); float radius=mix(.035,.085,hash(cell+seed+47)); float d=length((f-center)*vec2(1,1.25)); return (1.0-smoothstep(radius*.72,radius,d))*smoothstep(radius*.35,radius*.58,d)*smoothstep(.78,.98,rnd); }
        float bubbles(vec2 uv,float time){ return clamp(bubbleLayer(uv+vec2(.03,.01),time,8,.1,3)+bubbleLayer(uv+vec2(.41,.22),time,13,.16,19)+bubbleLayer(uv+vec2(.77,.37),time,21,.22,41),0.0,1.0); }
        void main(){
            vec2 uv=vs_Uv; float rawDepth=texture(sampler2D(sceneDepth,depthSampler),uv).r;
            float skyMask=smoothstep(.9985,1.0,rawDepth); float sceneDistance=linearDepth(rawDepth);
            float skyDistance=min(frame.depthParameters.y*.32,max(frame.depthParameters.w,frame.depthParameters.x)); float depthForWater=mix(sceneDistance,skyDistance,skyMask); float entry=smoothstep(0.0,.45,frame.effects.w);
            vec2 wave=vec2(sin(uv.y*32.0+frame.tintTime.w*.9)+sin((uv.x+uv.y)*22.0-frame.tintTime.w*1.35),cos(uv.x*28.0-frame.tintTime.w*.75)+sin((uv.x-uv.y)*18.0+frame.tintTime.w*1.1));
            float shimmer=noise(uv*14.0+vec2(frame.tintTime.w*.05,-frame.tintTime.w*.08)); vec2 distortion=wave*(.5+shimmer*.5)*frame.effects.x*entry*mix(1.0,.35,skyMask);
            vec4 source=texture(sampler2D(sceneColor,sceneSampler),clamp(uv+distortion,.001,.999)); vec3 color=source.rgb;
            float distanceFog=max(depthForWater-frame.depthParameters.x,0.0)/frame.depthParameters.w; float density=max(frame.fogDensity.w,0.0);
            float fog=1.0-exp(-distanceFog*density); fog=clamp(fog+clamp(frame.effects.w*.045,0.0,.38),0.0,.96)*entry;
            vec3 absorption=vec3(exp(-depthForWater*.018*density),exp(-depthForWater*.007*density),exp(-depthForWater*.0035*density));
            color*=mix(vec3(1),absorption,.55*entry); color*=mix(vec3(1),frame.tintTime.rgb,.34*entry); vec3 fogged=mix(color,frame.fogDensity.rgb,fog);
            float caustic=caustics(uv+distortion*4.0,frame.tintTime.w)*(1.0-fog)*(1.0-skyMask)*frame.effects.y*entry; fogged+=vec3(.16,.26,.23)*caustic;
            float bubble=bubbles(uv,frame.tintTime.w)*frame.effects.z*entry; fogged=mix(fogged,vec3(.72,.92,.95),bubble*.35);
            float vignette=1.0-smoothstep(.18,.82,distance(uv,vec2(.5))); fogged*=mix(.72,1.0,vignette*entry+(1.0-entry));
            out_Color=vec4(clamp(fogged,0.0,1.0),source.a);
        }
        """;
}
