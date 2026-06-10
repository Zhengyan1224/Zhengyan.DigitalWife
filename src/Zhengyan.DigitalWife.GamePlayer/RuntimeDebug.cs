using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeDebug
{
    private readonly RuntimeDebugDrawComponent _debugDraw;

    internal RuntimeDebug(RuntimeDebugDrawComponent debugDraw)
    {
        _debugDraw = debugDraw;
    }

    public void DrawRay(float originX, float originY, float originZ, float directionX, float directionY, float directionZ, float length = 10.0f, float durationSeconds = 0.1f)
    {
        DrawRay(
            new Vector3(originX, originY, originZ),
            new Vector3(directionX, directionY, directionZ),
            length,
            new Vector4(1.0f, 0.2f, 0.1f, 1.0f),
            durationSeconds);
    }

    public void DrawRay(
        Vector3 origin,
        Vector3 direction,
        float length = 10.0f,
        Vector4? color = null,
        float durationSeconds = 0.1f)
    {
        _debugDraw.DrawRay(origin, direction, length, color ?? new Vector4(1.0f, 0.2f, 0.1f, 1.0f), durationSeconds);
    }

    public void DrawLine(
        Vector3 start,
        Vector3 end,
        Vector4? color = null,
        float durationSeconds = 0.1f)
    {
        _debugDraw.DrawLine(start, end, color ?? new Vector4(1.0f, 1.0f, 0.1f, 1.0f), durationSeconds);
    }
}
