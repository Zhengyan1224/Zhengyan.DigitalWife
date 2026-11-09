using Microsoft.JSInterop;
using System.Numerics;
using System.Runtime.InteropServices;
using Zhengyan.DigitalWife.Mmd;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

/// <summary>Bullet WASM bridge using the same body offsets and joint frames as Android/PC.</summary>
public sealed class WebPmxBulletPhysics : IPmxPhysicsBridge
{
    private readonly IJSInProcessObjectReference _module;
    private readonly int _worldId;
    private readonly BodyState[] _bodies;
    private readonly Matrix4x4[] _targets;
    private bool _needsReset = true;
    private bool _disposed;

    private WebPmxBulletPhysics(IJSInProcessObjectReference module, int worldId, BodyState[] bodies)
    {
        _module = module;
        _worldId = worldId;
        _bodies = bodies;
        _targets = new Matrix4x4[bodies.Length];
    }

    public static async Task<WebPmxBulletPhysics> CreateAsync(IJSInProcessObjectReference module, PmxParsing pmx, Vector3 gravity)
    {
        BodyState[] bodies = pmx.RigidBodies.Select(source =>
        {
            Matrix4x4 body = InvZ(Matrix4x4.CreateRotationZ(source.Rotate.Z) * Matrix4x4.CreateRotationX(source.Rotate.X)
                * Matrix4x4.CreateRotationY(source.Rotate.Y) * Matrix4x4.CreateTranslation(source.Translate));
            Matrix4x4 rest = source.BoneIndex >= 0 && source.BoneIndex < pmx.Bones.Length
                ? Matrix4x4.CreateTranslation(FlipZ(pmx.Bones[source.BoneIndex].Position)) : Matrix4x4.Identity;
            Matrix4x4 offset = body * Invert(rest);
            return new BodyState(source, offset, Invert(offset), InvZ(body));
        }).ToArray();
        object definition = new
        {
            gravity = Vector(gravity),
            bodies = bodies.Select(body => new
            {
                bone = body.Source.BoneIndex, operation = (int)body.Source.Op, shape = (int)body.Source.Shape,
                size = Vector(body.Source.ShapeSize), group = (int)body.Source.Group, mask = (int)body.Source.CollisionGroup,
                mass = body.Source.Op == PmxOperation.Static ? 0 : MathF.Max(body.Source.Mass, 0),
                linearDamping = body.Source.TranslateDimmer, angularDamping = body.Source.RotateDimmer,
                restitution = body.Source.Repulsion, friction = body.Source.Friction, transform = Matrix(body.InitialBullet)
            }).ToArray(),
            joints = pmx.Joints.Where(joint => joint.RigidBodyIndexA >= 0 && joint.RigidBodyIndexA < bodies.Length
                && joint.RigidBodyIndexB >= 0 && joint.RigidBodyIndexB < bodies.Length && joint.RigidBodyIndexA != joint.RigidBodyIndexB)
                .Select(joint =>
                {
                    Matrix4x4 world = Matrix4x4.CreateFromYawPitchRoll(joint.Rotate.Y, joint.Rotate.X, joint.Rotate.Z)
                        * Matrix4x4.CreateTranslation(joint.Translate);
                    return new
                    {
                        a = joint.RigidBodyIndexA, b = joint.RigidBodyIndexB,
                        frameA = Matrix(world * Invert(bodies[joint.RigidBodyIndexA].InitialBullet)),
                        frameB = Matrix(world * Invert(bodies[joint.RigidBodyIndexB].InitialBullet)),
                        linearLower = Vector(joint.TranslateLowerLimit), linearUpper = Vector(joint.TranslateUpperLimit),
                        angularLower = Vector(joint.RotateLowerLimit), angularUpper = Vector(joint.RotateUpperLimit),
                        springs = new[] { joint.SpringTranslate.X, joint.SpringTranslate.Y, joint.SpringTranslate.Z,
                            joint.SpringRotate.X, joint.SpringRotate.Y, joint.SpringRotate.Z }
                    };
                }).ToArray()
        };
        int id = await module.InvokeAsync<int>("createWorld", definition);
        return new(module, id, bodies);
    }

    public void RequestReset() => _needsReset = true;

    public IReadOnlyDictionary<int, Matrix4x4> Step(IReadOnlyList<Matrix4x4> globals, float deltaSeconds, bool reset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int i = 0; i < _bodies.Length; i++)
        {
            BodyState body = _bodies[i];
            _targets[i] = body.Source.BoneIndex >= 0 && body.Source.BoneIndex < globals.Count
                ? InvZ(body.Offset * globals[body.Source.BoneIndex]) : body.InitialBullet;
        }
        float[] states = _module.Invoke<float[]>("stepWorld", _worldId,
            MemoryMarshal.AsBytes(_targets.AsSpan()).ToArray(), Math.Clamp(deltaSeconds, 0, 1.0f / 15), reset || _needsReset);
        _needsReset = false;
        Dictionary<int, Matrix4x4> overrides = [];
        for (int i = 0; i < states.Length; i += 8)
        {
            BodyState body = _bodies[(int)states[i]];
            int bone = body.Source.BoneIndex;
            if (bone < 0 || bone >= globals.Count) continue;
            Matrix4x4 bullet = Matrix4x4.CreateFromQuaternion(new Quaternion(states[i+4], states[i+5], states[i+6], states[i+7]))
                * Matrix4x4.CreateTranslation(states[i+1], states[i+2], states[i+3]);
            Matrix4x4 global = body.InverseOffset * InvZ(bullet);
            if (body.Source.Op == PmxOperation.DynamicAndBoneMerge)
            {
                global.M41 = globals[bone].M41; global.M42 = globals[bone].M42;
                global.M43 = globals[bone].M43; global.M44 = globals[bone].M44;
            }
            overrides[bone] = global;
        }
        return overrides;
    }

    public void ApplyImpulse(PmxMorph.ImpulseMorph morph, float weight)
    {
        if (weight <= 0 || (uint)morph.RigidBodyIndex >= (uint)_bodies.Length) return;
        Vector3 velocity = FlipZ(morph.Velocity) * weight;
        Vector3 torque = new Vector3(-morph.Torque.X, -morph.Torque.Y, morph.Torque.Z) * weight;
        _module.InvokeVoid("applyImpulse", _worldId, morph.RigidBodyIndex, Vector(velocity), Vector(torque), morph.Local);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _module.InvokeVoid("destroyWorld", _worldId);
    }

    private static Vector3 FlipZ(Vector3 value) => new(value.X, value.Y, -value.Z);
    private static Matrix4x4 InvZ(Matrix4x4 value)
    {
        Matrix4x4 flip = Matrix4x4.CreateScale(1,1,-1);
        return flip * value * flip;
    }
    private static Matrix4x4 Invert(Matrix4x4 value) => Matrix4x4.Invert(value,out Matrix4x4 inverse) ? inverse : Matrix4x4.Identity;
    private static float[] Vector(Vector3 value) => [value.X,value.Y,value.Z];
    private static float[] Matrix(Matrix4x4 value) => [value.M11,value.M12,value.M13,value.M14,value.M21,value.M22,value.M23,value.M24,
        value.M31,value.M32,value.M33,value.M34,value.M41,value.M42,value.M43,value.M44];
    private sealed record BodyState(PmxRigidBody Source, Matrix4x4 Offset, Matrix4x4 InverseOffset, Matrix4x4 InitialBullet);
}

public sealed class WebPmxPhysicsService(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private Task<IJSInProcessObjectReference>? _moduleTask;
    public async Task<IPmxPhysicsBridge?> CreateAsync(PmxParsing pmx, Vector3 gravity)
    {
        if (pmx.RigidBodies.Length == 0) return null;
        _moduleTask ??= LoadModuleAsync();
        return await WebPmxBulletPhysics.CreateAsync(await _moduleTask, pmx, gravity);
    }
    private async Task<IJSInProcessObjectReference> LoadModuleAsync()
    {
        IJSObjectReference module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", WebRuntimeModuleUrl.Resolve("web-physics.js"));
        await module.InvokeVoidAsync("initialize");
        return module as IJSInProcessObjectReference ?? throw new NotSupportedException("Bullet requires WebAssembly in-process JS interop.");
    }
    public async ValueTask DisposeAsync()
    {
        if (_moduleTask is { IsCompletedSuccessfully: true }) await (await _moduleTask).DisposeAsync();
    }
}
