using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebPmxMaterial(
    int StartIndex,
    int IndexCount,
    Vector4 Diffuse,
    Vector3 Ambient,
    Vector4 EdgeColor,
    float EdgeSize,
    bool DoubleSided,
    bool EnableEdge,
    bool EnableShadow,
    bool ReceiveShadow,
    string? TextureUri,
    string Name);

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

    private WebPmxAsset(
        GameEntity entity,
        PmxParsing parsing,
        PmxPoseEvaluator pose,
        WebPmxMesh mesh,
        float[] skinnedVertices,
        Func<string, VmdParsing?> animationLoader)
    {
        _entity = entity;
        _parsing = parsing;
        _pose = pose;
        Mesh = mesh;
        _skinnedVertices = skinnedVertices;
        _animationLoader = animationLoader;
    }

    public GameEntity Entity => _entity;
    public PmxParsing Parsing => _parsing;
    public PmxPoseEvaluator Pose => _pose;
    public WebPmxMesh Mesh { get; }
    public bool HasAnimation => _pose.HasAnimation;
    public string PhysicsBackend => _pose.PhysicsBackend;
    public IReadOnlyList<Vector3> PhysicsColliderPoints => _pose.PhysicsColliderPoints;

    public static WebPmxAsset Create(
        GameEntity entity,
        PmxParsing parsing,
        IReadOnlyList<(VmdParsing Animation, float Weight)> layers,
        WebPmxMesh? mesh = null,
        Func<string, VmdParsing?>? animationLoader = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(parsing);
        ArgumentNullException.ThrowIfNull(layers);

        PmxPoseEvaluator pose = new(
            parsing,
            layers,
            entity.EnablePhysics,
            entity.PhysicsGravity,
            entity.ResetPhysicsOnMotionLoop);
        ApplyPlaybackSettings(pose, entity);
        float[] vertices = new float[parsing.Vertices.Length * 8];
        pose.Update(0.0, entity.PlaybackSpeed, entity.LoopMotion, vertices, 1.0f / 30.0f);
        mesh ??= WebPmxMesh.FromParsing(parsing);
        mesh.UpdateFromInterleaved(vertices);
        return new WebPmxAsset(entity, parsing, pose, mesh, vertices, animationLoader ?? (_ => null));
    }

    public bool ApplyMotion(string motionPath)
    {
        string normalized = NormalizeMotionPath(motionPath);
        VmdParsing? animation = _animationLoader(normalized);
        if (animation is null)
        {
            return false;
        }

        ReplacePose([(animation, 1.0f)]);
        _entity.MotionLayers = [new MotionLayerSettings { Path = normalized, Weight = 1.0f }];
        _entity.IsPlaying = true;
        return true;
    }

    public void PlayMotion(bool restart)
    {
        if (restart)
        {
            for (int i = 0; i < Pose.MotionLayerCount; i++) Pose.SetMotionLayerFrame(i, 0.0f);
        }

        for (int i = 0; i < Pose.MotionLayerCount; i++) Pose.SetMotionLayerPlaying(i, true);
        _entity.IsPlaying = true;
    }

    public void PauseMotion()
    {
        for (int i = 0; i < Pose.MotionLayerCount; i++) Pose.SetMotionLayerPlaying(i, false);
        _entity.IsPlaying = false;
    }

    public void StopMotion()
    {
        for (int i = 0; i < Pose.MotionLayerCount; i++)
        {
            Pose.SetMotionLayerFrame(i, 0.0f);
            Pose.SetMotionLayerPlaying(i, false);
        }

        _entity.IsPlaying = false;
    }

    public bool Update(double timeSeconds, float deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Pose.RequiresUpdate)
        {
            return false;
        }

        ApplyPlaybackSettings(Pose, _entity);
        Pose.Update(
            timeSeconds,
            _entity.PlaybackSpeed,
            _entity.LoopMotion,
            _skinnedVertices,
            Math.Clamp(deltaSeconds, 0.0f, 1.0f / 15.0f));
        Mesh.UpdateFromInterleaved(_skinnedVertices);
        return true;
    }

    private static void ApplyPlaybackSettings(PmxPoseEvaluator pose, GameEntity entity)
    {
        for (int layer = 0; layer < pose.MotionLayerCount; layer++)
        {
            pose.SetMotionLayerPlaying(layer, entity.IsPlaying);
            pose.SetMotionLayerLoop(layer, entity.LoopMotion);
            pose.SetMotionLayerPlaybackSpeed(layer, entity.PlaybackSpeed);
        }
    }

    private void ReplacePose(IReadOnlyList<(VmdParsing Animation, float Weight)> layers)
    {
        _pose.Dispose();
        _pose = new PmxPoseEvaluator(
            _parsing,
            layers,
            _entity.EnablePhysics,
            _entity.PhysicsGravity,
            _entity.ResetPhysicsOnMotionLoop);
        ApplyPlaybackSettings(_pose, _entity);
        _pose.Update(0.0, _entity.PlaybackSpeed, _entity.LoopMotion, _skinnedVertices, 1.0f / 30.0f);
        Mesh.UpdateFromInterleaved(_skinnedVertices);
    }

    private static string NormalizeMotionPath(string path)
        => (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');

    public void ResetPhysics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Pose.ResetPhysics();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pose.Dispose();
    }
}


public sealed record WebPmxMeshInstance(string Id, WebPmxMesh Mesh);

public sealed record WebPmxMeshUpdate(float[] Positions, float[] Normals, float[] Uvs);

public sealed record WebPmxMeshUpdateInstance(string Id, WebPmxMeshUpdate Mesh);

public sealed class WebPmxMesh
{
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
    public WebPmxMeshUpdate CreateUpdate() => new(Positions, Normals, Uvs);

    public void UpdateFromInterleaved(ReadOnlySpan<float> vertices)
    {
        int vertexCount = Positions.Length / 3;
        if (vertices.Length < vertexCount * 8)
        {
            throw new ArgumentException("The PMX vertex buffer is too small.", nameof(vertices));
        }

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
                material.DrawMode.HasFlag(PmxDrawModeFlags.GroundShadow)
                    || material.DrawMode.HasFlag(PmxDrawModeFlags.CastSelfShadow),
                material.DrawMode.HasFlag(PmxDrawModeFlags.RecieveSelfShadow),
                textureUri,
                string.IsNullOrWhiteSpace(material.Name) ? $"Material {i}" : material.Name));
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




