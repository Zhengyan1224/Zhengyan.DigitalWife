using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Helpers;

namespace Zhengyan.DigitalWife.Mmd;

#region Classes
public abstract class VmdAnimationKey(int time)
{
    public int Time { get; } = time;
}

public class VmdBezier
{
    private readonly VmdInterpolationCurve _curve;

    public Vector2 Cp1 { get; set; }

    public Vector2 Cp2 { get; set; }

    public VmdBezier(byte[] cp)
    {
        int x0 = cp[0];
        int y0 = cp[4];
        int x1 = cp[8];
        int y1 = cp[12];

        Cp1 = new Vector2(x0 / 127.0f, y0 / 127.0f);
        Cp2 = new Vector2(x1 / 127.0f, y1 / 127.0f);
        _curve = new VmdInterpolationCurve(Cp1, Cp2);
    }

    public float EvalX(float t)
    {
        return _curve.EvaluateX(t);
    }

    public float EvalY(float t)
    {
        return _curve.EvaluateY(t);
    }

    public Vector2 Eval(float t)
    {
        return new Vector2(EvalX(t), EvalY(t));
    }

    public float FindBezierX(float time)
    {
        const float e = 0.00001f;
        float start = 0.0f;
        float stop = 1.0f;
        float t = 0.5f;
        float x = EvalX(t);
        while (MathHelper.Abs(time - x) > e)
        {
            if (time < x)
            {
                stop = t;
            }
            else
            {
                start = t;
            }
            t = (stop + start) * 0.5f;
            x = EvalX(t);
        }

        return t;
    }
}

public class VmdNodeAnimationKey : VmdAnimationKey
{
    public Vector3 Translate { get; }

    public Quaternion Rotate { get; }

    public VmdBezier TxBezier { get; }

    public VmdBezier TyBezier { get; }

    public VmdBezier TzBezier { get; }

    public VmdBezier RotBezier { get; }

    public VmdNodeAnimationKey(VmdMotion motion) : base((int)motion.Frame)
    {
        Translate = motion.Translate * new Vector3(1.0f, 1.0f, -1.0f);

        Matrix4x4 rot0 = Matrix4x4.CreateFromQuaternion(motion.Quaternion);
        Matrix4x4 rot1 = rot0.InvZ();
        Rotate = Quaternion.CreateFromRotationMatrix(rot1);

        TxBezier = new VmdBezier(motion.Interpolation[0..]);
        TyBezier = new VmdBezier(motion.Interpolation[1..]);
        TzBezier = new VmdBezier(motion.Interpolation[2..]);
        RotBezier = new VmdBezier(motion.Interpolation[3..]);
    }
}

public class VmdMorphAnimationKey(int time, float weight) : VmdAnimationKey(time)
{
    public float Weight { get; } = weight;
}

public class VmdIkAnimationKey(int time, bool enable) : VmdAnimationKey(time)
{
    public bool Enable { get; } = enable;
}
#endregion

public abstract class VmdAnimationController<TKey, TObject>(TObject @object) where TKey : VmdAnimationKey
{
    protected readonly List<TKey> _keys = [];

    public TObject Object { get; } = @object;

    public TKey[] Keys => [.. _keys];

    public int StartKeyIndex { get; protected set; }

    public void ResetPlaybackCursor()
    {
        StartKeyIndex = 0;
    }

    public void AddKey(TKey key)
    {
        _keys.Add(key);
    }

    public void SortKeys()
    {
        TKey[] keys = [.. _keys.OrderBy(key => key.Time)];

        _keys.Clear();
        _keys.AddRange(keys);
    }

    public abstract void Evaluate(float t, float weight = 1.0f);
}

