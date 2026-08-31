using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Android.Graphics;
using Android.Text;
using Android.Text.Method;
using Zhengyan.DigitalWife.GameProjects;
using System.Threading.Tasks;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

[Activity(
    Name = "com.zhengyan.digitalwife.gameplayer.MainActivity",
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
    private ImeEditText? _textEditor;
    private AndroidGameProjectLoadResult? _projectLoadResult;
    private AndroidLoadingOverlayView? _loadingOverlay;
    private bool _packagePickerActive;
    private const int PackagePickerRequestCode = 7002;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Window?.SetFlags(WindowManagerFlags.KeepScreenOn, WindowManagerFlags.KeepScreenOn);
        EnterImmersiveMode();
        _root = new FrameLayout(this);
        _loadingOverlay = new AndroidLoadingOverlayView(this);
        _root.AddView(_loadingOverlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        SetContentView(_root);
        if (AndroidGameProjectLoader.HasProjectInput(this, Intent))
        {
            RequestRecordAudioPermissionIfNeeded();
            _ = LoadProjectAsync(Intent);
        }
        else
        {
            // Let the activity finish its first layout before launching the system picker.
            Window?.DecorView?.Post(OpenPackagePicker);
        }
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
            _gameView.RenderInitializationFailed -= OnRenderInitializationFailed;
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
            ImeEditText editor = new(this)
            {
                Text = control.Text,
                TextSize = control.Style.FontSize
            };
            editor.SetSingleLine(!control.Multiline);
            editor.SetTextColor(Color.White);
            editor.SetBackgroundColor(Color.Argb(230, 25, 55, 90));
            editor.SetSelectAllOnFocus(false);
            editor.SetTextIsSelectable(true);
            editor.LongClickable = true;
            editor.InputType = control.Multiline
                ? (global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextFlagMultiLine | global::Android.Text.InputTypes.TextFlagCapSentences)
                : (global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextFlagCapSentences);
            editor.ImeOptions = control.Multiline ? ImeAction.None : ImeAction.Done;
            FrameLayout.LayoutParams layout = new((int)Math.Max(rect.Width, 1), (int)Math.Max(rect.Height, 1))
            {
                LeftMargin = (int)rect.X,
                TopMargin = (int)rect.Y
            };
            editor.TextChanged += (_, _) =>
            {
                string value = editor.Text ?? string.Empty;
                control.Text = value;
                control.CursorPosition = Math.Clamp(editor.SelectionStart, 0, value.Length);
                control.SelectionStart = Math.Clamp(editor.SelectionStart, 0, value.Length);
                control.SelectionEnd = Math.Clamp(editor.SelectionEnd, 0, value.Length);
                control.CompositionStart = editor.CompositionStart;
                control.CompositionEnd = editor.CompositionEnd;
                _gameView?.InvalidateOverlay();
            };
            editor.SelectionChanged += (_, _) =>
            {
                int length = (editor.Text ?? string.Empty).Length;
                control.CursorPosition = Math.Clamp(editor.SelectionStart, 0, length);
                control.SelectionStart = Math.Clamp(editor.SelectionStart, 0, length);
                control.SelectionEnd = Math.Clamp(editor.SelectionEnd, 0, length);
            };
            editor.FocusChange += (_, args) => { if (!args.HasFocus) CloseTextEditor(); };
            _root.AddView(editor, layout);
            _textEditor = editor;
            editor.RequestFocus();
            int textLength = (editor.Text ?? string.Empty).Length;
            editor.SetSelection(Math.Clamp(control.SelectionStart, 0, textLength), Math.Clamp(control.SelectionEnd, 0, textLength));
            ((InputMethodManager?)GetSystemService(InputMethodService))?.ShowSoftInput(editor, ShowFlags.Implicit);
        });
    }

    private void CloseTextEditor()
    {
        ImeEditText? editor = _textEditor;
        _textEditor = null;
        if (editor is null) return;
        ((InputMethodManager?)GetSystemService(InputMethodService))?.HideSoftInputFromWindow(editor.WindowToken, HideSoftInputFlags.None);
        _root?.RemoveView(editor);
        editor.Dispose();
    }

    private sealed class ImeEditText(Context context) : EditText(context)
    {
        public event EventHandler? SelectionChanged;
        public int CompositionStart { get; private set; } = -1;
        public int CompositionEnd { get; private set; } = -1;

        protected override void OnSelectionChanged(int selStart, int selEnd)
        {
            base.OnSelectionChanged(selStart, selEnd);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnTextChanged(Java.Lang.ICharSequence? text, int start, int before, int count)
        {
            base.OnTextChanged(text, start, before, count);
            if (text is global::Android.Text.ISpannable spanned)
            {
                CompositionStart = BaseInputConnection.GetComposingSpanStart(spanned);
                CompositionEnd = BaseInputConnection.GetComposingSpanEnd(spanned);
            }
            else
            {
                CompositionStart = -1;
                CompositionEnd = -1;
            }
        }
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
        if (AndroidGameProjectLoader.HasProjectInput(this, intent))
        {
            RequestRecordAudioPermissionIfNeeded();
            _ = LoadProjectAsync(intent);
        }
        else
        {
            Window?.DecorView?.Post(OpenPackagePicker);
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PackagePickerRequestCode) return;
        _packagePickerActive = false;
        if (resultCode == Result.Ok && data is not null)
        {
            RequestRecordAudioPermissionIfNeeded();
            _ = LoadProjectAsync(data);
        }
        else if (resultCode != Result.Ok)
        {
            _loadingOverlay?.SetError("Select a .dwgame package to start a game.");
        }
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

    private async Task LoadProjectAsync(Intent? intent, string? packagePassword = null, int passwordAttempt = 0)
    {
        _loadingOverlay?.ShowLoading();
        AndroidGameProjectLoadResult result = await Task.Run(() => AndroidGameProjectLoader.Load(this, intent, packagePassword));
        if (IsFinishing || IsDestroyed)
        {
            result.Dispose();
            return;
        }

        if (!result.Succeeded
            && passwordAttempt < 3
            && IsPackagePasswordError(result.Error))
        {
            result.Dispose();
            string? enteredPassword = await PromptPackagePasswordAsync();
            if (!string.IsNullOrEmpty(enteredPassword))
            {
                await LoadProjectAsync(intent, enteredPassword, passwordAttempt + 1);
                return;
            }
        }

        RunOnUiThread(() =>
        {
            if (!result.Succeeded)
            {
                if (!_packagePickerActive && IsNoProjectSuppliedError(result.Error))
                {
                    result.Dispose();
                    _loadingOverlay?.SetError("Select a .dwgame package or all of its split parts to open.");
                    OpenPackagePicker();
                    return;
                }
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
                _gameView.RenderInitializationFailed += OnRenderInitializationFailed;
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

    private Task<string?> PromptPackagePasswordAsync()
    {
        TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUiThread(() =>
        {
            EditText input = new(this)
            {
                Hint = "Password"
            };
            input.InputType = global::Android.Text.InputTypes.ClassText
                | global::Android.Text.InputTypes.TextVariationPassword;
            input.SetSingleLine(true);
            input.SetSelectAllOnFocus(false);
#pragma warning disable CS8600, CS8602
            AlertDialog dialog = new AlertDialog.Builder(this)
                .SetTitle("Encrypted package")
                .SetMessage("Enter the package password to continue.")
                .SetView(input)
                .SetNegativeButton("Cancel", (_, _) => completion.TrySetResult(null))
                .SetPositiveButton("Open", (_, _) => completion.TrySetResult(input.Text ?? string.Empty))
                .Create();
            dialog.SetOnDismissListener(new DialogDismissListener(() => completion.TrySetResult(null)));
            dialog.Show();
#pragma warning restore CS8600, CS8602
            input.RequestFocus();
        });
        return completion.Task;
    }

    private static bool IsPackagePasswordError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return false;
        return error.Contains("requires a password", StringComparison.OrdinalIgnoreCase)
            || error.Contains("integrity check failed", StringComparison.OrdinalIgnoreCase)
            || error.Contains("需要密码", StringComparison.OrdinalIgnoreCase)
            || error.Contains("完整性校验", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNoProjectSuppliedError(string? error) =>
        !string.IsNullOrWhiteSpace(error)
        && (error.Contains("No game project was supplied", StringComparison.OrdinalIgnoreCase)
            || error.Contains("没有提供游戏工程", StringComparison.OrdinalIgnoreCase));

    private void OpenPackagePicker()
    {
        if (_packagePickerActive || IsFinishing || IsDestroyed) return;
        _packagePickerActive = true;
        Intent picker = new(Intent.ActionOpenDocument);
        picker.AddCategory(Intent.CategoryOpenable);
        picker.SetType("*/*");
        picker.PutExtra(Intent.ExtraAllowMultiple, true);
        try
        {
            StartActivityForResult(picker, PackagePickerRequestCode);
        }
        catch (Exception ex)
        {
            _packagePickerActive = false;
            _loadingOverlay?.SetError($"Unable to open package picker: {ex.Message}");
        }
    }

    private void RequestRecordAudioPermissionIfNeeded()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M
            && CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio) != Permission.Granted)
        {
            RequestPermissions([global::Android.Manifest.Permission.RecordAudio], 7001);
        }
    }

    private sealed class DialogDismissListener(Action dismissed) : Java.Lang.Object, IDialogInterfaceOnDismissListener
    {
        public void OnDismiss(global::Android.Content.IDialogInterface? dialog) => dismissed();
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

    private void OnRenderInitializationFailed(string message)
    {
        RunOnUiThread(() =>
        {
            _loadingOverlay?.SetError(message);
            Toast.MakeText(this, message, ToastLength.Long)?.Show();
        });
    }
}
