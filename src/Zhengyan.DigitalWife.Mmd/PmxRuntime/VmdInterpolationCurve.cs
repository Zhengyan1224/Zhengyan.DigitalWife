using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd;

/// <summary>
/// Evaluates the cubic Bezier interpolation stored on the destination VMD key.
/// This type is shared by desktop and mobile animation paths so key semantics
/// cannot drift between render backends.
/// </summary>
public readonly record struct VmdInterpolationCurve(Vector2 ControlPoint1, Vector2 ControlPoint2)
{
    public static VmdInterpolationCurve Linear { get; } = new(
        new Vector2(20.0f / 127.0f),
        new Vector2(107.0f / 127.0f));

    public static VmdInterpolationCurve FromVmd(ReadOnlySpan<byte> interpolation, int channelOffset)
    {
        if ((uint)channelOffset > 3u || interpolation.Length < channelOffset + 13)
        {
            throw new ArgumentException("A VMD interpolation block must contain the four 16-byte channel curves.", nameof(interpolation));
        }

        return new VmdInterpolationCurve(
            new Vector2(interpolation[channelOffset] / 127.0f, interpolation[channelOffset + 4] / 127.0f),
            new Vector2(interpolation[channelOffset + 8] / 127.0f, interpolation[channelOffset + 12] / 127.0f));
    }

    public float Evaluate(float normalizedTime)
    {
        float time = Math.Clamp(normalizedTime, 0.0f, 1.0f);
        if (time is <= 0.0f or >= 1.0f)
        {
            return time;
        }

        float low = 0.0f;
        float high = 1.0f;
        float parameter = time;
        for (int i = 0; i < 20; i++)
        {
            parameter = (low + high) * 0.5f;
            if (EvaluateAxis(parameter, ControlPoint1.X, ControlPoint2.X) < time)
            {
                low = parameter;
            }
            else
            {
                high = parameter;
            }
        }

        return EvaluateAxis(parameter, ControlPoint1.Y, ControlPoint2.Y);
    }

    public float EvaluateX(float parameter) => EvaluateAxis(
        Math.Clamp(parameter, 0.0f, 1.0f),
        ControlPoint1.X,
        ControlPoint2.X);

    public float EvaluateY(float parameter) => EvaluateAxis(
        Math.Clamp(parameter, 0.0f, 1.0f),
        ControlPoint1.Y,
        ControlPoint2.Y);

    private static float EvaluateAxis(float t, float a, float b)
    {
        float inverse = 1.0f - t;
        return 3.0f * inverse * inverse * t * a
            + 3.0f * inverse * t * t * b
            + t * t * t;
    }
}
