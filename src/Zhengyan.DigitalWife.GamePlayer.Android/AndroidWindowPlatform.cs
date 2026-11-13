using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidWindowPlatform
{
    private static WeakReference<MainActivity>? _activity;
    internal static void Register(MainActivity activity) => _activity = new(activity);
    internal static void Unregister(MainActivity activity)
    { if (_activity?.TryGetTarget(out var current) == true && ReferenceEquals(current, activity)) _activity = null; }
    internal static bool Fullscreen => _activity?.TryGetTarget(out var activity) == true && activity.ScriptFullscreen;
    internal static bool CursorLocked=>_activity?.TryGetTarget(out var activity)==true&&activity.ScriptCursorLocked;
    internal static bool SetCursorLocked(bool locked)
    {
        if(_activity?.TryGetTarget(out var activity)!=true||activity is null||!activity.CanCaptureCursor)return !locked;
        activity.RunOnUiThread(()=>activity.SetScriptCursorLocked(locked));return true;
    }
    internal static bool SetCursorVisible(bool visible)
    {
        if(_activity?.TryGetTarget(out var activity)!=true||activity is null)return false;
        activity.RunOnUiThread(()=>activity.SetScriptCursorVisible(visible));return true;
    }
    internal static void Apply(RuntimeWindowCommand command)
    {
        if (_activity?.TryGetTarget(out var activity) == true)
            activity.RunOnUiThread(() => activity.ApplyScriptWindowCommand(command));
    }
}
