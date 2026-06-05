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
}
