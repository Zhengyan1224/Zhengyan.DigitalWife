namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public static class WebRuntimeModuleUrl
{
    // Unfingerprinted JS modules must belong to the same build as the WASM
    // assembly that calls them. The build includes JS hashes in the assembly,
    // so changing a shader or JS bridge also changes this module ID.
    public static string Version { get; } = typeof(WebRuntimeHost).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    public static string Resolve(string fileName) => $"./js/{fileName}?v={Version}";
}
