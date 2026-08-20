using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Android.Graphics;
using Zhengyan.DigitalWife.GameProjects;
using System.Threading.Tasks;

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
    private AndroidGuiOverlayView? _guiOverlay;
    private FrameLayout? _root;
    private EditText? _textEditor;
    private AndroidGameProjectLoadResult? _projectLoadResult;
    private AndroidLoadingOverlayView? _loadingOverlay;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Window?.SetFlags(WindowManagerFlags.KeepScreenOn, WindowManagerFlags.KeepScreenOn);
        EnterImmersiveMode();

        _root = new FrameLayout(this);
        _loadingOverlay = new AndroidLoadingOverlayView(this);
        _root.AddView(_loadingOverlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        SetContentView(_root);
        _ = LoadProjectAsync(Intent);
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
        if (_gameView is not null)
        {
            _gameView.OverlayInvalidated -= OnOverlayInvalidated;
            _gameView.FirstFramePresented -= OnFirstFramePresented;
            _gameView.TextInputRequested -= ShowTextEditor;
            _gameView.ContextMenuRequested -= ShowContextMenu;
        }
        CloseTextEditor();
        _root = null;
        _guiOverlay = null;
        _loadingOverlay = null;
        _gameView?.Dispose();
        _gameView = null;
        _projectLoadResult?.Dispose();
        _projectLoadResult = null;
        base.OnDestroy();
    }

    private void OnOverlayInvalidated() => _guiOverlay?.Refresh();

    private void ShowTextEditor(GuiControlSettings control, LayoutRect rect)
    {
        RunOnUiThread(() =>
        {
            CloseTextEditor();
            if (_root is null) return;
            EditText editor = new(this)
            {
                Text = control.Text,
                TextSize = control.Style.FontSize
            };
            editor.SetSingleLine(!control.Multiline);
            editor.SetTextColor(Color.White);
            editor.SetBackgroundColor(Color.Argb(230, 25, 55, 90));
            FrameLayout.LayoutParams layout = new((int)Math.Max(rect.Width, 1), (int)Math.Max(rect.Height, 1))
            {
                LeftMargin = (int)rect.X,
                TopMargin = (int)rect.Y
            };
            editor.TextChanged += (_, _) => control.Text = editor.Text ?? string.Empty;
            editor.FocusChange += (_, args) => { if (!args.HasFocus) CloseTextEditor(); };
            _root.AddView(editor, layout);
            _textEditor = editor;
            editor.RequestFocus();
            ((InputMethodManager?)GetSystemService(InputMethodService))?.ShowSoftInput(editor, ShowFlags.Implicit);
        });
    }

    private void CloseTextEditor()
    {
        EditText? editor = _textEditor;
        _textEditor = null;
        if (editor is null) return;
        ((InputMethodManager?)GetSystemService(InputMethodService))?.HideSoftInputFromWindow(editor.WindowToken, HideSoftInputFlags.None);
        _root?.RemoveView(editor);
        editor.Dispose();
    }

    private void ShowContextMenu(ContextMenuSettings menu, float x, float y)
    {
        RunOnUiThread(() =>
        {
            if (_gameView is null) return;
            PopupMenu popup = new(this, _gameView);
            foreach (ContextMenuItemSettings item in menu.Items.Where(item => item.Enabled))
            {
                popup.Menu?.Add(item.Text)?.SetIntent(new Intent().PutExtra("item_id", item.Id));
            }
            popup.MenuItemClick += (_, args) =>
            {
                ContextMenuItemSettings? selected = menu.Items.FirstOrDefault(item => item.Id == args.Item?.Intent?.GetStringExtra("item_id"));
                if (selected is not null) _gameView.DispatchContextMenuItem(menu, selected, x, y);
            };
            popup.Show();
        });
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        _ = LoadProjectAsync(intent);
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

    private async Task LoadProjectAsync(Intent? intent)
    {
        _loadingOverlay?.ShowLoading();
        AndroidGameProjectLoadResult result = await Task.Run(() => AndroidGameProjectLoader.Load(this, intent));
        if (IsFinishing || IsDestroyed)
        {
            result.Dispose();
            return;
        }

        RunOnUiThread(() =>
        {
            if (!result.Succeeded)
            {
                _loadingOverlay?.SetError(result.Error ?? "场景加载失败");
                Toast.MakeText(this, result.Error, ToastLength.Long)?.Show();
                result.Dispose();
                return;
            }

            _projectLoadResult?.Dispose();
            _projectLoadResult = result;
            Title = result.Project?.Name ?? GetString(Resource.String.app_name);
            if (_gameView is null)
            {
                _gameView = new AndroidGameSurfaceView(this, result.Project, result.ProjectDirectory);
                _guiOverlay = new AndroidGuiOverlayView(this, _gameView);
                _gameView.OverlayInvalidated += OnOverlayInvalidated;
                _gameView.FirstFramePresented += OnFirstFramePresented;
                _gameView.TextInputRequested += ShowTextEditor;
                _gameView.ContextMenuRequested += ShowContextMenu;
                _root?.AddView(_gameView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
                _root?.AddView(_guiOverlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
                _loadingOverlay?.BringToFront();
                _gameView.ResumeRendering();
            }
            else
            {
                _gameView.SetProject(result.Project, result.ProjectDirectory);
            }
            if (result.Compatibility is { CanPublish: false } report)
            {
                Toast.MakeText(this, report.ToStatusMessage(), ToastLength.Long)?.Show();
            }
        });
    }

    private void OnFirstFramePresented()
    {
        RunOnUiThread(() =>
        {
            if (_loadingOverlay is not null)
            {
                _loadingOverlay.Visibility = ViewStates.Gone;
            }
        });
    }
}
