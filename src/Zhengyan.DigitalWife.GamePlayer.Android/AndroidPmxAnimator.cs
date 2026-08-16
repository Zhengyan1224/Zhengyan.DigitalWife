using System.Numerics;
using Zhengyan.DigitalWife.Mmd;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

// Android animation evaluator. It mirrors PMX's order: morphs, bone tracks,
// append transforms, IK, then vertex skinning. Physics is intentionally kept
// as a separate post-solver seam so it can be replaced without changing VMD.
internal sealed class AndroidPmxAnimator : IDisposable
{
    private readonly PmxParsing _pmx;
    private readonly PmxVertex[] _vertices;
    private readonly BoneState[] _bones;
    private readonly Matrix4x4[] _globals;
    private readonly Matrix4x4[] _skinTransforms;
    private readonly Dictionary<int, MotionTrack>[] _tracks;
    private readonly Dictionary<int, MorphTrack>[] _morphTracks;
    private readonly float[] _layerWeights;
    private readonly MorphState[] _morphs;
    private readonly Vector3[] _morphPositions;
    private readonly Vector4[] _morphUvs;
    private readonly int[] _sortedBones;
    private readonly IkState[] _iks;
    private readonly PhysicsState[] _fallbackPhysics;
    private readonly AndroidPmxBulletPhysics? _bulletPhysics;
    private readonly bool _physicsEnabled;
    private readonly Vector3 _gravity;
    private readonly bool _resetPhysicsOnLoop;
    private float _previousFrame = -1.0f;
    private readonly float _durationFrames;

    public AndroidPmxAnimator(
        PmxParsing pmx,
        IReadOnlyList<(VmdParsing Animation, float Weight)> layers,
        bool physicsEnabled,
        Vector3 gravity,
        bool resetPhysicsOnLoop)
    {
        _pmx = pmx;
        _vertices = pmx.Vertices;
        _globals = new Matrix4x4[pmx.Bones.Length];
        _skinTransforms = new Matrix4x4[pmx.Bones.Length];
        _tracks = new Dictionary<int, MotionTrack>[layers.Count];
        _morphTracks = new Dictionary<int, MorphTrack>[layers.Count];
        _layerWeights = layers.Select(layer => Math.Clamp(layer.Weight, 0.0f, 1.0f)).ToArray();

        Dictionary<string, int> bonesByName = new(StringComparer.Ordinal);
        _bones = new BoneState[pmx.Bones.Length];
        for (int i = 0; i < pmx.Bones.Length; i++)
        {
            PmxBone source = pmx.Bones[i];
            Vector3 position = FlipZ(source.Position);
            Vector3 parentPosition = source.ParentBoneIndex >= 0 && source.ParentBoneIndex < pmx.Bones.Length
                ? FlipZ(pmx.Bones[source.ParentBoneIndex].Position)
                : Vector3.Zero;
            _bones[i] = new BoneState(
                source.ParentBoneIndex,
                position - parentPosition,
                Matrix4x4.CreateTranslation(-position),
                source.AppendBoneIndex,
                source.AppendWeight,
                source.BoneFlags.HasFlag(PmxBoneFlags.AppendRotate),
                source.BoneFlags.HasFlag(PmxBoneFlags.AppendTranslate),
                source.BoneFlags.HasFlag(PmxBoneFlags.AppendLocal),
                source.BoneFlags.HasFlag(PmxBoneFlags.IK),
                source.IKTargetBoneIndex,
                Math.Max(1, source.IKIterationCount),
                MathF.Max(source.IKLimit, 0.001f),
                source.IKLinks);
            bonesByName.TryAdd(source.Name, i);
        }

        for (int i = 0; i < layers.Count; i++)
        {
            VmdParsing animation = layers[i].Animation;
            _tracks[i] = BuildBoneTracks(animation, bonesByName);
            _morphTracks[i] = BuildMorphTracks(animation, pmx.Morphs);
        }

        Dictionary<string, int> morphsByName = pmx.Morphs
            .Select((morph, index) => (morph.Name, index))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);
        _morphs = pmx.Morphs.Select(morph => new MorphState(morph)).ToArray();
        _morphPositions = new Vector3[pmx.Vertices.Length];
        _morphUvs = new Vector4[pmx.Vertices.Length];
        _sortedBones = Enumerable.Range(0, _bones.Length)
            .OrderBy(index => pmx.Bones[index].DeformDepth)
            .ThenBy(index => index)
            .ToArray();
        _iks = _bones.Select((bone, index) => new IkState(index, bone)).Where(state => state.Enabled).ToArray();
        _physicsEnabled = physicsEnabled;
        _gravity = gravity;
        _resetPhysicsOnLoop = resetPhysicsOnLoop;
        _fallbackPhysics = pmx.RigidBodies
            .Where(body => body.BoneIndex >= 0 && body.BoneIndex < _bones.Length && body.Op != PmxOperation.Static)
            .Select(body => new PhysicsState(body.BoneIndex, body.Op, MathF.Max(body.TranslateDimmer, 0.0f), MathF.Max(body.Mass, 0.001f)))
            .ToArray();
        if (physicsEnabled && pmx.RigidBodies.Length != 0)
        {
            try
            {
                _bulletPhysics = new AndroidPmxBulletPhysics(pmx, gravity);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Android Bullet initialization failed; using lightweight fallback: {ex.Message}");
            }
        }
        _durationFrames = layers.Count == 0 ? 0.0f : layers.Max(layer => GetAnimationDuration(layer.Animation));
    }

    public bool HasAnimation => _tracks.Any(track => track.Count != 0) || _morphTracks.Any(track => track.Count != 0);
    public bool RequiresUpdate => HasAnimation || (_physicsEnabled && _fallbackPhysics.Length != 0);
    public string PhysicsBackend => !_physicsEnabled || _fallbackPhysics.Length == 0 ? "disabled" : _bulletPhysics is null ? "lightweight fallback" : "Bullet";

    public ReadOnlySpan<Matrix4x4> SkinTransforms => _skinTransforms;

    public void Update(double timeSeconds, float playbackSpeed, bool loop, float[] destination, bool skinVertices = true)
    {
        if (destination.Length < _vertices.Length * 8)
        {
            return;
        }

        float frame = (float)Math.Max(timeSeconds, 0.0) * 30.0f * Math.Max(playbackSpeed, 0.0f);
        if (_durationFrames > 0.0f)
        {
            frame = loop ? frame % _durationFrames : Math.Min(frame, _durationFrames);
        }

        bool looped = _previousFrame >= 0.0f && frame < _previousFrame;
        bool resetPhysics = _previousFrame < 0.0f || (_resetPhysicsOnLoop && looped);
        float deltaSeconds = _previousFrame < 0.0f || looped ? 1.0f / 30.0f : Math.Clamp((frame - _previousFrame) / 30.0f, 0.0f, 1.0f / 15.0f);
        if (resetPhysics && _bulletPhysics is null)
        {
            foreach (PhysicsState state in _fallbackPhysics)
            {
                state.Offset = Vector3.Zero;
                state.Velocity = Vector3.Zero;
            }
        }
        _previousFrame = frame;

        EvaluateMorphs(frame, loop);
        BonePose[] poses = new BonePose[_bones.Length];
        for (int i = 0; i < poses.Length; i++)
        {
            poses[i] = BonePose.Identity;
            float totalWeight = 0.0f;
            for (int layer = 0; layer < _tracks.Length; layer++)
            {
                if (_tracks[layer].TryGetValue(i, out MotionTrack? track))
                {
                    float weight = _layerWeights[layer];
                    if (weight <= 1e-5f)
                    {
                        continue;
                    }
                    BonePose sample = track.Evaluate(frame);
                    if (totalWeight == 0.0f)
                    {
                        poses[i] = sample;
                    }
                    else
                    {
                        float blend = weight / (totalWeight + weight);
                        poses[i] = new BonePose(
                            Vector3.Lerp(poses[i].Translation, sample.Translation, blend),
                            Quaternion.Slerp(poses[i].Rotation, sample.Rotation, blend));
                    }
                    totalWeight += weight;
                }
            }
            ApplyBoneMorphs(i, ref poses[i]);
        }
        if (_bulletPhysics is null)
        {
            IntegrateFallbackPhysics(poses, deltaSeconds);
        }

        Array.Fill(_globals, Matrix4x4.Identity);
        for (int pass = 0; pass < 2; pass++)
        {
            Array.Clear(_globals);
            foreach (int index in _sortedBones)
            {
                BoneState bone = _bones[index];
                BonePose pose = poses[index];
                if (bone.AppendIndex >= 0 && bone.AppendIndex < poses.Length)
                {
                    BonePose source = poses[bone.AppendIndex];
                    if (bone.AppendRotate)
                    {
                        pose = pose with { Rotation = pose.Rotation * Quaternion.Slerp(Quaternion.Identity, source.Rotation, bone.AppendWeight) };
                    }
                    if (bone.AppendTranslate)
                    {
                        pose = pose with { Translation = pose.Translation + source.Translation * bone.AppendWeight };
                    }
                }
                Matrix4x4 local = Matrix4x4.CreateFromQuaternion(pose.Rotation)
                    * Matrix4x4.CreateTranslation(bone.RestTranslation + pose.Translation);
                Matrix4x4 parent = bone.ParentIndex >= 0 && bone.ParentIndex < _globals.Length
                    ? _globals[bone.ParentIndex]
                    : Matrix4x4.Identity;
                _globals[index] = local * parent;
            }

            foreach (IkState ik in _iks)
            {
                SolveIk(ik, poses);
            }
        }

        if (_bulletPhysics is not null)
        {
            IReadOnlyDictionary<int, Matrix4x4> overrides = _bulletPhysics.Step(_globals, deltaSeconds, resetPhysics);
            ApplyPhysicsOverrides(overrides);
        }

        for (int i = 0; i < _skinTransforms.Length; i++)
        {
            _skinTransforms[i] = _bones[i].InverseBind * _globals[i];
        }

        if (!skinVertices)
        {
            return;
        }

        for (int i = 0; i < _vertices.Length; i++)
        {
            PmxVertex vertex = _vertices[i];
            Vector3 sourcePosition = FlipZ(vertex.Position) + MorphPosition(i);
            Vector3 sourceNormal = FlipZ(vertex.Normal);
            (Vector3 position, Vector3 normal) = SkinVertex(vertex, sourcePosition, sourceNormal);
            int offset = i * (destination.Length >= _vertices.Length * 16 ? 16 : 8);
            destination[offset] = position.X;
            destination[offset + 1] = position.Y;
            destination[offset + 2] = position.Z;
            destination[offset + 3] = normal.X;
            destination[offset + 4] = normal.Y;
            destination[offset + 5] = normal.Z;
            destination[offset + 6] = vertex.UV.X + MorphUv(i).X;
            destination[offset + 7] = vertex.UV.Y + MorphUv(i).Y;
        }
    }

    private void EvaluateMorphs(float frame, bool loop)
    {
        for (int i = 0; i < _morphs.Length; i++)
        {
            float value = 0.0f;
            for (int layer = 0; layer < _morphTracks.Length; layer++)
            {
                if (_morphTracks[layer].TryGetValue(i, out MorphTrack? track))
                {
                    value += track.Evaluate(frame, loop) * _layerWeights[layer];
                }
            }
            _morphs[i].Weight = Math.Clamp(value, 0.0f, 1.0f);
        }

        // Group/flip morphs are resolved after source weights, with a bounded
        // recursion so malformed cyclic PMX data cannot hang the render loop.
        for (int i = 0; i < _morphs.Length; i++)
        {
            ResolveGroupMorph(i, 0);
        }

        Array.Clear(_morphPositions);
        Array.Clear(_morphUvs);
        foreach (MorphState morph in _morphs.Where(morph => morph.Weight > 1e-5f))
        {
            if (morph.Source.MorphType == PmxMorphType.Position)
            {
                foreach (PmxMorph.PositionMorph item in morph.Source.PositionMorphs)
                {
                    if ((uint)item.VertexIndex < (uint)_morphPositions.Length)
                    {
                        _morphPositions[item.VertexIndex] += FlipZ(item.Position) * morph.Weight;
                    }
                }
            }
            else if (morph.Source.MorphType is PmxMorphType.UV or PmxMorphType.AddUV1 or PmxMorphType.AddUV2 or PmxMorphType.AddUV3 or PmxMorphType.AddUV4)
            {
                foreach (PmxMorph.UVMorph item in morph.Source.UVMorphs)
                {
                    if ((uint)item.VertexIndex < (uint)_morphUvs.Length)
                    {
                        _morphUvs[item.VertexIndex] += item.UV * morph.Weight;
                    }
                }
            }
        }
    }

    private float ResolveGroupMorph(int index, int depth)
    {
        if (depth > 32 || index < 0 || index >= _morphs.Length)
        {
            return 0.0f;
        }
        MorphState state = _morphs[index];
        if (state.Source.MorphType == PmxMorphType.Group)
        {
            float value = 0.0f;
            foreach (PmxMorph.GroupMorph item in state.Source.GroupMorphs)
            {
                if (item.MorphIndex >= 0 && item.MorphIndex < _morphs.Length)
                {
                    value += ResolveGroupMorph(item.MorphIndex, depth + 1) * item.Weight;
                }
            }
            state.Weight = Math.Clamp(state.Weight + value, 0.0f, 1.0f);
        }
        return state.Weight;
    }

    private Vector3 MorphPosition(int vertexIndex)
    {
        return (uint)vertexIndex < (uint)_morphPositions.Length ? _morphPositions[vertexIndex] : Vector3.Zero;
    }

    private Vector4 MorphUv(int vertexIndex)
    {
        return (uint)vertexIndex < (uint)_morphUvs.Length ? _morphUvs[vertexIndex] : Vector4.Zero;
    }

    private void ApplyBoneMorphs(int boneIndex, ref BonePose pose)
    {
        foreach (MorphState morph in _morphs.Where(morph => morph.Weight > 1e-5f && morph.Source.MorphType == PmxMorphType.Bone))
        {
            foreach (PmxMorph.BoneMorph item in morph.Source.BoneMorphs.Where(item => item.BoneIndex == boneIndex))
            {
                Vector3 translation = pose.Translation + FlipZ(item.Position) * morph.Weight;
                Matrix4x4 flip = Matrix4x4.CreateScale(1.0f, 1.0f, -1.0f);
                Quaternion rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(flip * Matrix4x4.CreateFromQuaternion(item.Quaternion) * flip));
                Quaternion finalRotation = Quaternion.Normalize(pose.Rotation * Quaternion.Slerp(Quaternion.Identity, rotation, morph.Weight));
                pose = new BonePose(translation, finalRotation);
            }
        }
    }

    private void SolveIk(IkState ik, BonePose[] poses)
    {
        if (ik.TargetIndex < 0 || ik.TargetIndex >= _globals.Length || ik.Links.Length == 0)
        {
            return;
        }
        for (int iteration = 0; iteration < ik.Iterations; iteration++)
        {
            Vector3 target = GetPosition(_globals[ik.TargetIndex]);
            for (int linkIndex = 0; linkIndex < ik.Links.Length; linkIndex++)
            {
                int link = ik.Links[linkIndex].BoneIndex;
                if (link < 0 || link >= _globals.Length)
                {
                    continue;
                }
                Vector3 pivot = GetPosition(_globals[link]);
                Vector3 current = GetPosition(_globals[ik.NodeIndex]);
                Vector3 a = NormalizeOrDefault(current - pivot, Vector3.UnitZ);
                Vector3 b = NormalizeOrDefault(target - pivot, Vector3.UnitZ);
                float dot = Math.Clamp(Vector3.Dot(a, b), -1.0f, 1.0f);
                float angle = MathF.Acos(dot);
                if (angle < 1e-4f)
                {
                    continue;
                }
                Vector3 axis = Vector3.Cross(a, b);
                if (axis.LengthSquared() < 1e-8f)
                {
                    continue;
                }
                angle = MathF.Min(angle, ik.Limit);
                Quaternion delta = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
                poses[link] = poses[link] with { Rotation = Quaternion.Normalize(delta * poses[link].Rotation) };
                RebuildGlobals(poses);
                if (Vector3.DistanceSquared(GetPosition(_globals[ik.NodeIndex]), target) < 1e-5f)
                {
                    return;
                }
            }
        }
    }

    public void Dispose()
    {
        _bulletPhysics?.Dispose();
    }

    private void IntegrateFallbackPhysics(BonePose[] poses, float deltaSeconds)
    {
        if (!_physicsEnabled || _fallbackPhysics.Length == 0)
        {
            return;
        }
        foreach (PhysicsState state in _fallbackPhysics)
        {
            float damping = Math.Clamp(1.0f - state.Damping * deltaSeconds, 0.0f, 1.0f);
            Vector3 acceleration = _gravity / state.Mass - state.Offset * 18.0f;
            state.Velocity = (state.Velocity + acceleration * deltaSeconds) * damping;
            state.Offset += state.Velocity * deltaSeconds;
            state.Offset = Vector3.Clamp(state.Offset, new Vector3(-2.0f), new Vector3(2.0f));
            float merge = state.Operation == PmxOperation.DynamicAndBoneMerge ? 0.5f : 1.0f;
            poses[state.BoneIndex] = poses[state.BoneIndex] with { Translation = poses[state.BoneIndex].Translation + state.Offset * merge };
        }
    }

    private void ApplyPhysicsOverrides(IReadOnlyDictionary<int, Matrix4x4> overrides)
    {
        if (overrides.Count == 0)
        {
            return;
        }
        Matrix4x4[] locals = new Matrix4x4[_globals.Length];
        for (int i = 0; i < _globals.Length; i++)
        {
            int parent = _bones[i].ParentIndex;
            locals[i] = parent >= 0 && parent < _globals.Length
                ? _globals[i] * InvertOrIdentity(_globals[parent])
                : _globals[i];
        }
        foreach (int index in _sortedBones)
        {
            if (overrides.TryGetValue(index, out Matrix4x4 physicsGlobal))
            {
                _globals[index] = physicsGlobal;
                continue;
            }
            int parent = _bones[index].ParentIndex;
            _globals[index] = parent >= 0 && parent < _globals.Length
                ? locals[index] * _globals[parent]
                : locals[index];
        }
    }

    private static Matrix4x4 InvertOrIdentity(Matrix4x4 matrix)
    {
        return Matrix4x4.Invert(matrix, out Matrix4x4 inverse) ? inverse : Matrix4x4.Identity;
    }

    private void RebuildGlobals(BonePose[] poses)
    {
        Array.Clear(_globals);
        foreach (int index in _sortedBones)
        {
            BoneState bone = _bones[index];
            Matrix4x4 local = Matrix4x4.CreateFromQuaternion(poses[index].Rotation)
                * Matrix4x4.CreateTranslation(bone.RestTranslation + poses[index].Translation);
            Matrix4x4 parent = bone.ParentIndex >= 0 && bone.ParentIndex < _globals.Length ? _globals[bone.ParentIndex] : Matrix4x4.Identity;
            _globals[index] = local * parent;
        }
    }

    private (Vector3 Position, Vector3 Normal) SkinVertex(PmxVertex vertex, Vector3 position, Vector3 normal)
    {
        int i0 = vertex.BoneIndices[0];
        int i1 = vertex.BoneIndices[1];
        Matrix4x4 m0 = GetSkinTransform(i0);
        Matrix4x4 m1 = GetSkinTransform(i1);
        if (vertex.WeightType == PmxVertexWeight.SDEF && IsValidBone(i0) && IsValidBone(i1))
        {
            float w0 = vertex.BoneWeights[0];
            float w1 = 1.0f - w0;
            Vector3 center = FlipZ(vertex.SdefC);
            Vector3 r0 = FlipZ(vertex.SdefR0);
            Vector3 r1 = FlipZ(vertex.SdefR1);
            Vector3 rw = r0 * w0 + r1 * w1;
            Vector3 cr0 = (center + r0 - rw + center) * 0.5f;
            Vector3 cr1 = (center + r1 - rw + center) * 0.5f;
            Quaternion q0 = Quaternion.CreateFromRotationMatrix(_globals[i0]);
            Quaternion q1 = Quaternion.CreateFromRotationMatrix(_globals[i1]);
            Matrix4x4 rotation = Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(q0, q1, w1));
            return (Vector3.Transform(position - center, rotation) + Vector3.Transform(cr0, m0) * w0 + Vector3.Transform(cr1, m1) * w1,
                NormalizeOrDefault(Vector3.TransformNormal(normal, rotation), normal));
        }
        Matrix4x4 transform = vertex.WeightType switch
        {
            PmxVertexWeight.BDEF1 => m0,
            PmxVertexWeight.BDEF2 => m0 * vertex.BoneWeights[0] + m1 * (1.0f - vertex.BoneWeights[0]),
            PmxVertexWeight.BDEF4 or PmxVertexWeight.QDEF => m0 * vertex.BoneWeights[0] + m1 * vertex.BoneWeights[1] + GetSkinTransform(vertex.BoneIndices[2]) * vertex.BoneWeights[2] + GetSkinTransform(vertex.BoneIndices[3]) * vertex.BoneWeights[3],
            _ => m0
        };
        return (Vector3.Transform(position, transform), NormalizeOrDefault(Vector3.TransformNormal(normal, transform), normal));
    }

    private Matrix4x4 GetSkinTransform(int index) => IsValidBone(index) ? _skinTransforms[index] : Matrix4x4.Identity;
    private bool IsValidBone(int index) => index >= 0 && index < _bones.Length;
    private static Vector3 GetPosition(Matrix4x4 matrix) => new(matrix.M41, matrix.M42, matrix.M43);
    private static Vector3 FlipZ(Vector3 value) => new(value.X, value.Y, -value.Z);
    private static Vector3 NormalizeOrDefault(Vector3 value, Vector3 fallback) => value.LengthSquared() > 1e-10f ? Vector3.Normalize(value) : fallback;
    private static float GetAnimationDuration(VmdParsing animation) => Math.Max(animation.Motions.Length == 0 ? 0u : animation.Motions.Max(motion => motion.Frame), animation.Morphs.Length == 0 ? 0u : animation.Morphs.Max(morph => morph.Frame));

    private static Dictionary<int, MotionTrack> BuildBoneTracks(VmdParsing animation, Dictionary<string, int> bones)
    {
        return animation.Motions.Where(motion => bones.ContainsKey(motion.BoneName)).GroupBy(motion => bones[motion.BoneName]).ToDictionary(group => group.Key, group => new MotionTrack(group.OrderBy(item => item.Frame).Select(MotionKey.Create).ToArray()));
    }
    private static Dictionary<int, MorphTrack> BuildMorphTracks(VmdParsing animation, IReadOnlyList<PmxMorph> morphs)
    {
        Dictionary<string, int> names = morphs.Select((morph, index) => (morph.Name, index)).GroupBy(item => item.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);
        return animation.Morphs.Where(morph => names.ContainsKey(morph.BlendShapeName)).GroupBy(morph => names[morph.BlendShapeName]).ToDictionary(group => group.Key, group => new MorphTrack(group.OrderBy(item => item.Frame).ToArray()));
    }

    private sealed class MorphState(PmxMorph source) { public PmxMorph Source { get; } = source; public float Weight { get; set; } }
    private readonly record struct BoneState(int ParentIndex, Vector3 RestTranslation, Matrix4x4 InverseBind, int AppendIndex, float AppendWeight, bool AppendRotate, bool AppendTranslate, bool AppendLocal, bool Enabled, int TargetIndex, int Iterations, float Limit, PmxBone.IKLink[] Links);
    private sealed class IkState(int nodeIndex, BoneState bone) { public int NodeIndex { get; } = nodeIndex; public int TargetIndex { get; } = bone.TargetIndex; public int Iterations { get; } = bone.Iterations; public float Limit { get; } = bone.Limit; public PmxBone.IKLink[] Links { get; } = bone.Links; public bool Enabled { get; } = bone.Enabled; }
    private sealed class PhysicsState(int boneIndex, PmxOperation operation, float damping, float mass) { public int BoneIndex { get; } = boneIndex; public PmxOperation Operation { get; } = operation; public float Damping { get; } = damping; public float Mass { get; } = mass; public Vector3 Offset { get; set; } public Vector3 Velocity { get; set; } }
    private readonly record struct BonePose(Vector3 Translation, Quaternion Rotation) { public static BonePose Identity => new(Vector3.Zero, Quaternion.Identity); }
    private sealed class MotionTrack(MotionKey[] keys) { public MotionKey[] Keys { get; } = keys; public BonePose Evaluate(float frame) => EvaluateKey(Keys, frame); }
    private sealed class MorphTrack(VmdMorph[] keys) { public float Evaluate(float frame, bool loop) { if (keys.Length == 0) return 0; if (frame <= keys[0].Frame) return keys[0].Weight; if (frame >= keys[^1].Frame) return keys[^1].Weight; int upper = Array.FindIndex(keys, key => key.Frame >= frame); VmdMorph a = keys[upper - 1], b = keys[upper]; return float.Lerp(a.Weight, b.Weight, (frame - a.Frame) / Math.Max(1.0f, b.Frame - a.Frame)); } }
    private readonly record struct MotionKey(float Frame, BonePose Pose, BezierCurve X, BezierCurve Y, BezierCurve Z, BezierCurve R)
    {
        public static MotionKey Create(VmdMotion motion) { Matrix4x4 flip = Matrix4x4.CreateScale(1.0f, 1.0f, -1.0f); Quaternion rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(flip * Matrix4x4.CreateFromQuaternion(motion.Quaternion) * flip)); return new(motion.Frame, new BonePose(FlipZ(motion.Translate), rotation), BezierCurve.FromVmd(motion.Interpolation, 0), BezierCurve.FromVmd(motion.Interpolation, 1), BezierCurve.FromVmd(motion.Interpolation, 2), BezierCurve.FromVmd(motion.Interpolation, 3)); }
    }
    private static BonePose EvaluateKey(MotionKey[] keys, float frame)
    {
        if (keys.Length == 0) return BonePose.Identity; if (frame <= keys[0].Frame) return keys[0].Pose; if (frame >= keys[^1].Frame) return keys[^1].Pose; int upper = Array.FindIndex(keys, key => key.Frame >= frame); MotionKey a = keys[upper - 1], b = keys[upper]; float t = (frame - a.Frame) / Math.Max(1.0f, b.Frame - a.Frame); return new BonePose(new Vector3(float.Lerp(a.Pose.Translation.X, b.Pose.Translation.X, b.X.Evaluate(t)), float.Lerp(a.Pose.Translation.Y, b.Pose.Translation.Y, b.Y.Evaluate(t)), float.Lerp(a.Pose.Translation.Z, b.Pose.Translation.Z, b.Z.Evaluate(t))), Quaternion.Slerp(a.Pose.Rotation, b.Pose.Rotation, b.R.Evaluate(t)));
    }
    private readonly record struct BezierCurve(Vector2 C1, Vector2 C2)
    {
        public static BezierCurve FromVmd(byte[] data, int offset) => new(new Vector2(data[offset] / 127.0f, data[offset + 4] / 127.0f), new Vector2(data[offset + 8] / 127.0f, data[offset + 12] / 127.0f));
        public float Evaluate(float time) { float lo = 0, hi = 1, t = time; for (int i = 0; i < 18; i++) { t = (lo + hi) * 0.5f; if (Cubic(t, C1.X, C2.X) < time) lo = t; else hi = t; } return Cubic(t, C1.Y, C2.Y); }
        private static float Cubic(float t, float a, float b) { float u = 1 - t; return 3 * u * u * t * a + 3 * u * t * t * b + t * t * t; }
    }
}
