extern alias AndroidRuntime;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd;
using RuntimeCore = AndroidRuntime::Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed record WebScriptPrecompileEntry(string Source, string Assembly, string Sha256, string ApiVersion);

internal sealed record WebScriptPrecompileResult(IReadOnlyList<WebScriptPrecompileEntry> Entries)
{
    public string ManifestPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>Compiles the existing .csx source into the small Web script ABI.</summary>
internal static class WebCSharpScriptPrecompiler
{
    private const string OutputRoot = "compiled/web";
    private const string ApiVersion = "1.0";

    public static WebScriptPrecompileResult Precompile(string projectDirectory, GameProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(project);
        string root = Path.GetFullPath(projectDirectory);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string scenePath in project.Scenes.Append(project.EditorScene).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            GameProjectScene scene = GameProjectStore.LoadScene(root, scenePath);
            AddBindings(scene.LoadingScripts, root, paths);
            foreach (GameEntity entity in scene.Entities) AddBindings(entity.Scripts, root, paths);
        }

        string outputRoot = Path.Combine(root, OutputRoot.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(outputRoot);
        List<WebScriptPrecompileEntry> entries = [];
        List<string> errors = [];
        foreach (string path in paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string source = Path.GetRelativePath(root, path).Replace('\\', '/');
            string relativeAssembly = Path.ChangeExtension(source, ".dll").Replace('\\', '/');
            string assembly = $"{OutputRoot}/{relativeAssembly}";
            string assemblyPath = Path.Combine(root, assembly.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
                byte[] image = Compile(path);
                File.WriteAllBytes(assemblyPath, image);
                entries.Add(new(source, assembly, Convert.ToHexString(SHA256.HashData(image)), ApiVersion));
            }
            catch (Exception exception)
            {
                // Do not leave an older successful assembly in place when a
                // script becomes incompatible with the Web ABI. The exporter
                // must never publish a stale DLL for a failed compilation.
                if (File.Exists(assemblyPath))
                {
                    File.Delete(assemblyPath);
                }

                errors.Add($"{path}: {exception.Message}");
            }
        }

        string manifestPath = Path.Combine(outputRoot, "manifest.json");
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            apiVersion = ApiVersion,
            targetFramework = "net10.0-browser",
            scripts = entries,
            errors
        }, new JsonSerializerOptions { WriteIndented = true }));
        return new WebScriptPrecompileResult(entries) { ManifestPath = manifestPath, Errors = errors };
    }

    private static void AddBindings(IEnumerable<ScriptBinding> bindings, string root, ISet<string> paths)
    {
        foreach (ScriptBinding binding in bindings.Where(binding => binding.Enabled && IsCSharp(binding.Language, binding.Path)))
        {
            string path = GameProjectPath.ToAbsolute(root, binding.Path);
            // Missing sources must also fail compilation and remove stale DLLs.
            paths.Add(Path.GetFullPath(path));
        }
    }

    private static byte[] Compile(string path)
    {
        string body = File.ReadAllText(path);
        if (string.Equals(body.Trim(), "return null;", StringComparison.Ordinal)) body = string.Empty;
        (string usings, string members, string statements) = SplitScriptBody(body, path);
        statements = NormalizeLocalOverloads(statements);
        string source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Numerics;
            using System.Text;
            using System.Text.RegularExpressions;
            using System.Threading;
            using System.Threading.Tasks;
            using Zhengyan.DigitalWife.GameProjects;
            using Zhengyan.DigitalWife.GamePlayer.Runtime;
            using RuntimeDialogueBubble = Zhengyan.DigitalWife.GamePlayer.Runtime.RuntimeDialogueBubble;
            using Zhengyan.DigitalWife.Mmd;
            using Zhengyan.DigitalWife.Mmd.Game.Input;
            {{usings}}

            namespace Zhengyan.GeneratedWebScripts;

            public sealed class Script : IWebGameScript
            {
                {{members}}
                public void Start(WebScriptContext context) => _ = ExecuteAsync(context, 0.0f, true, false, null);
                public void Update(WebScriptContext context, float deltaSeconds) => _ = ExecuteAsync(context, deltaSeconds, false, true, null);
                public void OnEvent(WebScriptContext context, WebScriptEvent GameEvent) => _ = ExecuteAsync(context, 0.0f, false, false, GameEvent);
                public ValueTask StartAsync(WebScriptContext context) => new(ExecuteAsync(context, 0.0f, true, false, null));
                public ValueTask UpdateAsync(WebScriptContext context, float deltaSeconds) => new(ExecuteAsync(context, deltaSeconds, false, true, null));
                public ValueTask OnEventAsync(WebScriptContext context, WebScriptEvent GameEvent) => new(ExecuteAsync(context, 0.0f, false, false, GameEvent));
                private async Task ExecuteAsync(WebScriptContext context, float DeltaSeconds, bool IsStart, bool IsUpdate, WebScriptEvent? GameEvent)
                {
                    RuntimeScene Scene = context.Scene;
                    var Graphics = context.Graphics;
                    RuntimeEntity? Entity = context.Entity;
                    RuntimeCameraInput Input = context.Input;
                    WebScriptAudio Audio = context.Audio;
                    bool IsGuiEvent = GameEvent?.IsGuiEvent == true;
                    bool IsSpeechEvent = GameEvent?.IsSpeechEvent == true;
                    string GuiControlId = GameEvent?.ControlId ?? string.Empty;
                    string GuiControlName = GameEvent?.ControlName ?? string.Empty;
                    string GuiEventName = GameEvent?.EventName ?? string.Empty;
                    string SpeechCallbackName = GameEvent?.CallbackName ?? string.Empty;
                    bool IsSpriteEvent = GameEvent?.IsSpriteEvent == true;
                    string SpriteId = GameEvent?.SpriteId ?? string.Empty;
                    string SpriteName = GameEvent?.SpriteName ?? string.Empty;
                    string SpriteEventName = GameEvent?.SpriteEventName ?? string.Empty;
                    bool IsAsrEvent = GameEvent?.IsAsrEvent == true;
                    var AsrEvent = GameEvent?.Asr;
                    string AsrCallbackName = AsrEvent?.CallbackName ?? "";
                    string AsrRequestId = AsrEvent?.RequestId ?? "";
                    string AsrEventName = AsrEvent?.EventName ?? "";
                    string AsrText = AsrEvent?.Text ?? "";
                    string AsrError = AsrEvent?.Error ?? "";
                    string AsrWakeWord = AsrEvent?.WakeWord ?? "";
                    bool AsrIsFinal = AsrEvent?.IsFinal ?? false;
                    bool IsLlmEvent = GameEvent?.IsLlmEvent == true;
                    var LlmEvent = GameEvent?.Llm;
                    string LlmCallbackName = LlmEvent?.CallbackName ?? "";
                    string LlmRequestId = LlmEvent?.RequestId ?? "";
                    string LlmEventName = LlmEvent?.EventName ?? "";
                    string LlmText = LlmEvent?.AccumulatedText ?? "";
                    string LlmDelta = LlmEvent?.Delta ?? "";
                    string LlmError = LlmEvent?.Error ?? "";
                    string LlmToolName = LlmEvent?.ToolCall?.Name ?? "";
                    string LlmToolArgumentsJson = LlmEvent?.ToolCall?.ArgumentsJson ?? "";
                    string LlmToolResult = LlmEvent?.ToolResult ?? "";
                    bool LlmIsFinal = LlmEvent?.IsFinal ?? false;
                    bool IsRealtimeVoiceEvent = GameEvent?.IsRealtimeVoiceEvent == true;
                    var RealtimeVoiceEvent = GameEvent?.RealtimeVoice;
                    string RealtimeVoiceRequestId = RealtimeVoiceEvent?.RequestId ?? "";
                    string RealtimeVoiceEventName = RealtimeVoiceEvent?.EventName ?? "";
                    string RealtimeVoiceCallbackName = RealtimeVoiceEvent?.CallbackName ?? "";
                    string RealtimeVoiceText = RealtimeVoiceEvent?.Text ?? "";
                    string RealtimeVoiceDelta = RealtimeVoiceEvent?.Delta ?? "";
                    string RealtimeVoiceAccumulatedText = RealtimeVoiceEvent?.AccumulatedText ?? "";
                    string RealtimeVoiceError = RealtimeVoiceEvent?.Error ?? "";
                    string RealtimeVoiceWakeWord = RealtimeVoiceEvent?.WakeWord ?? "";
                    bool RealtimeVoiceIsFinal = RealtimeVoiceEvent?.IsFinal ?? false;
                    {{statements}}
                }
            }
            """;
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "WebScript_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).Substring(0, 16),
            [syntaxTree],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image);
        if (!result.Success)
        {
            string diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException(diagnostics);
        }
        return image.ToArray();
    }

    private static (string Usings, string Members, string Statements) SplitScriptBody(string body, string path)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            body,
            new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse),
            path);
        CompilationUnitSyntax root = (CompilationUnitSyntax)tree.GetRoot();
        List<string> usings = root.Usings.Select(item => item.ToFullString().Trim()).Where(item => item.Length > 0).ToList();
        List<string> members = [];
        List<string> statements = [];
        foreach (MemberDeclarationSyntax member in root.Members)
        {
            if (member is GlobalStatementSyntax global)
            {
                if (global.Statement is LocalDeclarationStatementSyntax local
                    && local.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)))
                {
                    FieldDeclarationSyntax field = SyntaxFactory.FieldDeclaration(local.Declaration)
                        .WithModifiers(local.Modifiers);
                    members.Add(field.NormalizeWhitespace().ToFullString());
                }
                else
                {
                    statements.Add(global.Statement.ToFullString());
                }
            }
            else
            {
                members.Add(member.ToFullString());
            }
        }

        return (
            string.Join(Environment.NewLine, usings),
            string.Join(Environment.NewLine, members),
            string.Join(Environment.NewLine, statements));
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        Assembly[] assemblies =
        [
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Enumerable).Assembly,
            typeof(List<>).Assembly,
            typeof(Vector3).Assembly,
            typeof(System.Text.RegularExpressions.Regex).Assembly,
            typeof(Zhengyan.DigitalWife.Scripting.RuntimeGraphicsQuality).Assembly,
            typeof(RuntimeCore.RuntimeScene).Assembly,
            typeof(RuntimeCore.RuntimeScene).GetProperty("Definition")!.PropertyType.Assembly
        ];
        Queue<Assembly> pending = new(assemblies);
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out Assembly? assembly))
        {
            if (!visited.Add(assembly.FullName!)) continue;
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location) || !File.Exists(assembly.Location)) continue;
            if (paths.Add(assembly.Location)) yield return MetadataReference.CreateFromFile(assembly.Location);
            foreach (var reference in assembly.GetReferencedAssemblies()) pending.Enqueue(Assembly.Load(reference));
        }
    }

    private static string NormalizeLocalOverloads(string statements)
    {
        var unit = CSharpSyntaxTree.ParseText("class Host { void Run() { " + statements + "\n} }").GetCompilationUnitRoot();
        var block = unit.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;
        var groups = block.Statements.OfType<LocalFunctionStatementSyntax>().GroupBy(f => f.Identifier.ValueText)
            .Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.ToArray());
        if (groups.Count == 0) return statements;
        var rewritten = (BlockSyntax)new LocalOverloadRewriter(groups).Visit(block)!;
        return string.Join(Environment.NewLine, rewritten.Statements.Select(s => s.ToFullString()));
    }

    private sealed class LocalOverloadRewriter(Dictionary<string, LocalFunctionStatementSyntax[]> groups) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var rewritten = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
            if (!groups.TryGetValue(node.Identifier.ValueText, out var overloads)) return rewritten;
            int index = Array.FindIndex(overloads, f => f.ParameterList.ToFullString() == node.ParameterList.ToFullString());
            return index < 0 ? rewritten : rewritten.WithIdentifier(SyntaxFactory.Identifier(node.Identifier.ValueText + "__web" + index).WithTriviaFrom(node.Identifier));
        }
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var rewritten = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (node.Expression is not IdentifierNameSyntax name || !groups.TryGetValue(name.Identifier.ValueText, out var overloads)) return rewritten;
            int index = Array.FindIndex(overloads, f => node.ArgumentList.Arguments.Count >= f.ParameterList.Parameters.Count(p => p.Default is null)
                && node.ArgumentList.Arguments.Count <= f.ParameterList.Parameters.Count
                && node.ArgumentList.Arguments.Where(a => a.NameColon is not null).All(a => f.ParameterList.Parameters.Any(p => p.Identifier.ValueText == a.NameColon!.Name.Identifier.ValueText)));
            return index < 0 ? rewritten : rewritten.WithExpression(SyntaxFactory.IdentifierName(name.Identifier.ValueText + "__web" + index).WithTriviaFrom(name));
        }
    }

    private static bool IsCSharp(string language, string path)
        => language.Equals("csharp", StringComparison.OrdinalIgnoreCase)
            || language.Equals("cs", StringComparison.OrdinalIgnoreCase)
            || language.Equals("csx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase);
}
