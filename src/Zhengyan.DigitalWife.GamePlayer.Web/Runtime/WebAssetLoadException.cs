namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

/// <summary>
/// Resource failures retain the logical asset path and resolved request URI so
/// a scene can report actionable diagnostics instead of a browser-only URL.
/// </summary>
public sealed class WebAssetLoadException : IOException
{
    public WebAssetLoadException(
        string assetPath,
        string requestUri,
        string message,
        Exception? innerException = null,
        bool isMissing = false)
        : base(message, innerException)
    {
        AssetPath = assetPath;
        RequestUri = requestUri;
        IsMissing = isMissing;
    }

    public string AssetPath { get; }

    public string RequestUri { get; }

    public bool IsMissing { get; }
}
