using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Input;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

/// <summary>Platform-neutral camera gesture/input values. Pixel-to-world scaling is applied by the camera settings.</summary>
public readonly record struct RuntimeCameraInput(
    Vector2 LookDelta,
    Vector2 PanDelta,
    float ZoomDelta,
    Vector2 PointerPosition = default,
    Vector2 PointerDelta = default,
    bool PrimaryButtonDown = false,
    bool SecondaryButtonDown = false,
    bool MiddleButtonDown = false,
    Func<string, bool>? KeyState = null)
{
    public static RuntimeCameraInput None => default;

    public float MouseX => PointerPosition.X;
    public Action<bool>? CursorVisibility { get; init; }
    public Func<bool>? CursorState { get; init; }
    public bool CursorVisible { get => CursorState?.Invoke() ?? true; set => CursorVisibility?.Invoke(value); }
    public bool IsCursorVisible => CursorVisible;
    public void SetCursorVisible(bool visible) => CursorVisibility?.Invoke(visible);
    public float MouseY => PointerPosition.Y;
    public float MouseDeltaX => PointerDelta.X;
    public float MouseDeltaY => PointerDelta.Y;
    public float ScrollX => 0.0f;
    public float ScrollY => ZoomDelta;
    public IReadOnlyList<TouchPoint> Touches { get; init; } = [];
    public IReadOnlyList<TouchPoint> TouchTransitions { get; init; } = [];
    public bool IsTouchAvailable { get; init; }
    public bool HasTouch => TouchCount > 0;
    public int TouchCount => Touches?.Count ?? 0;
    public int ActiveTouchCount => Touches?.Count(t => t.IsActive) ?? 0;
    public bool IsTouchDown => ActiveTouchCount > 0;
    public bool IsTouchStarted => TouchTransitions?.Any(t => t.Phase == TouchPhase.Started) == true;
    public bool IsTouchEnded => TouchTransitions?.Any(t => t.IsEnded) == true;
    public TouchPoint? PrimaryTouch => ActiveTouchCount>0 ? Touches.First(t=>t.IsActive) : null;
    public bool TryGetTouch(int id, out TouchPoint touch) { touch = Touches?.FirstOrDefault(t => t.Id == id) ?? default; return Touches?.Any(t => t.Id == id) == true; }
    public float DevicePixelRatio { get; init; } = 1;
    public float TouchSlopPixels => 12.0f*Math.Max(1,DevicePixelRatio);
    public bool IsAltDown => IsKeyDown("AltLeft") || IsKeyDown("AltRight");
    public bool IsCtrlDown => IsKeyDown("ControlLeft") || IsKeyDown("ControlRight");
    public bool IsShiftDown => IsKeyDown("ShiftLeft") || IsKeyDown("ShiftRight");

    public bool IsMouseButtonDown(string button)
    {
        string normalized = (button ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "left" or "mouseleft" or "button0" or "0"
            ? PrimaryButtonDown
            : normalized is "right" or "mouseright" or "button1" or "1"
                ? SecondaryButtonDown
                : normalized is "middle" or "mousemiddle" or "button2" or "2" && MiddleButtonDown;
    }

    public bool IsKeyDown(string key) => KeyState?.Invoke(key) == true || (key?.Length==1 && char.IsLetter(key[0]) && KeyState?.Invoke("Key"+key.ToUpperInvariant())==true);
}
