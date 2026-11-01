namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

/// <summary>
/// Information returned by the browser WebGL2 renderer after context creation.
/// </summary>
public sealed class WebGl2Info
{
    public string Version { get; set; } = "WebGL 2";

    public string Renderer { get; set; } = "WebGL2";

    public string ShadingLanguageVersion { get; set; } = "GLSL ES 3.00";
}
