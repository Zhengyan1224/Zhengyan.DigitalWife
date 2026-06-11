namespace Zhengyan.DigitalWife.Mmd;

public class MMDPhysicsManager : IDisposable
{
    private readonly List<MMDRigidBody> _rigidBodies;
    private readonly List<MMDJoint> _joints;

    public MMDPhysics Physics { get; }

    public IReadOnlyList<MMDRigidBody> RigidBodies => _rigidBodies;

    public IReadOnlyList<MMDJoint> Joints => _joints;

    public MMDPhysicsManager()
    {
        Physics = new MMDPhysics();
        _rigidBodies = [];
        _joints = [];
    }

    public void AddRigidBody(MMDRigidBody rigidBody)
    {
        Physics.AddRigidBody(rigidBody);

        _rigidBodies.Add(rigidBody);
    }

    public void RemoveRigidBody(MMDRigidBody rigidBody)
    {
        Physics.RemoveRigidBody(rigidBody);

        _rigidBodies.Remove(rigidBody);
    }

    public void AddJoint(MMDJoint joint)
    {
        Physics.AddJoint(joint);

        _joints.Add(joint);
    }

    public void RemoveJoint(MMDJoint joint)
    {
        Physics.RemoveJoint(joint);

        _joints.Remove(joint);
    }

    public void Dispose()
    {
        for (int i = _joints.Count - 1; i >= 0; i--)
        {
            MMDJoint joint = _joints[i];
            Physics.RemoveJoint(joint);
            joint.Dispose();
        }

        for (int i = _rigidBodies.Count - 1; i >= 0; i--)
        {
            MMDRigidBody rigidBody = _rigidBodies[i];
            Physics.RemoveRigidBody(rigidBody);
            rigidBody.Dispose();
        }

        _rigidBodies.Clear();
        _joints.Clear();

        Physics.Dispose();

        GC.SuppressFinalize(this);
    }
}

