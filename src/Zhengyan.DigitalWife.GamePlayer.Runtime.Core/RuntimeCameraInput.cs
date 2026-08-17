using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

/// <summary>Platform-neutral camera gesture/input values. Pixel-to-world scaling is applied by the camera settings.</summary>
public readonly record struct RuntimeCameraInput(Vector2 LookDelta, Vector2 PanDelta, float ZoomDelta)
{
    public static RuntimeCameraInput None => default;
}
