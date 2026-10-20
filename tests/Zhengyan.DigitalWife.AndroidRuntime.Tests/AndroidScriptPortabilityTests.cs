using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidScriptPortabilityTests
{
    internal static void TestParamsAndSpans()
    {
        string path = Path.Combine(Path.GetTempPath(), "android-params-" + Guid.NewGuid().ToString("N") + ".csx");
        try
        {
            string source = """
                int calls = 0;
                string Next() => (++calls).ToString();
                string joined = string.Join("|", Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next());
                int Sum(params System.ReadOnlySpan<int> values)
                {
                    int total = 0;
                    foreach (int value in values) total += value;
                    return total;
                }
                int LastNumber()
                {
                    System.ReadOnlySpan<int> numbers = [1, 2, 3, 4];
                    return numbers[3];
                }
                return joined == "1|2|3|4|5|6|7|8|9|10|11|12" && calls == 12
                    && Sum(1, 2, 3) == 6 && Sum() == 0 && Sum(values: 9) == 9
                    && LastNumber() == 4 && string.Join("|", new string[] { "a", "b" }) == "a|b";
                """;
            File.WriteAllText(path, source);
            foreach (MetadataReference[]? references in new[] { (MetadataReference[]?)null, AndroidScriptMetadata.GetReferences().ToArray() })
            {
                byte[] image = AndroidScriptCompiler.Compile(path, references);
                Check(!AndroidPrecompiledScriptCompatibility.ReferencesPrivateInlineArray(image), "Portable compilation emitted private CoreLib InlineArray references.");
                Assembly assembly = Assembly.Load(image);
                MethodInfo factory = assembly.GetType("Script")!.GetMethod("<Factory>", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                object? result = ((Task<object?>)factory.Invoke(null, [new object?[2]])!).GetAwaiter().GetResult();
                Check(result is true, "Params/span rewriting changed argument ordering or results.");
            }
        }
        finally { File.Delete(path); }
    }

    internal static void TestOldPackageDetection()
    {
        string source = """
            return System.String.Join("|", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12");
            """;
        CSharpCompilation compilation = CSharpCompilation.CreateScriptCompilation("OldAndroidInlineArrayScript",
            CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script)),
            AndroidScriptMetadata.GetReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release),
            returnType: typeof(object));
        using MemoryStream image = new();
        var result = compilation.Emit(image);
        Check(result.Success, string.Join("\n", result.Diagnostics));
        Check(AndroidPrecompiledScriptCompatibility.ReferencesPrivateInlineArray(image.ToArray()), "Old desktop-compiled params script was not identified for runtime recompilation.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
