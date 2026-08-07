using System.Text.RegularExpressions;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>
/// Validation rules for custom GLSL that can be compiled for Vulkan and later
/// translated to other explicit APIs such as Direct3D.
/// </summary>
public static partial class PortableShaderContract
{
    public const int Version = 450;

    public static void ValidatePlane(string vertexShaderPath, string fragmentShaderPath)
    {
        string vertex = ReadShader(vertexShaderPath, nameof(vertexShaderPath));
        string fragment = ReadShader(fragmentShaderPath, nameof(fragmentShaderPath));
        List<string> errors = [];

        ValidateVersion(vertex, "vertex", errors);
        ValidateVersion(fragment, "fragment", errors);
        Require(vertex, VertexInputRegex(), "vertex shader input locations", errors);
        Require(vertex, VertexOutputRegex(), "vertex shader output locations", errors);
        Require(fragment, FragmentInputRegex(), "fragment shader input locations", errors);
        Require(vertex, PlaneFrameRegex(), "set=0,binding=0 PlaneFrame uniform block", errors);
        for (int binding = 1; binding <= 6; binding++)
        {
            string pattern = $@"layout\s*\(\s*set\s*=\s*0\s*,\s*binding\s*=\s*{binding}[^)]*\)\s*(uniform\s+)?(sampler|texture)";
            if (!Regex.IsMatch(fragment, pattern))
            {
                errors.Add($"missing sampled texture/sampler resource at set=0,binding={binding}");
            }
        }

        if (errors.Count != 0)
        {
            throw new InvalidDataException(
                "The custom shader does not satisfy the portable GLSL contract:\n- "
                + string.Join("\n- ", errors));
        }
    }

    private static string ReadShader(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Shader path cannot be empty.", parameterName);
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Shader file not found.", fullPath);
        }

        return File.ReadAllText(fullPath);
    }

    private static void ValidateVersion(string source, string stage, ICollection<string> errors)
    {
        Match match = VersionRegex().Match(source);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int version) || version < Version)
        {
            errors.Add($"{stage} shader must declare #version {Version} or newer");
        }
    }

    private static void Require(string source, Regex pattern, string description, ICollection<string> errors)
    {
        if (!pattern.IsMatch(source))
        {
            errors.Add($"missing {description}");
        }
    }

    [GeneratedRegex(@"#version\s+(\d+)")]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"layout\s*\(\s*location\s*=\s*0\s*\)\s*in\s+vec3\s+in_Pos")]
    private static partial Regex VertexInputRegex();

    [GeneratedRegex(@"layout\s*\(\s*location\s*=\s*0\s*\)\s*out\s+")]
    private static partial Regex VertexOutputRegex();

    [GeneratedRegex(@"layout\s*\(\s*location\s*=\s*0\s*\)\s*in\s+")]
    private static partial Regex FragmentInputRegex();

    [GeneratedRegex(@"layout\s*\(\s*set\s*=\s*0\s*,\s*binding\s*=\s*0[^)]*\)\s*uniform\s+PlaneFrame")]
    private static partial Regex PlaneFrameRegex();

}
