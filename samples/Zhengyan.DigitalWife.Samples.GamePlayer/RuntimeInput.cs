using Silk.NET.Input;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeInput
{
    private readonly GamePlayerGame _game;

    internal RuntimeInput(GamePlayerGame game)
    {
        _game = game;
    }

    public float MouseX => _game.Input.MousePosition.X;

    public float MouseY => _game.Input.MousePosition.Y;

    public float MouseDeltaX => _game.Input.MouseDelta.X;

    public float MouseDeltaY => _game.Input.MouseDelta.Y;

    public float ScrollX => _game.Input.ScrollDelta.X;

    public float ScrollY => _game.Input.ScrollDelta.Y;

    public string ClipboardText
    {
        get => TryGetClipboardText(out string text) ? text : string.Empty;
        set => SetClipboardText(value);
    }

    public bool HasClipboardText => TryGetClipboardText(out _);

    public bool IsAltDown => _game.Input.IsAltDown;

    public bool IsControlDown => _game.Input.IsControlDown;

    public bool IsShiftDown => IsKeyDown("ShiftLeft") || IsKeyDown("ShiftRight");

    public bool HasGamepad => _game.Input.HasGamepad;

    public string GamepadName => _game.Input.GamepadName;

    public int GamepadIndex => _game.Input.GamepadIndex;

    public float LeftStickX => _game.Input.LeftThumbstick.X;

    public float LeftStickY => _game.Input.LeftThumbstick.Y;

    public float RightStickX => _game.Input.RightThumbstick.X;

    public float RightStickY => _game.Input.RightThumbstick.Y;

    public float LeftTrigger => _game.Input.LeftTrigger;

    public float RightTrigger => _game.Input.RightTrigger;

    public bool TryGetClipboardText(out string text)
    {
        return _game.TryGetClipboardText(out text);
    }

    public bool TrySetClipboardText(string text)
    {
        return _game.TrySetClipboardText(text);
    }

    public void SetClipboardText(string text)
    {
        _ = TrySetClipboardText(text);
    }

    public bool IsMouseButtonDown(string button)
    {
        return TryParseMouseButton(button, out MouseButton parsed) && _game.Input.IsMouseButtonDown(parsed);
    }

    public bool IsKeyDown(string key)
    {
        if (!Enum.TryParse(key, ignoreCase: true, out Key parsed))
        {
            return false;
        }

        return _game.Input.IsKeyDown(parsed);
    }

    public bool IsGamepadButtonDown(string button)
    {
        return TryParseGamepadButton(button, out ButtonName parsed) && _game.Input.IsGamepadButtonDown(parsed);
    }

    private static bool TryParseMouseButton(string button, out MouseButton parsed)
    {
        parsed = default;
        string normalized = (button ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "left" or "mouseleft" or "button0" or "0")
        {
            parsed = MouseButton.Left;
            return true;
        }

        if (normalized is "right" or "mouseright" or "button1" or "1")
        {
            parsed = MouseButton.Right;
            return true;
        }

        if (normalized is "middle" or "mousemiddle" or "button2" or "2")
        {
            parsed = MouseButton.Middle;
            return true;
        }

        return Enum.TryParse(button, ignoreCase: true, out parsed);
    }

    private static bool TryParseGamepadButton(string button, out ButtonName parsed)
    {
        parsed = default;
        string normalized = (button ?? string.Empty).Trim().ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
        switch (normalized)
        {
            case "a":
                parsed = ButtonName.A;
                return true;
            case "b":
                parsed = ButtonName.B;
                return true;
            case "x":
                parsed = ButtonName.X;
                return true;
            case "y":
                parsed = ButtonName.Y;
                return true;
            case "lb":
            case "l1":
            case "leftbumper":
                parsed = ButtonName.LeftBumper;
                return true;
            case "rb":
            case "r1":
            case "rightbumper":
                parsed = ButtonName.RightBumper;
                return true;
            case "back":
            case "select":
                parsed = ButtonName.Back;
                return true;
            case "start":
            case "options":
                parsed = ButtonName.Start;
                return true;
            case "home":
            case "guide":
                parsed = ButtonName.Home;
                return true;
            case "ls":
            case "l3":
            case "leftstick":
            case "leftthumbstick":
            case "leftthumbstickbutton":
                parsed = ButtonName.LeftStick;
                return true;
            case "rs":
            case "r3":
            case "rightstick":
            case "rightthumbstick":
            case "rightthumbstickbutton":
                parsed = ButtonName.RightStick;
                return true;
            case "dpadup":
            case "up":
                parsed = ButtonName.DPadUp;
                return true;
            case "dpadright":
            case "right":
                parsed = ButtonName.DPadRight;
                return true;
            case "dpaddown":
            case "down":
                parsed = ButtonName.DPadDown;
                return true;
            case "dpadleft":
            case "left":
                parsed = ButtonName.DPadLeft;
                return true;
        }

        return Enum.TryParse(button, ignoreCase: true, out parsed);
    }
}
