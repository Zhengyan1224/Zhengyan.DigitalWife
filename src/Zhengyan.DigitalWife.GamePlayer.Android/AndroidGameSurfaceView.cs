using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidGameSurfaceView : SurfaceView, ISurfaceHolderCallback, Choreographer.IFrameCallback
{
    private const string LogTag = "ZhengyanGamePlayer";
    private readonly AndroidTouchState _touchState = new();
    private IAndroidRenderHost _renderHost;
    private GameProject? _project;
    private string _projectDirectory = string.Empty;
    private GraphicsBackend _requestedBackend;
    private Surface? _surface;
    private bool _hasSurface;
    private bool _isResumed;
    private bool _frameScheduled;
    private bool _firstFramePresented;
    private readonly GestureDetector _gestureDetector;

    public AndroidGameSurfaceView(Context context, GameProject? project, string? projectDirectory)
        : base(context)
    {
        SetZOrderMediaOverlay(true);
        _gestureDetector = new GestureDetector(context, new LongPressListener(this));
        _requestedBackend = GraphicsBackendNames.Parse(project?.Runtime.GraphicsBackend);
        _renderHost = CreateRenderHost(_requestedBackend);
        _renderHost.SetProject(project, projectDirectory);
        _project = project;
        _projectDirectory = projectDirectory ?? string.Empty;
        Holder?.AddCallback(this);
        Focusable = true;
        FocusableInTouchMode = true;
        RequestFocus();
    }

    public AndroidInputSnapshot Input { get; private set; } = AndroidInputSnapshot.Empty;

    public GameProject? Project => _renderHost.Project;

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

    public void ResumeRendering()
    {
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
        if (backendChanged)
        {
            _renderHost.Dispose();
            _renderHost = CreateRenderHost(requested);
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

    public void DispatchContextMenuItem(ContextMenuSettings menu, ContextMenuItemSettings item, float x, float y)
        => _renderHost.DispatchContextMenuItem(menu, item, x, y);

    public void PauseRendering()
    {
        _isResumed = false;
        CancelFrame();
        _renderHost.Pause();
    }

    public void DoFrame(long frameTimeNanos)
    {
        _frameScheduled = false;
        if (!_hasSurface || !_isResumed)
        {
            return;
        }

        Input = _touchState.BeginFrame(Width, Height);
        _renderHost.Render(frameTimeNanos, Input);
        if (!_firstFramePresented)
        {
            _firstFramePresented = true;
            FirstFramePresented?.Invoke();
        }
        OverlayInvalidated?.Invoke();
        ScheduleFrame();
    }

    public event Action? OverlayInvalidated;
    public event Action? FirstFramePresented;
    public event Action<GuiControlSettings, LayoutRect>? TextInputRequested;
    public event Action<ContextMenuSettings, float, float>? ContextMenuRequested;

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return false;
        }

        _touchState.Apply(e);
        _gestureDetector.OnTouchEvent(e);
        if (e.ActionMasked == MotionEventActions.Up)
        {
            RequestTextInput(e.GetX(), e.GetY());
        }
        return true;
    }

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
            ContextMenuSettings? menu = owner.Project?.Scene.ContextMenus.FirstOrDefault(menu => menu.Enabled);
            if (menu is not null) owner.ContextMenuRequested?.Invoke(menu, e.GetX(), e.GetY());
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelFrame();
            Holder?.RemoveCallback(this);
            _renderHost.Dispose();
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
        }
        catch (Exception ex) when (_requestedBackend == GraphicsBackend.Auto
            && _renderHost is AndroidVulkanRenderHost)
        {
            Log.Warn(LogTag, $"Vulkan initialization failed in Auto mode; falling back to OpenGL ES. {ex}");
            _renderHost.Dispose();
            _renderHost = new AndroidEglRenderHost();
            _renderHost.SetProject(_project, _projectDirectory);
            _renderHost.CreateSurface(surface);
        }
    }

    private static IAndroidRenderHost CreateRenderHost(GraphicsBackend requestedBackend)
        => requestedBackend == GraphicsBackend.OpenGL
            ? new AndroidEglRenderHost()
            : new AndroidVulkanRenderHost();
}
