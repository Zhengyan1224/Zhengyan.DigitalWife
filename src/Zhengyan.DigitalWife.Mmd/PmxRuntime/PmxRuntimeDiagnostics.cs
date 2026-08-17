using System.Numerics;
using System.Text;

namespace Zhengyan.DigitalWife.Mmd;

public sealed record PmxRuntimeFeatureReport(
    int VertexCount,
    int FaceCount,
    int MaterialCount,
    int BoneCount,
    int MorphCount,
    int RigidBodyCount,
    int JointCount,
    IReadOnlyDictionary<PmxVertexWeight, int> SkinningTypes,
    IReadOnlyDictionary<PmxMorphType, int> MorphTypes,
    IReadOnlyList<string> Warnings)
{
    public bool RequiresCpuSkinning => SkinningTypes.Keys.Any(type => type is PmxVertexWeight.SDEF or PmxVertexWeight.QDEF)
        || BoneCount > 96;
}

public static class PmxRuntimeDiagnostics
{
    public static PmxRuntimeFeatureReport Analyze(PmxParsing pmx)
    {
        ArgumentNullException.ThrowIfNull(pmx);

        Dictionary<PmxVertexWeight, int> skinning = pmx.Vertices
            .GroupBy(vertex => vertex.WeightType)
            .ToDictionary(group => group.Key, group => group.Count());
        Dictionary<PmxMorphType, int> morphs = pmx.Morphs
            .GroupBy(morph => morph.MorphType)
            .ToDictionary(group => group.Key, group => group.Count());
        List<string> warnings = [];

        if (pmx.Bones.Length > 96)
        {
            warnings.Add($"Model has {pmx.Bones.Length} bones and exceeds the GLES uniform skinning limit of 96.");
        }
        if (skinning.ContainsKey(PmxVertexWeight.SDEF))
        {
            warnings.Add("Model contains SDEF vertices; GLES uses the shared CPU parity path.");
        }
        if (skinning.ContainsKey(PmxVertexWeight.QDEF))
        {
            warnings.Add("Model contains QDEF vertices; GLES uses dual-quaternion CPU skinning.");
        }
        if (morphs.ContainsKey(PmxMorphType.Impluse))
        {
            warnings.Add("Model contains impulse morphs and requires an active Bullet physics bridge.");
        }
        if (pmx.SoftBodies.Length > 0)
        {
            warnings.Add($"Model contains {pmx.SoftBodies.Length} soft bodies; PMX soft bodies are not supported by the current runtime.");
        }

        return new PmxRuntimeFeatureReport(
            pmx.Vertices.Length,
            pmx.Faces.Length,
            pmx.Materials.Length,
            pmx.Bones.Length,
            pmx.Morphs.Length,
            pmx.RigidBodies.Length,
            pmx.Joints.Length,
            skinning,
            morphs,
            warnings);
    }

    public static string FormatPoseSnapshot(
        IReadOnlyList<string> boneNames,
        ReadOnlySpan<Matrix4x4> globalTransforms,
        ReadOnlySpan<Matrix4x4> skinTransforms,
        IReadOnlyDictionary<string, float>? morphWeights = null)
    {
        if (boneNames.Count != globalTransforms.Length || boneNames.Count != skinTransforms.Length)
        {
            throw new ArgumentException("Bone names, global transforms and skin transforms must have the same length.");
        }

        StringBuilder builder = new();
        builder.AppendLine("PMX_POSE_V1");
        for (int i = 0; i < boneNames.Count; i++)
        {
            builder.Append("bone\t").Append(Escape(boneNames[i])).Append('\t');
            AppendMatrix(builder, globalTransforms[i]);
            builder.Append('\t');
            AppendMatrix(builder, skinTransforms[i]);
            builder.AppendLine();
        }

        if (morphWeights is not null)
        {
            foreach ((string name, float weight) in morphWeights.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                builder.Append("morph\t").Append(Escape(name)).Append('\t')
                    .Append(weight.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                    .AppendLine();
            }
        }
        return builder.ToString();
    }

    private static void AppendMatrix(StringBuilder builder, Matrix4x4 value)
    {
        float[] values =
        [
            value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44
        ];
        builder.AppendJoin(',', values.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
