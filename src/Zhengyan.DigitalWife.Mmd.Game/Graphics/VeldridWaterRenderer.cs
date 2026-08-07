using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal sealed class VeldridWaterRenderer : IWaterPassRenderer
{
    private const int MaxRipples = 48;
    private readonly VulkanRenderer _renderer;
    private readonly DeviceBuffer _vertices;
    private readonly DeviceBuffer _indices;
    private readonly DeviceBuffer _uniforms;
    private readonly DeviceBuffer _ripples;
    private readonly ResourceLayout _layout;
    private readonly Sampler _sampler;
    private readonly Shader[] _shaders;
    private readonly ShaderSetDescription _shaderSet;
    private readonly Dictionary<TextureKey, ResourceSet> _sets = [];
    private readonly List<(OutputDescription Output, Pipeline Pipeline)> _pipelines = [];

    public VeldridWaterRenderer(VulkanRenderer renderer, uint vertexBytes, ReadOnlySpan<uint> indices)
    {
        _renderer = renderer;
        ResourceFactory factory = renderer.ResourceFactory;
        _vertices = factory.CreateBuffer(new BufferDescription(vertexBytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _indices = factory.CreateBuffer(new BufferDescription(checked((uint)(indices.Length * sizeof(uint))), BufferUsage.IndexBuffer));
        renderer.Device.UpdateBuffer(_indices, 0, indices);
        _uniforms = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<UniformData>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _ripples = factory.CreateBuffer(new BufferDescription((uint)((MaxRipples * 2 + 1) * Marshal.SizeOf<Vector4>()), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _sampler = factory.CreateSampler(SamplerDescription.Linear);
        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("WaterFrame", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalA", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalASampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalB", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalBSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("Sky", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SkySampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("Reflection", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ReflectionSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("WaterRipples", ResourceKind.UniformBuffer, ShaderStages.Fragment)));
        _shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("water.vert", VertexSource, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("water.frag", FragmentSource, ShaderStages.Fragment));
        _shaderSet = new ShaderSetDescription(
            [new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
                new VertexElementDescription("Uv", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3))], _shaders);
    }

    public void Draw<T>(ReadOnlySpan<T> vertices, uint indexCount, ITexture2D normalA, ITexture2D normalB,
        ITexture2D sky, RuntimeTextureHandle? reflection, ReadOnlySpan<Vector4> ripples,
        Matrix4x4 world, Matrix4x4 view, Matrix4x4 projection,
        Matrix4x4 reflectionViewProjection, Vector3 eye, Vector3 deepColor, Vector3 reflectionTint,
        float time, float textureLerp, float alpha, float normalTiling, float skyStrength, bool mirrorEnabled) where T : unmanaged
    {
        if (!_renderer.IsFrameOpen) return;
        if (normalA.NativeResource is not TextureView a || normalB.NativeResource is not TextureView b
            || sky.NativeResource is not TextureView skyView) return;
        TextureView reflectionView = reflection?.NativeResource as TextureView ?? skyView;
        TextureKey key = new(a, b, skyView, reflectionView);
        if (!_sets.TryGetValue(key, out ResourceSet? set))
        {
            set = _renderer.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _layout, _uniforms, a, _sampler, b, _sampler, skyView, _sampler, reflectionView, _sampler, _ripples));
            _sets.Add(key, set);
        }
        bool planar = reflection?.NativeResource is TextureView;
        UniformData data = new()
        {
            World = world,
            WorldViewProjection = world * view * projection,
            ReflectionViewProjection = reflectionViewProjection,
            EyeTime = new Vector4(eye, time),
            DeepAlpha = new Vector4(deepColor, Math.Clamp(alpha, 0, 1)),
            ReflectionSky = new Vector4(reflectionTint, Math.Clamp(skyStrength, 0, 1)),
            Parameters = new Vector4(textureLerp, normalTiling, mirrorEnabled ? 1 : 0, planar && mirrorEnabled ? 1 : 0)
        };
        CommandList commands = _renderer.CommandList;
        commands.UpdateBuffer(_vertices, 0, vertices);
        commands.UpdateBuffer(_uniforms, 0, data);
        commands.UpdateBuffer(_ripples, 0, ripples);
        commands.SetPipeline(GetPipeline(_renderer.CurrentOutputDescription));
        commands.SetVertexBuffer(0, _vertices);
        commands.SetIndexBuffer(_indices, IndexFormat.UInt32);
        commands.SetGraphicsResourceSet(0, set);
        commands.DrawIndexed(indexCount);
    }

    public void Dispose()
    {
        foreach (ResourceSet set in _sets.Values) set.Dispose();
        foreach ((_, Pipeline pipeline) in _pipelines) pipeline.Dispose();
        foreach (Shader shader in _shaders) shader.Dispose();
        _layout.Dispose(); _sampler.Dispose(); _ripples.Dispose(); _uniforms.Dispose(); _indices.Dispose(); _vertices.Dispose();
    }

    private Pipeline GetPipeline(OutputDescription output)
    {
        foreach ((OutputDescription candidate, Pipeline pipeline) in _pipelines) if (candidate.Equals(output)) return pipeline;
        Pipeline created = _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(true, false, ComparisonKind.LessEqual),
            RasterizerStateDescription.CullNone, PrimitiveTopology.TriangleList, _shaderSet, [_layout], output));
        _pipelines.Add((output, created)); return created;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniformData
    {
        public Matrix4x4 World;
        public Matrix4x4 WorldViewProjection;
        public Matrix4x4 ReflectionViewProjection;
        public Vector4 EyeTime;
        public Vector4 DeepAlpha;
        public Vector4 ReflectionSky;
        public Vector4 Parameters;
    }
    private readonly record struct TextureKey(TextureView A, TextureView B, TextureView Sky, TextureView Reflection);

    private const string VertexSource = """
        layout(set=0,binding=0,std140) uniform WaterFrame { mat4 world; mat4 wvp; mat4 reflectionVP; vec4 eyeTime; vec4 deepAlpha; vec4 reflectionSky; vec4 parameters; } frame;
        layout(location=0) in vec3 in_Pos; layout(location=1) in vec2 in_Uv; layout(location=2) in vec3 in_Normal;
        layout(location=0) out vec2 vs_Uv; layout(location=1) out vec3 vs_World; layout(location=2) out vec3 vs_Normal; layout(location=3) out vec4 vs_Reflection;
        void main(){ vec4 wp=frame.world*vec4(in_Pos,1); gl_Position=frame.wvp*vec4(in_Pos,1); vs_Uv=in_Uv*frame.parameters.y; vs_World=wp.xyz; vs_Normal=normalize(mat3(frame.world)*in_Normal); vs_Reflection=frame.reflectionVP*wp; }
        """;
    private const string FragmentSource = """
        layout(set=0,binding=0,std140) uniform WaterFrame { mat4 world; mat4 wvp; mat4 reflectionVP; vec4 eyeTime; vec4 deepAlpha; vec4 reflectionSky; vec4 parameters; } frame;
        layout(set=0,binding=1) uniform texture2D normalA; layout(set=0,binding=2) uniform sampler samplerA;
        layout(set=0,binding=3) uniform texture2D normalB; layout(set=0,binding=4) uniform sampler samplerB;
        layout(set=0,binding=5) uniform texture2D skyTex; layout(set=0,binding=6) uniform sampler skySampler;
        layout(set=0,binding=7) uniform texture2D reflectionTex; layout(set=0,binding=8) uniform sampler reflectionSampler;
        layout(set=0,binding=9,std140) uniform WaterRipples { vec4 data[97]; } ripples;
        layout(location=0) in vec2 vs_Uv; layout(location=1) in vec3 vs_World; layout(location=2) in vec3 vs_Normal; layout(location=3) in vec4 vs_Reflection;
        layout(location=0) out vec4 out_Color; const float PI=3.14159265359;
        vec2 skyUv(vec3 d){ d=normalize(d); return vec2(fract(atan(d.z,d.x)/(2.0*PI)+0.5),0.5-asin(clamp(d.y,-1.0,1.0))/PI); }
        void main(){
            float t=frame.eyeTime.w; vec3 na=texture(sampler2D(normalA,samplerA),vs_Uv*.1+vec2(t)).xyz;
            vec3 nb=texture(sampler2D(normalB,samplerB),vs_Uv*.1+vec2(t)).xyz;
            vec3 n=normalize(vs_Normal+(mix(nb,na,frame.parameters.x)*2.0-1.0).xzy*.55);
            vec4 rippleSettings=ripples.data[96]; float ripple=0.0; float rippleHighlight=0.0;
            for(int i=0;i<48;i++){ vec4 centerAge=ripples.data[i*2]; vec4 radiusStrength=ripples.data[i*2+1];
                float d=distance(vs_World.xz,centerAge.xy); float wave=sin(d*rippleSettings.z-centerAge.z*rippleSettings.y);
                float envelope=exp(-(centerAge.z/max(rippleSettings.x,.001))*2.4)*exp(-pow(d/max(centerAge.w,.001),2.0));
                ripple+=wave*envelope*radiusStrength.x; rippleHighlight+=max(wave,0.0)*envelope*radiusStrength.x; }
            n=normalize(n+vec3(cos(vs_World.x*8.0+t)*ripple*rippleSettings.w,abs(ripple)*rippleSettings.w*1.15,sin(vs_World.z*8.0+t)*ripple*rippleSettings.w));
            vec3 incident=normalize(vs_World-frame.eyeTime.xyz); vec3 reflected=reflect(incident,n);
            float fresnel=pow(1.0-max(dot(normalize(frame.eyeTime.xyz-vs_World),n),0.0),5.0);
            vec3 gradient=mix(frame.deepAlpha.rgb*.72,frame.reflectionSky.rgb,clamp(reflected.y*.5+.5,0.0,1.0));
            vec3 sky=texture(sampler2D(skyTex,skySampler),skyUv(reflected)).rgb;
            vec3 reflection=mix(gradient,sky,frame.reflectionSky.a*frame.parameters.z);
            vec2 ruv=vs_Reflection.xy/max(abs(vs_Reflection.w),.0001)*.5+.5; ruv.y=1.0-ruv.y; ruv+=n.xz*.035;
            float inside=step(0,ruv.x)*step(ruv.x,1)*step(0,ruv.y)*step(ruv.y,1);
            vec3 planar=texture(sampler2D(reflectionTex,reflectionSampler),clamp(ruv,.001,.999)).rgb;
            reflection=mix(reflection,planar,frame.parameters.w*inside*.65);
            vec3 color=mix(frame.deepAlpha.rgb,reflection,mix(.18,.35+fresnel*.65,frame.parameters.z));
            color=mix(color,mix(frame.deepAlpha.rgb,frame.reflectionSky.rgb,.30),.42);
            color+=vec3(.22,.25,.28)*clamp(rippleHighlight,0.0,1.0);
            out_Color=vec4(clamp(color,0.0,1.0),frame.deepAlpha.a);
        }
        """;
}
