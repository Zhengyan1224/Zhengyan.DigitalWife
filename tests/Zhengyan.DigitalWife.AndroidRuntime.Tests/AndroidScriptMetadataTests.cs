using System.Reflection;
using Microsoft.CodeAnalysis;
using Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidScriptMetadataTests
{
    internal static void TestDefaultImports()
    {
        MetadataReference[] references = AndroidScriptMetadata.GetReferences().ToArray();
        if (references.Cast<PortableExecutableReference>().Any(reference => reference.FilePath is not null))
            throw new Exception("Android metadata must not depend on Assembly.Location.");
        string script = Path.Combine(Path.GetTempPath(), "android-imports-" + Guid.NewGuid().ToString("N") + ".csx");
        try
        {
            File.WriteAllText(script, """
                using (var client = new HttpClient())
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    client.BaseAddress = new Uri("https://example.invalid/");
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>("{\"host\":\"example.invalid\"}");
                    return Regex.IsMatch(client.BaseAddress.Host, "example") && data["host"] == client.BaseAddress.Host;
                }
                """);
            Assembly assembly = Assembly.Load(AndroidScriptCompiler.Compile(script, references));
            MethodInfo factory = assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                .Single(method => method.Name == "<Factory>");
            var result = (Task<object>)factory.Invoke(null, [new object?[2]])!;
            if (result.GetAwaiter().GetResult() is not true) throw new Exception("Android imported framework APIs did not execute.");
            File.WriteAllText(script, "return null;");
            _ = AndroidScriptCompiler.Compile(script, references);
        }
        finally { File.Delete(script); }
    }
}
