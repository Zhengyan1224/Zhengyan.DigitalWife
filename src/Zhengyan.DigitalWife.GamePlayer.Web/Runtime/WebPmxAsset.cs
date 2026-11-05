using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebPmxMaterial(
    int StartIndex,
    int IndexCount,
    [property: JsonIgnore] Vector4 Diffuse,
    [property: JsonIgnore] Vector3 Ambient,
    [property: JsonIgnore] Vector4 EdgeColor,
    float EdgeSize,
    bool DoubleSided,
    bool EnableEdge,
    bool EnableShadow,
    bool ReceiveShadow,
    string? TextureUri,
    string Name)
{
    [JsonIgnore] public Vector3 Specular { get; init; }
    public float SpecularPower { get; init; }

    // JS interop does not serialize Numerics' public vector fields. Transfer
    // explicit arrays so material colors cannot silently become {}.
    [JsonPropertyName("diffuse")] public float[] DiffuseComponents => [Diffuse.X, Diffuse.Y, Diffuse.Z, Diffuse.W];
    [JsonPropertyName("ambient")] public float[] AmbientComponents => [Ambient.X, Ambient.Y, Ambient.Z];
    [JsonPropertyName("edgeColor")] public float[] EdgeComponents => [EdgeColor.X, EdgeColor.Y, EdgeColor.Z, EdgeColor.W];
    [JsonPropertyName("specular")] public float[] SpecularComponents => [Specular.X, Specular.Y, Specular.Z];
}

/// <summary>
/// A PMX asset together with its shared CPU pose evaluator. The evaluator is
/// intentionally kept in the platform-neutral Mmd.Core project so PC,
/// Android and Web consume the same morph, IK, append-bone and skinning code.
/// </summary>
public sealed class WebPmxAsset : IDisposable
{
    private readonly GameEntity _entity;
    private readonly PmxParsing _parsing;
    private PmxPoseEvaluator _pose;
    private readonly Func<string, VmdParsing?> _animationLoader;
    private readonly float[] _skinnedVertices;
    private bool _disposed;
    private bool _poseDirty;
    private bool _wasPlaying;
    private bool _skipPhysicsOnNextPlayFrame = true;
    private readonly bool _gpuSkinning;
    private readonly Dictionary<string, PmxPoseEvaluator> _preparedMotions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<PmxPoseEvaluator> _ownedPoses = [];
    private readonly Vector3[] _publishedMorphs;
    private readonly int[] _morphVertices;
    private int _publishedMorphRevision = -1;

    private WebPmxAsset(
        GameEntity entity,
        PmxParsing parsing,
        PmxPoseEvaluator pose,
        WebPmxMesh mesh,
        float[] skinnedVertices,
        Func<string, VmdParsing?> animationLoader,
        bool gpuSkinning)
    {
        _entity = entity;
        _parsing = parsing;
        _pose = pose;
        Mesh = mesh;
        _skinnedVertices = skinnedVertices;
        _animationLoader = animationLoader;
        _wasPlaying = entity.IsPlaying && pose.MotionLayerCount > 0;
        _gpuSkinning = gpuSkinning;
        _ownedPoses.Add(pose);
        _publishedMorphs = gpuSkinning ? new Vector3[parsing.Vertices.Length] : [];
        _morphVertices = gpuSkinning ? parsing.Morphs.SelectMany(morph => morph.PositionMorphs)
            .Select(morph => morph.VertexIndex).Where(index => index >= 0 && index < parsing.Vertices.Length).Distinct().ToArray() : [];
    }

    public GameEntity Entity => _entity;
    public PmxParsing Parsing => _parsing;
    public PmxPoseEvaluator Pose => _pose;
    public WebPmxMesh Mesh { get; }
    public bool HasAnimation => _pose.HasAnimation;
    public string PhysicsBackend => _pose.PhysicsBackend;
    public IReadOnlyList<Vector3> PhysicsColliderPoints => _pose.PhysicsColliderPoints;
    public bool GpuSkinning => _gpuSkinning;

    public static WebPmxAsset Create(
        GameEntity entity,
        PmxParsing parsing,
        IReadOnlyList<(VmdParsing Animation, float Weight)> layers,
        WebPmxMesh? mesh = null,
        Func<string, VmdParsing?>? animationLoader = null,
        bool preferGpuSkinning = false)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(parsing);
        ArgumentNullException.ThrowIfNull(layers);

        PmxPoseEvaluator pose = new(
            parsing,
            layers,
            physicsEnabled: true,
            entity.PhysicsGravity,
            entity.ResetPhysicsOnMotionLoop);
        ApplyPlaybackSettings(pose, entity);
        pose.UsePartialIkUpdates = true;
        pose.PhysicsSimulationEnabled = false;
        bool gpuSkinning = preferGpuSkinning && parsing.Bones.Length is > 0 and <= 2048 && parsing.Vertices.Length <= 524288
            && parsing.Vertices.All(vertex => vertex.WeightType is PmxVertexWeight.BDEF1 or PmxVertexWeight.BDEF2 or PmxVertexWeight.BDEF4)
            && !parsing.Morphs.Any(morph => morph.MorphType is PmxMorphType.UV or PmxMorphType.AddUV1 or PmxMorphType.AddUV2 or PmxMorphType.AddUV3 or PmxMorphType.AddUV4);
        float[] vertices = gpuSkinning ? [] : new float[parsing.Vertices.Length * 8];
        pose.Update(0.0, entity.PlaybackSpeed, entity.LoopMotion, vertices, 1.0f / 30.0f, skinVertices: !gpuSkinning);
        mesh ??= WebPmxMesh.FromParsing(parsing);
        if (!gpuSkinning) mesh.UpdateFromInterleaved(vertices);
        else mesh.Skinning = WebPmxSkinning.FromParsing(parsing, pose);
        WebPmxAsset asset = new(entity, parsing, pose, mesh, vertices, animationLoader ?? (_ => null), gpuSkinning);
        if (gpuSkinning) asset.UpdateGpuPose();
        return asset;
    }

    public bool PrepareMotion(string motionPath)
    {
        string path = NormalizeMotionPath(motionPath);
        if (_preparedMotions.ContainsKey(path)) return true;
        VmdParsing? animation = _animationLoader(path);
        if (animation is null) return false;
        PmxPoseEvaluator prepared = new(_parsing, [(animation, 1.0f)], true, _entity.PhysicsGravity, _entity.ResetPhysicsOnMotionLoop)
        { PhysicsSimulationEnabled = false, UsePartialIkUpdates = true };
        _preparedMotions.Add(path, prepared);
        _ownedPoses.Add(prepared);
        return true;
    }

    public bool ApplyMotion(string motionPath)
    {
        string normalized = NormalizeMotionPath(motionPath);
        if (!PrepareMotion(normalized)) return false;
        _pose = _preparedMotions[normalized];
        _pose.ResetPhysics();
        for (int i = 0; i < _pose.MotionLayerCount; i++) _pose.SetMotionLayerFrame(i, 0);
        _poseDirty = true;
        _wasPlaying = false;
        _publishedMorphRevision = -1;
        _entity.MotionLayers = [new MotionLayerSettings { Path = normalized, Weight = 1.0f }];
        _entity.IsPlaying = true;
        _skipPhysicsOnNextPlayFrame = true;
        return true;
    }

    public void PlayMotion(bool restart)
    {
        bool starting = !_entity.IsPlaying;
        if (restart)
        {
            for (int i = 0; i < Pose.MotionLayerCount; i++) Pose.SetMotionLayerFrame(i, 0.0f);
        }

        for (int i = 0; i < Pose.MotionLayerCount; i++) Pose.SetMotionLayerPlaying(i, true);
        _entity.IsPlaying = true;
        if (starting || restart) _skipPhysicsOnNextPlayFrame = true;
        if (restart) _poseDirty = true;
    }

    public void PauseMotion()
    {
        for (int i = 0; i < Pose.MotionLayerCount; i++) Pose.SetMotionLayerPlaying(i, false);
        _entity.IsPlaying = false;
        _wasPlaying = false;
        _skipPhysicsOnNextPlayFrame = true;
        Pose.PhysicsSimulationEnabled = false;
    }

    public void StopMotion()
    {
        for (int i = 0; i < Pose.MotionLayerCount; i++)
        {
            Pose.SetMotionLayerFrame(i, 0.0f);
            Pose.SetMotionLayerPlaying(i, false);
        }

        _entity.IsPlaying = false;
        _poseDirty = true;
        _skipPhysicsOnNextPlayFrame = true;
        Pose.ResetPhysics();
    }

    public bool Update(double timeSeconds, float deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ApplyPlaybackSettings(Pose, _entity);
        bool playing = _entity.IsPlaying && Pose.MotionLayerCount > 0;
        if (playing && !_wasPlaying)
        {
            Pose.SynchronizeTime(timeSeconds);
            _skipPhysicsOnNextPlayFrame = true;
        }
        _wasPlaying = playing;
        Pose.PhysicsSimulationEnabled = playing && _entity.EnablePhysics && !_skipPhysicsOnNextPlayFrame;
        if (!_poseDirty && !playing)
        {
            return false;
        }

        if (_skipPhysicsOnNextPlayFrame) Pose.SynchronizeTime(timeSeconds);
        Pose.Update(
            timeSeconds,
            _entity.PlaybackSpeed,
            _entity.LoopMotion,
            _skinnedVertices,
            Math.Clamp(deltaSeconds, 0.0f, 1.0f / 15.0f), skinVertices: !_gpuSkinning);
        if (_gpuSkinning) UpdateGpuPose();
        else Mesh.UpdateFromInterleaved(_skinnedVertices);
        _poseDirty = false;
        _skipPhysicsOnNextPlayFrame = false;
        return true;
    }

    private static void ApplyPlaybackSettings(PmxPoseEvaluator pose, GameEntity entity)
    {
        for (int layer = 0; layer < pose.MotionLayerCount; layer++)
        {
            pose.SetMotionLayerPlaying(layer, entity.IsPlaying);
            pose.SetMotionLayerLoop(layer, entity.LoopMotion);
            // Update already supplies the entity speed; a layer multiplier
            // of the same value would apply it twice.
            pose.SetMotionLayerPlaybackSpeed(layer, 1.0f);
        }
    }

    private void UpdateGpuPose()
    {
        byte[]? morphBytes = null;
        if (_publishedMorphRevision != _pose.VertexMorphRevision)
        {
            List<float> changes = [];
            ReadOnlySpan<Vector3> offsets = _pose.VertexMorphOffsets;
            foreach (int vertex in _morphVertices)
            {
                Vector3 offset = offsets[vertex];
                if (_publishedMorphs[vertex] == offset) continue;
                changes.Add(vertex); changes.Add(offset.X); changes.Add(offset.Y); changes.Add(offset.Z);
                _publishedMorphs[vertex] = offset;
            }
            if (changes.Count > 0) morphBytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(changes)).ToArray();
            _publishedMorphRevision = _pose.VertexMorphRevision;
        }
        Mesh.UpdateSkinning(MemoryMarshal.AsBytes(_pose.SkinTransforms).ToArray(), morphBytes);
    }

    private static string NormalizeMotionPath(string path)
        => (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');

    public void ResetPhysics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Pose.ResetPhysics();
        _poseDirty = true;
        _skipPhysicsOnNextPlayFrame = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (PmxPoseEvaluator pose in _ownedPoses) pose.Dispose();
    }
}


public sealed record WebPmxMeshInstance(string Id, WebPmxMesh Mesh);

// Blazor transfers byte[] through its binary interop path. Serializing three
// large float arrays as JSON on every frame stalls the browser's main thread.
public sealed record WebPmxMeshUpdate(byte[] Vertices)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public byte[]? Bones { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public byte[]? Morphs { get; init; }
}

public sealed record WebPmxSkinning(int BoneCount, byte[] Bindings, byte[] Bones, byte[]? Morphs = null)
{
    public static WebPmxSkinning FromParsing(PmxParsing parsing, PmxPoseEvaluator pose)
    {
        float[] bindings = new float[parsing.Vertices.Length * 8];
        for (int i = 0; i < parsing.Vertices.Length; i++)
        {
            PmxVertex vertex = parsing.Vertices[i];
            for (int bone = 0; bone < 4; bone++)
            {
                bindings[i * 8 + bone] = vertex.BoneIndices[bone];
                bindings[i * 8 + 4 + bone] = vertex.BoneWeights[bone];
            }
            if (vertex.WeightType == PmxVertexWeight.BDEF1) { bindings[i*8+4] = 1; for (int b=1;b<4;b++) bindings[i*8+4+b] = 0; }
            if (vertex.WeightType == PmxVertexWeight.BDEF2) { bindings[i*8+5] = 1 - bindings[i*8+4]; bindings[i*8+6] = bindings[i*8+7] = 0; }
        }
        return new(parsing.Bones.Length, MemoryMarshal.AsBytes(bindings.AsSpan()).ToArray(), MemoryMarshal.AsBytes(pose.SkinTransforms).ToArray());
    }
}

public sealed record WebPmxMeshUpdateInstance(string Id, WebPmxMeshUpdate Mesh);

public sealed class WebPmxMesh
{
    private float[]? _interleavedVertices;
    private WebPmxMeshUpdate? _skinningUpdate;
    public WebPmxSkinning? Skinning { get; internal set; }
    public void UpdateSkinning(byte[] bones, byte[]? morphs)
    {
        Skinning = Skinning! with { Bones = bones, Morphs = morphs };
        _skinningUpdate = new([]) { Bones = bones, Morphs = morphs };
    }

    public WebPmxMesh(
        float[] positions,
        float[] normals,
        float[] uvs,
        uint[] indices,
        Vector3 boundsMin,
        Vector3 boundsMax,
        string modelName,
        IReadOnlyList<WebPmxMaterial>? materials = null)
    {
        Positions = positions ?? throw new ArgumentNullException(nameof(positions));
        Normals = normals ?? throw new ArgumentNullException(nameof(normals));
        Uvs = uvs ?? throw new ArgumentNullException(nameof(uvs));
        Indices = indices ?? throw new ArgumentNullException(nameof(indices));
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        ModelName = modelName ?? string.Empty;
        Materials = materials ?? [];
    }

    public float[] Positions { get; }
    public float[] Normals { get; }
    public float[] Uvs { get; }
    public uint[] Indices { get; }
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public string ModelName { get; }
    public IReadOnlyList<WebPmxMaterial> Materials { get; }
    public WebPmxMeshUpdate CreateUpdate()
    {
        if (_skinningUpdate is not null) return _skinningUpdate;
        if (_interleavedVertices is null)
        {
            int vertexCount = Positions.Length / 3;
            _interleavedVertices = new float[vertexCount * 8];
            for (int i = 0; i < vertexCount; i++)
            {
                Positions.AsSpan(i * 3, 3).CopyTo(_interleavedVertices.AsSpan(i * 8, 3));
                Normals.AsSpan(i * 3, 3).CopyTo(_interleavedVertices.AsSpan(i * 8 + 3, 3));
                Uvs.AsSpan(i * 2, 2).CopyTo(_interleavedVertices.AsSpan(i * 8 + 6, 2));
            }
        }
        return new(MemoryMarshal.AsBytes(_interleavedVertices.AsSpan()).ToArray());
    }

    public void UpdateFromInterleaved(ReadOnlySpan<float> vertices)
    {
        int vertexCount = Positions.Length / 3;
        if (vertices.Length < vertexCount * 8)
        {
            throw new ArgumentException("The PMX vertex buffer is too small.", nameof(vertices));
        }

        _interleavedVertices ??= new float[vertexCount * 8];
        vertices[.._interleavedVertices.Length].CopyTo(_interleavedVertices);

        for (int i = 0; i < vertexCount; i++)
        {
            int source = i * 8;
            int position = i * 3;
            Positions[position] = vertices[source];
            Positions[position + 1] = vertices[source + 1];
            Positions[position + 2] = vertices[source + 2];
            Normals[position] = vertices[source + 3];
            Normals[position + 1] = vertices[source + 4];
            Normals[position + 2] = vertices[source + 5];
            int uv = i * 2;
            Uvs[uv] = vertices[source + 6];
            Uvs[uv + 1] = 1.0f - vertices[source + 7];
            _interleavedVertices[source + 7] = Uvs[uv + 1];
        }
    }

    public static WebPmxMesh FromParsing(
        PmxParsing parsing,
        string? assetPath = null,
        Func<string, string?>? textureUriResolver = null)
    {
        ArgumentNullException.ThrowIfNull(parsing);
        float[] positions = new float[parsing.Vertices.Length * 3];
        float[] normals = new float[parsing.Vertices.Length * 3];
        float[] uvs = new float[parsing.Vertices.Length * 2];
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        for (int i = 0; i < parsing.Vertices.Length; i++)
        {
            PmxVertex vertex = parsing.Vertices[i];
            Vector3 position = new(vertex.Position.X, vertex.Position.Y, -vertex.Position.Z);
            Vector3 normal = new(vertex.Normal.X, vertex.Normal.Y, -vertex.Normal.Z);
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
            int positionOffset = i * 3;
            positions[positionOffset] = position.X;
            positions[positionOffset + 1] = position.Y;
            positions[positionOffset + 2] = position.Z;
            normals[positionOffset] = normal.X;
            normals[positionOffset + 1] = normal.Y;
            normals[positionOffset + 2] = normal.Z;
            int uvOffset = i * 2;
            uvs[uvOffset] = vertex.UV.X;
            uvs[uvOffset + 1] = 1.0f - vertex.UV.Y;
        }

        uint[] indices = new uint[parsing.Faces.Length * 3];
        for (int i = 0; i < parsing.Faces.Length; i++)
        {
            PmxFace face = parsing.Faces[i];
            indices[i * 3] = face.Vertices[2];
            indices[i * 3 + 1] = face.Vertices[1];
            indices[i * 3 + 2] = face.Vertices[0];
        }

        string directory = string.Empty;
        if (!string.IsNullOrWhiteSpace(assetPath))
        {
            directory = Path.GetDirectoryName(assetPath.Replace('\\', '/'))?.Replace('\\', '/') ?? string.Empty;
        }

        List<WebPmxMaterial> materials = [];
        int start = 0;
        for (int i = 0; i < parsing.Materials.Length; i++)
        {
            PmxMaterial material = parsing.Materials[i];
            int count = Math.Clamp(material.FaceVerticesCount, 0, indices.Length - start);
            string? textureUri = null;
            if (material.TextureIndex >= 0 && material.TextureIndex < parsing.Textures.Length)
            {
                string texturePath = NormalizeTexturePath(directory, parsing.Textures[material.TextureIndex].Name);
                textureUri = textureUriResolver?.Invoke(texturePath);
            }

            materials.Add(new WebPmxMaterial(
                start,
                count,
                material.Diffuse,
                material.Ambient,
                material.EdgeColor,
                material.EdgeSize,
                material.DrawMode.HasFlag(PmxDrawModeFlags.BothFace),
                material.DrawMode.HasFlag(PmxDrawModeFlags.DrawEdge),
                material.DrawMode.HasFlag(PmxDrawModeFlags.CastSelfShadow),
                material.DrawMode.HasFlag(PmxDrawModeFlags.RecieveSelfShadow),
                textureUri,
                string.IsNullOrWhiteSpace(material.Name) ? $"Material {i}" : material.Name)
            {
                Specular = material.Specular,
                SpecularPower = material.SpecularPower
            });
            start += count;
        }

        return new WebPmxMesh(
            positions,
            normals,
            uvs,
            indices,
            min == new Vector3(float.MaxValue) ? Vector3.Zero : min,
            max == new Vector3(float.MinValue) ? Vector3.Zero : max,
            parsing.Info.ModelName,
            materials);
    }

    private static string NormalizeTexturePath(string directory, string texturePath)
    {
        string normalized = texturePath.Trim().Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0) return normalized;
        string combined = string.IsNullOrEmpty(directory) ? normalized : $"{directory}/{normalized}";
        string[] parts = combined.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> output = [];
        foreach (string part in parts)
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (output.Count > 0) output.RemoveAt(output.Count - 1);
                continue;
            }
            output.Add(part);
        }
        return string.Join('/', output);
    }
}




