using System.Numerics;
using System.Reflection;
using Veldrid;
using Veldrid.SPIRV;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

internal static unsafe class VulkanShadowGatherTests
{
    public static void TestGatherCoverage()
    {
        if (!VulkanRenderer.IsSupported(out string reason))
        {
            Console.WriteLine($"SKIP Vulkan shadow gather: {reason}");
            return;
        }
        using var device = Veldrid.GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions());
        ResourceFactory factory = device.ResourceFactory;
        using CommandList commands = factory.CreateCommandList();
        // Compile the actual production helper, and compare its pixels against
        // an independent CPU bilinear comparison reference in both dimensions.
        string source = (string)typeof(VeldridPmxMainPassRenderer)
            .GetField("FragmentShaderSource", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        int start = source.IndexOf("float SampleBilinearShadow(", StringComparison.Ordinal);
        int end = source.IndexOf("float SampleShadowMap(", start, StringComparison.Ordinal);
        string helper = source[start..end];
        Shader[] shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("gather-test.vert", VertexSource, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("gather-test.frag", helper + """
                layout(set=0,binding=0) uniform texture2D map;
                layout(set=0,binding=1) uniform sampler mapSampler;
                layout(location=0) out vec4 color;
                void main() {
                    vec2 uv = gl_FragCoord.xy / vec2(96,64) * 1.5 - 0.25;
                    float depth = int(gl_FragCoord.x) % 2 == 0 ? 0.5 : 0.3125;
                    float coverage = SampleBilinearShadow(map, mapSampler, uv, depth);
                    color = vec4(coverage, coverage, coverage, 1);
                }
                """, ShaderStages.Fragment));
        using Shader vertex = shaders[0];
        using Shader fragment = shaders[1];
        using ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Map", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MapSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        using Sampler sampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinPoint_MagPoint_MipPoint, null, 0, 0, uint.MaxValue, 0, SamplerBorderColor.OpaqueWhite));
        using Texture color = factory.CreateTexture(TextureDescription.Texture2D(96, 64, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        using Texture capture = factory.CreateTexture(TextureDescription.Texture2D(96, 64, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
        using Framebuffer target = factory.CreateFramebuffer(new(null, color));
        using Pipeline pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend, DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone, PrimitiveTopology.TriangleList,
            new ShaderSetDescription([], shaders), [layout], target.OutputDescription));

        foreach (bool depthTexture in new[] { false, true })
        foreach ((uint width, uint height) in new[] { (7u, 5u), (1u, 1u), (16u, 8u) })
        {
            using Texture map = factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1,
                depthTexture ? PixelFormat.D24_UNorm_S8_UInt : PixelFormat.R32_Float,
                depthTexture ? TextureUsage.DepthStencil | TextureUsage.Sampled : TextureUsage.Sampled));
            float[] depths = new float[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) depths[y * width + x] = Depth(x, y);
            if (depthTexture) PopulateDepth(map);
            else device.UpdateTexture(map, depths, 0, 0, 0, width, height, 1, 0, 0);
            using TextureView view = factory.CreateTextureView(map);
            using ResourceSet set = factory.CreateResourceSet(new ResourceSetDescription(layout, view, sampler));
            commands.Begin(); commands.SetFramebuffer(target); commands.ClearColorTarget(0, RgbaFloat.Black);
            commands.SetFullViewports(); commands.SetFullScissorRects();
            commands.SetPipeline(pipeline); commands.SetGraphicsResourceSet(0, set); commands.Draw(3);
            commands.CopyTexture(color, capture); commands.End(); device.SubmitCommands(commands); device.WaitForIdle();
            MappedResource pixels = device.Map(capture, MapMode.Read);
            try
            {
                for (int y = 0; y < 64; y++)
                for (int x = 0; x < 96; x++)
                {
                    Vector2 uv = new((x + .5f) / 96 * 1.5f - .25f, (y + .5f) / 64 * 1.5f - .25f);
                    Vector2 pixel = uv * new Vector2(width, height) - new Vector2(.5f);
                    int i = (int)MathF.Floor(pixel.X), j = (int)MathF.Floor(pixel.Y);
                    float fx = pixel.X - i, fy = pixel.Y - j, depth = x % 2 == 0 ? .5f : .3125f;
                    float a = Visibility(i, j), b = Visibility(i + 1, j), c = Visibility(i, j + 1), d = Visibility(i + 1, j + 1);
                    float expected = ((a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy) * 255;
                    byte actual = *((byte*)pixels.Data + y * pixels.RowPitch + x * 4);
                    if (MathF.Abs(actual - expected) > 2)
                        throw new InvalidOperationException($"Gather coverage depthTexture={depthTexture}, size={width}x{height}, pixel={x},{y}: {actual} != {expected}.");

                    float Visibility(int u, int v) => depth <= depths[Math.Clamp(v, 0, (int)height - 1) * width + Math.Clamp(u, 0, (int)width - 1)] ? 1 : 0;
                }
            }
            finally { device.Unmap(capture); }
            Console.WriteLine($"PASS shadow gather depthTexture={depthTexture}, size={width}x{height}, pixels=6144");
        }

        void PopulateDepth(Texture map)
        {
            using Framebuffer framebuffer = factory.CreateFramebuffer(new(map));
            Shader[] depthShaders = factory.CreateFromSpirv(
                VulkanShaderCompiler.CompileSource("gather-depth.vert", VertexSource, ShaderStages.Vertex),
                VulkanShaderCompiler.CompileSource("gather-depth.frag", """
                    void main() { gl_FragDepth = float((int(gl_FragCoord.x) * 3 + int(gl_FragCoord.y) * 5) % 9) / 8.0; }
                    """, ShaderStages.Fragment));
            using Shader depthVertex = depthShaders[0];
            using Shader depthFragment = depthShaders[1];
            using Pipeline depthPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.Empty, new DepthStencilStateDescription(true, true, ComparisonKind.Always),
                RasterizerStateDescription.CullNone, PrimitiveTopology.TriangleList,
                new ShaderSetDescription([], depthShaders), [], framebuffer.OutputDescription));
            commands.Begin(); commands.SetFramebuffer(framebuffer); commands.ClearDepthStencil(1);
            commands.SetFullViewports(); commands.SetFullScissorRects(); commands.SetPipeline(depthPipeline); commands.Draw(3);
            commands.End(); device.SubmitCommands(commands); device.WaitForIdle();
        }
    }

    private static float Depth(int x, int y) => (x * 3 + y * 5) % 9 / 8f;
    private const string VertexSource = """
        void main() {
            vec2 pos = gl_VertexIndex == 0 ? vec2(-1,-1) : gl_VertexIndex == 1 ? vec2(3,-1) : vec2(-1,3);
            gl_Position = vec4(pos,0,1);
        }
        """;
}
