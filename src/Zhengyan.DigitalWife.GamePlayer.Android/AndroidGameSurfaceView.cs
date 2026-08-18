using Android.Content;
using Android.Graphics;
using Android.Views;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidGameSurfaceView : SurfaceView, ISurfaceHolderCallback, Choreographer.IFrameCallback
{
    private readonly AndroidTouchState _touchState = new();
    private readonly AndroidEglRenderHost _renderHost = new();
    private bool _hasSurface;
    private bool _isResumed;
    private bool _frameScheduled;

    public AndroidGameSurfaceView(Context context, GameProject? project, string? projectDirectory)
        : base(context)
    {
        _renderHost.SetProject(project, projectDirectory);
        Holder?.AddCallback(this);
        Focusable = true;
        FocusableInTouchMode = true;
        RequestFocus();
    }

    public AndroidInputSnapshot Input { get; private set; } = AndroidInputSnapshot.Empty;

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        _hasSurface = true;
        _renderHost.CreateSurface(holder.Surface ?? throw new InvalidOperationException("Android surface is unavailable."));
        ScheduleFrame();
    }

    public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
    {
        _renderHost.Resize(width, height);
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        _hasSurface = false;
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
        _renderHost.SetProject(project, projectDirectory);
    }

    public void RequestSceneChange(string scenePath)
    {
        _renderHost.RequestSceneChange(scenePath);
    }

    public bool RequestRenderTextureRefresh(string idOrName)
    {
        return _renderHost.RequestRenderTextureRefresh(idOrName);
    }

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
        ScheduleFrame();
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return false;
        }

        _touchState.Apply(e);
        return true;
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
}
