using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd;

/// <summary>
/// Platform-neutral contract for the evaluated PMX pose consumed by a renderer.
/// Implementations may use CPU, GPU or a physics bridge, but expose the same
/// bone-name and transform semantics to PC and Android runtime code.
/// </summary>
public interface IPmxPoseEvaluator
{
    bool RequiresUpdate { get; }

    IReadOnlyList<string> BoneNames { get; }

    IReadOnlyList<Matrix4x4> GlobalPose { get; }

    IReadOnlyList<Matrix4x4> SkinPose { get; }

    IReadOnlyDictionary<string, float> MorphWeights { get; }

    void ApplyRelation(IPmxPoseEvaluator relation);
}

/// <summary>Physics bridge used by the shared pose evaluator.</summary>
public interface IPmxPhysicsBridge : IDisposable
{
    IReadOnlyDictionary<int, Matrix4x4> Step(
        IReadOnlyList<Matrix4x4> globals,
        float deltaSeconds,
        bool reset);

    void ApplyImpulse(PmxMorph.ImpulseMorph morph, float weight);

    IReadOnlyList<Vector3> GetColliderPoints() => [];
}
