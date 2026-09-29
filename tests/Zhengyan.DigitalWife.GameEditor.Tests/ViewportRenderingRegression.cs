using System.Numerics;
using System.Reflection;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Zhengyan.DigitalWife.GameEditor;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal static class ViewportRenderingRegression
{
    public static void Run()
    {
        foreach (GraphicsBackend backend in new[] { GraphicsBackend.OpenGL, GraphicsBackend.Vulkan })
        {
            using GameEditorGame editor = new(GraphicsBackend.OpenGL);
            RecordingRenderer renderer = new(backend);
            GraphicsDevice device = new(renderer, Vector4.Zero);
            typeof(Game).GetProperty(nameof(Game.GraphicsDevice))!.SetValue(editor, device);
            SceneRenderTarget target = new(device);
            SetField(editor, "_sceneRenderTarget", target);
            SetField(editor, "_renderTextureManager", new SceneRenderTextureManager(editor, () => editor.Project.Scene, () => []));
            editor.Project.Window.Width = 1280;
            editor.Project.Window.Height = 720;
            editor.Project.Scene.Cameras =
            [
                new SceneCameraSettings { Name = "Main Camera", Viewport = new CameraViewportSettings
                    { Enabled = true, LayoutMode = "relative", Width = 1280, Height = 720 } },
                new SceneCameraSettings { Name = "Camera 2", Viewport = new CameraViewportSettings
                    { Enabled = true, LayoutMode = "relative", X = 867, Y = 492, Width = 384, Height = 207 } }
            ];

            foreach (bool halfSize in new[] { false, true })
            {
                int width = halfSize ? 640 : 1280;
                int height = halfSize ? 360 : 720;
                target.EnsureSize(width, height);
                renderer.Rectangles.Clear();
                bool drew = (bool)typeof(GameEditorGame).GetMethod("TryDrawCameraViewports", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(editor, [default(GameTime)])!;
                if (!drew) throw new InvalidOperationException("Editor did not render the camera viewports.");

                Rect main = new(0, 0, width, height);
                Rect secondary = halfSize
                    ? new(434, backend == GraphicsBackend.OpenGL ? 10 : 246, 192, 104)
                    : new(867, backend == GraphicsBackend.OpenGL ? 21 : 492, 384, 207);
                (string, Rect)[] expected =
                [
                    ("viewport", main), ("scissor", main), ("clear", main),
                    ("viewport", secondary), ("scissor", secondary), ("clear", secondary),
                    ("viewport", main)
                ];
                if (!renderer.Rectangles.SequenceEqual(expected))
                    throw new InvalidOperationException($"{backend} editor viewport commands were misplaced at {width}x{height}: {string.Join(", ", renderer.Rectangles)}");
            }
        }
    }

    private static void SetField(GameEditorGame editor, string name, object value)
        => typeof(GameEditorGame).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, value);

    private readonly record struct Rect(int X, int Y, int Width, int Height);

    private sealed class RecordingRenderer(GraphicsBackend backend) : IRenderer
    {
        public List<(string, Rect)> Rectangles { get; } = [];
        public GraphicsBackend Backend => backend;
        public string Name => "Viewport command recorder";
        public IRenderBackendServices Services => throw new NotSupportedException();
        // Deliberately different from the editor's offscreen target.
        public Vector2D<int> BackBufferSize => new(1440, 860);
        public int RequestedAntiAliasingSamples => 1;
        public int AntiAliasingSamples => 1;
        public void SetViewport(int x, int y, int width, int height) => Rectangles.Add(("viewport", new(x, y, width, height)));
        public void SetScissor(int x, int y, int width, int height, bool enabled)
        {
            if (enabled) Rectangles.Add(("scissor", new(x, y, width, height)));
        }
        public void ClearViewport(int x, int y, int width, int height, Vector4 color) => Rectangles.Add(("clear", new(x, y, width, height)));
        public IRenderTarget CreateRenderTarget(string name) => new RecordingTarget(name, backend);
        public ITexture2D CreateTexture2D() => throw new NotSupportedException();
        public IScreenSpriteRenderer CreateScreenSpriteRenderer() => throw new NotSupportedException();
        public IGpuBuffer CreateBuffer(GpuBufferDescription description) => throw new NotSupportedException();
        public IGpuSampler CreateSampler(GpuSamplerDescription description) => throw new NotSupportedException();
        public void Initialize(IWindow window, Vector2D<int> backBufferSize, int requestedSamples) => throw new NotSupportedException();
        public void Resize(Vector2D<int> backBufferSize) => throw new NotSupportedException();
        public bool TryReadBackBufferRgba(Span<byte> destination) => false;
        public void Clear(Vector4 color) { }
        public void RestoreBackBuffer() { }
        public void WaitForIdle() { }
        public void Present() { }
        public void Dispose() { }
    }

    private sealed class RecordingTarget(string name, GraphicsBackend backend) : IRenderTarget
    {
        public string Name => name;
        public GraphicsBackend Backend => backend;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public uint LegacyColorTextureId => 0;
        public object? NativeColorResource => null;
        public object? NativeDepthResource => null;
        public void EnsureSize(int width, int height) { Width = width; Height = height; }
        public void BeginPass(Vector4 clearColor) { }
        public void ResumePass() { }
        public void EndPass() { }
        public void ForceOpaqueAlpha() { }
        public void Dispose() { }
    }
}
