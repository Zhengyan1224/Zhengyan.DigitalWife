using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidWindowPlatform
{
    private static WeakReference<MainActivity>? _activity;
    private static WeakReference<AndroidDesktopSpriteService>? _sprite;
    internal static void Register(AndroidDesktopSpriteService service) => _sprite = new(service);
    internal static void Unregister(AndroidDesktopSpriteService service)
    { if (_sprite?.TryGetTarget(out var current) == true && ReferenceEquals(current, service)) _sprite = null; }
    internal static void StopDesktopSprite()
    {
        if (_sprite?.TryGetTarget(out var service) == true) service.PrepareToStop();
        _sprite = null;
        AndroidDesktopSpriteService.CancelPending();
    }
    internal static void Register(MainActivity activity) => _activity = new(activity);
    internal static void Unregister(MainActivity activity)
    { if (_activity?.TryGetTarget(out var current) == true && ReferenceEquals(current, activity)) _activity = null; }
    internal static bool Fullscreen => _sprite?.TryGetTarget(out _) != true && _activity?.TryGetTarget(out var activity) == true && activity.ScriptFullscreen;
    internal static bool CursorLocked=>_sprite?.TryGetTarget(out _) != true && _activity?.TryGetTarget(out var activity)==true&&activity.ScriptCursorLocked;
    internal static bool SetCursorLocked(bool locked)
    {
        if (_sprite?.TryGetTarget(out _) == true) return !locked;
        if(_activity?.TryGetTarget(out var activity)!=true||activity is null||!activity.CanCaptureCursor)return !locked;
        activity.RunOnUiThread(()=>activity.SetScriptCursorLocked(locked));return true;
    }
    internal static bool SetCursorVisible(bool visible)
    {
        if (_sprite?.TryGetTarget(out var sprite) == true) return sprite.SetCursorVisible(visible);
        if(_activity?.TryGetTarget(out var activity)!=true||activity is null)return false;
        activity.RunOnUiThread(()=>activity.SetScriptCursorVisible(visible));return true;
    }
    internal static void Apply(RuntimeWindowCommand command)
    {
        if (_sprite?.TryGetTarget(out var sprite) == true)
        {
            sprite.ApplyScriptWindowCommand(command);
            return;
        }
        if (_activity?.TryGetTarget(out var activity) == true)
            activity.RunOnUiThread(() => activity.ApplyScriptWindowCommand(command));
    }
}
