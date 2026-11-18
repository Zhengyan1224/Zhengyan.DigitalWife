using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidGameSurfaceView : FrameLayout, ISurfaceHolderCallback, TextureView.ISurfaceTextureListener, Choreographer.IFrameCallback
{
    private const string LogTag = "ZhengyanGamePlayer";
    private readonly AndroidTouchState _touchState = new();
    private readonly AndroidDeviceInputState _deviceInputState = new();
    private IAndroidRenderHost _renderHost;
    private GameProject? _project;
    private string _projectDirectory = string.Empty;
    private GraphicsBackend _requestedBackend;
    private Surface? _surface;
    private bool _hasSurface;
    private bool _isResumed;
    private bool _frameScheduled;
    private bool _firstFramePresented;
    private long _lastRenderedFrameTimeNanos;
    public int MaximumFramesPerSecond { get; set; }
    private readonly GestureDetector _gestureDetector;
    private readonly SurfaceView? _surfaceView;
    private readonly TextureView? _textureView;
    private Surface? _textureSurface;
    private bool _longPressEligible, _contextGestureActive;
    private bool _secondaryDown, _secondaryDragged;
    private float _secondaryX, _secondaryY;
    private byte[] _readbackRgba = [];
    private long _lastAlphaReadback;
    public bool AlphaReadbackEnabled { get; set; }
    public long RenderedFrameCount { get; private set; }
    public event Action<ReadOnlyMemory<byte>, int, int>? AlphaFrameAvailable;
    public event Action? AlphaReadbackUnavailable;

    public AndroidGameSurfaceView(Context context, GameProject? project, string? projectDirectory)
        : base(context)
    {
        if (project?.Window.DesktopSpriteMode == true)
        {
            // A SurfaceView creates a separate BLOCK_UNTRUSTED input-occlusion
            // layer. TextureView composites into the overlay's own window.
            _textureView = new TextureView(context) { SurfaceTextureListener = this };
            _textureView.SetOpaque(false);
            AddView(_textureView, new FrameLayout.LayoutParams(-1, -1));
        }
        else
        {
            _surfaceView = new SurfaceView(context);
            _surfaceView.SetZOrderMediaOverlay(true);
            _surfaceView.Holder?.AddCallback(this);
            AddView(_surfaceView, new FrameLayout.LayoutParams(-1, -1));
        }
        _gestureDetector = new GestureDetector(context, new LongPressListener(this));
        _requestedBackend = GraphicsBackendNames.Parse(project?.Runtime.GraphicsBackend);
        _renderHost = CreateRenderHost(_requestedBackend);
        _renderHost.BeforePresent = ReadAlphaFrame;
        LogBackendRequest(project, _requestedBackend);
        _renderHost.SetProject(project, projectDirectory);
        _project = project;
        _projectDirectory = projectDirectory ?? string.Empty;
        Focusable = true;
        FocusableInTouchMode = true;
        RequestFocus();
    }

    public AndroidInputSnapshot Input { get; private set; } = AndroidInputSnapshot.Empty;

    public GameProject? Project => _renderHost.Project;

    public bool IsReady => _renderHost.IsReady;
    public bool HasSceneFrame => _renderHost.HasSceneFrame;
    public string? LoadingError => _renderHost.LoadingError;

    public float LoadingProgress => _renderHost.LoadingProgress;

    public string LoadingMessage => _renderHost.LoadingMessage;

    internal bool TryGetBubblePosition(RuntimeDialogueBubble bubble, int width, int height, out System.Numerics.Vector2 position)
        => _renderHost.TryGetBubblePosition(bubble, width, height, out position);

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        _hasSurface = true;
        _surface = holder.Surface ?? throw new InvalidOperationException("Android surface is unavailable.");
        _renderHost.Resize(Math.Max(Width, 1), Math.Max(Height, 1));
        CreateSelectedSurface(_surface);
        ScheduleFrame();
    }

    public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
    {
        _renderHost.Resize(width, height);
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        _hasSurface = false;
        _surface = null;
        CancelFrame();
        _renderHost.DestroySurface();
    }

    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
    {
        surface.SetDefaultBufferSize(Math.Max(width, 1), Math.Max(height, 1));
        _textureSurface = new Surface(surface);
        _surface = _textureSurface;
        _hasSurface = true;
        _renderHost.Resize(Math.Max(width, 1), Math.Max(height, 1));
        CreateSelectedSurface(_textureSurface);
        ScheduleFrame();
    }
    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
    {
        surface.SetDefaultBufferSize(Math.Max(width, 1), Math.Max(height, 1));
        _renderHost.Resize(Math.Max(width, 1), Math.Max(height, 1));
    }
    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
    {
        _hasSurface = false; _surface = null; CancelFrame();
        _renderHost.DestroySurface();
        _textureSurface?.Dispose(); _textureSurface = null;
        return true;
    }
    public void OnSurfaceTextureUpdated(SurfaceTexture surface) { }

    public void ResumeRendering()
    {
        _lastRenderedFrameTimeNanos = 0;
        _isResumed = true;
        ScheduleFrame();
    }

    public void SetProject(GameProject? project, string? projectDirectory)
    {
        _firstFramePresented = false;
        GraphicsBackend requested = GraphicsBackendNames.Parse(project?.Runtime.GraphicsBackend);
        bool backendChanged = requested != _requestedBackend;
        _project = project;
        _projectDirectory = projectDirectory ?? string.Empty;
        _requestedBackend = requested;
        LogBackendRequest(project, requested);
        if (backendChanged)
        {
            _renderHost.Dispose();
            _renderHost = CreateRenderHost(requested);
            _renderHost.BeforePresent = ReadAlphaFrame;
            _renderHost.SetProject(project, _projectDirectory);
            if (_hasSurface && _surface is not null) CreateSelectedSurface(_surface);
            return;
        }

        _renderHost.SetProject(project, _projectDirectory);
    }

    public void RequestSceneChange(string scenePath)
    {
        _renderHost.RequestSceneChange(scenePath);
    }

    public bool RequestRenderTextureRefresh(string idOrName)
    {
        return _renderHost.RequestRenderTextureRefresh(idOrName);
    }

    public void DispatchContextMenuItem(ContextMenuSelection selection, ContextMenuItemSettings item, float x, float y)
        => _renderHost.DispatchContextMenuItem(selection, item, x, y);
    public void NotifyContextMenuOpened(ContextMenuSelection selection, float x, float y)
        => _renderHost.DispatchContextMenuOpened(selection, x, y);
    public void EndContextMenuInteraction() => _renderHost.SuppressCameraInput = false;
    public void BeginContextMenuInteraction() => _renderHost.SuppressCameraInput = true;

    public void PauseRendering()
    {
        CancelInput();
        _isResumed = false;
        CancelFrame();
        _renderHost.Pause();
    }

    public void CancelInput()
    {
        long now = global::Android.OS.SystemClock.UptimeMillis();
        using MotionEvent? cancel = MotionEvent.Obtain(now, now, MotionEventActions.Cancel, 0, 0, MetaKeyStates.None);
        if (cancel is not null) OnTouchEvent(cancel);
    }

    public void DoFrame(long frameTimeNanos)
    {
        _frameScheduled = false;
        if (!_hasSurface || !_isResumed)
        {
            return;
        }

        if (!AndroidDesktopSpritePolicy.ShouldRender(_lastRenderedFrameTimeNanos, frameTimeNanos, MaximumFramesPerSecond))
        {
            ScheduleFrame();
            return;
        }
        _lastRenderedFrameTimeNanos = frameTimeNanos;

        Input = _touchState.BeginFrame(Width, Height).WithDeviceInput(_deviceInputState.BeginFrame());
        _renderHost.Render(frameTimeNanos, Input);
        RenderedFrameCount++;
        LoadingStateChanged?.Invoke();
        // GUI controls can be changed by scripts every frame (for example progress bars).
        // Keep the overlay synchronized while the hardware Canvas layer avoids software
        // bitmap uploads on every invalidation.
        OverlayInvalidated?.Invoke();
        if (!_firstFramePresented && IsReady)
        {
            _firstFramePresented = true;
            FirstFramePresented?.Invoke();
        }
        ScheduleFrame();
    }

    public event Action? OverlayInvalidated;
    public event Action? LoadingStateChanged;
    public event Action? FirstFramePresented;
    public event Action<string>? RenderInitializationFailed;
    public event Action<GuiControlSettings, LayoutRect>? TextInputRequested;
    public event Action<ContextMenuSelection, float, float>? ContextMenuRequested;
    public void InvalidateOverlay() => OverlayInvalidated?.Invoke();
    public void DispatchGuiControlChanged(GuiControlSettings control)=>_renderHost.DispatchGuiControlChanged(control);

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return false;
        }

        if (e.ActionMasked == MotionEventActions.Down) _longPressEligible = e.GetToolType(0) is MotionEventToolType.Finger or MotionEventToolType.Stylus;
        if (e.PointerCount > 1 || e.ActionMasked == MotionEventActions.Cancel) _longPressEligible = false;
        if (_contextGestureActive)
        {
            if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel) _contextGestureActive = false;
            _gestureDetector.OnTouchEvent(e);
            return true;
        }

        bool secondaryMouse = e.GetToolType(0) == MotionEventToolType.Mouse
            && (_secondaryDown || ((int)e.ButtonState & 2) != 0 || (int)e.ActionButton == 2);
        if (!secondaryMouse) _touchState.Apply(e);
        _deviceInputState.ApplyMotion(e);
        if (e.ActionMasked is MotionEventActions.ButtonPress or MotionEventActions.ButtonRelease)
            _deviceInputState.SetMouseButton(MapMouseButton((int)e.ActionButton), e.ActionMasked == MotionEventActions.ButtonPress);
        if (HandleSecondaryButton(e)) return true;
        _gestureDetector.OnTouchEvent(e);
        if (e.ActionMasked == MotionEventActions.Up && e.GetToolType(0) != MotionEventToolType.Mouse)
        {
            RequestTextInput(e.GetX(), e.GetY());
        }
        return true;
    }

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        _deviceInputState.ApplyKey(e, true);
        return base.OnKeyDown(keyCode, e);
    }

    public override bool OnCapturedPointerEvent(MotionEvent? e)
    {
        if(e is null)return false;
        _deviceInputState.ApplyCapturedPointer(e);
        if(e.ActionMasked is MotionEventActions.ButtonPress or MotionEventActions.ButtonRelease)
            _deviceInputState.SetMouseButton(MapMouseButton((int)e.ActionButton),e.ActionMasked==MotionEventActions.ButtonPress);
        return true;
    }

    public override bool OnKeyUp(Keycode keyCode, KeyEvent? e)
    {
        _deviceInputState.ApplyKey(e, false);
        return base.OnKeyUp(keyCode, e);
    }

    public override bool OnGenericMotionEvent(MotionEvent? e)
    {
        _deviceInputState.ApplyMotion(e);
        if (e is not null && e.ActionMasked is MotionEventActions.ButtonPress or MotionEventActions.ButtonRelease)
            _deviceInputState.SetMouseButton(MapMouseButton((int)e.ActionButton), e.ActionMasked == MotionEventActions.ButtonPress);
        if (e is not null && HandleSecondaryButton(e)) return true;
        return base.OnGenericMotionEvent(e);
    }

    private bool HandleSecondaryButton(MotionEvent e)
    {
        if (e.ActionMasked == MotionEventActions.Cancel) { _secondaryDown = false; return false; }
        bool down = ((int)e.ButtonState & 2) != 0;
        if (e.ActionMasked is MotionEventActions.ButtonPress or MotionEventActions.ButtonRelease && (int)e.ActionButton == 2)
            down = e.ActionMasked == MotionEventActions.ButtonPress;
        if (down && !_secondaryDown) { _secondaryDown = true; _secondaryDragged = false; _secondaryX = e.GetX(); _secondaryY = e.GetY(); }
        float slop = 8 * (Resources?.DisplayMetrics?.Density ?? 1);
        if (_secondaryDown && MathF.Pow(e.GetX() - _secondaryX, 2) + MathF.Pow(e.GetY() - _secondaryY, 2) > slop * slop)
            _secondaryDragged = true;
        if (!down && _secondaryDown)
        {
            _secondaryDown = false;
            if (!_secondaryDragged) return TryOpenContextMenu(e.GetX(), e.GetY(), e);
        }
        return false;
    }

    private bool TryOpenContextMenu(float x, float y, MotionEvent e)
    {
        if (!IsReady || ContextMenuRequested is null) return false;
        ContextMenuSelection? selection = _renderHost.ResolveContextMenu(x, y);
        if (selection is null) return false;
        if (e.GetToolType(0) is MotionEventToolType.Finger or MotionEventToolType.Stylus && IsTextInputAt(x, y)
            && Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeContextMenuRouting.Normalize(selection.Menu.TargetType) != "gui_control") return false;
        using var cancel = MotionEvent.Obtain(e.DownTime, global::Android.OS.SystemClock.UptimeMillis(), MotionEventActions.Cancel, x, y, MetaKeyStates.None);
        if (cancel is not null) _touchState.Apply(cancel);
        _contextGestureActive = e.ActionMasked != MotionEventActions.Up && e.ActionMasked != MotionEventActions.ButtonRelease;
        BeginContextMenuInteraction();
        ContextMenuRequested.Invoke(selection, x, y);
        return true;
    }

    private static int MapMouseButton(int button) => button switch
    {
        1 => 0,
        2 => 1,
        4 => 2,
        8 => 3,
        16 => 4,
        _ => -1
    };

    private void RequestTextInput(float x, float y)
    {
        GameProject? project = Project;
        if (project is null) return;
        foreach (GuiControlSettings control in project.Scene.GuiControls.Where(control => control.Visible).Reverse())
        {
            if (!string.Equals(control.Type, "textbox", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(control.Type, "text_input", StringComparison.OrdinalIgnoreCase)) continue;
            LayoutRect rect = LayoutResolver.Resolve(control.LayoutMode, control.X, control.Y, control.Width, control.Height,
                Width, Height, project.Window.Width, project.Window.Height);
            if (x >= rect.X && x <= rect.X + rect.Width && y >= rect.Y && y <= rect.Y + rect.Height)
            {
                TextInputRequested?.Invoke(control, rect);
                return;
            }
        }
    }

    private sealed class LongPressListener(AndroidGameSurfaceView owner) : GestureDetector.SimpleOnGestureListener
    {
        public override bool OnDown(MotionEvent e) => true;

        public override void OnLongPress(MotionEvent e)
        {
            if (!owner._longPressEligible || !owner.IsReady || owner.ContextMenuRequested is null) return;
            owner.TryOpenContextMenu(e.GetX(), e.GetY(), e);
        }
    }

    private bool IsTextInputAt(float x, float y)
    {
        GameProject? project = Project;
        if (project is null) return false;
        return project.Scene.GuiControls.Any(control =>
            control.Visible
            && (string.Equals(control.Type, "textbox", StringComparison.OrdinalIgnoreCase)
                || string.Equals(control.Type, "text_input", StringComparison.OrdinalIgnoreCase))
            && LayoutResolver.Resolve(control.LayoutMode, control.X, control.Y, control.Width, control.Height,
                Width, Height, project.Window.Width, project.Window.Height) is LayoutRect rect
            && x >= rect.X && x <= rect.X + rect.Width
            && y >= rect.Y && y <= rect.Y + rect.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelFrame();
            _surfaceView?.Holder?.RemoveCallback(this);
            if (_textureView is not null) _textureView.SurfaceTextureListener = null;
            _renderHost.Dispose();
            _textureSurface?.Dispose(); _textureSurface = null;
            _surfaceView?.Dispose(); _textureView?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ScheduleFrame()
    {
        if (_frameScheduled || !_hasSurface || !_isResumed)
        {
            return;
        }

        Choreographer.Instance?.PostFrameCallback(this);
        _frameScheduled = true;
    }

    private void CancelFrame()
    {
        if (!_frameScheduled)
        {
            return;
        }

        Choreographer.Instance?.RemoveFrameCallback(this);
        _frameScheduled = false;
    }

    private void CreateSelectedSurface(Surface surface)
    {
        try
        {
            _renderHost.CreateSurface(surface);
            Log.Info(
                LogTag,
                $"Android graphics backend selected: requested={_requestedBackend.ToSettingValue()}; " +
                $"resolved={DescribeBackend(_renderHost)}.");
        }
        catch (Exception ex) when ((_requestedBackend == GraphicsBackend.Auto || _project?.Window.DesktopSpriteMode == true)
            && _renderHost is AndroidVulkanRenderHost)
        {
            Log.Warn(LogTag, $"Vulkan initialization failed in Auto/desktop sprite mode; falling back to OpenGL ES. {ex}");
            _renderHost.Dispose();
            _renderHost = new AndroidEglRenderHost();
            _renderHost.BeforePresent = ReadAlphaFrame;
            _renderHost.SetProject(_project, _projectDirectory);
            try
            {
                _renderHost.CreateSurface(surface);
                Log.Info(
                    LogTag,
                    "Android graphics backend selected: requested=Auto; resolved=OpenGL ES; " +
                    "fallbackReason=Vulkan initialization failed.");
            }
            catch (Exception fallbackException)
            {
                ReportRenderInitializationFailure("OpenGL ES fallback initialization failed", fallbackException);
            }
        }
        catch (Exception ex)
        {
            ReportRenderInitializationFailure($"{_requestedBackend} initialization failed", ex);
        }
    }

    private void ReportRenderInitializationFailure(string message, Exception exception)
    {
        _hasSurface = false;
        CancelFrame();
        string detail = $"{message}: {exception.Message}";
        Log.Error(LogTag, $"{detail}\n{exception}");
        RenderInitializationFailed?.Invoke(detail);
    }

    private static IAndroidRenderHost CreateRenderHost(GraphicsBackend requestedBackend)
        => requestedBackend == GraphicsBackend.OpenGL
            ? new AndroidEglRenderHost()
            : new AndroidVulkanRenderHost();

    private static void LogBackendRequest(
        GameProject? project,
        GraphicsBackend requestedBackend)
    {
        if (project is null)
        {
            return;
        }

        string configured = string.IsNullOrWhiteSpace(project.Runtime.GraphicsBackend)
            ? "Auto"
            : project.Runtime.GraphicsBackend.Trim();
        Log.Info(
            LogTag,
            $"Android graphics backend request: configured={configured}; " +
            $"requested={requestedBackend.ToSettingValue()}; " +
            $"initial={(requestedBackend == GraphicsBackend.OpenGL ? "OpenGL ES" : "Vulkan")}" +
            (requestedBackend == GraphicsBackend.Auto ? "; fallback=OpenGL ES." : "."));
    }

    private static string DescribeBackend(IAndroidRenderHost renderHost)
        => renderHost is AndroidVulkanRenderHost ? "Vulkan" : "OpenGL ES";

    private void ReadAlphaFrame(IRenderer renderer)
    {
        if (!AlphaReadbackEnabled || AlphaFrameAvailable is null) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastAlphaReadback != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_lastAlphaReadback, now).TotalMilliseconds < 125) return;
        _lastAlphaReadback = now;
        try
        {
            int width = renderer.BackBufferSize.X, height = renderer.BackBufferSize.Y;
            int count = checked(width * height * 4);
            if (_readbackRgba.Length != count) _readbackRgba = new byte[count];
            if (renderer.TryReadBackBufferRgba(_readbackRgba)) AlphaFrameAvailable.Invoke(_readbackRgba, width, height);
        }
        catch (Exception ex)
        {
            AlphaReadbackEnabled = false;
            AlphaReadbackUnavailable?.Invoke();
            global::Android.Util.Log.Warn(LogTag, "Sprite touch readback disabled: " + ex.Message);
        }
    }
}
