extern alias MmdDesktop;

using System.Numerics;
using System.Reflection;
using Veldrid;
using Veldrid.SPIRV;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using VertexBoneInfo = MmdDesktop::Zhengyan.DigitalWife.Mmd.VertexBoneInfo;
using SkinningType = MmdDesktop::Zhengyan.DigitalWife.Mmd.SkinningType;
using Device = Veldrid.GraphicsDevice;

internal static unsafe class VulkanSynchronizationRegressionTests
{
    public static void TestSkinningFrames()
        => TestFrames(false);

    public static void TestCpuFallbackFrames()
        => TestFrames(true);

    private static void TestFrames(bool forceCpuFallback)
    {
        if (!VulkanRenderer.IsSupported(out string reason))
        {
            Console.WriteLine($"SKIP native Vulkan skinning: {reason}");
            return;
        }

        // No window or Android Surface is needed. Exercise real compute -> copy
        // -> vertex fetch across submissions, including reuse of all three slots.
        using Device device = Device.CreateVulkan(new GraphicsDeviceOptions());
        // Borrow the device for the compute implementation; this test owns it.
        VulkanRenderer renderer = new();
        typeof(VulkanRenderer).GetField("_device", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, device);
        ResourceFactory factory = device.ResourceFactory;
        using VeldridGpuBuffer cpuPositions = new(renderer, new GpuBufferDescription(100012 * 12, GpuBufferKind.Vertex));
        DeviceBuffer positions = cpuPositions.NativeBuffer;
        using DeviceBuffer normals = factory.CreateBuffer(new BufferDescription(36, BufferUsage.VertexBuffer));
        using DeviceBuffer uvs = factory.CreateBuffer(new BufferDescription(24, BufferUsage.VertexBuffer));
        using VulkanPmxSkinningCompute compute = new(renderer, 3, 1);
        Check(compute.TryBindGpuOutput(positions, normals, uvs), "GPU output binding failed.");
        using Texture color = factory.CreateTexture(TextureDescription.Texture2D(64, 64, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        using Framebuffer framebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, color));
        using DeviceBuffer material = factory.CreateBuffer(new BufferDescription(32, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        using ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Material", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
        using ResourceSet materialSet = factory.CreateResourceSet(new ResourceSetDescription(layout, material));
        Shader[] shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("sync.vert", """
                #version 450
                layout(location=0) in vec3 Position;
                layout(set=0, binding=0) uniform Material { vec4 Tint; vec4 Offset; } Params;
                layout(location=0) out vec4 VertexColor;
                void main() { gl_Position = vec4(Position + Params.Offset.xyz, 1.0); VertexColor = Params.Tint; }
                """, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("sync.frag", """
                #version 450
                layout(location=0) out vec4 Color;
                layout(location=0) in vec4 VertexColor;
                void main() { Color = VertexColor; }
                """, ShaderStages.Fragment));
        using Shader vertexShader = shaders[0];
        using Shader fragmentShader = shaders[1];
        using Pipeline pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription([
                new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
            ], shaders), [layout], framebuffer.OutputDescription));
        using CommandList commands = factory.CreateCommandList();
        Texture[] snapshots = Enumerable.Range(0, 180).Select(_ => factory.CreateTexture(
            TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging))).ToArray();
        Vector3* inputPositions = stackalloc Vector3[3] { new(-0.3f, -0.4f, 0), new(0.3f, -0.4f, 0), new(0, 0.4f, 0) };
        Vector3* inputNormals = stackalloc Vector3[3] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        Vector2* inputUvs = stackalloc Vector2[3];
        Vector3* morphs = stackalloc Vector3[3];
        Vector4* uvMorphs = stackalloc Vector4[3];
        VertexBoneInfo* bones = stackalloc VertexBoneInfo[3];
        for (int i = 0; i < 3; i++)
        {
            inputUvs[i] = Vector2.Zero;
            morphs[i] = Vector3.Zero;
            uvMorphs[i] = Vector4.Zero;
            bones[i] = default;
            bones[i].SkinningType = SkinningType.Weight1;
            bones[i].BoneWeights[0] = 1;
        }
        Matrix4x4 transform;
        Vector3[] fallbackPositions = new Vector3[100012];
        try
        {
            for (int frame = 0; frame < snapshots.Length; frame++)
            {
                transform = Matrix4x4.CreateTranslation((frame & 1) == 0 ? -0.5f : 0.5f, 0, 0);
                if (frame < 90 || !forceCpuFallback)
                {
                    Check(compute.ExecuteGpu(3, 1, inputPositions, inputNormals, inputUvs, bones,
                        morphs, uvMorphs, &transform, &transform), $"GPU skinning failed on frame {frame}.");
                }
                else
                {
                    // Match a runtime GPU validation failure: retire Compute,
                    // then upload a large CPU pose to the same renderer buffer.
                    if (frame == 90) compute.Dispose();
                    for (int i = 0; i < 3; i++) fallbackPositions[i] = Vector3.Transform(inputPositions[i], transform);
                    cpuPositions.Update<Vector3>(fallbackPositions);
                }
                commands.Begin();
                commands.SetFramebuffer(framebuffer);
                commands.ClearColorTarget(0, RgbaFloat.Black);
                commands.SetFullViewports();
                commands.SetFullScissorRects();
                commands.SetPipeline(pipeline);
                commands.SetVertexBuffer(0, positions);
                commands.SetGraphicsResourceSet(0, materialSet);
                commands.UpdateGraphicsBuffer(material, 0, new MaterialData(new(0, 1, 0, 1), Vector4.Zero));
                commands.Draw(3);
                // Reuse the same uniform buffer within a frame as PMX materials
                // do. Both draws must keep their own color and transform.
                commands.UpdateGraphicsBuffer(material, 0, new MaterialData(new(0, 0, 1, 1),
                    new((frame & 1) == 0 ? 1 : -1, 0, 0, 0)));
                commands.Draw(3);
                commands.CopyTexture(color, snapshots[frame]);
                commands.End();
                device.SubmitCommands(commands);
            }
            device.WaitForIdle();
            for (int frame = 0; frame < snapshots.Length; frame++)
            {
                MappedResource pixels = device.Map(snapshots[frame], MapMode.Read);
                try
                {
                    byte* row = (byte*)pixels.Data + 32 * pixels.RowPitch;
                    int visibleX = (frame & 1) == 0 ? 16 : 48;
                    int emptyX = (frame & 1) == 0 ? 48 : 16;
                    Check(row[visibleX * 4 + 1] > 240 && row[visibleX * 4 + 2] < 10 &&
                        row[emptyX * 4 + 1] < 10 && row[emptyX * 4 + 2] > 240,
                        $"Frame {frame} read a stale or overwritten skinning pose or material.");
                }
                finally { device.Unmap(snapshots[frame]); }
            }
        }
        finally
        {
            device.WaitForIdle();
            foreach (Texture snapshot in snapshots) snapshot.Dispose();
        }
    }

    private readonly record struct MaterialData(Vector4 Tint, Vector4 Offset);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
