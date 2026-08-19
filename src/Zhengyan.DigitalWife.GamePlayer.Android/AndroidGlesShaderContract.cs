using System.Text.RegularExpressions;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>
/// Android GLES3 shader contract. This is deliberately backend-specific: the
/// Android player accepts GLSL ES 3.00 source and does not silently reinterpret
/// desktop GLSL or Vulkan SPIR-V.
/// </summary>
public static class AndroidGlesShaderContract
{
    private static readonly Regex Version = new(@"^\s*#version\s+300\s+es\b", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static AndroidGlesShaderValidationResult Validate(string vertexSource, string fragmentSource)
    {
        List<string> errors = [];
        List<string> warnings = [];
        ValidateStage("vertex", vertexSource, errors, warnings);
        ValidateStage("fragment", fragmentSource, errors, warnings);
        if (vertexSource.Contains("layout(binding", StringComparison.OrdinalIgnoreCase)
            || fragmentSource.Contains("layout(binding", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("GLES3 custom shaders must bind samplers through the engine uniform contract, not layout(binding=...).");
        }
        return new AndroidGlesShaderValidationResult(errors, warnings);
    }

    public static AndroidGlesShaderValidationResult ValidateFiles(string vertexPath, string fragmentPath)
    {
        if (!File.Exists(vertexPath) || !File.Exists(fragmentPath))
        {
            return new AndroidGlesShaderValidationResult(
                [$"Shader file pair not found: '{vertexPath}' and '{fragmentPath}'."], []);
        }
        return Validate(File.ReadAllText(vertexPath), File.ReadAllText(fragmentPath));
    }

    private static void ValidateStage(string stage, string source, ICollection<string> errors, ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            errors.Add($"{stage} shader is empty.");
            return;
        }
        if (!Version.IsMatch(source))
        {
            errors.Add($"{stage} shader must begin with '#version 300 es'.");
        }
        if (!source.Contains("void main", StringComparison.Ordinal))
        {
            errors.Add($"{stage} shader does not declare void main().");
        }
        if (source.Contains("#version 330", StringComparison.OrdinalIgnoreCase)
            || source.Contains("#version 450", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{stage} shader uses desktop/Vulkan GLSL instead of GLES 3.00.");
        }
        if (source.Contains("#extension GL_ARB", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"{stage} shader requests an ARB desktop extension; verify it exists on Android GLES.");
        }
    }
}

public sealed record AndroidGlesShaderValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}
