using Android.App;
using Android.Content;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed class AndroidScriptInput : AndroidScriptInputApi
{
    private AndroidInputSnapshot _snapshot;
    private bool _cursorVisible = true;

    internal AndroidScriptInput(AndroidInputSnapshot snapshot) => _snapshot = snapshot;

    internal void Update(AndroidInputSnapshot snapshot) => _snapshot = snapshot;

    public override bool IsKeyDown(string key)
        => Enum.TryParse(key, true, out global::Android.Views.Keycode parsed) && _snapshot.DeviceInput.IsKeyDown(parsed);
    public override bool IsKeyPressed(string key)
        => Enum.TryParse(key, true, out global::Android.Views.Keycode parsed) && _snapshot.DeviceInput.IsKeyPressed(parsed);
    public override bool IsKeyReleased(string key)
        => Enum.TryParse(key, true, out global::Android.Views.Keycode parsed) && _snapshot.DeviceInput.IsKeyReleased(parsed);

    public override float MouseX => _snapshot.DeviceInput.MousePosition.X;
    public override float MouseY => _snapshot.DeviceInput.MousePosition.Y;
    public override float MouseDeltaX => _snapshot.DeviceInput.MouseDelta.X;
    public override float MouseDeltaY => _snapshot.DeviceInput.MouseDelta.Y;
    public override float ScrollX => _snapshot.DeviceInput.ScrollDelta.X;
    public override float ScrollY => _snapshot.DeviceInput.ScrollDelta.Y;
    public override bool CursorVisible { get => _cursorVisible; set => _cursorVisible = value; }
    public override bool IsMouseButtonDown(string button) => TryMouseButton(button, out int value) && _snapshot.DeviceInput.IsMouseButtonDown(value);
    public override bool IsMouseButtonPressed(string button) => TryMouseButton(button, out int value) && _snapshot.DeviceInput.PressedMouseButtons.Contains(value);
    public override bool IsMouseButtonReleased(string button) => TryMouseButton(button, out int value) && _snapshot.DeviceInput.ReleasedMouseButtons.Contains(value);
    public override bool HasGamepad => _snapshot.DeviceInput.Gamepad.Connected;
    public override string GamepadName => _snapshot.DeviceInput.Gamepad.Name;
    public override float LeftStickX => _snapshot.DeviceInput.Gamepad.LeftStick.X;
    public override float LeftStickY => _snapshot.DeviceInput.Gamepad.LeftStick.Y;
    public override float RightStickX => _snapshot.DeviceInput.Gamepad.RightStick.X;
    public override float RightStickY => _snapshot.DeviceInput.Gamepad.RightStick.Y;
    public override float LeftTrigger => _snapshot.DeviceInput.Gamepad.LeftTrigger;
    public override float RightTrigger => _snapshot.DeviceInput.Gamepad.RightTrigger;
    public override bool IsGamepadButtonDown(string button)
        => TryGamepadButton(button, out global::Android.Views.Keycode key) && _snapshot.DeviceInput.Gamepad.IsButtonDown(key);

    private static bool TryMouseButton(string value, out int button)
    {
        button = (value ?? string.Empty).Trim().ToLowerInvariant() switch { "left" or "button0" or "0" => 0, "right" or "button1" or "1" => 1, "middle" or "button2" or "2" => 2, "back" or "button3" or "3" => 3, "forward" or "button4" or "4" => 4, _ => -1 };
        return button >= 0;
    }

    private static bool TryGamepadButton(string value, out global::Android.Views.Keycode key)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", string.Empty).Replace("-", string.Empty);
        key = normalized switch { "a" => global::Android.Views.Keycode.ButtonA, "b" => global::Android.Views.Keycode.ButtonB, "x" => global::Android.Views.Keycode.ButtonX, "y" => global::Android.Views.Keycode.ButtonY, "lb" or "l1" => global::Android.Views.Keycode.ButtonL1, "rb" or "r1" => global::Android.Views.Keycode.ButtonR1, "back" or "select" => global::Android.Views.Keycode.ButtonSelect, "start" or "options" => global::Android.Views.Keycode.ButtonStart, "home" or "guide" => global::Android.Views.Keycode.ButtonMode, "ls" or "l3" => global::Android.Views.Keycode.ButtonThumbl, "rs" or "r3" => global::Android.Views.Keycode.ButtonThumbr, "dpadup" or "up" => global::Android.Views.Keycode.DpadUp, "dpaddown" or "down" => global::Android.Views.Keycode.DpadDown, "dpadleft" or "left" => global::Android.Views.Keycode.DpadLeft, "dpadright" or "right" => global::Android.Views.Keycode.DpadRight, _ => global::Android.Views.Keycode.Unknown };
        return key != global::Android.Views.Keycode.Unknown;
    }

    public override string ClipboardText
    {
        get
        {
            try
            {
                ClipboardManager? clipboard = Application.Context.GetSystemService(Context.ClipboardService) as ClipboardManager;
                if (clipboard?.HasPrimaryClip != true)
                {
                    return string.Empty;
                }

                return clipboard.PrimaryClip?.GetItemAt(0)?.CoerceToText(Application.Context)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public override bool HasClipboardText => ClipboardText.Length > 0;

    public override bool TrySetClipboardText(string text)
    {
        try
        {
            ClipboardManager? clipboard = Application.Context.GetSystemService(Context.ClipboardService) as ClipboardManager;
            if (clipboard is null)
            {
                return false;
            }

            clipboard.PrimaryClip = ClipData.NewPlainText("text", text ?? string.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public override void SetClipboardText(string text) => _ = TrySetClipboardText(text);
}

