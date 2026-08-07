using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd;

/// <summary>Backend-neutral PMX skinning dispatch used by GPU compute implementations.</summary>
public unsafe interface IPmxSkinningCompute : IDisposable
{
    string BackendName { get; }

    bool Execute(
        int vertexCount,
        int boneCount,
        Vector3* positions,
        Vector3* normals,
        Vector2* uvs,
        VertexBoneInfo* vertexBoneInfos,
        Vector3* morphPositions,
        Vector4* morphUVs,
        Matrix4x4* updateTransforms,
        Matrix4x4* globalTransforms,
        Vector3* updatePositions,
        Vector3* updateNormals,
        Vector2* updateUVs);
}

public delegate IPmxSkinningCompute? PmxSkinningComputeFactory(int vertexCount, int boneCount);
