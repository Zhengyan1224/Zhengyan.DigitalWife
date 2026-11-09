using System.Numerics;
using System.Reflection;
using Veldrid;
using Veldrid.SPIRV;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal static unsafe class VulkanWaterRippleTests
{
    public static void TestPrecomputedRipples()
    {
        if (!VulkanRenderer.IsSupported(out string reason)) { Console.WriteLine($"SKIP water ripples: {reason}"); return; }
        using var device = Veldrid.GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions());
        ResourceFactory factory = device.ResourceFactory;
        string source = (string)typeof(VeldridWaterRenderer).GetField("FragmentSource", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        int start = source.IndexOf("vec2 EvaluateRipples(", StringComparison.Ordinal);
        string helper = source[start..source.IndexOf("void main()", start, StringComparison.Ordinal)];
        Shader[] shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("ripple-test.vert", """
                void main(){ vec2 p=gl_VertexIndex==0?vec2(-1,-1):gl_VertexIndex==1?vec2(3,-1):vec2(-1,3); gl_Position=vec4(p,0,1); }
                """, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("ripple-test.frag", """
                layout(set=0,binding=0,std140) uniform PackedRipples { vec4 data[97]; } ripples;
                layout(set=0,binding=1,std140) uniform OriginalRipples { vec4 data[97]; } original;
                layout(location=0) out vec4 color;
                """ + helper + """
                vec2 ReferenceRipples(vec3 worldPosition){
                    vec4 settings=original.data[96]; float ripple=0.0,highlight=0.0;
                    for(int i=0;i<48;i++){
                        vec4 centerAge=original.data[i*2]; vec4 radiusStrength=original.data[i*2+1];
                        float d=distance(worldPosition.xz,centerAge.xy);
                        float wave=sin(d*settings.z-centerAge.z*settings.y);
                        float envelope=exp(-(centerAge.z/max(settings.x,.001))*2.4)*exp(-pow(d/max(centerAge.w,.001),2.0));
                        ripple+=wave*envelope*radiusStrength.x; highlight+=max(wave,0.0)*envelope*radiusStrength.x;
                    }
                    return vec2(ripple,highlight);
                }
                void main(){
                    vec3 world=vec3((gl_FragCoord.x/64.0-.5)*6.0,0,(gl_FragCoord.y/64.0-.5)*6.0);
                    vec2 expected=ReferenceRipples(world),actual=EvaluateRipples(world);
                    color=vec4(abs(actual-expected),max(abs(expected.x),abs(expected.y)),1);
                }
                """, ShaderStages.Fragment));
        using Shader vertex = shaders[0], fragment = shaders[1];
        using DeviceBuffer packed = factory.CreateBuffer(new BufferDescription(97 * 16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        using DeviceBuffer original = factory.CreateBuffer(new BufferDescription(97 * 16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        using ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Packed", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("Original", ResourceKind.UniformBuffer, ShaderStages.Fragment)));
        using ResourceSet set = factory.CreateResourceSet(new ResourceSetDescription(layout, packed, original));
        using Texture color = factory.CreateTexture(TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
        using Texture capture = factory.CreateTexture(TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));
        using Framebuffer target = factory.CreateFramebuffer(new(null, color));
        using Pipeline pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend, DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList, new ShaderSetDescription([], shaders), [layout], target.OutputDescription));
        using CommandList commands = factory.CreateCommandList();
        foreach (int count in new[] { 0, 1, 5, 17, 48 })
        foreach (float lifetime in new[] { .05f, 10f })
        {
            Vector4[] raw = new Vector4[97], compact = new Vector4[97];
            for (int i = 0; i < 48; i++) raw[i * 2] = new(0, 0, 999, 0);
            raw[^1] = new(lifetime, 18, 24, .3f);
            for (int i = 0; i < count; i++)
            {
                int index = count == 1 ? 47 : i * 17 % 48;
                raw[index * 2] = new((i % 7 - 3) * .4f, (i % 5 - 2) * .3f, i % 11 * .17f, .25f + i % 9 * .4f);
                raw[index * 2 + 1] = new(i == 0 ? .0005f : i % 3 == 0 ? -.25f : 1.2f, 0, 0, 0);
            }
            Check(VeldridWaterRenderer.PackRipples(raw, compact) == count && compact[^1].Y == count,
                "Packing omitted nonzero strengths or counted unused slots.");
            device.UpdateBuffer(packed, 0, compact); device.UpdateBuffer(original, 0, raw);
            commands.Begin(); commands.SetFramebuffer(target); commands.ClearColorTarget(0, RgbaFloat.Black);
            commands.SetFullViewports(); commands.SetFullScissorRects(); commands.SetPipeline(pipeline);
            commands.SetGraphicsResourceSet(0, set); commands.Draw(3); commands.CopyTexture(color, capture);
            commands.End(); device.SubmitCommands(commands); device.WaitForIdle();
            MappedResource pixels = device.Map(capture, MapMode.Read);
            float maximum = 0;
            try
            {
                for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float* value = (float*)((byte*)pixels.Data + y * pixels.RowPitch + x * 16);
                    float error = Math.Max(value[0], value[1]);
                    maximum = Math.Max(maximum, error);
                    if (!float.IsFinite(error) || error > .0002f + value[2] * .0001f)
                        throw new InvalidOperationException($"Ripple math changed at {x},{y}, count={count}, lifetime={lifetime}: error={error}.");
                }
            }
            finally { device.Unmap(capture); }
            Console.WriteLine($"PASS ripple GPU comparison count={count}, lifetime={lifetime}, maximum error={maximum:G6}");
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
