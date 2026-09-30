using System.Numerics;
using System.Reflection;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Zhengyan.DigitalWife.GamePlayer;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal static class HostedLoadingScreenRegressionTests
{
    public static void TestProgressLayout()
    {
        foreach (GraphicsBackend backend in new[] { GraphicsBackend.OpenGL, GraphicsBackend.Vulkan })
        {
            RecordingRenderer renderer = new(backend);
            using HostedGame game = new(renderer);
            game.InitializeHosted();
            LoadingScreenSettings settings = CreateSettings();
            float progress = 0.25f;
            using LoadingScreenComponent screen = CreateScreen(game, settings, () => progress);
            List<Vector4> rectangles = [];
            Action<Vector4, Vector4> drawRect = (rect, _) => rectangles.Add(rect);
            MethodInfo draw = typeof(LoadingScreenComponent).GetMethod("DrawProgressBar", BindingFlags.Instance | BindingFlags.NonPublic)!;

            foreach (Vector2D<int> size in new[] { new Vector2D<int>(1280, 720), new Vector2D<int>(2560, 1440) })
            {
                game.ResizeHosted(size);
                rectangles.Clear();
                // This is the shared progress routine called by both GLES and Vulkan.
                draw.Invoke(screen, [settings, drawRect]);
                Check(rectangles.Count == 4, "Expected border, background, track, and fill.");
                Near(rectangles[0], new(-0.8f, -0.7f, 0.8f, -0.5f), "Relative progress bar moved after resize.");
                Vector4 track = rectangles[2];
                Near(rectangles[3], track with { Z = track.X + (track.Z - track.X) * 0.25f }, "Incorrect progress fill.");
            }

            settings.ProgressBar.LayoutMode = "absolute";
            settings.ProgressBar.X = 10;
            settings.ProgressBar.Y = 20;
            settings.ProgressBar.Width = 80;
            settings.ProgressBar.Height = 10;
            game.ResizeHosted(new(200, 100));
            rectangles.Clear();
            draw.Invoke(screen, [settings, drawRect]);
            Near(rectangles[0], new(-0.9f, 0.4f, -0.1f, 0.6f), "Absolute bar did not use framebuffer pixels.");
            game.ResizeHosted(new(400, 200));
            rectangles.Clear();
            draw.Invoke(screen, [settings, drawRect]);
            Near(rectangles[0], new(-0.95f, 0.7f, -0.55f, 0.8f), "Absolute bar ignored the resized surface.");

            foreach (float amount in new[] { -1f, 0f, 1f, 2f })
            {
                progress = amount;
                rectangles.Clear();
                draw.Invoke(screen, [settings, drawRect]);
                Check(rectangles.Count == (amount <= 0 ? 3 : 4), "Progress clamping drew an unexpected fill.");
                if (amount >= 1) Near(rectangles[3], rectangles[2], "Full progress must cover the track.");
            }
            settings.ProgressBar.Visible = false;
            rectangles.Clear();
            draw.Invoke(screen, [settings, drawRect]);
            Check(rectangles.Count == 0, "Hidden progress bar was rendered.");
        }
    }

    public static void TestVulkanFrames()
    {
        RecordingRenderer renderer = new(GraphicsBackend.Vulkan);
        using HostedGame game = new(renderer);
        game.InitializeHosted();
        LoadingScreenSettings settings = CreateSettings();
        float progress = 0;
        LoadingScreenComponent screen = CreateScreen(game, settings, () => progress);
        RecordingLoadingPass pass = new();
        typeof(LoadingScreenComponent).GetField("_backendRenderer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(screen, pass);
        game.AddComponent(screen);

        for (int frame = 0; frame < 5; frame++)
        {
            progress = frame * 0.25f;
            game.ResizeHosted(frame % 2 == 0 ? new(1280, 720) : new(2560, 1440));
            pass.Rectangles.Clear();
            game.UpdateHosted(1.0 / 60);
            game.RenderHostedWithoutPresent(1.0 / 60);
            game.PresentHosted();
            Check(pass.Rectangles.Count == (frame == 0 ? 4 : 5), "Hosted loading frame was incomplete.");
            Near(pass.Rectangles[1], new(-0.8f, -0.7f, 0.8f, -0.5f), "Vulkan progress bar used an incorrect surface size.");
        }
        Check(renderer.PresentedFrames == 5, "Loading did not advance through consecutive hosted frames.");
    }

    private static LoadingScreenSettings CreateSettings() => new()
    {
        ProgressBar = new()
        {
            Visible = true, LayoutMode = "relative", X = 128, Y = 540, Width = 1024, Height = 72,
            BorderThickness = 2, Padding = 3
        }
    };

    private static LoadingScreenComponent CreateScreen(Game game, LoadingScreenSettings settings, Func<float> progress)
    {
        LoadingScreenComponent screen = new(progress, () => "Loading scene...", () => settings, path => path);
        // Bypass native shader initialization; retain the real hosted Game (which has no Window).
        typeof(GameComponent).GetProperty(nameof(GameComponent.Game))!.SetValue(screen, game);
        return screen;
    }

    private static void Near(Vector4 actual, Vector4 expected, string message)
        => Check(Vector4.Distance(actual, expected) < 0.00001f, $"{message} Expected {expected}, got {actual}.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class HostedGame(IRenderer renderer) : Game(
        new GameOptions { GraphicsBackend = renderer.Backend, UseOpenCL = false }, renderer, renderer.BackBufferSize);

    private sealed class RecordingLoadingPass : ILoadingScreenPassRenderer
    {
        public List<Vector4> Rectangles { get; } = [];
        public void DrawRect(Vector4 clipRect, Vector4 color, ITexture2D? texture = null, float opacity = 1) => Rectangles.Add(clipRect);
        public void Dispose() { }
    }

    private sealed class RecordingRenderer(GraphicsBackend backend) : IRenderer
    {
        public int PresentedFrames { get; private set; }
        public GraphicsBackend Backend => backend;
        public string Name => "Hosted loading command recorder";
        public IRenderBackendServices Services => throw new NotSupportedException();
        public Vector2D<int> BackBufferSize { get; private set; } = new(1280, 720);
        public int RequestedAntiAliasingSamples => 1;
        public int AntiAliasingSamples => 1;
        public void Resize(Vector2D<int> size) => BackBufferSize = size;
        public void Present() => PresentedFrames++;
        public void Initialize(IWindow window, Vector2D<int> backBufferSize, int requestedSamples) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(string name) => throw new NotSupportedException();
        public ITexture2D CreateTexture2D() => throw new NotSupportedException();
        public IScreenSpriteRenderer CreateScreenSpriteRenderer() => throw new NotSupportedException();
        public IGpuBuffer CreateBuffer(GpuBufferDescription description) => throw new NotSupportedException();
        public IGpuSampler CreateSampler(GpuSamplerDescription description) => throw new NotSupportedException();
        public void Clear(Vector4 color) { }
        public void ClearViewport(int x, int y, int width, int height, Vector4 color) { }
        public void SetViewport(int x, int y, int width, int height) { }
        public void SetScissor(int x, int y, int width, int height, bool enabled) { }
        public void RestoreBackBuffer() { }
        public bool TryReadBackBufferRgba(Span<byte> destination) => false;
        public void WaitForIdle() { }
        public void Dispose() { }
    }
}
