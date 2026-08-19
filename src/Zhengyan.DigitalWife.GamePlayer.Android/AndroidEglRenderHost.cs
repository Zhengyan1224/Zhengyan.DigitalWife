using Android.Opengl;
using Android.Util;
using Android.Views;
using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using EGLConfig = Android.Opengl.EGLConfig;
using EGLContext = Android.Opengl.EGLContext;
using EGLDisplay = Android.Opengl.EGLDisplay;
using EGLSurface = Android.Opengl.EGLSurface;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidEglRenderHost : IDisposable
{
    private const string LogTag = "ZhengyanGamePlayer";
    private const int EglContextClientVersion = 0x3098;
    private const int EglOpenGlEs2Bit = 0x0004;
    private const int EglOpenGlEs3BitKhr = 0x0040;
    private const int EglSampleBuffers = 0x3032;
    private const int EglSamples = 0x3031;

    private EGLDisplay? _display;
    private EGLContext? _context;
    private EGLSurface? _surface;
    private AndroidPmxSceneRenderer? _sceneRenderer;
    private RuntimeSceneManager? _sceneManager;
    private AndroidCSharpScriptHost? _scriptHost;
    private AndroidAudioHost? _audioHost;
    private GameProject? _project;
    private string _projectDirectory = string.Empty;
    private int _width = 1;
    private int _height = 1;
    private int _openGlEsVersion;
    private int _requestedMsaaSamples = 1;
    private int _actualMsaaSamples = 1;
    private long _lastFrameTimeNanos;
    private double _elapsedSeconds;
    private bool _disposed;
    private Vector4 _clearColor = new(0.025f, 0.035f, 0.055f, 1.0f);

    public GameProject? Project => _project;

    public void SetProject(GameProject? project, string? projectDirectory)
    {
        _sceneManager?.Dispose();
        _sceneManager = null;
        _scriptHost?.Dispose();
        _scriptHost = null;
        _audioHost?.Dispose();
        _audioHost = null;
        _project = project;
        _projectDirectory = projectDirectory ?? string.Empty;
        if (project is not null && !string.IsNullOrWhiteSpace(_projectDirectory))
        {
            _sceneManager = new RuntimeSceneManager(project, _projectDirectory);
            _audioHost = new AndroidAudioHost(_projectDirectory);
            _scriptHost = new AndroidCSharpScriptHost(
                _projectDirectory,
                RequestSceneChange,
                (scene, name) => _audioHost?.Play(scene, name) == true,
                name => _audioHost?.Stop(name) == true);
            _sceneManager.SceneChanged += OnSceneChanged;
            _sceneManager.SceneLoadFailed += failure =>
                Log.Warn(LogTag, $"Runtime scene load failed '{failure.ScenePath}': {failure.Error.Message}");
            _sceneManager.LoadInitial();
        }
        SetClearColor(_sceneManager?.Current?.Definition.Lighting.ClearColor ?? project?.Scene.Lighting.ClearColor);
        if (_display is not null && _context is not null && _surface is not null)
        {
            ResetFrameClock();
            ReloadScene();
        }
    }

    public void SetClearColor(Vector4Dto? color)
    {
        if (color is null)
        {
            return;
        }

        Vector4 value = color.Value.ToVector4();
        _clearColor = new(
            Math.Clamp(value.X, 0.0f, 1.0f),
            Math.Clamp(value.Y, 0.0f, 1.0f),
            Math.Clamp(value.Z, 0.0f, 1.0f),
            Math.Clamp(value.W, 0.0f, 1.0f));
    }

    public void CreateSurface(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        DestroySurface();

        _display = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
        if (_display is null || _display == EGL14.EglNoDisplay)
        {
            throw new InvalidOperationException("Unable to obtain an EGL display.");
        }

        int[] versions = new int[2];
        if (!EGL14.EglInitialize(_display, versions, 0, versions, 1))
        {
            throw CreateEglException("EGL initialization failed");
        }

        _requestedMsaaSamples = NormalizeMsaa(_project?.Window.AntiAliasingSamples ?? 1);
        EGLConfig? config = null;
        foreach (int samples in ResolveMsaaFallbacks(_requestedMsaaSamples))
        {
            config = ChooseConfig(_display, requestOpenGlEs3: true, samples);
            if (config is not null)
            {
                _actualMsaaSamples = samples;
                break;
            }
        }
        _context = config is null ? null : CreateContext(_display, config, 3);
        if (_context is null || _context == EGL14.EglNoContext)
        {
            config = null;
            foreach (int samples in ResolveMsaaFallbacks(_requestedMsaaSamples))
            {
                config = ChooseConfig(_display, requestOpenGlEs3: false, samples);
                if (config is not null)
                {
                    _actualMsaaSamples = samples;
                    break;
                }
            }
            config = config
                ?? throw CreateEglException("No compatible EGL window configuration was found");
            _context = CreateContext(_display, config, 2);
            _openGlEsVersion = 2;
        }
        else
        {
            _openGlEsVersion = 3;
        }

        if (_context is null || _context == EGL14.EglNoContext)
        {
            throw CreateEglException("OpenGL ES context creation failed");
        }

        int[] surfaceAttributes = [EGL14.EglNone];
        _surface = EGL14.EglCreateWindowSurface(_display, config, surface, surfaceAttributes, 0);
        if (_surface is null || _surface == EGL14.EglNoSurface)
        {
            throw CreateEglException("EGL window surface creation failed");
        }

        MakeCurrent();
        Log.Info(LogTag, $"Android graphics backend: OpenGL ES {_openGlEsVersion}; MSAA requested={_requestedMsaaSamples}x, actual={_actualMsaaSamples}x");
        ReloadScene();
        _ = EGL14.EglSwapInterval(_display, 1);
        ResetFrameClock();
    }

    public void Resize(int width, int height)
    {
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);
    }

    public void Render(long frameTimeNanos, AndroidInputSnapshot input)
    {
        if (_display is null || _context is null || _surface is null)
        {
            return;
        }

        MakeCurrent();
        double deltaSeconds = 0.0;
        if (_lastFrameTimeNanos != 0 && frameTimeNanos >= _lastFrameTimeNanos)
        {
            deltaSeconds = Math.Min((frameTimeNanos - _lastFrameTimeNanos) / 1_000_000_000.0, 0.1);
            _elapsedSeconds += deltaSeconds;
        }

        _lastFrameTimeNanos = frameTimeNanos;
        double seconds = _elapsedSeconds;
        RuntimeScene? inputScene = _sceneManager?.Current;
        if (inputScene is not null)
        {
            DispatchTouchEvents(inputScene, input);
        }
        _sceneManager?.Update((float)deltaSeconds, ToCameraInput(input));

        RuntimeScene? runtimeScene = _sceneManager?.Current;
        if (runtimeScene is not null)
        {
            _scriptHost?.Update(runtimeScene, (float)deltaSeconds);
            _project!.Scene = runtimeScene.Definition;
            SetClearColor(runtimeScene.Definition.Lighting.ClearColor);
        }

        GLES30.GlViewport(0, 0, _width, _height);
        GLES30.GlClearColor(
            _clearColor.X,
            _clearColor.Y,
            _clearColor.Z,
            _clearColor.W);
        GLES30.GlClear(GLES30.GlColorBufferBit | GLES30.GlDepthBufferBit | GLES30.GlStencilBufferBit);
        if (runtimeScene is not null)
        {
            _sceneRenderer?.Draw(runtimeScene, _project!.Window.Width, _project.Window.Height, _width, _height, seconds);
            if (_sceneRenderer is not null)
            {
                foreach (AndroidRuntimeEvent runtimeEvent in _sceneRenderer.DrainRuntimeEvents())
                {
                    _scriptHost?.DispatchEvent(runtimeScene, runtimeEvent);
                }
            }
        }

        if (!EGL14.EglSwapBuffers(_display, _surface))
        {
            throw CreateEglException("EGL buffer presentation failed");
        }
    }

    public void Pause()
    {
        _lastFrameTimeNanos = 0;
        if (_display is not null)
        {
            _ = EGL14.EglMakeCurrent(_display, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);
        }
    }

    public void DestroySurface()
    {
        if (_display is null)
        {
            return;
        }

        if (_surface is not null && _surface != EGL14.EglNoSurface
            && _context is not null && _context != EGL14.EglNoContext)
        {
            _ = EGL14.EglMakeCurrent(_display, _surface, _surface, _context);
        }

        _sceneRenderer?.Dispose();
        _sceneRenderer = null;
        _ = EGL14.EglMakeCurrent(_display, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);
        if (_surface is not null && _surface != EGL14.EglNoSurface)
        {
            _ = EGL14.EglDestroySurface(_display, _surface);
        }

        if (_context is not null && _context != EGL14.EglNoContext)
        {
            _ = EGL14.EglDestroyContext(_display, _context);
        }

        _ = EGL14.EglTerminate(_display);
        _surface = null;
        _context = null;
        _display = null;
        _openGlEsVersion = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroySurface();
        _sceneManager?.Dispose();
        _sceneManager = null;
        _scriptHost?.Dispose();
        _scriptHost = null;
        _audioHost?.Dispose();
        _audioHost = null;
    }

    private static EGLConfig? ChooseConfig(EGLDisplay display, bool requestOpenGlEs3, int samples)
    {
        int renderableType = requestOpenGlEs3 ? EglOpenGlEs3BitKhr : EglOpenGlEs2Bit;
        List<int> attributeList =
        [
            EGL14.EglSurfaceType, EGL14.EglWindowBit,
            EGL14.EglRenderableType, renderableType,
            EGL14.EglRedSize, 8,
            EGL14.EglGreenSize, 8,
            EGL14.EglBlueSize, 8,
            EGL14.EglAlphaSize, 8,
            EGL14.EglDepthSize, 24,
            EGL14.EglStencilSize, 8
        ];
        if (samples > 1)
        {
            attributeList.Add(EglSampleBuffers);
            attributeList.Add(1);
            attributeList.Add(EglSamples);
            attributeList.Add(samples);
        }
        attributeList.Add(EGL14.EglNone);
        int[] attributes = attributeList.ToArray();

        EGLConfig[] configs = new EGLConfig[1];
        int[] count = new int[1];
        return EGL14.EglChooseConfig(display, attributes, 0, configs, 0, configs.Length, count, 0)
            && count[0] > 0
                ? configs[0]
                : null;
    }

    private static IReadOnlyList<int> ResolveMsaaFallbacks(int requested)
    {
        int normalized = NormalizeMsaa(requested);
        return normalized switch
        {
            >= 16 => [16, 8, 4, 2, 1],
            8 => [8, 4, 2, 1],
            4 => [4, 2, 1],
            2 => [2, 1],
            _ => [1]
        };
    }

    private static int NormalizeMsaa(int samples)
    {
        if (samples <= 1) return 1;
        if (samples <= 2) return 2;
        if (samples <= 4) return 4;
        if (samples <= 8) return 8;
        return 16;
    }

    private static EGLContext? CreateContext(EGLDisplay display, EGLConfig config, int version)
    {
        int[] attributes = [EglContextClientVersion, version, EGL14.EglNone];
        return EGL14.EglCreateContext(display, config, EGL14.EglNoContext, attributes, 0);
    }

    private void MakeCurrent()
    {
        if (_display is null || _surface is null || _context is null
            || !EGL14.EglMakeCurrent(_display, _surface, _surface, _context))
        {
            throw CreateEglException("Unable to activate the EGL context");
        }
    }

    private void ReloadScene()
    {
        _sceneRenderer?.Dispose();
        _sceneRenderer = null;
        if (_project is null || string.IsNullOrWhiteSpace(_projectDirectory))
        {
            return;
        }

        if (_openGlEsVersion < 3)
        {
            Log.Warn(LogTag, "This device only exposed OpenGL ES 2.0; PMX rendering currently requires OpenGL ES 3.0.");
            return;
        }

        _sceneRenderer = new AndroidPmxSceneRenderer();
        if (_sceneManager?.Current is { } runtimeScene)
        {
            _sceneRenderer.Load(runtimeScene, _projectDirectory);
            _audioHost?.StartScene(runtimeScene);
            _scriptHost?.Start(runtimeScene);
        }
    }

    private static RuntimeCameraInput ToCameraInput(AndroidInputSnapshot input)
    {
        if (input.ActiveTouchCount == 0)
        {
            return RuntimeCameraInput.None;
        }

        AndroidTouchPoint[] active = input.Touches.Where(point => point.IsActive).ToArray();
        if (active.Length == 1)
        {
            return new RuntimeCameraInput(active[0].Delta, Vector2.Zero, 0.0f);
        }

        Vector2 currentA = active[0].PixelPosition;
        Vector2 currentB = active[1].PixelPosition;
        Vector2 previousA = currentA - active[0].Delta;
        Vector2 previousB = currentB - active[1].Delta;
        float currentDistance = Vector2.Distance(currentA, currentB);
        float previousDistance = Vector2.Distance(previousA, previousB);
        Vector2 pan = (active[0].Delta + active[1].Delta) * 0.5f;
        return new RuntimeCameraInput(Vector2.Zero, pan, (previousDistance - currentDistance) * 0.01f);
    }

    private void ResetFrameClock()
    {
        _lastFrameTimeNanos = 0;
        _elapsedSeconds = 0.0;
    }

    private static InvalidOperationException CreateEglException(string message)
    {
        return new InvalidOperationException($"{message} (EGL error 0x{EGL14.EglGetError():X}).");
    }

    public void RequestSceneChange(string scenePath) => _sceneManager?.RequestSceneChange(scenePath);

    public bool RequestRenderTextureRefresh(string idOrName)
    {
        return _sceneRenderer?.RequestRenderTextureRefresh(idOrName) == true;
    }

    public void DispatchContextMenuItem(ContextMenuSettings menu, ContextMenuItemSettings item, float x, float y)
    {
        RuntimeScene? scene = _sceneManager?.Current;
        if (scene is null) return;
        _scriptHost?.DispatchEvent(scene, new AndroidRuntimeEvent(
            "context_menu", item.Id, item.EventName, new Vector2(x / Math.Max(_width, 1), y / Math.Max(_height, 1)), item.Text));
    }

    private void DispatchTouchEvents(RuntimeScene scene, AndroidInputSnapshot input)
    {
        if (_scriptHost is null || input.Touches.Count == 0)
        {
            return;
        }

        foreach (AndroidTouchPoint touch in input.Touches.Where(point => point.Phase is AndroidTouchPhase.Began or AndroidTouchPhase.Ended))
        {
            float x = touch.PixelPosition.X;
            float y = touch.PixelPosition.Y;
            string? eventType = touch.Phase == AndroidTouchPhase.Began ? "pointer_down" : "pointer_up";
            bool handled = false;
            foreach (GuiControlSettings control in scene.Definition.GuiControls
                .Where(control => control.Visible)
                .Reverse())
            {
                LayoutRect rect = LayoutResolver.Resolve(control.LayoutMode, control.X, control.Y, control.Width, control.Height,
                    _width, _height, _project?.Window.Width ?? _width, _project?.Window.Height ?? _height);
                if (x >= rect.X && x <= rect.X + rect.Width && y >= rect.Y && y <= rect.Y + rect.Height)
                {
                    _scriptHost.DispatchEvent(scene, new AndroidRuntimeEvent(
                        "gui", control.Id, touch.Phase == AndroidTouchPhase.Ended ? control.EventName : eventType!,
                        touch.Position, control.Text, control.TargetEntity));
                    handled = true;
                    break;
                }
            }

            if (!handled)
            {
                foreach (SpriteSettings sprite in scene.Definition.Sprites.Where(sprite => sprite.Visible).Reverse())
                {
                    if (!SpriteLayoutResolver.ContainsPoint(sprite, x, y, _width, _height,
                        _project?.Window.Width ?? _width, _project?.Window.Height ?? _height))
                    {
                        continue;
                    }

                    _scriptHost.DispatchEvent(scene, new AndroidRuntimeEvent(
                        "sprite", sprite.Id, touch.Phase == AndroidTouchPhase.Ended ? "clicked" : eventType!,
                        touch.Position, sprite.Name, sprite.TargetEntity));
                    handled = true;
                    break;
                }
            }

            if (!handled && touch.Phase == AndroidTouchPhase.Ended)
            {
                foreach (ContextMenuSettings menu in scene.Definition.ContextMenus.Where(menu => menu.Enabled))
                {
                    _scriptHost.DispatchEvent(scene, new AndroidRuntimeEvent(
                        "context_menu", menu.Id, "opened", touch.Position));
                }
            }
        }
    }

    private void OnSceneChanged(RuntimeSceneChange change)
    {
        if (change.Current is not null)
        {
            _project!.Scene = change.Current.Definition;
            SetClearColor(change.Current.Definition.Lighting.ClearColor);
        }
        if (_display is not null && _context is not null && _surface is not null)
        {
            ReloadScene();
        }
    }
}
