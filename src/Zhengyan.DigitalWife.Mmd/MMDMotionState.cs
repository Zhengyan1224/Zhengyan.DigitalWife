using BulletSharp;

namespace Zhengyan.DigitalWife.Mmd;

public abstract class MMDMotionState : MotionState
{
    public abstract void Reset();

    public abstract void ReflectGlobalTransform();
}

