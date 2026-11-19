using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Windowing;
using Veldrid;
using Veldrid.SPIRV;
using Zhengyan.DigitalWife.GamePlayer;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using GraphicsBackend = Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend;

internal static class WindowsVulkanSpriteRegionRegression
{
    private const int Width = 64, Height = 64;
    private static readonly Assembly Player = typeof(CSharpScriptGlobals).Assembly;

    public static void CurrentFrame()
    {
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP Windows Vulkan sprite region"); return; }
        using var window = CreateWindow(GraphicsBackend.Vulkan);
        using VulkanRenderer renderer = new();
        renderer.Initialize(window, new(Width, Height), 4);
        var component = CreateHitTest(window, renderer);
        try
        {
            int previousX = -1, previousBubbleX = -1;
            foreach (int x in new[] { 8, 40, 4, 44, 8, 40, 8, 8 })
            {
                int bubbleX = x < 24 ? 44 : 4;
                renderer.Clear(Vector4.Zero);
                renderer.ClearViewport(x, 24, 16, 16, new(1, 0, 0, 1));
                renderer.ClearViewport(bubbleX, 8, 8, 8, new(0, 1, 0, 1));
                component.Draw(default);
                // The new region must not be installed while the GPU is still
                // recording this image, nor use the previously completed image.
                if (previousX >= 0) Check(Contains(window, previousX + 8, 32), "Queued copy changed the displayed region before submission.");
                renderer.Present();
                byte[] pixels = Pixels(component);
                Check(pixels[(32 * Width + x + 8) * 4 + 3] == 255, "Readback is not the current moving character frame.");
                Check(Contains(window, x + 8, Height - 1 - 32), "Current character was clipped by a stale window region.");
                int bubbleRow = Enumerable.Range(0, Height).First(y => pixels[(y * Width + bubbleX + 4) * 4 + 1] == 255);
                Check(Contains(window, bubbleX + 4, Height - 1 - bubbleRow), "Current dialogue bubble was clipped by a stale window region.");
                if (previousX >= 0 && previousX != x)
                {
                    Check(!Contains(window, previousX + 8, Height - 1 - 32), "Previous character silhouette left an input/visual trail.");
                    Check(!Contains(window, previousBubbleX + 4, Height - 1 - bubbleRow), "Previous dialogue bubble silhouette remained.");
                }
                CheckCoverage(window, pixels, Width, Height);
                previousX = x; previousBubbleX = bubbleX;
            }
            CheckMsaaCoverage(window, renderer, component);
            window.Size = new(96, 80);
            renderer.Resize(new(96, 80));
            renderer.Clear(Vector4.Zero);
            renderer.ClearViewport(72, 48, 16, 16, Vector4.One);
            component.Draw(default); renderer.Present();
            Check(Pixels(component).Length == 96 * 80 * 4, "Resize retained the previous frame's mask dimensions.");
            Check(Enumerable.Range(0, 96 * 80).Count(i => Pixels(component)[i * 4 + 3] == 255) == 16 * 16,
                "Resized readback lost the current opaque rectangle.");
            CheckCoverage(window, Pixels(component), 96, 80);
        }
        finally { component.Dispose(); }
    }

    public static void OtherReadbacks()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (var window = CreateWindow(GraphicsBackend.Vulkan))
        using (VulkanRenderer renderer = new())
        {
            renderer.Initialize(window, new(Width, Height), 1);
            byte[] pixels = new byte[Width * Height * 4];
            renderer.Clear(Vector4.Zero); renderer.ClearViewport(8, 24, 16, 16, Vector4.One);
            Check(!renderer.TryReadBackBufferRgba(pixels), "Ordinary Vulkan readback became synchronous.");
            renderer.Present(); renderer.WaitForIdle();
            renderer.Clear(Vector4.Zero); renderer.ClearViewport(40, 24, 16, 16, Vector4.One);
            Check(renderer.TryReadBackBufferRgba(pixels), "Ordinary Vulkan staging readback did not complete.");
            Check(pixels[(32 * Width + 16) * 4 + 3] == 255 && pixels[(32 * Width + 48) * 4 + 3] == 0,
                "Ordinary Vulkan readback no longer returns its completed previous frame.");
            renderer.Present();
        }
        using (var window = CreateWindow(GraphicsBackend.OpenGL))
        using (OpenGlRenderer renderer = new())
        {
            window.MakeCurrent(); renderer.Initialize(window, new(Width, Height), 4);
            var component = CreateHitTest(window, renderer);
            try
            {
                foreach (int x in new[] { 8, 40, 8 })
                {
                    renderer.Clear(Vector4.Zero); renderer.ClearViewport(x, 24, 16, 16, Vector4.One);
                    component.Draw(default);
                    Check(Contains(window, x + 8, 32), "Windows OpenGL stopped updating its region during Draw.");
                    CheckCoverage(window, Pixels(component), Width, Height);
                    renderer.Present(); window.SwapBuffers();
                }
            }
            finally { component.Dispose(); }
        }
    }

    private static void CheckMsaaCoverage(IWindow window, VulkanRenderer renderer, DrawableGameComponent component)
    {
        var device = (Veldrid.GraphicsDevice)typeof(VulkanRenderer).GetProperty("Device", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        var commands = (CommandList)typeof(VulkanRenderer).GetProperty("CommandList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        Shader[] shaders = device.ResourceFactory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("sprite-coverage.vert", "void main(){vec2 p=gl_VertexIndex==0?vec2(-.81,-.77):gl_VertexIndex==1?vec2(.86,-.31):vec2(-.37,.91);gl_Position=vec4(p,0,1);}", ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("sprite-coverage.frag", "layout(location=0)out vec4 color;void main(){color=vec4(1,0,0,1);}", ShaderStages.Fragment));
        using Shader vertex = shaders[0], fragment = shaders[1];
        foreach (int samples in new[] { 4, 1, 4 })
        {
            renderer.SetAntiAliasingSamples(samples); renderer.Clear(Vector4.Zero);
            var framebuffer = (Framebuffer)typeof(VulkanRenderer).GetProperty("MainFramebuffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
            using Pipeline pipeline = device.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend, DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.CullNone, PrimitiveTopology.TriangleList, new ShaderSetDescription([], shaders), [], framebuffer.OutputDescription));
            commands.SetFullViewports(); commands.SetFullScissorRects(); commands.SetPipeline(pipeline); commands.Draw(3);
            component.Draw(default); renderer.Present();
            byte[] pixels = Pixels(component);
            int partial = Enumerable.Range(0, Width * Height).Count(i => pixels[i * 4 + 3] is > 0 and < 255);
            Check(renderer.AntiAliasingSamples == 1 ? partial == 0 : partial > 0, "Current-frame readback lost resolved MSAA alpha coverage.");
            CheckCoverage(window, pixels, Width, Height);
        }
    }

    private static IWindow CreateWindow(GraphicsBackend backend)
    {
        WindowOptions options = WindowOptions.Default;
        options.IsVisible = false; options.Size = new(Width, Height);
        options.WindowBorder = WindowBorder.Hidden; options.TransparentFramebuffer = true;
        RendererFactory.ConfigureWindow(ref options, backend);
        var window = Window.Create(options); window.Initialize(); return window;
    }
    private static DrawableGameComponent CreateHitTest(IWindow window, IRenderer renderer)
    {
        var game = (Game)RuntimeHelpers.GetUninitializedObject(Player.GetType("Zhengyan.DigitalWife.GamePlayer.GamePlayerGame")!);
        typeof(Game).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, window);
        typeof(Game).GetProperty("GraphicsDevice")!.SetValue(game, new Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsDevice(renderer, Vector4.Zero));
        var component = (DrawableGameComponent)Activator.CreateInstance(Player.GetType("Zhengyan.DigitalWife.GamePlayer.DesktopSpriteHitTestComponent")!, (Func<bool>)(() => true))!;
        typeof(GameComponent).GetProperty("Game")!.SetValue(component, game); return component;
    }
    private static byte[] Pixels(DrawableGameComponent component) => (byte[])component.GetType().GetField("_framebufferBytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
    private static nint Hwnd(IWindow window) => (nint)Player.GetType("Zhengyan.DigitalWife.GamePlayer.DesktopSpritePlatform")!.GetMethod("TryGetWindowsHwnd")!.Invoke(null, [window])!;
    private static bool Contains(IWindow window, int x, int y)
    {
        nint region = CreateRectRgn(0, 0, 0, 0);
        try { Check(GetWindowRgn(Hwnd(window), region) != 0, "Windows sprite region is missing."); return PtInRegion(region, x, y); }
        finally { DeleteObject(region); }
    }
    private static void CheckCoverage(IWindow window, byte[] pixels, int width, int height)
    {
        nint region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            Check(GetWindowRgn(Hwnd(window), region) != 0, "Windows sprite region is missing.");
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                if (pixels[(y * width + x) * 4 + 3] >= 8)
                    Check(PtInRegion(region, x, height - 1 - y), $"Current visible pixel ({x},{y}) is clipped.");
        }
        finally { DeleteObject(region); }
    }
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint region);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint hwnd, nint region);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
