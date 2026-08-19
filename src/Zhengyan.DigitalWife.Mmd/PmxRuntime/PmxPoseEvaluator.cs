using System.Numerics;
using Zhengyan.DigitalWife.Mmd;

namespace Zhengyan.DigitalWife.Mmd;

// Shared PMX animation evaluator. It mirrors PMX's order: morphs, bone tracks,
// append transforms, IK, then vertex skinning. Physics is intentionally kept
// as a separate post-solver seam so it can be replaced without changing VMD.
public sealed class PmxPoseEvaluator : IDisposable, IPmxPoseEvaluator
{
    private readonly PmxParsing _pmx;
    private readonly PmxVertex[] _vertices;
    private readonly Dictionary<string, int> _boneIndicesByName;
    private readonly string[] _boneNames;
    private readonly BoneState[] _bones;
    private readonly Matrix4x4[] _globals;
    private readonly Matrix4x4[] _skinTransforms;
    private readonly Dictionary<int, MotionTrack>[] _tracks;
    private readonly Dictionary<int, MorphTrack>[] _morphTracks;
    private readonly Dictionary<int, IkTrack>[] _ikTracks;
    private readonly float[] _layerWeights;
    private readonly float[] _layerFrames;
    private readonly float[] _layerDurations;
    private readonly float[] _layerPlaybackSpeeds;
    private readonly bool[] _layerPlaying;
    private readonly bool[] _layerLoops;
    private readonly MorphState[] _morphs;
    private readonly float[] _sourceMorphWeights;
    private readonly float[] _effectiveMorphWeights;
    private readonly float[] _previousEffectiveMorphWeights;
    private readonly Vector3[] _morphPositions;
    private readonly Vector4[] _morphUvs;
    private readonly MaterialState[] _materialStates;
    private readonly int[] _sortedBones;
    private readonly IkState[] _iks;
    private readonly PhysicsState[] _fallbackPhysics;
    private readonly IPmxPhysicsBridge? _physicsBridge;
    private readonly bool _physicsEnabled;
    private readonly Vector3 _gravity;
    private readonly bool _resetPhysicsOnLoop;
    private float _previousFrame = -1.0f;
    private double _previousTimeSeconds = -1.0;

    public PmxPoseEvaluator(
        PmxParsing pmx,
        IReadOnlyList<(VmdParsing Animation, float Weight)> layers,
        bool physicsEnabled,
        Vector3 gravity,
        bool resetPhysicsOnLoop,
        IPmxPhysicsBridge? physicsBridge = null)
    {
        _pmx = pmx;
        _vertices = pmx.Vertices;
        _globals = new Matrix4x4[pmx.Bones.Length];
        _skinTransforms = new Matrix4x4[pmx.Bones.Length];
        _tracks = new Dictionary<int, MotionTrack>[layers.Count];
        _morphTracks = new Dictionary<int, MorphTrack>[layers.Count];
        _ikTracks = new Dictionary<int, IkTrack>[layers.Count];
        _layerWeights = layers.Select(layer => Math.Clamp(layer.Weight, 0.0f, 1.0f)).ToArray();
        _layerFrames = new float[layers.Count];
        _layerDurations = layers.Select(layer => GetAnimationDuration(layer.Animation)).ToArray();
        _layerPlaybackSpeeds = Enumerable.Repeat(1.0f, layers.Count).ToArray();
        _layerPlaying = Enumerable.Repeat(true, layers.Count).ToArray();
        _layerLoops = Enumerable.Repeat(true, layers.Count).ToArray();

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
                source.BoneFlags.HasFlag(PmxBoneFlags.FixedAxis),
                FlipZ(source.FixedAxis),
                source.BoneFlags.HasFlag(PmxBoneFlags.LocalAxis),
                FlipZ(source.LocalAxisX),
                FlipZ(source.LocalAxisZ),
                source.BoneFlags.HasFlag(PmxBoneFlags.IK),
                source.IKTargetBoneIndex,
                Math.Max(1, source.IKIterationCount),
                MathF.Max(source.IKLimit, 0.001f),
                source.IKLinks);
            bonesByName.TryAdd(source.Name, i);
        }
        _boneIndicesByName = bonesByName;
        _boneNames = pmx.Bones.Select(bone => bone.Name).ToArray();

        for (int i = 0; i < layers.Count; i++)
        {
            VmdParsing animation = layers[i].Animation;
            _tracks[i] = BuildBoneTracks(animation, bonesByName);
            _morphTracks[i] = BuildMorphTracks(animation, pmx.Morphs);
            _ikTracks[i] = BuildIkTracks(animation, bonesByName);
        }

        Dictionary<string, int> morphsByName = pmx.Morphs
            .Select((morph, index) => (morph.Name, index))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);
        _morphs = pmx.Morphs.Select(morph => new MorphState(morph)).ToArray();
        _sourceMorphWeights = new float[_morphs.Length];
        _effectiveMorphWeights = new float[_morphs.Length];
        _previousEffectiveMorphWeights = new float[_morphs.Length];
        _morphPositions = new Vector3[pmx.Vertices.Length];
        _morphUvs = new Vector4[pmx.Vertices.Length];
        _materialStates = pmx.Materials.Select(MaterialState.FromMaterial).ToArray();
        _sortedBones = Enumerable.Range(0, _bones.Length)
            .OrderBy(index => pmx.Bones[index].DeformDepth)
            .ThenBy(index => index)
            .ToArray();
        _iks = _bones.Select((bone, index) => new IkState(index, bone)).Where(state => state.Enabled).ToArray();
        _physicsEnabled = physicsEnabled;
        _gravity = gravity;
        _resetPhysicsOnLoop = resetPhysicsOnLoop;
        _physicsBridge = physicsBridge;
        _fallbackPhysics = pmx.RigidBodies
            .Where(body => body.BoneIndex >= 0 && body.BoneIndex < _bones.Length && body.Op != PmxOperation.Static)
            .Select(body => new PhysicsState(body.BoneIndex, body.Op, MathF.Max(body.TranslateDimmer, 0.0f), MathF.Max(body.Mass, 0.001f)))
            .ToArray();
        BonePose[] restPoses = Enumerable.Repeat(BonePose.Identity, _bones.Length).ToArray();
        RebuildGlobals(restPoses);
        UpdateSkinTransforms();
    }

    public bool HasAnimation => _tracks.Any(track => track.Count != 0) || _morphTracks.Any(track => track.Count != 0);
    public bool RequiresUpdate => HasAnimation || (_physicsEnabled && _fallbackPhysics.Length != 0);
    public string PhysicsBackend => !_physicsEnabled || _fallbackPhysics.Length == 0 ? "disabled" : _physicsBridge is null ? "lightweight fallback" : "Bullet";

    public ReadOnlySpan<Matrix4x4> SkinTransforms => _skinTransforms;

    public ReadOnlySpan<Matrix4x4> GlobalTransforms => _globals;

    public IReadOnlyList<Vector3> PhysicsColliderPoints => _physicsBridge?.GetColliderPoints() ?? [];

    public IReadOnlyList<Matrix4x4> GlobalPose => _globals;

    public IReadOnlyList<Matrix4x4> SkinPose => _skinTransforms;

    public IReadOnlyList<string> BoneNames => _boneNames;

    public IReadOnlyDictionary<string, float> MorphWeights => _morphs
        .Select((morph, index) => (morph.Source.Name, Weight: _effectiveMorphWeights[index]))
        .GroupBy(item => item.Name, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Last().Weight, StringComparer.Ordinal);

    public MaterialState GetMaterialState(int materialIndex)
    {
        return (uint)materialIndex < (uint)_materialStates.Length
            ? _materialStates[materialIndex]
            : MaterialState.Default;
    }

    public int MotionLayerCount => _layerFrames.Length;

    public float GetMotionLayerFrame(int layerIndex) => _layerFrames[ValidateLayerIndex(layerIndex)];

    public void SetMotionLayerFrame(int layerIndex, float frame)
    {
        int index = ValidateLayerIndex(layerIndex);
        _layerFrames[index] = Math.Clamp(frame, 0.0f, Math.Max(_layerDurations[index], 0.0f));
        _previousFrame = index == 0 ? _layerFrames[index] : _previousFrame;
    }

    public void SetMotionLayerPlaying(int layerIndex, bool playing) => _layerPlaying[ValidateLayerIndex(layerIndex)] = playing;

    public void SetMotionLayerLoop(int layerIndex, bool loop) => _layerLoops[ValidateLayerIndex(layerIndex)] = loop;

    public void SetMotionLayerPlaybackSpeed(int layerIndex, float speed) => _layerPlaybackSpeeds[ValidateLayerIndex(layerIndex)] = Math.Max(speed, 0.0f);

    public void SetMotionLayerWeight(int layerIndex, float weight) => _layerWeights[ValidateLayerIndex(layerIndex)] = Math.Clamp(weight, 0.0f, 1.0f);

    public void Update(double timeSeconds, float playbackSpeed, bool loop, float[] destination, bool skinVertices = true)
    {
        if (destination.Length < _vertices.Length * 8)
        {
            return;
        }

        bool looped = AdvanceMotionLayers(timeSeconds, playbackSpeed, loop);
        float frame = _layerFrames.Length == 0 ? 0.0f : _layerFrames[0];

        bool resetPhysics = _previousFrame < 0.0f || (_resetPhysicsOnLoop && looped);
        float deltaSeconds = _previousFrame < 0.0f || looped ? 1.0f / 30.0f : Math.Clamp((frame - _previousFrame) / 30.0f, 0.0f, 1.0f / 15.0f);
        if (resetPhysics && _physicsBridge is null)
        {
            foreach (PhysicsState state in _fallbackPhysics)
            {
                state.Offset = Vector3.Zero;
                state.Velocity = Vector3.Zero;
            }
        }
        _previousFrame = frame;

        EvaluateMorphs();
        EvaluateIkTracks();
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
                    BonePose sample = track.Evaluate(_layerFrames[layer]);
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
        if (_physicsBridge is null)
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
                    BonePose source = bone.AppendLocal
                        ? poses[bone.AppendIndex]
                        : GetGlobalAppendPose(bone.AppendIndex);
                    if (bone.AppendRotate)
                    {
                        pose = pose with { Rotation = pose.Rotation * Quaternion.Slerp(Quaternion.Identity, source.Rotation, bone.AppendWeight) };
                    }
                    if (bone.AppendTranslate)
                    {
                        pose = pose with { Translation = pose.Translation + source.Translation * bone.AppendWeight };
                    }
                }
                pose = ApplyBoneAxisConstraints(bone, pose);
                poses[index] = pose;
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

        if (_physicsBridge is not null)
        {
            IReadOnlyDictionary<int, Matrix4x4> overrides = _physicsBridge.Step(_globals, deltaSeconds, resetPhysics);
            ApplyPhysicsOverrides(overrides);
        }

        UpdateSkinTransforms();

        if (!skinVertices)
        {
            return;
        }

        WriteSkinnedVertices(destination);
    }

    public void ApplyRelation(PmxPoseEvaluator relation)
    {
        ApplyRelation((IPmxPoseEvaluator)relation);
    }

    public void ApplyRelation(IPmxPoseEvaluator relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        foreach ((string name, int targetIndex) in _boneIndicesByName)
        {
            int relationIndex = -1;
            for (int candidate = 0; candidate < relation.BoneNames.Count; candidate++)
            {
                if (string.Equals(relation.BoneNames[candidate], name, StringComparison.Ordinal))
                {
                    relationIndex = candidate;
                    break;
                }
            }

            if (relationIndex >= 0 && relationIndex < relation.GlobalPose.Count)
            {
                _globals[targetIndex] = relation.GlobalPose[relationIndex];
            }
        }
        UpdateSkinTransforms();
    }

    public void WriteSkinnedVertices(float[] destination)
    {
        if (destination.Length < _vertices.Length * 8)
        {
            throw new ArgumentException("The vertex destination buffer is too small.", nameof(destination));
        }

        int stride = destination.Length / Math.Max(_vertices.Length, 1);
        for (int i = 0; i < _vertices.Length; i++)
        {
            PmxVertex vertex = _vertices[i];
            Vector3 sourcePosition = FlipZ(vertex.Position) + MorphPosition(i);
            Vector3 sourceNormal = FlipZ(vertex.Normal);
            (Vector3 position, Vector3 normal) = SkinVertex(vertex, sourcePosition, sourceNormal);
            int offset = i * Math.Max(stride, 8);
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

    private void UpdateSkinTransforms()
    {
        for (int i = 0; i < _skinTransforms.Length; i++)
        {
            _skinTransforms[i] = _bones[i].InverseBind * _globals[i];
        }
    }

    private void EvaluateMorphs()
    {
        for (int i = 0; i < _morphs.Length; i++)
        {
            float value = 0.0f;
            for (int layer = 0; layer < _morphTracks.Length; layer++)
            {
                if (_morphTracks[layer].TryGetValue(i, out MorphTrack? track))
                {
                    value += track.Evaluate(_layerFrames[layer], _layerLoops[layer]) * _layerWeights[layer];
                }
            }
            _sourceMorphWeights[i] = Math.Clamp(value, 0.0f, 1.0f);
        }

        Array.Clear(_effectiveMorphWeights);
        for (int i = 0; i < _morphs.Length; i++)
        {
            if (_sourceMorphWeights[i] > 1e-6f)
            {
                PropagateMorph(i, _sourceMorphWeights[i], 0);
            }
        }
        for (int i = 0; i < _morphs.Length; i++)
        {
            _morphs[i].Weight = Math.Clamp(_effectiveMorphWeights[i], 0.0f, 1.0f);
        }

        Array.Clear(_morphPositions);
        Array.Clear(_morphUvs);
        for (int i = 0; i < _materialStates.Length; i++)
        {
            _materialStates[i] = MaterialState.FromMaterial(_pmx.Materials[i]);
        }
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
            else if (morph.Source.MorphType == PmxMorphType.Material)
            {
                ApplyMaterialMorph(morph.Source, morph.Weight);
            }
        }

        if (_physicsBridge is not null)
        {
            for (int i = 0; i < _morphs.Length; i++)
            {
                float addedWeight = _effectiveMorphWeights[i] - _previousEffectiveMorphWeights[i];
                if (addedWeight <= 1e-6f || _morphs[i].Source.MorphType != PmxMorphType.Impluse)
                {
                    continue;
                }
                foreach (PmxMorph.ImpulseMorph impulse in _morphs[i].Source.ImpulseMorphs)
                {
                    _physicsBridge?.ApplyImpulse(impulse, addedWeight);
                }
            }
        }
        Array.Copy(_effectiveMorphWeights, _previousEffectiveMorphWeights, _effectiveMorphWeights.Length);
    }

    private void PropagateMorph(int index, float weight, int depth)
    {
        if (depth > 32 || index < 0 || index >= _morphs.Length || MathF.Abs(weight) <= 1e-7f)
        {
            return;
        }

        PmxMorph source = _morphs[index].Source;
        if (source.MorphType == PmxMorphType.Group)
        {
            foreach (PmxMorph.GroupMorph item in source.GroupMorphs)
            {
                PropagateMorph(item.MorphIndex, weight * item.Weight, depth + 1);
            }
            return;
        }
        if (source.MorphType == PmxMorphType.Flip)
        {
            foreach (PmxMorph.FlipMorph item in source.FlipMorphs)
            {
                PropagateMorph(item.MorphIndex, weight * item.Weight, depth + 1);
            }
            return;
        }

        _effectiveMorphWeights[index] += weight;
    }

    private void ApplyMaterialMorph(PmxMorph morph, float weight)
    {
        foreach (PmxMorph.MaterialMorph item in morph.MaterialMorphs)
        {
            if (item.MaterialIndex < 0)
            {
                for (int i = 0; i < _materialStates.Length; i++)
                {
                    _materialStates[i] = _materialStates[i].Apply(item, weight);
                }
            }
            else if ((uint)item.MaterialIndex < (uint)_materialStates.Length)
            {
                _materialStates[item.MaterialIndex] = _materialStates[item.MaterialIndex].Apply(item, weight);
            }
        }
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
        if (!ik.Enabled || ik.TargetIndex < 0 || ik.TargetIndex >= _globals.Length || ik.Links.Length == 0)
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
                Quaternion rotation = Quaternion.Normalize(delta * poses[link].Rotation);
                if (ik.Links[linkIndex].EnableLimit)
                {
                    rotation = ClampIkRotation(rotation, ik.Links[linkIndex]);
                }
                poses[link] = poses[link] with { Rotation = rotation };
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
        _physicsBridge?.Dispose();
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

    private BonePose GetGlobalAppendPose(int boneIndex)
    {
        Matrix4x4 deformation = _bones[boneIndex].InverseBind * _globals[boneIndex];
        return Matrix4x4.Decompose(deformation, out _, out Quaternion rotation, out Vector3 translation)
            ? new BonePose(translation, Quaternion.Normalize(rotation))
            : BonePose.Identity;
    }

    private static BonePose ApplyBoneAxisConstraints(BoneState bone, BonePose pose)
    {
        Quaternion rotation = pose.Rotation;
        if (bone.FixedAxis && bone.FixedAxisVector.LengthSquared() > 1e-8f)
        {
            Vector3 axis = Vector3.Normalize(bone.FixedAxisVector);
            Vector3 vector = new(rotation.X, rotation.Y, rotation.Z);
            Vector3 projected = axis * Vector3.Dot(vector, axis);
            Quaternion twist = new(projected, rotation.W);
            rotation = twist.LengthSquared() > 1e-8f ? Quaternion.Normalize(twist) : Quaternion.Identity;
        }
        if (bone.LocalAxis && bone.LocalAxisX.LengthSquared() > 1e-8f && bone.LocalAxisZ.LengthSquared() > 1e-8f)
        {
            Vector3 x = Vector3.Normalize(bone.LocalAxisX);
            Vector3 z = Vector3.Normalize(bone.LocalAxisZ);
            Vector3 y = NormalizeOrDefault(Vector3.Cross(z, x), Vector3.UnitY);
            z = NormalizeOrDefault(Vector3.Cross(x, y), z);
            Matrix4x4 basis = new(
                x.X, x.Y, x.Z, 0.0f,
                y.X, y.Y, y.Z, 0.0f,
                z.X, z.Y, z.Z, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f);
            Matrix4x4 localRotation = basis * Matrix4x4.CreateFromQuaternion(rotation) * InvertOrIdentity(basis);
            rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(localRotation));
        }
        return pose with { Rotation = rotation };
    }

    private static Quaternion ClampIkRotation(Quaternion rotation, PmxBone.IKLink link)
    {
        Vector3 angles = QuaternionToEuler(rotation);
        Vector3 minimum = new(-link.LimitMax.X, -link.LimitMax.Y, link.LimitMin.Z);
        Vector3 maximum = new(-link.LimitMin.X, -link.LimitMin.Y, link.LimitMax.Z);
        Vector3 clamped = Vector3.Clamp(angles, Vector3.Min(minimum, maximum), Vector3.Max(minimum, maximum));
        return Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(clamped.Y, clamped.X, clamped.Z));
    }

    private static Vector3 QuaternionToEuler(Quaternion value)
    {
        Matrix4x4 matrix = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(value));
        float pitch = MathF.Asin(Math.Clamp(-matrix.M32, -1.0f, 1.0f));
        if (MathF.Abs(MathF.Cos(pitch)) > 1e-5f)
        {
            return new Vector3(pitch, MathF.Atan2(matrix.M31, matrix.M33), MathF.Atan2(matrix.M12, matrix.M22));
        }
        return new Vector3(pitch, MathF.Atan2(-matrix.M13, matrix.M11), 0.0f);
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
        if (vertex.WeightType == PmxVertexWeight.QDEF)
        {
            return SkinQdef(vertex, position, normal);
        }

        Matrix4x4 transform = vertex.WeightType switch
        {
            PmxVertexWeight.BDEF1 => m0,
            PmxVertexWeight.BDEF2 => m0 * vertex.BoneWeights[0] + m1 * (1.0f - vertex.BoneWeights[0]),
            PmxVertexWeight.BDEF4 => m0 * vertex.BoneWeights[0] + m1 * vertex.BoneWeights[1] + GetSkinTransform(vertex.BoneIndices[2]) * vertex.BoneWeights[2] + GetSkinTransform(vertex.BoneIndices[3]) * vertex.BoneWeights[3],
            _ => m0
        };
        return (Vector3.Transform(position, transform), NormalizeOrDefault(Vector3.TransformNormal(normal, transform), normal));
    }

    private (Vector3 Position, Vector3 Normal) SkinQdef(PmxVertex vertex, Vector3 position, Vector3 normal)
    {
        Quaternion real = new(0.0f, 0.0f, 0.0f, 0.0f);
        Quaternion dual = new(0.0f, 0.0f, 0.0f, 0.0f);
        Quaternion reference = Quaternion.Identity;
        bool hasReference = false;
        for (int i = 0; i < 4; i++)
        {
            float weight = vertex.BoneWeights[i];
            if (weight <= 0.0f || !IsValidBone(vertex.BoneIndices[i]))
            {
                continue;
            }

            Matrix4x4 matrix = GetSkinTransform(vertex.BoneIndices[i]);
            if (!Matrix4x4.Decompose(matrix, out _, out Quaternion rotation, out Vector3 translation))
            {
                continue;
            }
            rotation = Quaternion.Normalize(rotation);
            if (!hasReference)
            {
                reference = rotation;
                hasReference = true;
            }
            else if (Quaternion.Dot(reference, rotation) < 0.0f)
            {
                rotation = Negate(rotation);
            }

            Quaternion translationQuaternion = new(translation, 0.0f);
            Quaternion boneDual = Scale(translationQuaternion * rotation, 0.5f);
            real = Add(real, Scale(rotation, weight));
            dual = Add(dual, Scale(boneDual, weight));
        }

        float length = MathF.Sqrt(Quaternion.Dot(real, real));
        if (length <= 1e-8f)
        {
            return (position, normal);
        }
        real = Scale(real, 1.0f / length);
        dual = Scale(dual, 1.0f / length);
        Quaternion translationResult = Scale(dual * Quaternion.Conjugate(real), 2.0f);
        Vector3 translationVector = new(translationResult.X, translationResult.Y, translationResult.Z);
        return (
            Vector3.Transform(position, real) + translationVector,
            NormalizeOrDefault(Vector3.Transform(normal, real), normal));
    }

    private static Quaternion Add(Quaternion left, Quaternion right) => new(
        left.X + right.X,
        left.Y + right.Y,
        left.Z + right.Z,
        left.W + right.W);

    private static Quaternion Scale(Quaternion value, float scale) => new(
        value.X * scale,
        value.Y * scale,
        value.Z * scale,
        value.W * scale);

    private static Quaternion Negate(Quaternion value) => Scale(value, -1.0f);

    private Matrix4x4 GetSkinTransform(int index) => IsValidBone(index) ? _skinTransforms[index] : Matrix4x4.Identity;
    private bool IsValidBone(int index) => index >= 0 && index < _bones.Length;
    private static Vector3 GetPosition(Matrix4x4 matrix) => new(matrix.M41, matrix.M42, matrix.M43);
    private static Vector3 FlipZ(Vector3 value) => new(value.X, value.Y, -value.Z);
    private static Vector3 NormalizeOrDefault(Vector3 value, Vector3 fallback) => value.LengthSquared() > 1e-10f ? Vector3.Normalize(value) : fallback;
    private static float GetAnimationDuration(VmdParsing animation)
    {
        uint motion = animation.Motions.Length == 0 ? 0u : animation.Motions.Max(key => key.Frame);
        uint morph = animation.Morphs.Length == 0 ? 0u : animation.Morphs.Max(key => key.Frame);
        uint ik = animation.Iks.Length == 0 ? 0u : animation.Iks.Max(key => key.Frame);
        return Math.Max(motion, Math.Max(morph, ik));
    }

    private void EvaluateIkTracks()
    {
        foreach (IkState ik in _iks)
        {
            ik.Enabled = ik.BaseEnabled;
            for (int layer = 0; layer < _ikTracks.Length; layer++)
            {
                if (_layerWeights[layer] >= 0.999f
                    && _ikTracks[layer].TryGetValue(ik.NodeIndex, out IkTrack? track))
                {
                    ik.Enabled = track.Evaluate(_layerFrames[layer]);
                }
            }
        }
    }

    private bool AdvanceMotionLayers(double timeSeconds, float entityPlaybackSpeed, bool entityLoop)
    {
        double current = Math.Max(timeSeconds, 0.0);
        float elapsed = _previousTimeSeconds < 0.0
            ? (float)current
            : (float)Math.Clamp(current - _previousTimeSeconds, 0.0, 0.1);
        _previousTimeSeconds = current;
        bool primaryLooped = false;
        for (int i = 0; i < _layerFrames.Length; i++)
        {
            if (!_layerPlaying[i])
            {
                continue;
            }
            float duration = _layerDurations[i];
            float previous = _layerFrames[i];
            float next = previous + elapsed * 30.0f * Math.Max(entityPlaybackSpeed, 0.0f) * _layerPlaybackSpeeds[i];
            bool shouldLoop = entityLoop && _layerLoops[i];
            if (duration > 0.0f)
            {
                next = shouldLoop ? next % duration : Math.Min(next, duration);
            }
            _layerFrames[i] = next;
            if (i == 0 && next < previous)
            {
                primaryLooped = true;
            }
        }
        return primaryLooped;
    }

    private int ValidateLayerIndex(int layerIndex)
    {
        return (uint)layerIndex < (uint)_layerFrames.Length
            ? layerIndex
            : throw new ArgumentOutOfRangeException(nameof(layerIndex));
    }

    private static Dictionary<int, MotionTrack> BuildBoneTracks(VmdParsing animation, Dictionary<string, int> bones)
    {
        return animation.Motions.Where(motion => bones.ContainsKey(motion.BoneName)).GroupBy(motion => bones[motion.BoneName]).ToDictionary(group => group.Key, group => new MotionTrack(group.OrderBy(item => item.Frame).Select(MotionKey.Create).ToArray()));
    }
    private static Dictionary<int, MorphTrack> BuildMorphTracks(VmdParsing animation, IReadOnlyList<PmxMorph> morphs)
    {
        Dictionary<string, int> names = morphs.Select((morph, index) => (morph.Name, index)).GroupBy(item => item.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);
        return animation.Morphs.Where(morph => names.ContainsKey(morph.BlendShapeName)).GroupBy(morph => names[morph.BlendShapeName]).ToDictionary(group => group.Key, group => new MorphTrack(group.OrderBy(item => item.Frame).ToArray()));
    }
    private static Dictionary<int, IkTrack> BuildIkTracks(VmdParsing animation, IReadOnlyDictionary<string, int> bones)
    {
        return animation.Iks
            .SelectMany(key => key.Infos.Select(info => new IkKey(key.Frame, info.Name, info.Enable)))
            .Where(key => bones.ContainsKey(key.Name))
            .GroupBy(key => bones[key.Name])
            .ToDictionary(group => group.Key, group => new IkTrack(group.OrderBy(key => key.Frame).ToArray()));
    }

    public readonly record struct MaterialState(
        Vector4 Diffuse,
        Vector3 Ambient,
        Vector3 Specular,
        float SpecularPower,
        Vector4 EdgeColor,
        float EdgeSize,
        Vector4 TextureMultiply,
        Vector4 TextureAdd,
        Vector4 SphereTextureMultiply,
        Vector4 SphereTextureAdd,
        Vector4 ToonTextureMultiply,
        Vector4 ToonTextureAdd)
    {
        public static MaterialState Default { get; } = new(
            Vector4.One,
            Vector3.One,
            Vector3.Zero,
            0.0f,
            Vector4.Zero,
            0.0f,
            Vector4.One,
            Vector4.Zero,
            Vector4.One,
            Vector4.Zero,
            Vector4.One,
            Vector4.Zero);

        public static MaterialState FromMaterial(PmxMaterial material) => new(
            material.Diffuse,
            material.Ambient,
            material.Specular,
            material.SpecularPower,
            material.EdgeColor,
            material.EdgeSize,
            Vector4.One,
            Vector4.Zero,
            Vector4.One,
            Vector4.Zero,
            Vector4.One,
            Vector4.Zero);

        public MaterialState Apply(PmxMorph.MaterialMorph morph, float weight)
        {
            if (morph.OpType == PmxOpType.Mul)
            {
                return new MaterialState(
                    Diffuse * Vector4.Lerp(Vector4.One, morph.Diffuse, weight),
                    Ambient * Vector3.Lerp(Vector3.One, morph.Ambient, weight),
                    Specular * Vector3.Lerp(Vector3.One, morph.Specular, weight),
                    SpecularPower * float.Lerp(1.0f, morph.SpecularPower, weight),
                    EdgeColor * Vector4.Lerp(Vector4.One, morph.EdgeColor, weight),
                    EdgeSize * float.Lerp(1.0f, morph.EdgeSize, weight),
                    TextureMultiply * Vector4.Lerp(Vector4.One, morph.TextureCoefficient, weight),
                    TextureAdd,
                    SphereTextureMultiply * Vector4.Lerp(Vector4.One, morph.SphereTextureCoefficient, weight),
                    SphereTextureAdd,
                    ToonTextureMultiply * Vector4.Lerp(Vector4.One, morph.ToonTextureCoefficient, weight),
                    ToonTextureAdd);
            }

            return new MaterialState(
                Diffuse + morph.Diffuse * weight,
                Ambient + morph.Ambient * weight,
                Specular + morph.Specular * weight,
                SpecularPower + morph.SpecularPower * weight,
                EdgeColor + morph.EdgeColor * weight,
                EdgeSize + morph.EdgeSize * weight,
                TextureMultiply,
                TextureAdd + morph.TextureCoefficient * weight,
                SphereTextureMultiply,
                SphereTextureAdd + morph.SphereTextureCoefficient * weight,
                ToonTextureMultiply,
                ToonTextureAdd + morph.ToonTextureCoefficient * weight);
        }
    }

    private sealed class MorphState(PmxMorph source) { public PmxMorph Source { get; } = source; public float Weight { get; set; } }
    private readonly record struct BoneState(
        int ParentIndex,
        Vector3 RestTranslation,
        Matrix4x4 InverseBind,
        int AppendIndex,
        float AppendWeight,
        bool AppendRotate,
        bool AppendTranslate,
        bool AppendLocal,
        bool FixedAxis,
        Vector3 FixedAxisVector,
        bool LocalAxis,
        Vector3 LocalAxisX,
        Vector3 LocalAxisZ,
        bool Enabled,
        int TargetIndex,
        int Iterations,
        float Limit,
        PmxBone.IKLink[] Links);
    private sealed class IkState(int nodeIndex, BoneState bone) { public int NodeIndex { get; } = nodeIndex; public int TargetIndex { get; } = bone.TargetIndex; public int Iterations { get; } = bone.Iterations; public float Limit { get; } = bone.Limit; public PmxBone.IKLink[] Links { get; } = bone.Links; public bool BaseEnabled { get; } = bone.Enabled; public bool Enabled { get; set; } = bone.Enabled; }
    private sealed class PhysicsState(int boneIndex, PmxOperation operation, float damping, float mass) { public int BoneIndex { get; } = boneIndex; public PmxOperation Operation { get; } = operation; public float Damping { get; } = damping; public float Mass { get; } = mass; public Vector3 Offset { get; set; } public Vector3 Velocity { get; set; } }
    private readonly record struct BonePose(Vector3 Translation, Quaternion Rotation) { public static BonePose Identity => new(Vector3.Zero, Quaternion.Identity); }
    private sealed class MotionTrack(MotionKey[] keys) { public MotionKey[] Keys { get; } = keys; public BonePose Evaluate(float frame) => EvaluateKey(Keys, frame); }
    private sealed class MorphTrack(VmdMorph[] keys) { public float Evaluate(float frame, bool loop) { if (keys.Length == 0) return 0; if (frame <= keys[0].Frame) return keys[0].Weight; if (frame >= keys[^1].Frame) return keys[^1].Weight; int upper = Array.FindIndex(keys, key => key.Frame >= frame); VmdMorph a = keys[upper - 1], b = keys[upper]; return float.Lerp(a.Weight, b.Weight, (frame - a.Frame) / Math.Max(1.0f, b.Frame - a.Frame)); } }
    private readonly record struct IkKey(float Frame, string Name, bool Enabled);
    private sealed class IkTrack(IkKey[] keys) { public bool Evaluate(float frame) { if (keys.Length == 0) return true; int upper = Array.FindIndex(keys, key => key.Frame > frame); return upper <= 0 ? keys[0].Enabled : keys[upper - 1].Enabled; } }
    private readonly record struct MotionKey(float Frame, BonePose Pose, VmdInterpolationCurve X, VmdInterpolationCurve Y, VmdInterpolationCurve Z, VmdInterpolationCurve R)
    {
        public static MotionKey Create(VmdMotion motion) { Matrix4x4 flip = Matrix4x4.CreateScale(1.0f, 1.0f, -1.0f); Quaternion rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(flip * Matrix4x4.CreateFromQuaternion(motion.Quaternion) * flip)); return new(motion.Frame, new BonePose(FlipZ(motion.Translate), rotation), VmdInterpolationCurve.FromVmd(motion.Interpolation, 0), VmdInterpolationCurve.FromVmd(motion.Interpolation, 1), VmdInterpolationCurve.FromVmd(motion.Interpolation, 2), VmdInterpolationCurve.FromVmd(motion.Interpolation, 3)); }
    }
    private static BonePose EvaluateKey(MotionKey[] keys, float frame)
    {
        if (keys.Length == 0) return BonePose.Identity; if (frame <= keys[0].Frame) return keys[0].Pose; if (frame >= keys[^1].Frame) return keys[^1].Pose; int upper = Array.FindIndex(keys, key => key.Frame >= frame); MotionKey a = keys[upper - 1], b = keys[upper]; float t = (frame - a.Frame) / Math.Max(1.0f, b.Frame - a.Frame); return new BonePose(new Vector3(float.Lerp(a.Pose.Translation.X, b.Pose.Translation.X, b.X.Evaluate(t)), float.Lerp(a.Pose.Translation.Y, b.Pose.Translation.Y, b.Y.Evaluate(t)), float.Lerp(a.Pose.Translation.Z, b.Pose.Translation.Z, b.Z.Evaluate(t))), Quaternion.Slerp(a.Pose.Rotation, b.Pose.Rotation, b.R.Evaluate(t)));
    }
}
