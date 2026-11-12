using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Windowing;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using GL = Silk.NET.OpenGLES.GL;
using GLEnum = Silk.NET.OpenGLES.GLEnum;

internal static class DesktopSpriteTransparencyRegression
{
    public static unsafe void OpenGl()
    {
        if (!CanTestX11()) return;
        using IWindow window = CreateWindow(GraphicsBackend.OpenGL);
        CheckArgbWindow(window);
        window.MakeCurrent();
        using OpenGlRenderer renderer = new();
        renderer.Initialize(window, new(64, 64), 4);
        using GL gl = GL.GetApi(window);
        Check(renderer.RequestedAntiAliasingSamples == 4, "Transparent window lost its scene MSAA request.");

        foreach (int samples in new[] { 4, 1, 2, 4 })
        {
            renderer.SetAntiAliasingSamples(samples);
            renderer.Clear(Vector4.Zero);
            renderer.ClearViewport(24, 24, 16, 16, Vector4.One);
            renderer.Present();
            gl.BindFramebuffer(GLEnum.Framebuffer, 0);
            byte[] pixels = new byte[64 * 64 * 4];
            fixed (byte* data = pixels)
                gl.ReadPixels(0, 0, 64, 64, GLEnum.Rgba, GLEnum.UnsignedByte, data);
            CheckAlpha(pixels);
            Check(gl.GetError() == GLEnum.NoError, "Transparent scene presentation raised a GL error.");
        }
    }

    public static void Vulkan()
    {
        if (!CanTestX11()) return;
        if (!VulkanRenderer.IsSupported(out string reason))
        {
            Console.WriteLine($"SKIP transparent Vulkan: {reason}");
            return;
        }

        string? previous = Environment.GetEnvironmentVariable("ZHENGYAN_VULKAN_TRANSPARENT_SWAPCHAIN");
        Environment.SetEnvironmentVariable("ZHENGYAN_VULKAN_TRANSPARENT_SWAPCHAIN", "1");
        try
        {
            using IWindow window = CreateWindow(GraphicsBackend.Vulkan);
            CheckArgbWindow(window);
            using VulkanRenderer renderer = new();
            renderer.Initialize(window, new(64, 64), 4);
            Check(!window.ShouldSwapAutomatically, "GLFW must not swap a GLES buffer over Vulkan presentation.");
            byte[] pixels = new byte[64 * 64 * 4];
            bool received = false;
            for (int frame = 0; frame < 4; frame++)
            {
                renderer.Clear(Vector4.Zero);
                renderer.ClearViewport(24, 24, 16, 16, Vector4.One);
                if (renderer.TryReadBackBufferRgba(pixels))
                {
                    CheckAlpha(pixels);
                    received = true;
                }
                renderer.Present();
                renderer.WaitForIdle();
            }
            Check(received, "Transparent Vulkan readback did not complete.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZHENGYAN_VULKAN_TRANSPARENT_SWAPCHAIN", previous);
        }
    }

    private static bool CanTestX11()
    {
        if (OperatingSystem.IsLinux()
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
            && !string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)) return true;
        Console.WriteLine("SKIP desktop sprite transparency: requires a Linux X11 desktop.");
        return false;
    }

    private static IWindow CreateWindow(GraphicsBackend backend)
    {
        WindowOptions options = WindowOptions.Default;
        options.IsVisible = false;
        options.Size = new(64, 64);
        options.Samples = 4;
        options.PreferredBitDepth = new(8);
        options.TransparentFramebuffer = true;
        RendererFactory.ConfigureWindow(ref options, backend);
        IWindow window = Window.Create(options);
        window.Initialize();
        return window;
    }

    private static void CheckArgbWindow(IWindow window)
    {
        var native = window.Native?.X11 ?? throw new InvalidOperationException("Expected an X11 test window.");
        int result = XGetGeometry(native.Item1, native.Item2, out _, out _, out _, out _, out _, out _, out uint depth);
        Check(result != 0 && depth == 32, $"Desktop sprite window has depth {depth}; its visual cannot preserve alpha.");
    }

    private static void CheckAlpha(byte[] pixels)
    {
        Check(pixels[3] == 0, "Transparent background became opaque during resolve or presentation.");
        Check(pixels[(32 * 64 + 32) * 4 + 3] == 255, "Opaque scene content lost its alpha coverage.");
    }

    [DllImport("libX11.so.6")]
    private static extern int XGetGeometry(nint display, nuint drawable, out nuint root,
        out int x, out int y, out uint width, out uint height, out uint borderWidth, out uint depth);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
