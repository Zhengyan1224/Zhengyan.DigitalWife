using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Input;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

/// <summary>Platform-neutral camera gesture/input values. Pixel-to-world scaling is applied by the camera settings.</summary>
public readonly partial record struct RuntimeCameraInput(
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
    public float HorizontalScroll {get;init;}
    public float ScrollX => HorizontalScroll;
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

    public bool IsKeyDown(string key)
    {
        if(KeyState?.Invoke(key)==true)return true;
        if(key?.Length==1&&char.IsLetter(key[0]))return KeyState?.Invoke("Key"+key.ToUpperInvariant())==true;
        if(key?.StartsWith("Key")==true&&key.Length==4)return KeyState?.Invoke(key[3..])==true;
        var alias=key switch{"ControlLeft"=>"CtrlLeft","ControlRight"=>"CtrlRight","ArrowUp"=>"DpadUp","ArrowDown"=>"DpadDown","ArrowLeft"=>"DpadLeft","ArrowRight"=>"DpadRight","Backspace"=>"Del",_=>null};
        return alias is not null&&KeyState?.Invoke(alias)==true;
    }

    public RuntimeGamepadInput? Gamepad { get; init; }
    public Func<string?>? ClipboardReader { get; init; }
    public Func<string,bool>? ClipboardWriter { get; init; }
    public Func<Task<string>>? ClipboardAsyncReader { get; init; }
    public Task<string> ReadClipboardTextAsync()=>ClipboardAsyncReader?.Invoke()??Task.FromResult(ClipboardText);
    public Func<bool,bool,bool>? CursorLockSetter { get; init; }
    public Func<bool>? CursorLockReader { get; init; }
    public Func<bool>? RawMouseReader { get; init; }
    public bool IsControlDown=>IsCtrlDown;
    public bool IsCursorLocked=>CursorLockReader?.Invoke()??false;
    public bool IsRawMouseInput=>RawMouseReader?.Invoke()??false;
    public bool CursorLocked {get=>IsCursorLocked;set=>SetCursorLocked(value);}
    public string CursorMode=>IsCursorLocked?"locked":CursorVisible?"normal":"hidden";
    public bool HasGamepad=>Gamepad?.Connected==true;
    public string GamepadName=>HasGamepad?Gamepad!.Name:string.Empty;
    public int GamepadIndex=>HasGamepad?Gamepad!.Index:-1;
    public float LeftStickX=>Gamepad?.Axis(0)??0;
    public float LeftStickY=>Gamepad?.Axis(1)??0;
    public float RightStickX=>Gamepad?.Axis(2)??0;
    public float RightStickY=>Gamepad?.Axis(3)??0;
    public float LeftTrigger=>Gamepad?.Button(6)??0;
    public float RightTrigger=>Gamepad?.Button(7)??0;
    public bool IsGamepadButtonDown(string button)
    {
        int index=(button??"").ToLowerInvariant().Replace("_","").Replace("-","") switch
        {"a"=>0,"b"=>1,"x"=>2,"y"=>3,"lb" or "l1"=>4,"rb" or "r1"=>5,"back" or "select"=>8,"start" or "options"=>9,"ls" or "l3"=>10,"rs" or "r3"=>11,"dpadup" or "up"=>12,"dpaddown" or "down"=>13,"dpadleft" or "left"=>14,"dpadright" or "right"=>15,"home" or "guide"=>16,_=>-1};
        return HasGamepad&&index>=0&&Gamepad!.Button(index)>.5f;
    }
    public string ClipboardText {get=>ClipboardReader?.Invoke()??string.Empty;set=>SetClipboardText(value);}
    public bool HasClipboardText=>TryGetClipboardText(out _);
    public bool TryGetClipboardText(out string text){text=ClipboardReader?.Invoke()??string.Empty;return text.Length>0;}
    public bool TrySetClipboardText(string text)=>ClipboardWriter?.Invoke(text??string.Empty)??false;
    public void SetClipboardText(string text)=>TrySetClipboardText(text);
    public void HideCursor()=>SetCursorVisible(false);
    public void ShowCursor()=>SetCursorVisible(true);
    public bool TrySetCursorVisible(bool visible){if(CursorVisibility is null)return false;CursorVisibility(visible);return true;}
    public bool TrySetCursorLocked(bool locked,bool rawInput=false)=>CursorLockSetter?.Invoke(locked,rawInput)??false;
    public void SetCursorLocked(bool locked,bool rawInput=false)=>TrySetCursorLocked(locked,rawInput);
    public void LockCursor(bool rawInput=false)=>SetCursorLocked(true,rawInput);
    public void UnlockCursor()=>SetCursorLocked(false);
}
