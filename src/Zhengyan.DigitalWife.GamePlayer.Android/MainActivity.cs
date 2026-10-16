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
    private GameProjectScene? _textEditorScene;
    private GuiControlSettings? _textEditorControl;
    private AndroidGameProjectLoadResult? _projectLoadResult;
    private AndroidLoadingOverlayView? _loadingOverlay;
    private AlertDialog? _packagePasswordDialog;
    private int _projectLoadVersion;
    private bool _openingProject;
    private bool _packagePickerActive;
    private const int PackagePickerRequestCode = 7002;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        global::Android.Util.Log.Info("ZhengyanGamePlayer",
            $"Managed runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; " +
            $"architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; " +
            $"dynamicCode={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");

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
        CloseTextEditor();
        _gameView?.PauseRendering();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _projectLoadVersion++;
        _packagePasswordDialog?.Dismiss();
        if (_gameView is not null)
        {
            _gameView.OverlayInvalidated -= OnOverlayInvalidated;
            _gameView.LoadingStateChanged -= OnLoadingStateChanged;
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

    private void OnOverlayInvalidated()
    {
        RefreshTextEditor();
        _guiOverlay?.Refresh();
    }

    private void OnLoadingStateChanged()
    {
        if (_textEditor is not null && (_gameView?.IsReady != true
            || !ReferenceEquals(_textEditorScene, _gameView.Project?.Scene))) CloseTextEditor();
        if (_openingProject || _gameView is null || _loadingOverlay is null) return;
        if (_guiOverlay is not null) _guiOverlay.Visibility = _gameView.IsReady ? ViewStates.Visible : ViewStates.Gone;
        if (_gameView.LoadingError is { } error) _loadingOverlay.SetError(error);
        else if (_gameView.HasSceneFrame) _loadingOverlay.Visibility = ViewStates.Gone;
        else _loadingOverlay.SetProgress(_gameView.LoadingProgress, _gameView.LoadingMessage);
    }

    private void ShowTextEditor(GuiControlSettings control, LayoutRect rect)
    {
        RunOnUiThread(() =>
        {
            CloseTextEditor();
            if (_root is null || _gameView?.Project is not { } project || !_gameView.IsReady
                || !control.Visible || !project.Scene.GuiControls.Contains(control)) return;
            ImeEditText editor = new(this)
            {
                Text = control.Text
            };
            editor.SetSingleLine(!control.Multiline);
            editor.SetBackgroundColor(Color.Transparent);
            editor.SetPadding(4, 4, 4, 4);
            editor.SetTypeface(Typeface.Create(Typeface.Default, TypefaceStyle.Normal), TypefaceStyle.Normal);
            editor.SetSelectAllOnFocus(false);
            editor.LongClickable = true;
            editor.InputType = control.Multiline
                ? (global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextFlagMultiLine | global::Android.Text.InputTypes.TextFlagCapSentences)
                : (global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextFlagCapSentences);
            editor.ImeOptions = (control.Multiline ? ImeAction.None : ImeAction.Done)
                | (ImeAction)(ImeFlags.NoExtractUi | ImeFlags.NoFullscreen);
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
            void FinishEditing()
            {
                editor.Post(() => { if (ReferenceEquals(editor, _textEditor)) CloseTextEditor(); });
            }
            editor.FocusChange += (_, args) => { if (!args.HasFocus) FinishEditing(); };
            editor.EditingFinished += (_, _) => FinishEditing();
            editor.EditorAction += (_, args) =>
            {
                args.Handled = args.ActionId == ImeAction.Done;
                if (args.Handled) FinishEditing();
            };
            _textEditor = editor;
            _textEditorScene = project.Scene;
            _textEditorControl = control;
            if (_guiOverlay is not null) _guiOverlay.EditingControl = control;
            _root.AddView(editor, layout);
            RefreshTextEditor();
            editor.RequestFocus();
            int textLength = (editor.Text ?? string.Empty).Length;
            editor.SetSelection(Math.Clamp(control.SelectionStart, 0, textLength), Math.Clamp(control.SelectionEnd, 0, textLength));
            ((InputMethodManager?)GetSystemService(InputMethodService))?.ShowSoftInput(editor, ShowFlags.Implicit);
        });
    }

    private void CloseTextEditor()
    {
        ImeEditText? editor = _textEditor;
        if (_textEditorControl is { } control)
        {
            control.CompositionStart = -1;
            control.CompositionEnd = -1;
        }
        _textEditor = null;
        _textEditorScene = null;
        _textEditorControl = null;
        if (_guiOverlay is not null) _guiOverlay.EditingControl = null;
        if (editor is null) return;
        ((InputMethodManager?)GetSystemService(InputMethodService))?.HideSoftInputFromWindow(editor.WindowToken, HideSoftInputFlags.None);
        _root?.RemoveView(editor);
        editor.Dispose();
        _guiOverlay?.Refresh();
    }

    private void RefreshTextEditor()
    {
        if (_textEditor is not { } editor || _textEditorControl is not { } control) return;
        if (_gameView?.Project is not { } project || !_gameView.IsReady
            || !ReferenceEquals(_textEditorScene, project.Scene) || !control.Visible
            || !project.Scene.GuiControls.Contains(control))
        {
            CloseTextEditor();
            return;
        }
        LayoutRect rect = LayoutResolver.Resolve(control.LayoutMode, control.X, control.Y, control.Width, control.Height,
            _gameView.Width, _gameView.Height, project.Window.Width, project.Window.Height);
        if ((editor.Text ?? string.Empty) != (control.Text ?? string.Empty))
        {
            int start = control.SelectionStart, end = control.SelectionEnd;
            editor.Text = control.Text ?? string.Empty;
            int length = editor.Text.Length;
            editor.SetSelection(Math.Clamp(start, 0, length), Math.Clamp(end, 0, length));
        }
        if (editor.LayoutParameters is FrameLayout.LayoutParams layout)
        {
            int width = (int)Math.Max(rect.Width, 1), height = (int)Math.Max(rect.Height, 1);
            int left = (int)rect.X, top = (int)rect.Y;
            if (layout.Width != width || layout.Height != height || layout.LeftMargin != left || layout.TopMargin != top)
            {
                layout.Width = width; layout.Height = height; layout.LeftMargin = left; layout.TopMargin = top;
                editor.LayoutParameters = layout;
            }
        }
        float fontSize = LayoutResolver.ResolveFontSize(control.LayoutMode, control.Style.FontSize,
            _gameView.Width, _gameView.Height, project.Window.Width, project.Window.Height);
        if (Math.Abs(editor.TextSize - fontSize) > 0.01f) editor.SetTextSize(global::Android.Util.ComplexUnitType.Px, fontSize);
        Vector4Dto color = control.Style.TextColor;
        editor.SetTextColor(Color.Argb((int)(Math.Clamp(color.W, 0, 1) * 255),
            (int)(Math.Clamp(color.X, 0, 1) * 255), (int)(Math.Clamp(color.Y, 0, 1) * 255), (int)(Math.Clamp(color.Z, 0, 1) * 255)));
        editor.Gravity = (control.Style.HorizontalAlignment.ToLowerInvariant() switch
        {
            "left" => GravityFlags.Left,
            "right" => GravityFlags.Right,
            _ => GravityFlags.CenterHorizontal
        }) | (control.Style.VerticalAlignment.ToLowerInvariant() switch
        {
            "top" => GravityFlags.Top,
            "bottom" => GravityFlags.Bottom,
            _ => GravityFlags.CenterVertical
        });
    }

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e?.ActionMasked == MotionEventActions.Down && _textEditor is { } editor)
        {
            using Rect bounds = new();
            if (!editor.GetGlobalVisibleRect(bounds) || !bounds.Contains((int)e.RawX, (int)e.RawY)) CloseTextEditor();
        }
        return base.DispatchTouchEvent(e);
    }

    private sealed class ImeEditText(Context context) : EditText(context)
    {
        public event EventHandler? SelectionChanged;
        public event EventHandler? EditingFinished;
        public int CompositionStart { get; private set; } = -1;
        public int CompositionEnd { get; private set; } = -1;

        public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
        {
            IInputConnection? connection = base.OnCreateInputConnection(outAttrs);
            return connection is null ? null : new AndroidTextInputConnection(connection);
        }

        public override bool OnKeyPreIme(Keycode keyCode, KeyEvent? e)
        {
            bool handled = base.OnKeyPreIme(keyCode, e);
            if (keyCode == Keycode.Back && e?.Action == KeyEventActions.Up) EditingFinished?.Invoke(this, EventArgs.Empty);
            return handled;
        }

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

    private async Task LoadProjectAsync(Intent? intent)
    {
        CloseTextEditor();
        int loadVersion = ++_projectLoadVersion;
        _packagePasswordDialog?.Dismiss();
        _openingProject = true;
        string? packagePassword = null;
        string? sourcePath = null;
        AndroidGameProjectLoadResult result;
        while (true)
        {
            _loadingOverlay?.ShowLoading();
            result = await Task.Run(() => AndroidGameProjectLoader.Load(this, intent, packagePassword, sourcePath));
            if (IsFinishing || IsDestroyed || loadVersion != _projectLoadVersion)
            {
                result.Dispose();
                return;
            }
            if (result.PasswordError is not { } passwordError) break;

            sourcePath = result.Source;
            result.Dispose();
            packagePassword = await PromptPackagePasswordAsync(passwordError);
            if (IsFinishing || IsDestroyed || loadVersion != _projectLoadVersion) return;
            if (packagePassword is null)
            {
                _openingProject = false;
                if (_gameView is not null) OnLoadingStateChanged();
                else
                {
                    _loadingOverlay?.SetError("已取消打开加密包，请重新选择游戏包。");
                    OpenPackagePicker();
                }
                return;
            }
        }

        RunOnUiThread(() =>
        {
            if (IsFinishing || IsDestroyed || loadVersion != _projectLoadVersion)
            {
                result.Dispose();
                return;
            }
            _openingProject = false;
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
            if (result.Project is not null && result.ProjectDirectory is not null)
            {
                ApplyProjectOrientation(result.Project);
                _loadingOverlay?.Configure(result.Project.Scene.LoadingScreen, result.ProjectDirectory);
            }
            if (_gameView is null)
            {
                _gameView = new AndroidGameSurfaceView(this, result.Project, result.ProjectDirectory);
                _guiOverlay = new AndroidGuiOverlayView(this, _gameView);
                _gameView.OverlayInvalidated += OnOverlayInvalidated;
                _gameView.LoadingStateChanged += OnLoadingStateChanged;
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

    private void ApplyProjectOrientation(GameProject project)
    {
        // Apply only after a successful load, before creating/replacing the
        // game view. ConfigChanges keeps the Activity alive during rotation;
        // SurfaceChanged resizes either graphics backend to the actual surface.
        // Square projects retain the player's landscape default.
        ScreenOrientation orientation = project.Window.Width < project.Window.Height
            ? ScreenOrientation.SensorPortrait
            : ScreenOrientation.SensorLandscape;
        if (RequestedOrientation != orientation) RequestedOrientation = orientation;
        global::Android.Util.Log.Info("ZhengyanGamePlayer",
            $"Project orientation: window={project.Window.Width}x{project.Window.Height}; requested={orientation}");
    }

    private Task<string?> PromptPackagePasswordAsync(GameProjectPackagePasswordError passwordError)
    {
        TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUiThread(() =>
        {
            if (IsFinishing || IsDestroyed)
            {
                completion.TrySetResult(null);
                return;
            }
            AndroidPasswordEditText input = new(this)
            {
                Hint = "请输入游戏包密码"
            };
#pragma warning disable CS8600, CS8602
            AlertDialog dialog = new AlertDialog.Builder(this)
                .SetTitle("打开加密游戏包")
                .SetMessage(passwordError == GameProjectPackagePasswordError.Required
                    ? "此游戏包已加密，请输入导出时设置的密码。"
                    : "密码不正确或游戏包已损坏，请重新输入密码。")
                .SetView(input)
                .SetNegativeButton("取消", (_, _) => completion.TrySetResult(null))
                .SetPositiveButton("打开", (_, _) => { })
                .Create();
            dialog.SetOnDismissListener(new DialogDismissListener(() =>
            {
                if (ReferenceEquals(_packagePasswordDialog, dialog)) _packagePasswordDialog = null;
                completion.TrySetResult(null);
            }));
            _packagePasswordDialog = dialog;
            dialog.Show();
            // AlertDialog installs its window flags during Show. A password
            // editor needs the dialog window itself to be an IME target.
            dialog.Window?.ClearFlags(WindowManagerFlags.NotFocusable | WindowManagerFlags.AltFocusableIm);
            Button open = dialog.GetButton((int)DialogButtonType.Positive)!;
            open.Enabled = false;
            open.Click += (_, _) => Submit();
            input.TextChanged += (_, _) => open.Enabled = !string.IsNullOrEmpty(input.Text);
            input.EditorAction += (_, args) =>
            {
                args.Handled = args.ActionId == ImeAction.Done;
                if (!args.Handled) return;
                Submit();
            };
            dialog.Window?.SetSoftInputMode(SoftInput.StateAlwaysVisible | SoftInput.AdjustResize);
#pragma warning restore CS8600, CS8602
            input.RequestFocus();

            void Submit()
            {
                string password = input.Text ?? string.Empty;
                if (password.Length == 0)
                {
                    input.Error = "请输入密码";
                    return;
                }
                completion.TrySetResult(password);
                dialog.Dismiss();
            }
        });
        return completion.Task;
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
            if (!_openingProject && _loadingOverlay is not null)
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
