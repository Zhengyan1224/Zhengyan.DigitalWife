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

    internal static void TestGuiControlTypes()
    {
        using AndroidRuntimeRegressionTests.ScriptFixture fixture = new("return null;");
        string path = Path.Combine(fixture.Root, "gui-control.csx");
        string[] sources =
        [
            """
            RuntimeGuiControl? fpsLabel = Scene.GetGuiControl("status");
            fpsLabel?.SetValue($"FPS: {Scene.Fps:F1}");
            RuntimeGuiControl[] controls = Scene.GuiControls.ToArray();
            return fpsLabel != null && controls.Single().Value == fpsLabel.Value
                && nameof(RuntimeGuiControl) == "RuntimeGuiControl"
                && nameof(RuntimeGuiControl.Name) == "Name" && fpsLabel is RuntimeGuiControl
                && typeof(RuntimeGuiControl).FullName == "Zhengyan.DigitalWife.GamePlayer.Android.RuntimeGuiControl";
            """,
            """
            using RuntimeGuiControl = global::Zhengyan.DigitalWife.GamePlayer.Android.RuntimeGuiControl;
            RuntimeGuiControl? control = Scene.GetGuiControl("status");
            return control != null;
            """,
            """
            using RuntimeGuiControl = global::Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeGuiControl;
            RuntimeGuiControl? control = null;
            return control == null && typeof(RuntimeGuiControl).FullName
                == "Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeGuiControl";
            """,
            """
            class RuntimeGuiControl { public string Value => "custom"; }
            return new RuntimeGuiControl().Value == "custom";
            """
        ];
        foreach (MetadataReference[]? references in new[] { (MetadataReference[]?)null, AndroidScriptMetadata.GetReferences().ToArray() })
        {
            foreach (string source in sources)
            {
                File.WriteAllText(path, source);
                Assembly assembly = Assembly.Load(AndroidScriptCompiler.Compile(path, references));
                MethodInfo factory = assembly.GetType("Script")!.GetMethod("<Factory>", BindingFlags.Public | BindingFlags.Static)!;
                object? result = ((Task<object?>)factory.Invoke(null, [new object?[] { fixture.Globals, null }])!).GetAwaiter().GetResult();
                if (result is not true) throw new Exception("GUI type resolution changed an explicit alias or the returned control type.");
            }
            if (fixture.Status != $"FPS: {fixture.Scene.Fps:F1}") throw new Exception("Typed GUI controls did not update the scene definition.");

            File.WriteAllText(path, "RuntimeGuiControl? control = Scene.GetGuiControl(\"status\");\nMissingGuiSymbol();");
            try
            {
                _ = AndroidScriptCompiler.Compile(path, references);
                throw new Exception("The invalid GUI script unexpectedly compiled.");
            }
            catch (InvalidOperationException ex)
            {
                if (!ex.Message.Contains("(2,") || !ex.Message.Contains("CS0103") || ex.Message.Contains("CS0104"))
                    throw new Exception("GUI type resolution lost the original script diagnostic location.", ex);
            }
        }
    }
}
