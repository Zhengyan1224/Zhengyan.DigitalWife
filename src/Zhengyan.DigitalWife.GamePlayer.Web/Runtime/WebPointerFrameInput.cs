namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebPointerFrameInput(
    float LookX,
    float LookY,
    float PanX,
    float PanY,
    float Zoom,
    float PointerX,
    float PointerY,
    int Buttons);
