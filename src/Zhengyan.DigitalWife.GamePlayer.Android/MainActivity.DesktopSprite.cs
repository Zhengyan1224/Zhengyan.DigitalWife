using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Widget;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed partial class MainActivity
{
    // Device tests use the exported Activity, never export the overlay Service.
    // Release builds ignore these commands.
    private bool HandleOverlayDebugCommand(Intent? intent)
    {
        if ((ApplicationInfo?.Flags & ApplicationInfoFlags.Debuggable) != ApplicationInfoFlags.Debuggable) return false;
        var command = intent?.GetStringExtra("zhengyan.overlay_action") switch
        {
            "hide" => new Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeWindowCommand("visible") { Flag = false },
            "show" => new Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeWindowCommand("visible") { Flag = true },
            "close" => new Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeWindowCommand("exit"),
            _ => null
        };
        if (command is null) return false;
        AndroidWindowPlatform.Apply(command);
        MoveTaskToBack(true);
        return true;
    }
    private const int OverlayPermissionRequestCode = 7003;
    private const int NotificationPermissionRequestCode = 7004;
    private AndroidGameProjectLoadResult? _pendingDesktopSprite;
    private AlertDialog? _desktopSpritePermissionDialog;
    private bool _overlayPermissionLaunched;
    private bool _microphonePermissionPending;
    private bool _notificationPermissionPending;
    private bool _notificationPermissionRequested;

    private void OpenDesktopSprite(AndroidGameProjectLoadResult result)
    {
        ClearDesktopSpriteRequest();
        _pendingDesktopSprite = result;
        if (Settings.CanDrawOverlays(this))
        {
            ResumeDesktopSpriteRequest();
            return;
        }
        using AlertDialog.Builder builder = new(this);
        builder.SetTitle("开启桌面精灵");
        builder.SetMessage("这个游戏会以透明小窗浮在桌面和其他应用上。请允许 GamePlayer 显示在其他应用上层。");
        builder.SetPositiveButton("前往授权", (_, _) =>
            {
                _desktopSpritePermissionDialog = null;
                try
                {
                    _overlayPermissionLaunched = true;
                    StartActivityForResult(new Intent(Settings.ActionManageOverlayPermission,
                        global::Android.Net.Uri.Parse("package:" + PackageName)), OverlayPermissionRequestCode);
                }
                catch (Exception ex)
                {
                    _overlayPermissionLaunched = false;
                    ClearDesktopSpriteRequest();
                    _loadingOverlay?.SetError("无法打开悬浮窗设置：" + ex.Message);
                }
            });
        builder.SetNegativeButton("取消", (_, _) =>
            {
                ClearDesktopSpriteRequest();
                _loadingOverlay?.SetError("未开启桌面精灵。请允许显示在其他应用上层后重新打开游戏包。");
            });
        builder.SetCancelable(false);
        AlertDialog dialog = builder.Create() ?? throw new InvalidOperationException("无法创建悬浮窗权限提示。");
        _desktopSpritePermissionDialog = dialog;
        dialog.SetOnDismissListener(new DialogDismissListener(() =>
        {
            if (ReferenceEquals(_desktopSpritePermissionDialog, dialog)) _desktopSpritePermissionDialog = null;
        }));
        dialog.Show();
    }

    private void ResumeDesktopSpriteRequest()
    {
        if (_pendingDesktopSprite is null || _desktopSpritePermissionDialog is not null || _overlayPermissionLaunched
            || _microphonePermissionPending || _notificationPermissionPending || IsFinishing || IsDestroyed) return;
        if (!Settings.CanDrawOverlays(this))
        {
            ClearDesktopSpriteRequest();
            _loadingOverlay?.SetError("未获得悬浮窗权限，请授权后重新打开游戏包。");
            return;
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && !_notificationPermissionRequested
            && CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            _notificationPermissionRequested = true;
            _notificationPermissionPending = true;
            RequestPermissions([global::Android.Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);
            return;
        }
        try
        {
            AndroidDesktopSpriteService.Start(this, _pendingDesktopSprite);
            _pendingDesktopSprite = null; // The service owns the package session from here.
            ReleaseActivityGame();
            MoveTaskToBack(true);
        }
        catch (Exception ex)
        {
            ClearDesktopSpriteRequest();
            _loadingOverlay?.SetError("桌面精灵启动失败：" + ex.Message);
            Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show();
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == 7001) _microphonePermissionPending = false;
        if (requestCode == NotificationPermissionRequestCode) _notificationPermissionPending = false;
        ResumeDesktopSpriteRequest();
    }

    private void ClearDesktopSpriteRequest()
    {
        _desktopSpritePermissionDialog?.Dismiss();
        _desktopSpritePermissionDialog = null;
        _pendingDesktopSprite?.Dispose();
        _pendingDesktopSprite = null;
    }

    private void ReleaseActivityGame()
    {
        _contextMenuPopup?.Dispose(); _contextMenuPopup = null;
        CloseTextEditor();
        if (_gameView is not null)
        {
            _gameView.PauseRendering();
            _gameView.OverlayInvalidated -= OnOverlayInvalidated;
            _gameView.LoadingStateChanged -= OnLoadingStateChanged;
            _gameView.FirstFramePresented -= OnFirstFramePresented;
            _gameView.RenderInitializationFailed -= OnRenderInitializationFailed;
            _gameView.TextInputRequested -= ShowTextEditor;
            _gameView.ContextMenuRequested -= ShowContextMenu;
            _root?.RemoveView(_gameView);
            _gameView.Dispose();
            _gameView = null;
        }
        if (_guiOverlay is not null)
        {
            _root?.RemoveView(_guiOverlay);
            _guiOverlay.Dispose();
            _guiOverlay = null;
        }
        _projectLoadResult?.Dispose();
        _projectLoadResult = null;
    }
}
