using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Veldrid;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal static unsafe class VulkanDesktopOverlayTests
{
    public static void TestOverlaySnapshots()
    {
        if (!VulkanRenderer.IsSupported(out string reason)) { Console.WriteLine($"SKIP Vulkan overlays: {reason}"); return; }
        using var device = Veldrid.GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions
            { PreferStandardClipSpaceYDirection = true, PreferDepthRangeZeroToOne = true });
        var factory = device.ResourceFactory;
        using CommandList commands = factory.CreateCommandList();
        VulkanRenderer renderer = new();
        Set("_device", device); Set("_commandList", commands);
        using VeldridImGuiRenderer gui = new(renderer, null);
        using VeldridLineRenderer lines = new(renderer, 256);
        using VeldridUtilityPassRenderer utility = new(renderer);
        using Texture color = factory.CreateTexture(TextureDescription.Texture2D(128, 128, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        using Framebuffer target = factory.CreateFramebuffer(new(null, color));
        Fence[] fences = Enumerable.Range(0, VulkanRenderer.FrameSlotCount).Select(_ => factory.CreateFence(true)).ToArray();
        const int frames = 18;
        Texture[] captures = Enumerable.Range(0, frames).Select(_ => factory.CreateTexture(TextureDescription.Texture2D(
            128, 128, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging))).ToArray();
        float[] largeLines = new float[12000 * 6];
        for (int i = 0; i < 12000; i++) { largeLines[i * 6] = 4; largeLines[i * 6 + 1] = 4; }
        float[] redLine = [-.8f, 0, 0, 1, 0, 0, -.6f, 0, 0, 1, 0, 0];
        float[] greenLine = [-.8f, 0, 0, 0, 1, 0, -.6f, 0, 0, 0, 1, 0];
        try
        {
            for (int frame = 0; frame < frames; frame++)
            {
                int slot = frame % VulkanRenderer.FrameSlotCount;
                device.WaitForFence(fences[slot]); Set("_frameSlot", slot); Set("_uniformFrameId", (long)frame); Set("_frameOpen", true);
                commands.Begin(); commands.SetFramebuffer(target); commands.ClearColorTarget(0, RgbaFloat.Black);
                commands.SetFullViewports(); commands.SetFullScissorRects();
                typeof(VulkanRenderer).GetProperty("CurrentOutputDescription", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, target.OutputDescription);
                utility.ClearViewport(0, 0, 1, 1, Vector4.One);
                commands.SetFullViewports(); commands.SetFullScissorRects();
                object pass = ActivePass();

                // Render twice before submission with different transforms. The
                // second call must not overwrite the first UI's frame uniforms.
                gui.Update(1f / 60, 64, 64, null, null, 0, 0, []);
                ImGui.GetBackgroundDrawList().AddRectFilled(new(8, 8), new(24, 24), 0xff0000ff);
                gui.Render();
                Check(pass.Equals(ActivePass()), "ImGui uploads interrupted the scene render pass.");

                gui.Update(1f / 60, 128, 128, null, null, 0, 0, []);
                ImDrawListPtr background = ImGui.GetBackgroundDrawList();
                // More than 64K vertices: force ImGui's VtxOffset commands and
                // grow both geometry pages after the earlier UI was recorded.
                for (int i = 0; i < 20000; i++) background.AddRectFilled(new(200, 200), new(201, 201), 0xffffffff);
                background.AddRectFilled(new(80, 16), new(104, 40), 0xffff0000);
                // A separate list with its own index base, texture and scissor.
                ImDrawListPtr foreground = ImGui.GetForegroundDrawList();
                foreground.PushClipRect(new(72, 88), new(96, 112));
                foreground.AddRectFilled(new(64, 80), new(104, 120), 0xff00ffff);
                foreground.PopClipRect();
                gui.Render();
                Check(pass.Equals(ActivePass()), "Growing UI buffers interrupted the render pass.");
                commands.SetFullScissorRects();

                lines.Draw(redLine, 2, Matrix4x4.Identity);
                lines.Draw(greenLine, 2, Matrix4x4.CreateTranslation(1.4f, 0, 0));
                lines.Draw(largeLines, 12000, Matrix4x4.Identity);
                Check(pass.Equals(ActivePass()), "Debug line uploads interrupted the render pass.");
                commands.CopyTexture(color, captures[frame]); commands.End();
                device.ResetFence(fences[slot]); device.SubmitCommands(commands, fences[slot]); Set("_frameOpen", false);
            }
            device.WaitForIdle();
            for (int frame = 0; frame < frames; frame++)
            {
                MappedResource pixels = device.Map(captures[frame], MapMode.Read);
                try
                {
                    Pixel(32, 32, 255, 0, 0, "first UI transform");
                    Pixel(92, 28, 0, 0, 255, "large UI vertex/index offsets");
                    Pixel(84, 100, 255, 255, 0, "second draw list");
                    Pixel(68, 100, 0, 0, 0, "UI scissor outside");
                    Check(LinePixels(10, 28, 0) >= 8, $"Frame {frame}: first line data or transform was overwritten.");
                    Check(LinePixels(99, 118, 1) >= 8, $"Frame {frame}: second line data or transform was overwritten.");

                    void Pixel(int x, int y, byte r, byte g, byte b, string label)
                    {
                        byte* pixel = (byte*)pixels.Data + y * pixels.RowPitch + x * 4;
                        Check(Math.Abs(pixel[0] - r) <= 2 && Math.Abs(pixel[1] - g) <= 2 && Math.Abs(pixel[2] - b) <= 2,
                            $"Frame {frame}, {label}: expected {r},{g},{b}; got {pixel[0]},{pixel[1]},{pixel[2]}.");
                    }
                    int LinePixels(int left, int right, int channel)
                    {
                        int count = 0;
                        for (int y = 61; y <= 66; y++)
                        for (int x = left; x <= right; x++)
                        {
                            byte* p = (byte*)pixels.Data + y * pixels.RowPitch + x * 4;
                            if (p[channel] > 240 && p[1 - channel] < 10 && p[2] < 10) count++;
                        }
                        return count;
                    }
                }
                finally { device.Unmap(captures[frame]); }
            }
            Console.WriteLine("PASS Vulkan desktop UI and debug lines: 18 frames, all frame slots, repeated UI renders, 64K vertex offsets, index growth and scissor preservation");
        }
        finally
        {
            device.WaitForIdle();
            foreach (Texture capture in captures) capture.Dispose();
            foreach (Fence fence in fences) fence.Dispose();
            renderer.FrameIndices.Dispose(); renderer.FrameVertices.Dispose(); renderer.FrameUniforms.Dispose();
        }
        void Set(string name, object value) => typeof(VulkanRenderer).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(renderer, value);
        object ActivePass() => commands.GetType().GetField("_activeRenderPass", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(commands)!;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
