using System.Numerics;
using System.Reflection;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Zhengyan.DigitalWife.GameEditor;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Components;

internal static class ViewportRenderingRegression
{
    public static void RunReflections()
    {
        using GameEditorGame editor = new(GraphicsBackend.OpenGL);
        RecordingRenderer renderer = new(GraphicsBackend.Vulkan, 4);
        typeof(Game).GetProperty(nameof(Game.GraphicsDevice))!.SetValue(editor, new GraphicsDevice(renderer, Vector4.Zero));
        OrbitCamera camera = new(); camera.SetLookAt(new(0,2,8), new(0,0,0), Vector3.UnitY);
        OrbitCamera thumbnail = new(); thumbnail.SetLookAt(new(0,2,8), new(0,0,0), Vector3.UnitY);
        using WaterSurfaceComponent water = new(camera) { MirrorReflectionSamples = 2 };
        using TexturedPlaneComponent plane = new(camera, string.Empty) { MirrorReflectionEnabled = true, MirrorReflectionSamples = 8 };
        using PlanarReflectionRenderer reflections = new(editor);
        Render(camera, 640, 480);
        if (renderer.Targets.Count != 2 || renderer.Targets[0].SampleCount != 2 || renderer.Targets[1].SampleCount != 4
            || renderer.Targets.Any(target => target.EndedPasses != 1))
            throw new InvalidOperationException("Per-surface samples were not capped at scene MSAA or the reflection was not resolved.");
        water.MirrorReflectionSamples = 1;
        Render(camera, 640, 480);
        if (renderer.Targets.Count != 2 || renderer.Targets[0].SampleCount != 1)
            throw new InvalidOperationException("Changing sample levels must update the existing surface/camera target.");
        Render(thumbnail, 160, 120);
        if (renderer.Targets.Count != 4 || renderer.Targets[0].Width != 640 || renderer.Targets[2].Width != 160)
            throw new InvalidOperationException("Camera viewports must retain independent reflection targets.");
        reflections.RenderAll(default, camera, [], [plane], [], _ => { }, _ => { }, Vector4.Zero, 640, 480, () => { });
        if (!renderer.Targets[0].Disposed || !renderer.Targets[2].Disposed)
            throw new InvalidOperationException("Removed reflection surfaces leaked camera targets.");
        void Render(OrbitCamera source, int width, int height)
            => reflections.RenderAll(default, source, [water], [plane], [], _ => { }, _ => { }, Vector4.Zero, width, height, () => { });
    }

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

    private sealed class RecordingRenderer(GraphicsBackend backend, int samples = 1) : IRenderer
    {
        public List<(string, Rect)> Rectangles { get; } = [];
        public List<RecordingTarget> Targets { get; } = [];
        public GraphicsBackend Backend => backend;
        public string Name => "Viewport command recorder";
        public IRenderBackendServices Services => throw new NotSupportedException();
        // Deliberately different from the editor's offscreen target.
        public Vector2D<int> BackBufferSize => new(1440, 860);
        public int RequestedAntiAliasingSamples => samples;
        public int AntiAliasingSamples => samples;
        public void SetViewport(int x, int y, int width, int height) => Rectangles.Add(("viewport", new(x, y, width, height)));
        public void SetScissor(int x, int y, int width, int height, bool enabled)
        {
            if (enabled) Rectangles.Add(("scissor", new(x, y, width, height)));
        }
        public void ClearViewport(int x, int y, int width, int height, Vector4 color) => Rectangles.Add(("clear", new(x, y, width, height)));
        public IRenderTarget CreateRenderTarget(string name)
        {
            RecordingTarget target = new(name, backend); Targets.Add(target); return target;
        }
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
        public void EnsureSize(int width, int height, int samples) { EnsureSize(width, height); SampleCount = samples; }
        public int SampleCount { get; private set; } = 1;
        public int EndedPasses { get; private set; }
        public bool Disposed { get; private set; }
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
        public void EndPass() => EndedPasses++;
        public void ForceOpaqueAlpha() { }
        public void Dispose() => Disposed = true;
    }
}
