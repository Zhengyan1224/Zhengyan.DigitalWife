using Android.Views;
using Android.Views.InputMethods;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>Keep IME digit key events on the editor's input connection.</summary>
internal sealed class AndroidTextInputConnection(IInputConnection target) : InputConnectionWrapper(target, false)
{
    public override bool SendKeyEvent(KeyEvent? e)
    {
        if (e is not null && TryGetDigit(e, out char digit))
        {
            // Some keyboards commit letters but send digits as key events. The
            // default connection forwards those events to the window, where a
            // game surface/dialog can consume them. Commit on this connection
            // instead, preserving native selection/replacement and masking.
            if (e.Action == KeyEventActions.Down) return CommitText(digit.ToString(), 1);
            if (e.Action == KeyEventActions.Up) return true;
        }
        return base.SendKeyEvent(e);
    }

    private static bool TryGetDigit(KeyEvent e, out char digit)
    {
        digit = '\0';
        int number = e.KeyCode switch
        {
            >= Keycode.Num0 and <= Keycode.Num9 => e.KeyCode - Keycode.Num0,
            >= Keycode.Numpad0 and <= Keycode.Numpad9 => e.KeyCode - Keycode.Numpad0,
            _ => -1
        };
        if (number < 0 || e.IsCtrlPressed || e.IsAltPressed || e.IsMetaPressed) return false;
        int unicode = e.UnicodeChar;
        if (unicode is >= '0' and <= '9') digit = (char)unicode;
        else if (unicode == 0 && !e.IsShiftPressed) digit = (char)('0' + number);
        return digit != '\0';
    }
}
