using System.Numerics;

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
    public float MouseY => PointerPosition.Y;
    public float MouseDeltaX => PointerDelta.X;
    public float MouseDeltaY => PointerDelta.Y;
    public float ScrollX => 0.0f;
    public float ScrollY => ZoomDelta;
    public bool IsTouchAvailable => false;
    public bool HasTouch => false;
    public int TouchCount => 0;
    public int ActiveTouchCount => 0;
    public bool IsTouchDown => false;
    public bool IsTouchStarted => false;
    public bool IsTouchEnded => false;
    public float TouchSlopPixels => 12.0f;

    public bool IsMouseButtonDown(string button)
    {
        string normalized = (button ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "left" or "mouseleft" or "button0" or "0"
            ? PrimaryButtonDown
            : normalized is "right" or "mouseright" or "button1" or "1"
                ? SecondaryButtonDown
                : normalized is "middle" or "mousemiddle" or "button2" or "2" && MiddleButtonDown;
    }

    public bool IsKeyDown(string key) => KeyState?.Invoke(key) == true;
}
