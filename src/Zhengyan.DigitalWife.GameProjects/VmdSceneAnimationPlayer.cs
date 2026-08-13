using System.Numerics;
using Zhengyan.DigitalWife.Mmd;

namespace Zhengyan.DigitalWife.GameProjects;

public readonly record struct VmdCameraPose(Vector3 Position, Vector3 Target, Vector3 Up, float Fov, bool Perspective);

public readonly record struct VmdLightPose(Vector3 Color, Vector3 Position);

/// <summary>Loads only VMD camera/light tracks. VMD shadow tracks are intentionally ignored.</summary>
public sealed class VmdSceneAnimationPlayer : IDisposable
{
    private VmdParsing? _data;
    private string _path = string.Empty;
    private bool _loadAttempted;

    public int CameraMaxFrame { get; private set; }

    public int LightMaxFrame { get; private set; }

    public bool Load(string? path)
    {
        string normalized = path?.Trim() ?? string.Empty;
        if (string.Equals(normalized, _path, StringComparison.OrdinalIgnoreCase) && _loadAttempted)
        {
            return _data is not null;
        }

        _data = null;
        _path = normalized;
        _loadAttempted = true;
        CameraMaxFrame = 0;
        LightMaxFrame = 0;
        if (string.IsNullOrWhiteSpace(normalized) || !File.Exists(normalized))
        {
            return false;
        }

        try
        {
            _data = VmdParsing.ParsingByFile(normalized);
            if (_data is null)
            {
                return false;
            }

            Array.Sort(_data.Cameras, static (left, right) => left.Frame.CompareTo(right.Frame));
            Array.Sort(_data.Lights, static (left, right) => left.Frame.CompareTo(right.Frame));

            CameraMaxFrame = _data.Cameras.Length == 0 ? 0 : (int)_data.Cameras.Max(item => item.Frame);
            LightMaxFrame = _data.Lights.Length == 0 ? 0 : (int)_data.Lights.Max(item => item.Frame);
            return true;
        }
        catch
        {
            _data = null;
            return false;
        }
    }

    public static void Update(VmdPlaybackSettings settings, float deltaSeconds, int maxFrame)
    {
        if (!settings.IsPlaying || maxFrame <= 0)
        {
            return;
        }

        settings.Frame += Math.Max(0.0f, deltaSeconds) * 30.0f * Math.Max(0.0f, settings.PlaybackSpeed);
        if (settings.Loop)
        {
            settings.Frame %= maxFrame;
        }
        else if (settings.Frame >= maxFrame)
        {
            settings.Frame = maxFrame;
            settings.IsPlaying = false;
        }
    }

    public bool TrySampleCamera(float frame, out VmdCameraPose pose)
    {
        pose = default;
        VmdCamera[]? keys = _data?.Cameras;
        if (keys is null || keys.Length == 0)
        {
            return false;
        }

        VmdCamera a = keys[0];
        VmdCamera b = keys[^1];
        if (frame <= keys[0].Frame)
        {
            b = a;
        }
        for (int i = 1; i < keys.Length; i++)
        {
            if (keys[i].Frame >= frame)
            {
                a = keys[i - 1];
                b = keys[i];
                break;
            }
        }

        float t = b.Frame == a.Frame ? 0.0f : Math.Clamp((frame - a.Frame) / (b.Frame - a.Frame), 0.0f, 1.0f);
        float tx = BezierWeight(a.Interpolation, 0, t);
        float ty = BezierWeight(a.Interpolation, 4, t);
        float tz = BezierWeight(a.Interpolation, 8, t);
        float tr = BezierWeight(a.Interpolation, 12, t);
        float td = BezierWeight(a.Interpolation, 16, t);
        float tf = BezierWeight(a.Interpolation, 20, t);
        Vector3 interest = ToEngine(new Vector3(
            Lerp(a.Interest.X, b.Interest.X, tx),
            Lerp(a.Interest.Y, b.Interest.Y, ty),
            Lerp(a.Interest.Z, b.Interest.Z, tz)));
        Vector3 rotate = Vector3.Lerp(a.Rotate, b.Rotate, tr);
        float distance = Lerp(a.Distance, b.Distance, td);
        float fov = Lerp(a.ViewAngle, b.ViewAngle, tf);
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(
            rotate.Y,
            rotate.X,
            -rotate.Z);
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, rotation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, rotation);
        if (up.LengthSquared() < 1e-8f)
        {
            up = Vector3.UnitY;
        }
        else
        {
            up = Vector3.Normalize(up);
        }
        // VMD camera distance is conventionally negative. Keep the MMD
        // convention so a zero-rotation camera looks toward the interest point.
        pose = new VmdCameraPose(interest + (forward * distance), interest, up, fov, a.IsPerspective);
        return true;
    }

    public bool TrySampleLight(float frame, out VmdLightPose pose)
    {
        pose = default;
        VmdLight[]? keys = _data?.Lights;
        if (keys is null || keys.Length == 0)
        {
            return false;
        }

        VmdLight a = keys[0];
        VmdLight b = keys[^1];
        if (frame <= keys[0].Frame)
        {
            b = a;
        }
        for (int i = 1; i < keys.Length; i++)
        {
            if (keys[i].Frame >= frame)
            {
                a = keys[i - 1];
                b = keys[i];
                break;
            }
        }

        float t = b.Frame == a.Frame ? 0.0f : Math.Clamp((frame - a.Frame) / (b.Frame - a.Frame), 0.0f, 1.0f);
        pose = new VmdLightPose(Vector3.Lerp(a.Color, b.Color, t), Vector3.Lerp(ToEngine(a.Position), ToEngine(b.Position), t));
        return true;
    }

    public void Dispose()
    {
        _data = null;
        _loadAttempted = false;
    }

    private static Vector3 ToEngine(Vector3 value) => value * new Vector3(1.0f, 1.0f, -1.0f);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float BezierWeight(byte[] interpolation, int offset, float x)
    {
        if (interpolation.Length < offset + 4)
        {
            return x;
        }

        // Camera interpolation groups are packed as x1, x2, y1, y2.
        float x1 = interpolation[offset] / 127.0f;
        float x2 = interpolation[offset + 1] / 127.0f;
        float y1 = interpolation[offset + 2] / 127.0f;
        float y2 = interpolation[offset + 3] / 127.0f;
        float low = 0.0f;
        float high = 1.0f;
        for (int i = 0; i < 15; i++)
        {
            float mid = (low + high) * 0.5f;
            float curveX = Cubic(mid, x1, x2);
            if (curveX < x) low = mid; else high = mid;
        }

        return Cubic((low + high) * 0.5f, y1, y2);
    }

    private static float Cubic(float t, float p1, float p2)
    {
        float oneMinusT = 1.0f - t;
        return (3.0f * oneMinusT * oneMinusT * t * p1)
            + (3.0f * oneMinusT * t * t * p2)
            + (t * t * t);
    }
}
