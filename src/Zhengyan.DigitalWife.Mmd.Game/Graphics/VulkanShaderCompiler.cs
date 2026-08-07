using System.Text;
using System.Text.RegularExpressions;
using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>
/// Compiles the engine's existing GLSL ES source into Vulkan-compatible SPIR-V.
/// Resource declarations are intentionally kept in source files; pipeline migration
/// will add explicit descriptor bindings alongside each shader.
/// </summary>
public static class VulkanShaderCompiler
{
    private static readonly Regex VersionDirective = new("^\\s*#version\\s+\\d+(?:\\s+es)?\\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex PrecisionDeclaration = new("^\\s*precision\\s+(?:lowp|mediump|highp)\\s+float\\s*;\\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static ShaderDescription CompileFile(string path, ShaderStages stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Shader file not found.", fullPath);
        }

        return CompileSource(Path.GetFileName(fullPath), File.ReadAllText(fullPath), stage);
    }

    public static ShaderDescription CompileSource(string sourceName, string source, ShaderStages stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(source);

        string vulkanGlsl = NormalizeSource(source);
        SpirvCompilationResult result = SpirvCompilation.CompileGlslToSpirv(
            vulkanGlsl,
            sourceName,
            stage,
            GlslCompileOptions.Default);
        return new ShaderDescription(stage, result.SpirvBytes, "main");
    }

    public static Shader[] CreateProgram(ResourceFactory factory, string vertexPath, string fragmentPath)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ShaderDescription vertex = CompileFile(vertexPath, ShaderStages.Vertex);
        ShaderDescription fragment = CompileFile(fragmentPath, ShaderStages.Fragment);
        return factory.CreateFromSpirv(vertex, fragment);
    }

    private static string NormalizeSource(string source)
    {
        StringBuilder builder = new(source.Length + 32);
        builder.AppendLine("#version 450");

        // Normalize both legacy GLSL ES and portable GLSL 450 sources to one
        // explicit Vulkan version directive.
        string withoutVersion = VersionDirective.Replace(source, string.Empty);
        string withoutPrecision = PrecisionDeclaration.Replace(withoutVersion, string.Empty);
        builder.Append(withoutPrecision);
        return builder.ToString();
    }
}
