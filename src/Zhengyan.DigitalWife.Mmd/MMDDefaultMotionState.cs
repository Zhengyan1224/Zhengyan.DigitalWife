using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Helpers;
using BtMatrix4x4 = Evergine.Mathematics.Matrix4x4;

namespace Zhengyan.DigitalWife.Mmd;

public class MMDDefaultMotionState : MMDMotionState
{
    private readonly BtMatrix4x4 _initialTransform;

    private BtMatrix4x4 _transform;

    public MMDDefaultMotionState(Matrix4x4 transform)
    {
        _initialTransform = transform.ToBtMatrix4x4();
        _transform = _initialTransform;
    }

    public override void GetWorldTransform(out BtMatrix4x4 worldTrans)
    {
        worldTrans = _transform;
    }

    public override void ReflectGlobalTransform()
    {

    }

    public override void Reset()
    {
        _transform = _initialTransform;
    }

    public override void SetWorldTransform(ref BtMatrix4x4 worldTrans)
    {
        _transform = worldTrans;
    }
}

