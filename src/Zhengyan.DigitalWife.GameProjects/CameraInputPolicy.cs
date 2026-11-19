using System.Numerics;

namespace Zhengyan.DigitalWife.GameProjects;

public readonly record struct CameraTouchContact(int Id, Vector2 Position, Vector2 Delta, bool Started = false);
public readonly record struct CameraGesture(Vector2 Look, Vector2 Pan, float Zoom);
public readonly record struct CameraMouseMotion(float X, float Y, int Buttons, bool Alt = false, bool Locked = false);

public sealed class CameraTouchGestureTracker
{
    private int[] _ids = [];
    public void Reset() => _ids = [];
    public CameraGesture Update(IReadOnlyList<CameraTouchContact> touches)
    {
        int[] ids=touches.Select(t=>t.Id).Order().ToArray();
        bool changed=!ids.SequenceEqual(_ids);
        _ids=ids;
        return changed ? default : CameraInputPolicy.Touch(touches);
    }
}

/// <summary>PC mouse bindings and Android touch gestures, shared by player backends.</summary>
public static class CameraInputPolicy
{
    public static CameraGesture Mouse(Vector2 delta, bool left, bool right, bool middle, bool alt,
        bool editor, bool locked = false, bool enableLook = true, bool requireRight = true)
    {
        if (editor)
        {
            if (alt && right) return new(default, default, -delta.Y * .05f);
            if (right || (alt && (middle || left))) return new(delta, default, 0);
            return middle ? new(default, delta, 0) : default;
        }
        return enableLook && (locked || !requireRight || right) ? new(delta, default, 0) : default;
    }

    public static CameraGesture Touch(IReadOnlyList<CameraTouchContact> touches)
    {
        if (touches.Count == 0) return default;
        if (touches.Count == 1) return new(touches[0].Started ? default : touches[0].Delta, default, 0);
        var a = touches[0]; var b = touches[1];
        if (a.Started || b.Started) return default;
        float zoom = (Vector2.Distance(a.Position - a.Delta, b.Position - b.Delta)
            - Vector2.Distance(a.Position, b.Position)) * .01f;
        return new(default, (a.Delta + b.Delta) * .5f, zoom);
    }

    public static (Vector3 Position, Vector3 Target) ApplyTouch(Vector3 position, Vector3 target,
        CameraGesture gesture, float sensitivity, float panSensitivity, float farClip)
    {
        Vector3 offset = position - target;
        float radius = Math.Max(offset.Length(), .01f);
        float yaw = MathF.Atan2(offset.X, offset.Z) - gesture.Look.X * Math.Max(sensitivity, .001f) * MathF.PI / 180;
        float pitch = Math.Clamp(MathF.Asin(Math.Clamp(offset.Y / radius, -1, 1))
            - gesture.Look.Y * Math.Max(sensitivity, .001f) * MathF.PI / 180, -1.52f, 1.52f);
        radius = Math.Clamp(radius * MathF.Exp(gesture.Zoom * .08f), .05f, Math.Max(farClip, .1f));
        Vector3 front = offset.LengthSquared() > 1e-8f ? -Vector3.Normalize(offset) : -Vector3.UnitZ;
        Vector3 right = Vector3.Cross(front, Vector3.UnitY);
        right = right.LengthSquared() > 1e-8f ? Vector3.Normalize(right) : Vector3.UnitX;
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, front));
        target += (-right * gesture.Pan.X - up * gesture.Pan.Y) * radius * .0025f * panSensitivity;
        return (target + new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch),
            MathF.Cos(yaw) * MathF.Cos(pitch)) * radius, target);
    }

}
