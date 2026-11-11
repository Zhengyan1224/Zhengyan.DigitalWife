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
}
