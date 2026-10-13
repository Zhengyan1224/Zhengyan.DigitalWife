using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Veldrid;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Device = Veldrid.GraphicsDevice;
using GraphicsBackend = Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend;

internal static unsafe class VulkanSceneUploadTests
{
    public static void TestReflectionSizes()
    {
        Check(AndroidRenderPolicy.ResolveReflectionSize(2400, 1080, "auto") == (1024, 461), "Landscape reflection exceeds GLES detail.");
        Check(AndroidRenderPolicy.ResolveReflectionSize(1080, 2400, "high") == (461, 1024), "Portrait reflection aspect changed.");
        Check(AndroidRenderPolicy.ResolveReflectionSize(2400, 1080, " LOW ") == (512, 230), "Low profile reflection is too large.");
        Check(AndroidRenderPolicy.ResolveReflectionSize(320, 180, null) == (320, 180), "Small camera was upscaled.");
        Check(AndroidRenderPolicy.ResolveReflectionSize(0, -1, "auto") == (1, 1), "Invalid viewport was not clamped.");
    }

    public static void TestScenePasses()
    {
        if (!VulkanRenderer.IsSupported(out string reason))
        {
            Console.WriteLine($"SKIP native scene uploads: {reason}");
            return;
        }
        using Device device = Device.CreateVulkan(new GraphicsDeviceOptions());
        Console.WriteLine($"Scene upload GPU: {device.DeviceName}");
        ResourceFactory factory = device.ResourceFactory;
        using CommandList commands = factory.CreateCommandList();
        VulkanRenderer renderer = new();
        Set("_device", device);
        Set("_commandList", commands);
        Set("_frameSlot", 2);
        using VeldridTexture2D white = new(renderer);
        using VeldridTexture2D normal = new(renderer);
        white.Fill(255, 255, 255);
        normal.Fill(128, 128, 255);
        using VeldridParticleRenderer particles = new(renderer, 256);
        using VeldridWaterRenderer water = new(renderer, 96, [0u, 1u, 2u]);
        using VeldridSkyboxRenderer sky = new(renderer);
        using VeldridScreenSpriteRenderer sprites = new(renderer);
        using VeldridUtilityPassRenderer utility = new(renderer);
        using Texture color = Color();
        using Texture mirrorColor = Color();
        using Texture depth = Depth();
        using Texture mirrorDepth = Depth();
        using Framebuffer target = factory.CreateFramebuffer(new(depth, color));
        using Framebuffer mirror = factory.CreateFramebuffer(new(mirrorDepth, mirrorColor));
        using Framebuffer shadow = factory.CreateFramebuffer(new(depth));
        Fence[] fences = Enumerable.Range(0, 3).Select(_ => factory.CreateFence(true)).ToArray();
        const int frames = 60;
        Texture[] captures = Enumerable.Range(0, frames * 2).Select(_ => factory.CreateTexture(
            TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging))).ToArray();
        // One allocation larger than the default vertex page. Earlier recorded
        // draws must survive expansion and reuse in later frame slots.
        byte[] largeUpload = new byte[300_000];
        Vector4[] ripples = new Vector4[97];
        ripples[^1] = new(1, 1, 1, 1);
        for (int i = 0; i < 48; i++) ripples[i * 2] = new(0, 0, 999, 1);
        try
        {
            for (int frame = 0; frame < frames; frame++)
            {
                int slot = frame % 3;
                device.WaitForFence(fences[slot]);
                Set("_frameSlot", slot);
                Set("_uniformFrameId", (long)frame);
                Set("_frameOpen", true);
                commands.Begin();
                SelectTarget(shadow);
                particles.DrawShadow<ParticleVertex>(ParticleTriangle(0), 3, white, null, Matrix4x4.Identity,
                    1, Vector4.One, Vector4.One, 0);
                float x = (frame & 1) == 0 ? -.5f : .5f;
                SelectTarget(mirror);
                DrawScene(-x, Vector3.UnitY, Vector3.UnitX, new(1, 1, 0));
                renderer.FrameVertices.Upload<byte>(largeUpload);
                // Grow the uniform arena after recording the first camera.
                int uniformCount = 128 * 1024 / (int)Math.Max(device.UniformBufferMinOffsetAlignment, 16);
                for (int i = 0; i < uniformCount; i++) renderer.FrameUniforms.Upload(new Vector4(i));
                SelectTarget(target);
                DrawScene(x, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
                commands.CopyTexture(color, captures[frame * 2]);
                commands.CopyTexture(mirrorColor, captures[frame * 2 + 1]);
                commands.End();
                device.ResetFence(fences[slot]);
                device.SubmitCommands(commands, fences[slot]);
                Set("_frameOpen", false);
            }
            device.WaitForIdle();
            for (int frame = 0; frame < frames; frame++)
            {
                int x = (frame & 1) == 0 ? 16 : 48;
                CheckColor(captures[frame * 2], x, 32, Vector3.UnitX, frame, "water main");
                CheckColor(captures[frame * 2], 64 - x, 32, Vector3.UnitY, frame, "particle main");
                CheckColor(captures[frame * 2], 32, 4, Vector3.UnitZ, frame, "sky main");
                CheckColor(captures[frame * 2 + 1], 64 - x, 32, Vector3.UnitY, frame, "water reflection");
                CheckColor(captures[frame * 2 + 1], x, 32, Vector3.UnitX, frame, "particle reflection");
                CheckColor(captures[frame * 2 + 1], 32, 4, new(1, 1, 0), frame, "sky reflection");
                float opacity = (frame & 1) == 0 ? .25f : .75f;
                CheckSprites(captures[frame * 2], Vector3.UnitZ, opacity, frame);
                CheckSprites(captures[frame * 2 + 1], new(1, 1, 0), 1 - opacity, frame);
            }
        }
        finally
        {
            device.WaitForIdle();
            foreach (Fence fence in fences) fence.Dispose();
            foreach (Texture capture in captures) capture.Dispose();
            renderer.FrameVertices.Dispose();
            renderer.FrameUniforms.Dispose();
        }

        void DrawScene(float x, Vector3 waterColor, Vector3 particleColor, Vector3 skyColor)
        {
            sky.Draw(white, Matrix4x4.Identity, skyColor, 1);
            object activePass = ActivePass();
            water.Draw<WaterVertex>(WaterTriangle(x), 3, normal, normal, white, null, ripples,
                Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity,
                new(0, 0, 2), waterColor, waterColor, 0, 0, 1, 1, 0, false);
            particles.Draw<ParticleVertex>(ParticleTriangle(-x), 3, white, null, Matrix4x4.Identity,
                1, new(particleColor, 1), new(particleColor, 1), false, false);
            float opacity = x < 0 ? .25f : .75f;
            RuntimeTextureHandle texture = new(GraphicsBackend.Vulkan, 0, white.NativeResource);
            sprites.Draw([
                new(texture, new(4, 0), new(10, 64), 0, opacity, false),
                new(texture, new(54, 0), new(60, 64), 0, 1 - opacity, false)
            ], 64, 64);
            // Clear a small corner without ending the pass or overwriting any
            // camera's earlier uniform snapshots.
            utility.ClearViewport(0, 0, 2, 2, Vector4.One);
            commands.SetFullViewports();
            commands.SetFullScissorRects();
            Check(activePass.Equals(ActivePass()), "Scene uploads interrupted the render pass.");
        }

        void SelectTarget(Framebuffer framebuffer)
        {
            commands.SetFramebuffer(framebuffer);
            typeof(VulkanRenderer).GetProperty("CurrentOutputDescription", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(renderer, framebuffer.OutputDescription);
            if (framebuffer.ColorTargets.Count > 0) commands.ClearColorTarget(0, RgbaFloat.Black);
            commands.ClearDepthStencil(1);
            commands.SetFullViewports();
            commands.SetFullScissorRects();
        }
        Texture Color() => factory.CreateTexture(TextureDescription.Texture2D(
            64, 64, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        Texture Depth() => factory.CreateTexture(TextureDescription.Texture2D(
            64, 64, 1, 1, PixelFormat.D24_UNorm_S8_UInt, TextureUsage.DepthStencil));
        void Set(string name, object value) => typeof(VulkanRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, value);
        object ActivePass() => commands.GetType().GetField("_activeRenderPass", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(commands)!;

        void CheckSprites(Texture texture, Vector3 background, float opacity, int frame)
        {
            MappedResource pixels = device.Map(texture, MapMode.Read);
            try
            {
                CheckPixel(7, opacity);
                CheckPixel(57, 1 - opacity);
                void CheckPixel(int x, float alpha)
                {
                    byte* pixel = (byte*)pixels.Data + 32 * pixels.RowPitch + x * 4;
                    Vector3 expected = Vector3.Lerp(background, Vector3.One, alpha) * 255;
                    Vector3 actual = new(pixel[0], pixel[1], pixel[2]);
                    Check(Vector3.Distance(actual, expected) < 3,
                        $"Frame {frame}, sprite at {x}: expected {expected}, got {actual}.");
                }
            }
            finally { device.Unmap(texture); }
        }

        void CheckColor(Texture texture, int x, int y, Vector3 expected, int frame, string label)
        {
            MappedResource pixels = device.Map(texture, MapMode.Read);
            try
            {
                byte* pixel = (byte*)pixels.Data + y * pixels.RowPitch + x * 4;
                Vector3 actual = new(pixel[0], pixel[1], pixel[2]);
                Check(actual.Length() > 40 && Vector3.Distance(Vector3.Normalize(actual), Vector3.Normalize(expected)) < .05f,
                    $"Frame {frame}, {label}: expected color direction {expected}, got {actual}.");
            }
            finally { device.Unmap(texture); }
        }
    }

    private static WaterVertex[] WaterTriangle(float x) =>
        [new(new(x - .3f, -.4f, .5f), Vector2.Zero, Vector3.UnitZ),
         new(new(x + .3f, -.4f, .5f), Vector2.Zero, Vector3.UnitZ),
         new(new(x, .4f, .5f), Vector2.Zero, Vector3.UnitZ)];
    private static ParticleVertex[] ParticleTriangle(float x) =>
        [new(new(x - .3f, -.4f, .5f), Vector2.Zero, 0),
         new(new(x + .3f, -.4f, .5f), Vector2.Zero, 0),
         new(new(x, .4f, .5f), Vector2.Zero, 0)];
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct WaterVertex(Vector3 Position, Vector2 Uv, Vector3 Normal);
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ParticleVertex(Vector3 Position, Vector2 Uv, float Life);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
