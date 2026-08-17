using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

[Activity(
    Name = "com.zhengyan.digitalwife.gameplayer.MainActivity",
    Label = "@string/app_name",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.KeyboardHidden
        | ConfigChanges.UiMode)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["content", "file"],
    DataMimeTypes = ["application/octet-stream", "application/zip", "application/x-dwgame"])]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["content", "file"],
    DataPathPatterns = [".*\\.dwgame", ".*\\.DWGAME"])]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["content", "file"],
    DataMimeTypes = ["*/*"],
    DataPathPatterns = [".*\\.dwgame", ".*\\.DWGAME"])]
[IntentFilter(
    [Intent.ActionSend, Intent.ActionSendMultiple],
    Categories = [Intent.CategoryDefault],
    DataMimeTypes = ["application/octet-stream", "application/zip", "application/x-dwgame", "*/*"])]
public sealed class MainActivity : Activity
{
    private AndroidGameSurfaceView? _gameView;
    private AndroidGameProjectLoadResult? _projectLoadResult;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Window?.SetFlags(WindowManagerFlags.KeepScreenOn, WindowManagerFlags.KeepScreenOn);
        EnterImmersiveMode();

        LoadProject(Intent);
        _gameView = new AndroidGameSurfaceView(this, _projectLoadResult?.Project, _projectLoadResult?.ProjectDirectory);
        SetContentView(_gameView);
    }

    protected override void OnResume()
    {
        base.OnResume();
        EnterImmersiveMode();
        _gameView?.ResumeRendering();
    }

    protected override void OnPause()
    {
        _gameView?.PauseRendering();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _gameView?.Dispose();
        _gameView = null;
        _projectLoadResult?.Dispose();
        _projectLoadResult = null;
        base.OnDestroy();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        LoadProject(intent);
        _gameView?.SetProject(_projectLoadResult?.Project, _projectLoadResult?.ProjectDirectory);
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            EnterImmersiveMode();
        }
    }

    private void EnterImmersiveMode()
    {
        if (Window is not { } window || window.DecorView is not { } decorView)
        {
            return;
        }

#pragma warning disable CA1416, CA1422
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            window.SetDecorFitsSystemWindows(false);
            window.InsetsController?.Hide(WindowInsets.Type.SystemBars());
            if (window.InsetsController is { } controller)
            {
                controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            }

            return;
        }

        decorView.SystemUiFlags = SystemUiFlags.Fullscreen
            | SystemUiFlags.HideNavigation
            | SystemUiFlags.ImmersiveSticky
            | SystemUiFlags.LayoutFullscreen
            | SystemUiFlags.LayoutHideNavigation
            | SystemUiFlags.LayoutStable;
#pragma warning restore CA1422
    }

    private void LoadProject(Intent? intent)
    {
        _projectLoadResult?.Dispose();
        _projectLoadResult = AndroidGameProjectLoader.Load(this, intent);
        if (!_projectLoadResult.Succeeded)
        {
            Toast.MakeText(this, _projectLoadResult.Error, ToastLength.Long)?.Show();
            return;
        }

        Title = _projectLoadResult.Project?.Name ?? GetString(Resource.String.app_name);
        if (_projectLoadResult.Compatibility is { CanPublish: false } report)
        {
            Toast.MakeText(this, report.ToStatusMessage(), ToastLength.Long)?.Show();
        }
    }
}
