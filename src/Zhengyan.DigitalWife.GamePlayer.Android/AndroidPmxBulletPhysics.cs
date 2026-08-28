using BulletSharp;
using System.Numerics;
using Zhengyan.DigitalWife.Mmd;
using BtMatrix = Evergine.Mathematics.Matrix4x4;
using BtVector3 = Evergine.Mathematics.Vector3;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidPmxBulletPhysics : IPmxPhysicsBridge
{
    private readonly DefaultCollisionConfiguration _collisionConfiguration;
    private readonly CollisionDispatcher _dispatcher;
    private readonly DbvtBroadphase _broadphase;
    private readonly SequentialImpulseConstraintSolver _solver;
    private readonly DiscreteDynamicsWorld _world;
    private readonly List<BodyState> _bodies = [];
    private readonly List<CollisionShape> _shapes = [];
    private readonly List<TypedConstraint> _constraints = [];
    private readonly StaticPlaneShape _groundShape;
    private readonly DefaultMotionState _groundMotionState;
    private readonly RigidBody _groundBody;
    private readonly AndroidMmdFilterCallback _filterCallback;
    private bool _disposed;

    public AndroidPmxBulletPhysics(PmxParsing pmx, Vector3 gravity)
    {
        _collisionConfiguration = new DefaultCollisionConfiguration();
        _dispatcher = new CollisionDispatcher(_collisionConfiguration);
        _broadphase = new DbvtBroadphase();
        _solver = new SequentialImpulseConstraintSolver();
        _world = new DiscreteDynamicsWorld(_dispatcher, _broadphase, _solver, _collisionConfiguration)
        {
            Gravity = ToBt(gravity)
        };

        _groundShape = new StaticPlaneShape(BtVector3.UnitY, 0.0f);
        _groundMotionState = new DefaultMotionState(BtMatrix.Identity);
        using (RigidBodyConstructionInfo groundInfo = new(0.0f, _groundMotionState, _groundShape, BtVector3.Zero))
        {
            _groundBody = new RigidBody(groundInfo);
        }
        _world.AddRigidBody(_groundBody);
        // Match the PC world: PMX group/mask filtering is enforced by the
        // broadphase callback, while the ground plane remains collidable.
        _world.PairCache.SetOverlapFilterCallback(_filterCallback = new AndroidMmdFilterCallback(_groundBody.BroadphaseProxy));

        Matrix4x4[] restGlobals = CreateRestGlobals(pmx.Bones);
        foreach (PmxRigidBody source in pmx.RigidBodies)
        {
            BodyState state = CreateBody(source, restGlobals);
            _bodies.Add(state);
            _world.AddRigidBody(state.Body, 1 << source.Group, source.CollisionGroup);
        }

        foreach (PmxJoint joint in pmx.Joints)
        {
            if (joint.RigidBodyIndexA < 0 || joint.RigidBodyIndexA >= _bodies.Count
                || joint.RigidBodyIndexB < 0 || joint.RigidBodyIndexB >= _bodies.Count
                || joint.RigidBodyIndexA == joint.RigidBodyIndexB)
            {
                continue;
            }
            Generic6DofSpringConstraint constraint = CreateJoint(joint, _bodies[joint.RigidBodyIndexA].Body, _bodies[joint.RigidBodyIndexB].Body);
            _constraints.Add(constraint);
            _world.AddConstraint(constraint, true);
        }
    }

    public IReadOnlyDictionary<int, Matrix4x4> Step(IReadOnlyList<Matrix4x4> animatedGlobals, float elapsedSeconds, bool reset)
    {
        Dictionary<int, Matrix4x4> overrides = [];
        if (reset)
        {
            // PmxModel.Reset() removes stale contact pairs before restoring
            // rigid-body transforms. Do the same when an Android animation
            // loop or first frame resets the Bullet scene.
            foreach (BodyState state in _bodies)
            {
                state.ClearContacts(_world.PairCache, _world.Dispatcher);
            }
        }
        foreach (BodyState state in _bodies)
        {
            if (state.BoneIndex < 0 || state.BoneIndex >= animatedGlobals.Count)
            {
                continue;
            }
            if (state.Operation == PmxOperation.Static)
            {
                state.SetKinematicTransform(animatedGlobals[state.BoneIndex]);
            }
            else if (reset || !state.Initialized)
            {
                state.ResetDynamicTransform(animatedGlobals[state.BoneIndex]);
            }
            else
            {
                // PC calls SetActivation(true) before every physics update.
                // Android bodies are created active, but explicitly activating
                // them here also wakes bodies disturbed by a previous contact.
                state.Activate();
            }
        }

        if (reset)
        {
            _world.StepSimulation(1.0f / 60.0f, 1, 1.0f / 60.0f);
        }
        float elapsed = Math.Clamp(elapsedSeconds, 0.0f, 1.0f / 15.0f);
        if (elapsed > 0.0f)
        {
            // Match the PC path: submit one animated target and let Bullet
            // derive kinematic velocity once across its internal 120 Hz steps.
            _world.StepSimulation(elapsed, 10, 1.0f / 120.0f);
        }

        foreach (BodyState state in _bodies)
        {
            if (state.Operation == PmxOperation.Static || state.BoneIndex < 0 || state.BoneIndex >= animatedGlobals.Count)
            {
                continue;
            }
            Matrix4x4 global = state.ReadBoneGlobal();
            if (state.Operation == PmxOperation.DynamicAndBoneMerge)
            {
                Matrix4x4 animated = animatedGlobals[state.BoneIndex];
                global.M41 = animated.M41;
                global.M42 = animated.M42;
                global.M43 = animated.M43;
                global.M44 = animated.M44;
            }
            overrides[state.BoneIndex] = global;
        }
        return overrides;
    }

    public void ApplyImpulse(PmxMorph.ImpulseMorph impulse, float weight)
    {
        if ((uint)impulse.RigidBodyIndex >= (uint)_bodies.Count || weight <= 0.0f)
        {
            return;
        }

        BodyState state = _bodies[impulse.RigidBodyIndex];
        if (state.Operation == PmxOperation.Static)
        {
            return;
        }

        Vector3 velocity = new(impulse.Velocity.X, impulse.Velocity.Y, -impulse.Velocity.Z);
        Vector3 torque = new(-impulse.Torque.X, -impulse.Torque.Y, impulse.Torque.Z);
        if (impulse.Local)
        {
            Matrix4x4 world = InvZ(FromBt(state.Body.WorldTransform));
            velocity = Vector3.TransformNormal(velocity, world);
            torque = Vector3.TransformNormal(torque, world);
        }
        state.Body.Activate(true);
        state.Body.ApplyCentralImpulse(ToBt(velocity * weight));
        state.Body.ApplyTorqueImpulse(ToBt(torque * weight));
    }

    public IReadOnlyList<Vector3> GetColliderPoints()
    {
        return _bodies
            .Select(state =>
            {
                BtMatrix transform = state.Body.WorldTransform;
                Vector3 point = new(transform.M41, transform.M42, -transform.M43);
                return point;
            })
            .ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        for (int i = _constraints.Count - 1; i >= 0; i--)
        {
            _world.RemoveConstraint(_constraints[i]);
            _constraints[i].Dispose();
        }
        for (int i = _bodies.Count - 1; i >= 0; i--)
        {
            _world.RemoveRigidBody(_bodies[i].Body);
            _bodies[i].Dispose();
        }
        _world.RemoveRigidBody(_groundBody);
        _groundBody.Dispose();
        _groundMotionState.Dispose();
        _groundShape.Dispose();
        _filterCallback.Dispose();
        foreach (CollisionShape shape in _shapes)
        {
            shape.Dispose();
        }
        _world.Dispose();
        _solver.Dispose();
        _broadphase.Dispose();
        _dispatcher.Dispose();
        _collisionConfiguration.Dispose();
    }

    private BodyState CreateBody(PmxRigidBody source, Matrix4x4[] restGlobals)
    {
        CollisionShape shape = source.Shape switch
        {
            PmxShape.Sphere => new SphereShape(source.ShapeSize.X),
            PmxShape.Box => new BoxShape(source.ShapeSize.X, source.ShapeSize.Y, source.ShapeSize.Z),
            PmxShape.Capsule => new CapsuleShape(source.ShapeSize.X, source.ShapeSize.Y),
            _ => throw new NotSupportedException($"Unsupported PMX rigid-body shape: {source.Shape}")
        };
        _shapes.Add(shape);

        Matrix4x4 rigidBodyMatrix = (
            Matrix4x4.CreateRotationZ(source.Rotate.Z)
            * Matrix4x4.CreateRotationX(source.Rotate.X)
            * Matrix4x4.CreateRotationY(source.Rotate.Y)
            * Matrix4x4.CreateTranslation(source.Translate));
        rigidBodyMatrix = InvZ(rigidBodyMatrix);
        Matrix4x4 boneGlobal = source.BoneIndex >= 0 && source.BoneIndex < restGlobals.Length
            ? restGlobals[source.BoneIndex]
            : Matrix4x4.Identity;
        Matrix4x4 offset = rigidBodyMatrix * Invert(boneGlobal);
        Matrix4x4 initialBullet = InvZ(offset * boneGlobal);
        AndroidBulletMotionState motionState = new(ToBt(initialBullet));
        float mass = source.Op == PmxOperation.Static ? 0.0f : MathF.Max(source.Mass, 0.0f);
        BtVector3 inertia = BtVector3.Zero;
        if (mass > 0.0f)
        {
            shape.CalculateLocalInertia(mass, out inertia);
        }
        RigidBody body;
        using (RigidBodyConstructionInfo info = new(mass, motionState, shape, inertia)
        {
            LinearDamping = source.TranslateDimmer,
            AngularDamping = source.RotateDimmer,
            Restitution = source.Repulsion,
            Friction = source.Friction,
            AdditionalDamping = true
        })
        {
            body = new RigidBody(info);
        }
        body.SetSleepingThresholds(0.01f, MathF.PI / 1800.0f);
        body.ActivationState = ActivationState.DisableDeactivation;
        if (source.Op == PmxOperation.Static)
        {
            body.CollisionFlags |= CollisionFlags.KinematicObject;
        }
        return new BodyState(source.BoneIndex, source.Op, offset, motionState, body);
    }

    private static Generic6DofSpringConstraint CreateJoint(PmxJoint source, RigidBody bodyA, RigidBody bodyB)
    {
        Matrix4x4 jointWorld = Matrix4x4.CreateFromYawPitchRoll(source.Rotate.Y, source.Rotate.X, source.Rotate.Z)
            * Matrix4x4.CreateTranslation(source.Translate);
        Matrix4x4 frameA = jointWorld * Invert(FromBt(bodyA.WorldTransform));
        Matrix4x4 frameB = jointWorld * Invert(FromBt(bodyB.WorldTransform));
        Generic6DofSpringConstraint constraint = new(bodyA, bodyB, ToBt(frameA), ToBt(frameB), true)
        {
            LinearLowerLimit = ToBt(source.TranslateLowerLimit),
            LinearUpperLimit = ToBt(source.TranslateUpperLimit),
            AngularLowerLimit = ToBt(source.RotateLowerLimit),
            AngularUpperLimit = ToBt(source.RotateUpperLimit)
        };
        float[] springs = [source.SpringTranslate.X, source.SpringTranslate.Y, source.SpringTranslate.Z, source.SpringRotate.X, source.SpringRotate.Y, source.SpringRotate.Z];
        for (int i = 0; i < springs.Length; i++)
        {
            if (springs[i] != 0.0f)
            {
                constraint.EnableSpring(i, true);
                constraint.SetStiffness(i, springs[i]);
            }
        }
        return constraint;
    }

    private static Matrix4x4[] CreateRestGlobals(IReadOnlyList<PmxBone> bones)
    {
        Matrix4x4[] result = new Matrix4x4[bones.Count];
        for (int i = 0; i < bones.Count; i++)
        {
            Vector3 position = new(bones[i].Position.X, bones[i].Position.Y, -bones[i].Position.Z);
            result[i] = Matrix4x4.CreateTranslation(position);
        }
        return result;
    }

    private static Matrix4x4 Invert(Matrix4x4 value)
    {
        return Matrix4x4.Invert(value, out Matrix4x4 result) ? result : Matrix4x4.Identity;
    }

    private static Matrix4x4 InvZ(Matrix4x4 value)
    {
        Matrix4x4 flip = Matrix4x4.CreateScale(1.0f, 1.0f, -1.0f);
        return flip * value * flip;
    }

    private static BtVector3 ToBt(Vector3 value) => new(value.X, value.Y, value.Z);
    private static BtMatrix ToBt(Matrix4x4 value) => new(value.M11, value.M12, value.M13, value.M14, value.M21, value.M22, value.M23, value.M24, value.M31, value.M32, value.M33, value.M34, value.M41, value.M42, value.M43, value.M44);
    private static Matrix4x4 FromBt(BtMatrix value) => new(value.M11, value.M12, value.M13, value.M14, value.M21, value.M22, value.M23, value.M24, value.M31, value.M32, value.M33, value.M34, value.M41, value.M42, value.M43, value.M44);

    private sealed class BodyState(int boneIndex, PmxOperation operation, Matrix4x4 offset, AndroidBulletMotionState motionState, RigidBody body) : IDisposable
    {
        private readonly Matrix4x4 _inverseOffset = Invert(offset);
        public int BoneIndex { get; } = boneIndex;
        public PmxOperation Operation { get; } = operation;
        public AndroidBulletMotionState MotionState { get; } = motionState;
        public RigidBody Body { get; } = body;
        public bool Initialized { get; private set; }

        public void SetKinematicTransform(Matrix4x4 boneGlobal)
        {
            MotionState.Transform = ToBt(InvZ(offset * boneGlobal));
        }

        public void ResetDynamicTransform(Matrix4x4 boneGlobal)
        {
            BtMatrix transform = ToBt(InvZ(offset * boneGlobal));
            MotionState.Transform = transform;
            Body.WorldTransform = transform;
            Body.CenterOfMassTransform = transform;
            Body.LinearVelocity = BtVector3.Zero;
            Body.AngularVelocity = BtVector3.Zero;
            Body.ClearForces();
            Body.Activate(true);
            Initialized = true;
        }

        public void Activate() => Body.Activate(true);

        public void ClearContacts(OverlappingPairCache cache, Dispatcher dispatcher)
        {
            if (Body.BroadphaseHandle is not null)
            {
                cache.CleanProxyFromPairs(Body.BroadphaseHandle, dispatcher);
            }
        }

        public Matrix4x4 ReadBoneGlobal()
        {
            return _inverseOffset * InvZ(FromBt(MotionState.Transform));
        }

        public void Dispose()
        {
            Body.Dispose();
            MotionState.Dispose();
        }

    }

    private sealed class AndroidBulletMotionState(BtMatrix transform) : MotionState
    {
        public BtMatrix Transform { get; set; } = transform;
        public override void GetWorldTransform(out BtMatrix worldTrans) => worldTrans = Transform;
        public override void SetWorldTransform(ref BtMatrix worldTrans) => Transform = worldTrans;
    }

    private sealed class AndroidMmdFilterCallback(BroadphaseProxy floor) : OverlapFilterCallback
    {
        public override bool NeedBroadphaseCollision(BroadphaseProxy proxy0, BroadphaseProxy proxy1)
        {
            if (proxy1 is null)
            {
                return false;
            }
            if (proxy0 == floor || proxy1 == floor)
            {
                return true;
            }
            return (proxy0.CollisionFilterGroup & proxy1.CollisionFilterMask) != 0
                && (proxy1.CollisionFilterGroup & proxy0.CollisionFilterMask) != 0;
        }
    }
}
