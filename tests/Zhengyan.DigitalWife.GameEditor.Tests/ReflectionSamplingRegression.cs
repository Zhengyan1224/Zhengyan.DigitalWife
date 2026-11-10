using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using GL = Silk.NET.OpenGLES.GL;
using GLEnum = Silk.NET.OpenGLES.GLEnum;
using Silk.NET.Windowing;
using Veldrid;
using Veldrid.SPIRV;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using GraphicsBackend = Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend;

internal static unsafe class ReflectionSamplingRegression
{
    private const string VertexBody = """
        vec2 positions[3] = vec2[3](vec2(-.81,-.77),vec2(.86,-.31),vec2(-.37,.91));
        """;

    public static void Serialization()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        GameProjectScene scene = new() { Entities = [
            new() { Type = "textured_plane", Plane = new() { MirrorReflectionSamples = 8 } },
            new() { Type = "water_surface", Water = new() { MirrorReflectionSamples = 2 } }] };
        GameProjectScene saved = JsonSerializer.Deserialize<GameProjectScene>(JsonSerializer.Serialize(scene, options), options)!;
        Check(saved.Entities[0].Plane.MirrorReflectionSamples == 8 && saved.Entities[1].Water.MirrorReflectionSamples == 2,
            "Scene serialization lost independent reflection sample levels.");
        Check(JsonSerializer.Deserialize<WaterSurfaceSettings>("{}", options)!.MirrorReflectionSamples == 1
            && JsonSerializer.Deserialize<TexturedPlaneSettings>("{}", options)!.MirrorReflectionSamples == 1,
            "Old projects must keep the previous 1x reflection behavior.");
    }

    public static void OpenGl()
    {
        WindowOptions options = WindowOptions.Default;
        RendererFactory.ConfigureWindow(ref options, GraphicsBackend.OpenGL);
        options.IsVisible = false; options.Size = new(64, 64); options.Samples = 1;
        using IWindow window = Window.Create(options);
        window.Initialize(); window.MakeCurrent();
        using GL gl = GL.GetApi(window);
        using RenderTexture target = new(gl, "ReflectionSamplingTest");
        uint vertex = Compile(GLEnum.VertexShader, "#version 300 es\n" + VertexBody
            + "void main(){ gl_Position=vec4(positions[gl_VertexID],0,1); }");
        uint fragment = Compile(GLEnum.FragmentShader,
            "#version 300 es\nprecision highp float; out vec4 color; void main(){ color=vec4(1,0,0,1); }");
        uint program = gl.CreateProgram(), vao = gl.GenVertexArray();
        try
        {
            gl.AttachShader(program, vertex); gl.AttachShader(program, fragment); gl.LinkProgram(program);
            gl.GetProgram(program, GLEnum.LinkStatus, out int linked);
            Check(linked != 0, gl.GetProgramInfoLog(program));
            gl.BindVertexArray(vao); gl.UseProgram(program); gl.Disable(GLEnum.Blend);
            foreach ((int requested, int height) in new[] { (1,64), (16,64), (2,96), (4,96), (1,64) })
            {
                target.EnsureSize(64, height, requested);
                Check(target.SampleCount <= requested, "Reflection MSAA exceeded the requested level.");
                target.BeginPass(new(0,0,0,1)); gl.DrawArrays(GLEnum.Triangles, 0, 3); target.EndPass();
                gl.BindFramebuffer(GLEnum.Framebuffer, target.FramebufferId);
                byte[] pixels = new byte[64 * height * 4];
                fixed (byte* p = pixels) gl.ReadPixels(0, 0, 64, (uint)height, GLEnum.Rgba, GLEnum.UnsignedByte, p);
                VerifyPixels(pixels, target.SampleCount);
                Check(gl.GetError() == GLEnum.NoError, "OpenGL reflection allocation or resolve raised a GL error.");
                Console.WriteLine($"PASS OpenGL reflection requested={requested}x, actual={target.SampleCount}x, size=64x{height}");
            }
        }
        finally
        {
            gl.BindFramebuffer(GLEnum.Framebuffer, 0); gl.BindVertexArray(0); gl.UseProgram(0);
            gl.DeleteVertexArray(vao); gl.DeleteProgram(program); gl.DeleteShader(vertex); gl.DeleteShader(fragment);
        }
        uint Compile(GLEnum stage, string source)
        {
            uint shader = gl.CreateShader(stage); gl.ShaderSource(shader, source); gl.CompileShader(shader);
            gl.GetShader(shader, GLEnum.CompileStatus, out int compiled);
            Check(compiled != 0, gl.GetShaderInfoLog(shader)); return shader;
        }
    }

    public static void Vulkan()
    {
        if (!VulkanRenderer.IsSupported(out string reason)) { Console.WriteLine($"SKIP Vulkan reflections: {reason}"); return; }
        using var device = Veldrid.GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions());
        ResourceFactory factory = device.ResourceFactory;
        using CommandList commands = factory.CreateCommandList();
        using Texture mainColor = factory.CreateTexture(TextureDescription.Texture2D(64,64,1,1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        using Framebuffer main = factory.CreateFramebuffer(new(null, mainColor));
        VulkanRenderer renderer = new(); Set("_device", device); Set("_commandList", commands); Set("_multisampleFramebuffer", main);
        using VeldridRenderTarget target = new(renderer, "ReflectionSamplingTest");
        Shader[] shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("reflection-test.vert", VertexBody
                + "void main(){ gl_Position=vec4(positions[gl_VertexIndex],0,1); }", ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("reflection-test.frag",
                "layout(location=0) out vec4 color; void main(){ color=vec4(1,0,0,1); }", ShaderStages.Fragment));
        using Shader vertex = shaders[0], fragment = shaders[1];
        foreach ((int requested, int height) in new[] { (1,64), (16,64), (2,96), (4,96), (1,64) })
        {
            target.EnsureSize(64, height, requested);
            Check(target.SampleCount <= requested, "Reflection MSAA exceeded the requested level.");
            Texture color = ((TextureView)target.NativeColorResource!).Target;
            Check(color.SampleCount == TextureSampleCount.Count1, "The sampled reflection texture must be resolved to 1x.");
            Framebuffer framebuffer = (Framebuffer)typeof(VeldridRenderTarget).GetProperty("Framebuffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
            using Pipeline pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend, DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone,
                PrimitiveTopology.TriangleList, new ShaderSetDescription([], shaders), [], framebuffer.OutputDescription));
            using Texture capture = factory.CreateTexture(TextureDescription.Texture2D(64,(uint)height,1,1,
                PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
            target.BeginPass(new(0,0,0,1)); commands.SetPipeline(pipeline); commands.Draw(3); target.EndPass();
            commands.CopyTexture(color, capture); commands.End(); device.SubmitCommands(commands); device.WaitForIdle(); Set("_frameOpen", false);
            MappedResource mapped = device.Map(capture, MapMode.Read);
            try
            {
                byte[] pixels = new byte[64 * height * 4];
                for (int y = 0; y < height; y++) Marshal.Copy(mapped.Data + (int)(y * mapped.RowPitch), pixels, y * 64 * 4, 64 * 4);
                VerifyPixels(pixels, target.SampleCount);
            }
            finally { device.Unmap(capture); }
            Console.WriteLine($"PASS Vulkan reflection requested={requested}x, actual={target.SampleCount}x, size=64x{height}");
        }
        void Set(string name, object value) => typeof(VulkanRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, value);
    }

    private static void VerifyPixels(byte[] pixels, int samples)
    {
        int solid = 0, partial = 0, background = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            Check(pixels[i+1] == 0 && pixels[i+2] == 0 && pixels[i+3] == 255, "Resolved reflection has corrupt color or alpha.");
            if (pixels[i] == 255) solid++; else if (pixels[i] == 0) background++; else partial++;
        }
        Check(solid > 100 && background > 100, "Resolved reflection lost the scene or background.");
        Check(samples == 1 ? partial == 0 : partial > 0, "Triangle edge coverage does not match the reflection sample count.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
