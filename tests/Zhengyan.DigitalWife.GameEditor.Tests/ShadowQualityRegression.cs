using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Silk.NET.Windowing;
using Veldrid;
using Veldrid.SPIRV;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using GL = Silk.NET.OpenGLES.GL;
using GLEnum = Silk.NET.OpenGLES.GLEnum;
using GraphicsBackend = Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend;

internal static unsafe class ShadowQualityRegression
{
    private const int MapSize = 16, Width = 64, Height = 32;
    private static IEnumerable<(int Samples, float Radius)> Cases =>
        GameProjectShadowSettings.SampleChoices.SelectMany(samples => new[] { 0f, 1f, 3f, 8f }.Select(radius => (samples, radius)));

    public static void Serialization()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Check(JsonSerializer.Deserialize<GameProject>("{}", options)!.Shadows is null, "Old projects must retain their platform defaults.");
        GameProject project = new() { Shadows = new() { DirectionalResolution = 4096, LocalLightResolution = 512, FilterSamples = 25, FilterRadius = 3 } };
        string json = JsonSerializer.Serialize(project, options);
        GameProject saved = JsonSerializer.Deserialize<GameProject>(json, options)!;
        Check(saved.Shadows is { DirectionalResolution: 4096, LocalLightResolution: 512, FilterSamples: 25, FilterRadius: 3 }, "Global shadow quality was not serialized.");
        Check(!json.Contains("normalized", StringComparison.OrdinalIgnoreCase), "Derived renderer values were saved as author settings.");
        GameProjectShadowSettings invalid = new() { DirectionalResolution = 1500, LocalLightResolution = 8192, FilterSamples = 8, FilterRadius = float.NaN };
        Check(invalid.NormalizedDirectionalResolution == 1024 && invalid.NormalizedLocalLightResolution == 2048
            && invalid.NormalizedFilterSamples == 4 && invalid.NormalizedFilterRadius == 1, "Invalid settings were not bounded.");
    }

    public static void OpenGl()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        string source = File.ReadAllText(Path.Combine(root, "assets/mmd/engine/Resources/Shader/pmx_model.frag"));
        string helper = Slice(source, "int ShadowFilterSide(", "float SampleShadowMap(");
        string sampling = Slice(source, "float SampleShadowMap(", "float ComputeLocalShadowDepthBias(");
        WindowOptions options = WindowOptions.Default;
        RendererFactory.ConfigureWindow(ref options, GraphicsBackend.OpenGL);
        options.IsVisible = false; options.Size = new(Width, Height); options.Samples = 1;
        using IWindow window = Window.Create(options); window.Initialize(); window.MakeCurrent();
        using GL gl = GL.GetApi(window);
        using RenderTexture target = new(gl, "ShadowQualityCoverage"); target.EnsureSize(Width, Height);
        uint map = gl.GenTexture(), vao = gl.GenVertexArray(), program = gl.CreateProgram();
        uint vertex = Compile(GLEnum.VertexShader, "#version 300 es\n" + Vertex(false));
        uint fragment = Compile(GLEnum.FragmentShader, """
            #version 300 es
            precision highp float;
            precision highp sampler2DShadow;
            uniform sampler2DShadow u_ShadowMap;
            uniform vec2 u_ShadowMapTexelSize;
            uniform vec2 u_ShadowFilterParameters;
            out vec4 color;
            float ComputeDirectionalShadowDepthBias(float n) { return 0.0; }
            """ + helper + sampling + """
            void main() {
                vec2 uv = gl_FragCoord.xy / vec2(64,32);
                float coverage = SampleShadowMap(u_ShadowMap, vec4(uv * 2.0 - 1.0, 0, 1), 1.0);
                color = vec4(vec3(coverage),1);
            }
            """);
        try
        {
            gl.AttachShader(program, vertex); gl.AttachShader(program, fragment); gl.LinkProgram(program);
            gl.GetProgram(program, GLEnum.LinkStatus, out int linked); Check(linked != 0, gl.GetProgramInfoLog(program));
            uint[] depths = Enumerable.Range(0, MapSize * MapSize).Select(i => (uint)(Depth(i % MapSize, i / MapSize) * uint.MaxValue)).ToArray();
            gl.ActiveTexture(GLEnum.Texture0); gl.BindTexture(GLEnum.Texture2D, map);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
            gl.TexParameter(GLEnum.Texture2D, (GLEnum)0x884C, 0x884E);
            gl.TexParameter(GLEnum.Texture2D, (GLEnum)0x884D, 0x0203);
            fixed (uint* data = depths) gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.DepthComponent24, MapSize, MapSize, 0, GLEnum.DepthComponent, GLEnum.UnsignedInt, data);
            gl.BindVertexArray(vao); gl.UseProgram(program); gl.Disable(GLEnum.Blend);
            gl.SetUniform(gl.GetUniformLocation(program, "u_ShadowMap"), 0);
            gl.SetUniform(gl.GetUniformLocation(program, "u_ShadowMapTexelSize"), new Vector2(1f / MapSize));
            foreach ((int samples, float radius) in Cases)
            {
                gl.SetUniform(gl.GetUniformLocation(program, "u_ShadowFilterParameters"), new Vector2(samples, radius));
                target.BeginPass(Vector4.Zero); gl.DrawArrays(GLEnum.Triangles, 0, 3);
                byte[] pixels = new byte[Width * Height * 4];
                fixed (byte* data = pixels) gl.ReadPixels(0, 0, Width, Height, GLEnum.Rgba, GLEnum.UnsignedByte, data);
                Verify(pixels, samples, radius);
            }
            Check(gl.GetError() == GLEnum.NoError, "OpenGL shadow filtering raised a GL error.");
            Console.WriteLine("PASS OpenGL production shadow filter: six sample levels, four radii, CPU pixel comparison");
        }
        finally
        {
            gl.UseProgram(0); gl.BindVertexArray(0); gl.BindFramebuffer(GLEnum.Framebuffer, 0);
            gl.DeleteProgram(program); gl.DeleteShader(vertex); gl.DeleteShader(fragment); gl.DeleteVertexArray(vao); gl.DeleteTexture(map);
        }
        uint Compile(GLEnum stage, string text)
        {
            uint shader = gl.CreateShader(stage); gl.ShaderSource(shader, text); gl.CompileShader(shader);
            gl.GetShader(shader, GLEnum.CompileStatus, out int compiled); Check(compiled != 0, gl.GetShaderInfoLog(shader)); return shader;
        }
    }

    public static void Vulkan()
    {
        if (!VulkanRenderer.IsSupported(out string reason)) { Console.WriteLine($"SKIP Vulkan shadow quality: {reason}"); return; }
        Type production = typeof(VulkanRenderer).Assembly.GetType("Zhengyan.DigitalWife.Mmd.Game.Pmx.VeldridPmxMainPassRenderer")!;
        string source = (string)production.GetField("FragmentShaderSource", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        string helper = Slice(source, "int ShadowFilterSide(", "float SampleShadowMap(");
        string sampling = Slice(source, "float SampleShadowMap(", "float ComputeLocalShadowDepthBias(");
        using var device = Veldrid.GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions());
        ResourceFactory factory = device.ResourceFactory;
        using CommandList commands = factory.CreateCommandList();
        using Texture map = factory.CreateTexture(TextureDescription.Texture2D(MapSize,MapSize,1,1,PixelFormat.R32_Float,TextureUsage.Sampled));
        device.UpdateTexture(map, Enumerable.Range(0, MapSize * MapSize).Select(i => Depth(i % MapSize,i / MapSize)).ToArray(), 0,0,0,MapSize,MapSize,1,0,0);
        using TextureView mapView = factory.CreateTextureView(map);
        using Sampler sampler = factory.CreateSampler(new SamplerDescription(SamplerAddressMode.Clamp,SamplerAddressMode.Clamp,SamplerAddressMode.Clamp,
            SamplerFilter.MinPoint_MagPoint_MipPoint,null,0,0,uint.MaxValue,0,SamplerBorderColor.OpaqueWhite));
        using DeviceBuffer filter = factory.CreateBuffer(new BufferDescription(16,BufferUsage.UniformBuffer));
        using Texture color = factory.CreateTexture(TextureDescription.Texture2D(Width,Height,1,1,PixelFormat.R8_G8_B8_A8_UNorm,TextureUsage.RenderTarget));
        using Texture capture = factory.CreateTexture(TextureDescription.Texture2D(Width,Height,1,1,PixelFormat.R8_G8_B8_A8_UNorm,TextureUsage.Staging));
        using Framebuffer framebuffer = factory.CreateFramebuffer(new(null,color));
        using ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Filter",ResourceKind.UniformBuffer,ShaderStages.Fragment),
            new ResourceLayoutElementDescription("Map",ResourceKind.TextureReadOnly,ShaderStages.Fragment),
            new ResourceLayoutElementDescription("Sampler",ResourceKind.Sampler,ShaderStages.Fragment)));
        using ResourceSet set = factory.CreateResourceSet(new ResourceSetDescription(layout,filter,mapView,sampler));
        Shader[] shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("shadow-quality.vert",Vertex(true),ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("shadow-quality.frag", """
                layout(set=0,binding=0,std140) uniform FilterParameters { vec4 value; } samplingParameters;
                layout(set=0,binding=1) uniform texture2D u_ShadowMap;
                layout(set=0,binding=2) uniform sampler u_ShadowSampler;
                struct TestFrame { vec4 u_Parameters; vec4 u_ShadowParameters; vec4 u_ShadowFilterParameters; };
                TestFrame u_Frame;
                vec4 vs_ShadowPos;
                layout(location=0) out vec4 color;
                float ComputeDirectionalShadowDepthBias(float n) { return 0.0; }
                """ + helper + sampling + """
                void main() {
                    u_Frame.u_Parameters = vec4(0,0,0,1);
                    u_Frame.u_ShadowParameters = vec4(0,0,0,1.0/16.0);
                    u_Frame.u_ShadowFilterParameters = samplingParameters.value;
                    vec2 uv = gl_FragCoord.xy / vec2(64,32);
                    vs_ShadowPos = vec4(uv * 2.0 - 1.0, 0.5, 1);
                    float coverage = SampleShadowMap(1.0);
                    color = vec4(vec3(coverage),1);
                }
                """,ShaderStages.Fragment));
        using Shader vertex = shaders[0], fragment = shaders[1];
        using Pipeline pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,RasterizerStateDescription.CullNone,PrimitiveTopology.TriangleList,
            new ShaderSetDescription([],shaders),[layout],framebuffer.OutputDescription));
        foreach ((int samples,float radius) in Cases)
        {
            device.UpdateBuffer(filter,0,new Vector4(samples,radius,0,0));
            commands.Begin(); commands.SetFramebuffer(framebuffer); commands.SetFullViewports(); commands.SetFullScissorRects();
            commands.SetPipeline(pipeline); commands.SetGraphicsResourceSet(0,set); commands.Draw(3); commands.CopyTexture(color,capture);
            commands.End(); device.SubmitCommands(commands); device.WaitForIdle();
            MappedResource mapped = device.Map(capture,MapMode.Read);
            try
            {
                byte[] pixels = new byte[Width * Height * 4];
                for (int y=0;y<Height;y++) Marshal.Copy(mapped.Data+(int)(y*mapped.RowPitch),pixels,y*Width*4,Width*4);
                Verify(pixels,samples,radius);
            }
            finally { device.Unmap(capture); }
        }
        Console.WriteLine("PASS Vulkan production shadow filter: six sample levels, four radii, CPU pixel comparison");
    }

    private static string Slice(string text,string from,string to)
    {
        int start=text.IndexOf(from,StringComparison.Ordinal); return text[start..text.IndexOf(to,start,StringComparison.Ordinal)];
    }
    private static string Vertex(bool vulkan) => "void main(){ vec2 p=" + (vulkan?"gl_VertexIndex":"gl_VertexID")
        + "==0?vec2(-1,-1):" + (vulkan?"gl_VertexIndex":"gl_VertexID") + "==1?vec2(3,-1):vec2(-1,3); gl_Position=vec4(p,0,1); }";
    private static float Depth(int x,int y) => (x<MapSize/2) != (y<MapSize/2) ? .25f : .75f;
    private static void Verify(byte[] pixels,int samples,float radius)
    {
        int side=(int)Math.Sqrt(samples);
        for(int y=0;y<Height;y++) for(int x=0;x<Width;x++)
        {
            float expected=0;
            for(int j=0;j<side;j++) for(int i=0;i<side;i++)
            {
                float u=Math.Clamp((x+.5f)/Width+(side==1?0:(2f*i/(side-1)-1)*radius/MapSize),1f/MapSize,1-1f/MapSize);
                float v=Math.Clamp((y+.5f)/Height+(side==1?0:(2f*j/(side-1)-1)*radius/MapSize),1f/MapSize,1-1f/MapSize);
                float px=u*MapSize-.5f,py=v*MapSize-.5f;
                int a=(int)MathF.Floor(px),b=(int)MathF.Floor(py); float fx=px-a,fy=py-b;
                float Visible(int ix,int iy) => Depth(Math.Clamp(ix,0,MapSize-1),Math.Clamp(iy,0,MapSize-1))>=.5f?1:0;
                expected+=(Visible(a,b)*(1-fx)+Visible(a+1,b)*fx)*(1-fy)+(Visible(a,b+1)*(1-fx)+Visible(a+1,b+1)*fx)*fy;
            }
            expected=expected/samples*255;
            int index=(y*Width+x)*4;
            Check(MathF.Abs(pixels[index]-expected)<=2 && pixels[index]==pixels[index+1] && pixels[index]==pixels[index+2] && pixels[index+3]==255,
                $"Shadow coverage samples={samples}, radius={radius}, pixel={x},{y}: {pixels[index]} != {expected}");
        }
    }
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
