using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using System.Collections.Concurrent;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

[Service(Name = "com.zhengyan.digitalwife.gameplayer.DesktopSpriteService", Exported = false)]
public sealed class AndroidDesktopSpriteService : Service
{
    private const string ChannelId = "desktop-sprite";
    private const int NotificationId = 7100;
    private const string TokenExtra = "desktop-sprite.project-token";
    private const string ToggleAction = "desktop-sprite.toggle";
    private const string ShowAction = "desktop-sprite.show";
    private const string StopAction = "desktop-sprite.stop";
    private static readonly ConcurrentDictionary<string, AndroidGameProjectLoadResult> Pending = new();
    private readonly Handler _ui = new(Looper.MainLooper!);
    private AndroidGameProjectLoadResult? _loadedProject;
    private IWindowManager? _windows;
    private Context? _windowContext;
    private WindowManagerLayoutParams? _layout;
    private FrameLayout? _root;
    private AndroidGameSurfaceView? _game;
    private AndroidGuiOverlayView? _gui;
    private TextView? _handle;
    private EditText? _editor;
    private GuiControlSettings? _editingControl;
    private ScreenReceiver? _screenReceiver;
    private bool _visible = true;
    private bool _screenOn = true;
    private bool _stopping;
    private bool _dragging;
    private float _dragX, _dragY;
    private int _windowX, _windowY;
    private AndroidContextMenuPopup? _contextMenuPopup;
    private Bitmap? _guiMask;
    private int[] _guiMaskPixels = [];
    private int? _touchRegionHash;
    private float _visibleOpacity = 1;

    internal static void Start(Activity activity, AndroidGameProjectLoadResult project)
    {
        if (!Settings.CanDrawOverlays(activity)) throw new InvalidOperationException("没有显示在其他应用上层的权限。");
        CancelPending();
        string token = Guid.NewGuid().ToString("N");
        Pending[token] = project;
        try
        {
            using Intent intent = new(activity, typeof(AndroidDesktopSpriteService));
            intent.PutExtra(TokenExtra, token);
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) activity.StartForegroundService(intent);
            else activity.StartService(intent);
        }
        catch
        {
            Pending.TryRemove(token, out _); // The caller still owns the result on failure.
            throw;
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    internal static void CancelPending()
    {
        foreach (string token in Pending.Keys)
            if (Pending.TryRemove(token, out var project)) project.Dispose();
    }

    internal void PrepareToStop()
    {
        SetVisible(false);
        _stopping = true;
        StopSelf();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == StopAction) { StopSelf(); return StartCommandResult.NotSticky; }
        if (intent?.GetStringExtra(TokenExtra) is { } token && Pending.TryRemove(token, out var project))
        {
            DestroyWindow();
            _loadedProject?.Dispose();
            _loadedProject = project;
            _stopping = false;
            try
            {
                BeginForeground();
                if (!Settings.CanDrawOverlays(this)) throw new InvalidOperationException("悬浮窗权限已被撤销。");
                CreateWindow(project);
                AndroidWindowPlatform.Register(this);
            }
            catch (Exception ex)
            {
                Log.Error("ZhengyanDesktopSprite", ex.ToString());
                Toast.MakeText(this, "桌面精灵启动失败：" + ex.Message, ToastLength.Long)?.Show();
                StopSelf();
            }
        }
        else if (_root is not null)
        {
            if (intent?.Action == ToggleAction) SetVisible(!_visible);
            else if (intent?.Action == ShowAction) SetVisible(true);
        }
        else StopSelf(startId); // A canceled older handoff must not stop a newer queued project.
        return StartCommandResult.NotSticky;
    }

    private void BeginForeground()
    {
        NotificationManager manager = (NotificationManager)GetSystemService(NotificationService)!;
        Notification.Builder builder;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "桌面精灵", NotificationImportance.Low));
            builder = new Notification.Builder(this, ChannelId);
        }
        else builder = new Notification.Builder(this);
        PendingIntent Action(string name, int id) => PendingIntent.GetService(this, id,
            new Intent(this, typeof(AndroidDesktopSpriteService)).SetAction(name), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        using PendingIntent show = Action(ShowAction, 1);
        using PendingIntent toggle = Action(ToggleAction, 2);
        using PendingIntent stop = Action(StopAction, 3);
        builder.SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay);
        builder.SetContentTitle(_loadedProject?.Project?.Name ?? "桌面精灵");
        builder.SetContentText("桌面精灵正在运行 · 点击显示人物");
        builder.SetContentIntent(show);
        builder.SetOngoing(true);
        using var toggleIcon = global::Android.Graphics.Drawables.Icon.CreateWithResource(this, global::Android.Resource.Drawable.IcMenuView);
        using var stopIcon = global::Android.Graphics.Drawables.Icon.CreateWithResource(this, global::Android.Resource.Drawable.IcMenuCloseClearCancel);
        using var toggleBuilder = new Notification.Action.Builder(toggleIcon, "显示 / 隐藏", toggle);
        using var stopBuilder = new Notification.Action.Builder(stopIcon, "关闭", stop);
        builder.AddAction(toggleBuilder.Build());
        builder.AddAction(stopBuilder.Build());
        using Notification notification = builder.Build() ?? throw new InvalidOperationException("无法创建桌面精灵通知。");
        bool microphone = CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio) == Permission.Granted;
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse | (microphone ? ForegroundService.TypeMicrophone : 0));
        else if (OperatingSystem.IsAndroidVersionAtLeast(30))
            StartForeground(NotificationId, notification, microphone ? ForegroundService.TypeMicrophone : ForegroundService.TypeNone);
        else StartForeground(NotificationId, notification);
    }

    private void CreateWindow(AndroidGameProjectLoadResult project)
    {
        _windows = GetSystemService(WindowService)?.JavaCast<IWindowManager>()
            ?? throw new InvalidOperationException("Android WindowManager 不可用。");
        _windowContext = this;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
#pragma warning disable CA1422, CS0618
            Display display = _windows.DefaultDisplay ?? throw new InvalidOperationException("Android display 不可用。");
#pragma warning restore CA1422, CS0618
            _windowContext = OperatingSystem.IsAndroidVersionAtLeast(31)
                ? CreateWindowContext(display, (int)WindowManagerTypes.ApplicationOverlay, null)
                : CreateDisplayContext(display)?.CreateWindowContext((int)WindowManagerTypes.ApplicationOverlay, null);
            if (_windowContext is null) throw new InvalidOperationException("无法创建悬浮窗显示上下文。");
            _windows = _windowContext.GetSystemService(WindowService)?.JavaCast<IWindowManager>()
                ?? throw new InvalidOperationException("悬浮窗 WindowManager 不可用。");
        }
        GameProject settings = project.Project!;
        _visibleOpacity = 1;
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && settings.Window.DesktopSpriteClickThrough)
        {
            var input = GetSystemService(InputService) as global::Android.Hardware.Input.InputManager;
            _visibleOpacity = Math.Clamp(input?.MaximumObscuringOpacityForTouch ?? .8f, 0, 1);
        }
        var screen = ScreenSize();
        var size = AndroidDesktopSpritePolicy.Size(settings.Window.Width, settings.Window.Height, screen.Width, screen.Height, Density);
        _layout = new WindowManagerLayoutParams(size.Width, size.Height,
            OperatingSystem.IsAndroidVersionAtLeast(26) ? WindowManagerTypes.ApplicationOverlay : WindowManagerTypes.Phone,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal, Format.Translucent)
        {
            Gravity = GravityFlags.Top | GravityFlags.Left,
            X = Math.Max(0, screen.Width - size.Width - Dp(16)),
            Y = Math.Max(0, screen.Height - size.Height - Dp(16)),
            SoftInputMode = SoftInput.AdjustResize,
            Alpha = _visibleOpacity
        };
        _root = new SpriteRoot(this, _windowContext);
        _root.SetBackgroundColor(Color.Transparent);
        _game = new AndroidGameSurfaceView(_windowContext, settings, project.ProjectDirectory)
        {
            MaximumFramesPerSecond = AndroidDesktopSpritePolicy.FramesPerSecond,
            AlphaReadbackEnabled = OperatingSystem.IsAndroidVersionAtLeast(33) && settings.Window.DesktopSpriteClickThrough
        };
        _game.AlphaFrameAvailable += UpdateTouchRegion;
        _game.AlphaReadbackUnavailable += ResetTouchRegion;
        _gui = new AndroidGuiOverlayView(_windowContext, _game);
        _game.OverlayInvalidated += RefreshOverlay;
        _game.LoadingStateChanged += UpdateLoading;
        _game.RenderInitializationFailed += RenderFailed;
        _game.TextInputRequested += ShowTextEditor;
        _game.ContextMenuRequested += ShowGameMenu;
        _root.AddView(_game, new FrameLayout.LayoutParams(-1, -1));
        _root.AddView(_gui, new FrameLayout.LayoutParams(-1, -1));
        LinearLayout bar = new(_windowContext) { Orientation = global::Android.Widget.Orientation.Horizontal };
        bar.SetBackgroundColor(Color.Argb(140, 20, 30, 45));
        _handle = new TextView(_windowContext) { Text = "拖动 · 加载中", Gravity = GravityFlags.CenterVertical };
        _handle.SetTextColor(Color.White);
        _handle.SetTextSize(ComplexUnitType.Sp, 12);
        _handle.SetPadding(Dp(8), 0, 0, 0);
        _handle.Touch += DragWindow;
        bar.AddView(_handle, new LinearLayout.LayoutParams(0, Dp(32), 1));
        Button close = new(_windowContext) { Text = "×", ContentDescription = "关闭桌面精灵" };
        close.SetPadding(0, 0, 0, 0);
        close.SetTextColor(Color.White);
        close.SetBackgroundColor(Color.Transparent);
        close.Click += (_, _) => StopSelf();
        bar.AddView(close, new LinearLayout.LayoutParams(Dp(36), Dp(32)));
        _root.AddView(bar, new FrameLayout.LayoutParams(-1, Dp(32), GravityFlags.Top));
        _windows.AddView(_root, _layout);
        _visible = true;
        _screenOn = (GetSystemService(PowerService) as PowerManager)?.IsInteractive != false
            && (GetSystemService(KeyguardService) as KeyguardManager)?.IsKeyguardLocked != true;
        if (_screenReceiver is null)
        {
            _screenReceiver = new ScreenReceiver(this);
            IntentFilter filter = new(); filter.AddAction(Intent.ActionScreenOff); filter.AddAction(Intent.ActionScreenOn);
            filter.AddAction(Intent.ActionUserPresent);
            if (OperatingSystem.IsAndroidVersionAtLeast(33)) RegisterReceiver(_screenReceiver, filter, ReceiverFlags.NotExported);
            else RegisterReceiver(_screenReceiver, filter);
        }
        ResumeIfVisible();
        Log.Info("ZhengyanDesktopSprite", $"Overlay started: {size.Width}x{size.Height}; backend={settings.Runtime.GraphicsBackend}; fps=30");
    }

    private float Density => (_windowContext?.Resources ?? Resources)?.DisplayMetrics?.Density ?? 1;
    private int Dp(int value) => Math.Max(1, (int)(value * Density));
    private (int Width, int Height) ScreenSize()
    {
        using DisplayMetrics metrics = new();
#pragma warning disable CA1422, CS0618
        _windows?.DefaultDisplay?.GetMetrics(metrics);
#pragma warning restore CA1422, CS0618
        return (Math.Max(1, metrics.WidthPixels), Math.Max(1, metrics.HeightPixels));
    }

    private void DragWindow(object? sender, View.TouchEventArgs args)
    {
        if (_layout is null || args.Event is not { } touch) return;
        if (touch.PointerCount > 1) { _dragging = false; args.Handled = true; return; }
        switch (touch.ActionMasked)
        {
            case MotionEventActions.Down:
                CloseTextEditor();
                _dragging = true; _dragX = touch.RawX; _dragY = touch.RawY; _windowX = _layout.X; _windowY = _layout.Y;
                break;
            case MotionEventActions.Move when _dragging:
                var screen = ScreenSize();
                var position = AndroidDesktopSpritePolicy.Position(_windowX + (int)(touch.RawX - _dragX),
                    _windowY + (int)(touch.RawY - _dragY), _layout.Width, _layout.Height, screen.Width, screen.Height);
                _layout.X = position.X; _layout.Y = position.Y;
                UpdateWindow();
                break;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel: _dragging = false; break;
        }
        args.Handled = true;
    }

    private void UpdateWindow()
    {
        if (_root is null || _layout is null || _stopping) return;
        try { _windows?.UpdateViewLayout(_root, _layout); }
        catch (Exception ex) { Log.Warn("ZhengyanDesktopSprite", ex.ToString()); StopSelf(); }
    }

    internal void ApplyScriptWindowCommand(RuntimeWindowCommand command)
    {
        FrameLayout? target = _root;
        _ui.Post(() =>
        {
            if (target is null || !ReferenceEquals(target, _root) || _layout is null || _stopping) return;
            switch (command.Operation)
            {
                case "exit": StopSelf(); break;
                case "visible": SetVisible(command.Flag); break;
                case "size":
                    CloseTextEditor();
                    if (_loadedProject?.Project is { } sized) { sized.Window.Width = command.Width; sized.Window.Height = command.Height; }
                    var screen = ScreenSize();
                    var size = AndroidDesktopSpritePolicy.Size(command.Width, command.Height, screen.Width, screen.Height, Density);
                    _layout.Width = size.Width; _layout.Height = size.Height;
                    ClampPosition(); UpdateWindow(); break;
                case "fullscreen": Toast.MakeText(this, "桌面精灵使用悬浮小窗。", ToastLength.Short)?.Show(); break;
                case "title": if (_loadedProject?.Project is { } titled) titled.Window.Title = command.Text; break;
                case "timing": if (_loadedProject?.Project is { } timed) timed.Window.TimingMode = command.Text; break;
                case "resizable": if (_loadedProject?.Project is { } resizable) resizable.Window.Resizable = command.Flag; break;
            }
        });
    }

    internal bool SetCursorVisible(bool visible)
    {
        if (_game is null) return false;
        _ui.Post(() => { if (_game is not null) _game.PointerIcon = PointerIcon.GetSystemIcon(this, visible ? PointerIconType.Arrow : PointerIconType.Null); });
        return true;
    }

    private void SetVisible(bool visible)
    {
        _contextMenuPopup?.Dismiss();
        _touchRegionHash = null;
        CloseTextEditor();
        _visible = visible;
        // Keep the Surface alive while hidden so animation/script state is
        // paused and resumed instead of reloaded by SurfaceDestroyed.
        if (_layout is not null)
        {
            _layout.Alpha = visible ? _visibleOpacity : 0;
            if (visible) _layout.Flags &= ~WindowManagerFlags.NotTouchable;
            else _layout.Flags |= WindowManagerFlags.NotTouchable;
            UpdateWindow();
        }
        if (_root is not null) _root.ImportantForAccessibility = visible
            ? ImportantForAccessibility.Auto : ImportantForAccessibility.NoHideDescendants;
        ResumeIfVisible();
    }

    private void ResumeIfVisible()
    {
        if (_visible && _screenOn) _game?.ResumeRendering();
        else { _game?.CancelInput(); _game?.PauseRendering(); }
    }

    private sealed class ScreenReceiver(AndroidDesktopSpriteService service) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            service._screenOn = intent?.Action != Intent.ActionScreenOff
                && (service.GetSystemService(KeyguardService) as KeyguardManager)?.IsKeyguardLocked != true;
            if (!service._screenOn) { service._contextMenuPopup?.Dismiss(); service.CloseTextEditor(); }
            service.ResumeIfVisible();
        }
    }

    public override void OnConfigurationChanged(Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        CloseTextEditor();
        if (_layout is null || _loadedProject?.Project is not { } project) return;
        var screen = ScreenSize();
        var size = AndroidDesktopSpritePolicy.Size(project.Window.Width, project.Window.Height, screen.Width, screen.Height, Density);
        _layout.Width = size.Width; _layout.Height = size.Height;
        ClampPosition(); UpdateWindow();
    }

    private void ClampPosition()
    {
        if (_layout is null) return;
        var screen = ScreenSize();
        var point = AndroidDesktopSpritePolicy.Position(_layout.X, _layout.Y, _layout.Width, _layout.Height, screen.Width, screen.Height);
        _layout.X = point.X; _layout.Y = point.Y;
    }

    private void UpdateLoading()
    {
        if (_game is null) return;
        if (_handle is not null) _handle.Text = _game.IsReady ? "拖动" : $"加载 {Math.Round(_game.LoadingProgress * 100)}%";
        if (_game.LoadingError is { } error) RenderFailed(error);
    }
    private void RefreshOverlay()
    {
        _contextMenuPopup?.Validate();
        _gui?.Refresh();
        if (_editingControl is not null && _game?.Project?.Scene.GuiControls.Contains(_editingControl) != true) CloseTextEditor();
    }
    private void RenderFailed(string message)
    {
        if (_stopping) return;
        _stopping = true;
        Log.Error("ZhengyanDesktopSprite", message);
        Toast.MakeText(this, "桌面精灵渲染失败：" + message, ToastLength.Long)?.Show();
        StopSelf();
    }

    private void ShowGameMenu(ContextMenuSettings menu, float x, float y)
    {
        if (_game is null || _root is null) return;
        _contextMenuPopup ??= new AndroidContextMenuPopup(_root, _game);
        _contextMenuPopup.Show(menu, x, y);
    }

    private void ShowTextEditor(GuiControlSettings control, LayoutRect rect)
    {
        CloseTextEditor();
        if (_root is null || _layout is null || _game is null) return;
        _editingControl = control;
        _editor = new EditText(_windowContext ?? this) { Text = control.Text };
        _editor.SetSingleLine(!control.Multiline);
        _editor.SetTextColor(Color.White);
        _editor.SetBackgroundColor(Color.Argb(230, 25, 35, 50));
        _editor.TextChanged += (_, _) =>
        {
            if (_editor is null) return;
            bool changed = control.Text != _editor.Text;
            control.Text = _editor.Text ?? string.Empty;
            control.SelectionStart = Math.Max(0, _editor.SelectionStart); control.SelectionEnd = Math.Max(0, _editor.SelectionEnd);
            _game?.InvalidateOverlay();
            if (changed) _game?.DispatchGuiControlChanged(control);
        };
        _editor.EditorAction += (_, args) => { args.Handled = true; CloseTextEditor(); };
        _gui!.EditingControl = control;
        _root.AddView(_editor, new FrameLayout.LayoutParams(Math.Max(1, (int)rect.Width), Math.Max(1, (int)rect.Height))
        { LeftMargin = (int)rect.X, TopMargin = (int)rect.Y });
        _layout.Flags &= ~WindowManagerFlags.NotFocusable;
        UpdateWindow();
        _editor.RequestFocus();
        (GetSystemService(InputMethodService) as InputMethodManager)?.ShowSoftInput(_editor, ShowFlags.Implicit);
    }

    private void CloseTextEditor()
    {
        if (_editor is null) return;
        (GetSystemService(InputMethodService) as InputMethodManager)?.HideSoftInputFromWindow(_editor.WindowToken, HideSoftInputFlags.None);
        _root?.RemoveView(_editor); _editor.Dispose(); _editor = null; _editingControl = null;
        if (_gui is not null) _gui.EditingControl = null;
        if (_layout is not null) { _layout.Flags |= WindowManagerFlags.NotFocusable; UpdateWindow(); }
    }

    private void UpdateTouchRegion(ReadOnlyMemory<byte> pixels, int width, int height)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) || !_visible || _root?.RootSurfaceControl is not { } surface || _stopping) return;
        try
        {
            if (_guiMask is null || _guiMask.Width != width || _guiMask.Height != height)
            {
                _guiMask?.Dispose();
                _guiMask = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
                _guiMaskPixels = new int[checked(width * height)];
                _touchRegionHash = null;
            }
            using (Canvas canvas = new(_guiMask))
            {
                canvas.DrawColor(Color.Transparent, PorterDuff.Mode.Clear!);
                _gui?.Draw(canvas);
            }
            _guiMask.GetPixels(_guiMaskPixels, 0, width, 0, 0, width, height);
            List<AndroidTouchRect> rects = AndroidAlphaTouchRegion.Build(pixels.Span, _guiMaskPixels, width, height);
            rects.Add(new(0, 0, width, Math.Min(height, Dp(32)))); // Drag handle and close button stay touchable.
            if (_editor is not null) rects.Add(new(_editor.Left, _editor.Top, _editor.Right, _editor.Bottom));
            HashCode hash = new(); hash.Add(width); hash.Add(height);
            foreach (var rect in rects) hash.Add(rect);
            int signature = hash.ToHashCode();
            if (_touchRegionHash == signature) return;
            using Region region = new();
            foreach (var rect in rects) region.InvokeOp(rect.Left, rect.Top, rect.Right, rect.Bottom, Region.Op.Union!);
            surface.SetTouchableRegion(region);
            if (_touchRegionHash is null) Log.Info("ZhengyanDesktopSprite", $"Alpha touch region enabled: {rects.Count} rectangles; {width}x{height}");
            _touchRegionHash = signature;
        }
        catch (Exception ex)
        {
            Log.Warn("ZhengyanDesktopSprite", "Alpha touch region disabled: " + ex.Message);
            if (_game is not null) _game.AlphaReadbackEnabled = false;
            ResetTouchRegion();
        }
    }

    private void ResetTouchRegion()
    {
        _touchRegionHash = null;
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;
        try { _root?.RootSurfaceControl?.SetTouchableRegion(null); }
        catch (Exception ex) { Log.Warn("ZhengyanDesktopSprite", "Reset touch region: " + ex.Message); }
    }

    private sealed class SpriteRoot(AndroidDesktopSpriteService service, Context context) : FrameLayout(context)
    {
        public override bool DispatchTouchEvent(MotionEvent? touch)
        {
            if (touch?.ActionMasked == MotionEventActions.Down && service._editor is { } editor)
            {
                using Rect bounds = new();
                if (!editor.GetGlobalVisibleRect(bounds) || !bounds.Contains((int)touch.RawX, (int)touch.RawY)) service.CloseTextEditor();
            }
            return base.DispatchTouchEvent(touch);
        }
        public override void OnWindowFocusChanged(bool hasWindowFocus)
        {
            base.OnWindowFocusChanged(hasWindowFocus);
            if (!hasWindowFocus) service.CloseTextEditor();
        }
    }

    private void DestroyWindow()
    {
        _contextMenuPopup?.Dispose(); _contextMenuPopup = null;
        _guiMask?.Dispose(); _guiMask = null; _guiMaskPixels = []; _touchRegionHash = null;
        CloseTextEditor();
        if (_game is not null)
        {
            _game.PauseRendering();
            _game.OverlayInvalidated -= RefreshOverlay; _game.LoadingStateChanged -= UpdateLoading;
            _game.RenderInitializationFailed -= RenderFailed; _game.TextInputRequested -= ShowTextEditor; _game.ContextMenuRequested -= ShowGameMenu;
            _game.AlphaFrameAvailable -= UpdateTouchRegion; _game.AlphaReadbackUnavailable -= ResetTouchRegion;
            _root?.RemoveView(_game);
            _game.Dispose(); _game = null;
        }
        if (_root is not null)
        {
            try { _windows?.RemoveViewImmediate(_root); } catch (Java.Lang.IllegalArgumentException) { }
            _root.Dispose(); _root = null;
        }
        _gui?.Dispose(); _gui = null; _layout?.Dispose(); _layout = null; _handle = null;
        if (_windowContext is not null && !ReferenceEquals(_windowContext, this)) _windowContext.Dispose();
        _windowContext = null;
    }

    public override void OnDestroy()
    {
        _stopping = true;
        AndroidWindowPlatform.Unregister(this);
        if (_screenReceiver is not null) { UnregisterReceiver(_screenReceiver); _screenReceiver.Dispose(); _screenReceiver = null; }
        DestroyWindow();
        _loadedProject?.Dispose(); _loadedProject = null;
        StopForeground(StopForegroundFlags.Remove);
        _ui.Dispose();
        base.OnDestroy();
    }

    protected override void Dump(Java.IO.FileDescriptor? fd, Java.IO.PrintWriter? writer, string[]? args)
    {
        writer?.Println($"desktopSprite visible={_visible} screenOn={_screenOn} frames={_game?.RenderedFrameCount ?? 0} alphaReadback={_game?.AlphaReadbackEnabled == true} touchRegion={_touchRegionHash.HasValue}");
        if ((ApplicationInfo?.Flags & ApplicationInfoFlags.Debuggable) == ApplicationInfoFlags.Debuggable)
            writer?.Println("projectDirectory=" + _loadedProject?.ProjectDirectory);
    }
}
