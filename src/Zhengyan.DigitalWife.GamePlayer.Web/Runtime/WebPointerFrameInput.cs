namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebPointerFrameInput(
    float LookX,
    float LookY,
    float PanX,
    float PanY,
    float Zoom,
    float PointerX,
    float PointerY,
    int Buttons)
{
    public IReadOnlyList<Zhengyan.DigitalWife.Mmd.Game.Input.TouchPoint> Touches { get; init; } = [];
    public IReadOnlyList<Zhengyan.DigitalWife.Mmd.Game.Input.TouchPoint> TouchTransitions { get; init; } = [];
    public bool TouchAvailable { get; init; }
    public float PointerDeltaX { get; init; }
    public float PointerDeltaY { get; init; }
    public float Wheel { get; init; }
    public IReadOnlyList<Zhengyan.DigitalWife.GameProjects.CameraMouseMotion> MouseMotions { get; init; } = [];
    public IReadOnlyList<Zhengyan.DigitalWife.Mmd.Game.Input.TouchPoint> CameraTouches { get; init; } = [];
    public float ScrollX {get;init;}
    public Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeGamepadInput? Gamepad { get; init; }
}
