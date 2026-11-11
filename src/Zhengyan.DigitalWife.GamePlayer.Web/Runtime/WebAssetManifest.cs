namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

/// <summary>
/// A publish-time inventory for the static Web game directory.
/// Paths are always project-relative and are never allowed to escape the
/// asset-store root.
/// </summary>
public sealed class WebAssetManifest
{
    public int FormatVersion { get; set; } = 1;
    public string Runtime { get; set; } = "web";
    public string Renderer { get; set; } = "webgl2";
    public string ApiVersion { get; set; } = "1.0";
    public string EntryScene { get; set; } = "scenes/main.scene.json";
    public List<WebAssetEntry> Assets { get; set; } = [];
    public List<WebScriptEntry> Scripts { get; set; } = [];
    public List<string> Capabilities { get; set; } = [];
    public string SignatureAlgorithm { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;

    public void Validate() => WebHttpAssetStore.ValidateManifest(this);
}

public sealed class WebAssetEntry
{
    public string Path { get; set; } = string.Empty;
    public string Kind { get; set; } = "binary";
    public long Size { get; set; } = -1;
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class WebScriptEntry
{
    public string Scene { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Assembly { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Factory { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "1.0";
    public string TargetFramework { get; set; } = "net10.0-browser";
    public List<string> Dependencies { get; set; } = [];
}
