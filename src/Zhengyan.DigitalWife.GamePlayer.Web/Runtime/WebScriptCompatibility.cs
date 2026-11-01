using System.Reflection;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebScriptCompatibilityReport(bool IsCompatible, string Message, IReadOnlyList<string> References);

public static class WebScriptCompatibility
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Zhengyan.DigitalWife.GamePlayer.Android",
        "Zhengyan.DigitalWife.GamePlayer.Desktop",
        "Silk.NET",
        "Veldrid",
        "OpenTK",
        "Microsoft.Win32"
    ];

    public static WebScriptCompatibilityReport Analyze(Assembly assembly)
    {
        List<string> references = [];
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
        {
            string name = reference.Name ?? string.Empty;
            references.Add(name);
            if (ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return new WebScriptCompatibilityReport(false, $"Web script references unsupported assembly '{name}'.", references);
            }
        }
        return new WebScriptCompatibilityReport(true, "Compatible with the Web script ABI.", references);
    }
}
