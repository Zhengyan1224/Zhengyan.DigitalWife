using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

internal static class PmxShaderResources
{
    private const string ResourceDirectory = "Resources";
    private const string ShaderDirectory = "Shader";

    public const string ModelVertexShader = "pmx_model.vert";
    public const string ModelFragmentShader = "pmx_model.frag";
    public const string EdgeVertexShader = "pmx_edge.vert";
    public const string EdgeFragmentShader = "pmx_edge.frag";
    public const string GroundShadowVertexShader = "pmx_ground_shadow.vert";
    public const string GroundShadowFragmentShader = "pmx_ground_shadow.frag";

    public static uint CreateProgram(GL gl, string vertexShaderFileName, string fragmentShaderFileName)
    {
        string vertexShaderSource = LoadShaderSource(vertexShaderFileName);
        string fragmentShaderSource = LoadShaderSource(fragmentShaderFileName);
        return gl.CreateShaderProgramFromSource(vertexShaderSource, fragmentShaderSource);
    }

    private static string LoadShaderSource(string fileName)
    {
        string path = ResolveShaderPath(fileName);
        return File.ReadAllText(path);
    }

    private static string ResolveShaderPath(string fileName)
    {
        return BundledAssetPathResolver.ResolveRequiredFile(
            "PMX shader resource",
            ResourceDirectory,
            ShaderDirectory,
            fileName);
    }
}

