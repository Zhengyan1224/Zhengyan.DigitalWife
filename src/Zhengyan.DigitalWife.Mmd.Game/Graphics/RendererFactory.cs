using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public static class RendererFactory
{
    private static readonly IRendererFactory OpenGl = new OpenGlRendererFactory();
    private static readonly IRendererFactory Vulkan = new VulkanRendererFactory();

    public static RendererSelection Select(GraphicsBackend requestedBackend)
    {
        if (requestedBackend == GraphicsBackend.OpenGL)
        {
            return new RendererSelection(requestedBackend, GraphicsBackend.OpenGL);
        }

        if (requestedBackend == GraphicsBackend.Vulkan)
        {
            if (!Vulkan.IsSupported(out string reason))
            {
                throw new NotSupportedException($"Vulkan was explicitly selected but is unavailable: {reason}");
            }

            return new RendererSelection(requestedBackend, GraphicsBackend.Vulkan);
        }

        string vulkanReason = "Vulkan is restricted to Windows, Linux, and Android for automatic selection.";
        if ((OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            && Vulkan.IsSupported(out vulkanReason))
        {
            return new RendererSelection(requestedBackend, GraphicsBackend.Vulkan);
        }

        string fallbackReason = OperatingSystem.IsMacOS()
            ? "Vulkan is not selected automatically on macOS; using OpenGL."
            : $"Vulkan is unavailable; using OpenGL. {vulkanReason}";
        return new RendererSelection(requestedBackend, GraphicsBackend.OpenGL, fallbackReason);
    }

    public static IRenderer Create(RendererSelection selection) => selection.ResolvedBackend switch
    {
        GraphicsBackend.OpenGL => OpenGl.Create(),
        GraphicsBackend.Vulkan => Vulkan.Create(),
        _ => throw new NotSupportedException($"No renderer is registered for {selection.ResolvedBackend}.")
    };

    public static void ConfigureWindow(ref WindowOptions options, GraphicsBackend backend)
    {
        bool transparentLinuxWindow = OperatingSystem.IsLinux()
            && !OperatingSystem.IsAndroid()
            && options.TransparentFramebuffer;
        options.API = backend switch
        {
            GraphicsBackend.OpenGL => new GraphicsAPI(ContextAPI.OpenGLES, new APIVersion(3, 0)),
            // GLFW's X11 no-context window uses the opaque default visual. A GLES
            // context selects an ARGB visual; Veldrid creates its Xlib surface directly.
            GraphicsBackend.Vulkan when transparentLinuxWindow
                => new GraphicsAPI(ContextAPI.OpenGLES, new APIVersion(3, 0)),
            GraphicsBackend.Vulkan => GraphicsAPI.None,
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null)
        };
        options.ShouldSwapAutomatically = backend == GraphicsBackend.OpenGL;
        if (transparentLinuxWindow)
        {
            // GLX may choose an opaque visual even for a one-sample request.
            // Scene MSAA is provided by the renderer's RGBA offscreen target.
            options.Samples = 0;
        }
    }

    private sealed class OpenGlRendererFactory : IRendererFactory
    {
        public GraphicsBackend Backend => GraphicsBackend.OpenGL;

        public bool IsSupported(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public IRenderer Create() => new OpenGlRenderer();
    }

    private sealed class VulkanRendererFactory : IRendererFactory
    {
        public GraphicsBackend Backend => GraphicsBackend.Vulkan;

        public bool IsSupported(out string reason) => VulkanRenderer.IsSupported(out reason);

        public IRenderer Create() => new VulkanRenderer();
    }
}
