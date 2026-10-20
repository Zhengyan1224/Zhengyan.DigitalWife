using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Operations;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public static class AndroidScriptCompiler
{
    private static readonly Lazy<MetadataReference[]> PublishedReferences = new(() => GetMetadataReferences().ToArray());
    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        // Only reference the actual Android scripting dependency graph. Loading
        // arbitrary editor assemblies also imports incompatible desktop types
        // with the same names (GameProject, RuntimeEntity, etc.).
        Dictionary<string, Assembly> assemblies = new(StringComparer.OrdinalIgnoreCase);
        Queue<Assembly> pending = new([
            typeof(AndroidScriptGlobals).Assembly,
            typeof(RuntimeScene).Assembly,
            typeof(GameProject).Assembly,
            typeof(PmxModelComponent).Assembly,
            typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly
        ]);
        while (pending.TryDequeue(out Assembly? assembly))
        {
            if (assembly.IsDynamic || !assemblies.TryAdd(assembly.GetName().Name!, assembly)) continue;
            foreach (AssemblyName dependency in assembly.GetReferencedAssemblies())
            {
                if (assemblies.ContainsKey(dependency.Name!)) continue;
                try { pending.Enqueue(Assembly.Load(dependency)); }
                // Some rendering libraries mention optional desktop backends in
                // metadata. Their missing private dependencies are not script APIs.
                catch (FileNotFoundException) { }
                catch (FileLoadException) { }
            }
        }

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (Assembly assembly in assemblies.Values)
        {
            if (string.IsNullOrEmpty(assembly.Location) || !paths.Add(assembly.Location)) continue;
            MetadataReferenceProperties properties = assembly.GetName().Name == "Zhengyan.DigitalWife.Mmd"
                ? MetadataReferenceProperties.Assembly.WithAliases(["MmdDesktop"])
                : MetadataReferenceProperties.Assembly;
            yield return MetadataReference.CreateFromFile(assembly.Location, properties);
        }

        string frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (string path in Directory.EnumerateFiles(frameworkDirectory, "*.dll"))
        {
            if (!paths.Add(path)) continue;
            try { _ = AssemblyName.GetAssemblyName(path); }
            catch (BadImageFormatException) { continue; }
            yield return MetadataReference.CreateFromFile(path);
        }
    }

    public static byte[] Compile(string path, IEnumerable<MetadataReference>? references = null)
    {
        string scriptSource = File.ReadAllText(path);
        string compilationBody = string.IsNullOrWhiteSpace(scriptSource) ? "return null;" : scriptSource;
        string source = "using System;\n"
            + "using System.Collections.Generic;\n"
            + "using System.Globalization;\n"
            + "using System.IO;\n"
            + "using System.Linq;\n"
            + "using System.Net;\n"
            + "using System.Net.Http;\n"
            + "using System.Net.Sockets;\n"
            + "using System.Numerics;\n"
            + "using System.Text;\n"
            + "using System.Text.Json;\n"
            + "using System.Text.RegularExpressions;\n"
            + "using System.Threading;\n"
            + "using System.Threading.Tasks;\n"
            + "using Zhengyan.DigitalWife.GameProjects;\n"
            + "using Zhengyan.DigitalWife.GamePlayer;\n"
            + "using Zhengyan.DigitalWife.GamePlayer.Runtime;\n"
            + "using Zhengyan.DigitalWife.GamePlayer.Android;\n"
            + "using Zhengyan.DigitalWife.Mmd.Game.Pmx;\n\n"
            + "#line 1 \"" + path.Replace('\\', '/').Replace("\"", "\\\"") + "\"\n"
            + compilationBody;
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest, kind: SourceCodeKind.Script);
        SyntaxTree parsedTree = CSharpSyntaxTree.ParseText(source, parseOptions, path);
        SyntaxNode portableRoot = new PortableInterpolatedStringRewriter().Visit(parsedTree.GetRoot())!;
        SyntaxTree syntaxTree = CSharpSyntaxTree.Create(
            (CSharpSyntaxNode)portableRoot, parseOptions, path, parsedTree.Encoding);
        CSharpCompilation compilation = CSharpCompilation.CreateScriptCompilation(
            "AndroidScript_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).Substring(0, 16),
            syntaxTree,
            references ?? PublishedReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                concurrentBuild: false),
            returnType: typeof(object),
            globalsType: typeof(AndroidScriptGlobals));

        // Desktop .NET 10 overload resolution can choose params ReadOnlySpan<T>
        // and lower expanded arguments to private CoreLib InlineArray types.
        // Pass an explicit array instead, preserving arguments and evaluation.
        SyntaxNode portableCalls = new PortableParamsRewriter(compilation.GetSemanticModel(syntaxTree))
            .Visit(syntaxTree.GetRoot())!;
        SyntaxTree portableTree = CSharpSyntaxTree.Create((CSharpSyntaxNode)portableCalls, parseOptions, path, parsedTree.Encoding);
        compilation = compilation.ReplaceSyntaxTree(syntaxTree, portableTree);

        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image);
        if (!result.Success)
        {
            // Emit diagnostics can be empty for script compilations when the
            // failure is produced by the compilation pipeline itself. Include
            // the complete compilation diagnostic set as a fallback so export
            // never fails with an opaque "no diagnostics" message.
            Diagnostic[] allDiagnostics = result.Diagnostics
                .Concat(compilation.GetDiagnostics())
                .GroupBy(diagnostic => diagnostic.ToString(), StringComparer.Ordinal)
                .Select(group => group.First())
                .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning).ToArray();
            Diagnostic[] errors = allDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            string diagnostics = string.Join(Environment.NewLine, (errors.Length > 0 ? errors : allDiagnostics).Select(diagnostic => diagnostic.ToString()));
            if (string.IsNullOrWhiteSpace(diagnostics))
            {
                diagnostics = $"Roslyn did not emit an assembly (result.Success={result.Success}, " +
                    $"diagnosticCount={result.Diagnostics.Length}, compilationDiagnosticCount={compilation.GetDiagnostics().Length}).";
            }
            throw new InvalidOperationException(diagnostics);
        }

        return image.ToArray();
    }

    private sealed class PortableInterpolatedStringRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            List<ExpressionSyntax> values = [];
            StringBuilder format = new();
            foreach (InterpolatedStringContentSyntax content in node.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    format.Append(text.TextToken.ValueText.Replace("{", "{{", StringComparison.Ordinal)
                        .Replace("}", "}}", StringComparison.Ordinal));
                    continue;
                }

                InterpolationSyntax interpolation = (InterpolationSyntax)content;
                int index = values.Count;
                values.Add((ExpressionSyntax)Visit(interpolation.Expression)!);
                format.Append('{').Append(index);
                if (interpolation.AlignmentClause is not null)
                    format.Append(',').Append(interpolation.AlignmentClause.Value.ToString());
                if (interpolation.FormatClause is not null)
                    format.Append(':').Append(interpolation.FormatClause.FormatStringToken.ValueText);
                format.Append('}');
            }

            ExpressionSyntax provider = SyntaxFactory.ParseExpression(
                "global::System.Globalization.CultureInfo.CurrentCulture");
            ExpressionSyntax method = SyntaxFactory.ParseExpression("global::System.String.Format");
            ArrayCreationExpressionSyntax arguments = SyntaxFactory.ArrayCreationExpression(
                SyntaxFactory.ArrayType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)))
                    .WithRankSpecifiers(SyntaxFactory.SingletonList(
                        SyntaxFactory.ArrayRankSpecifier(
                            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                                SyntaxFactory.OmittedArraySizeExpression())))))
                .WithInitializer(SyntaxFactory.InitializerExpression(
                    SyntaxKind.ArrayInitializerExpression,
                    SyntaxFactory.SeparatedList(values)));
            return SyntaxFactory.InvocationExpression(method)
                .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList([
                    SyntaxFactory.Argument(provider),
                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(format.ToString()))),
                    SyntaxFactory.Argument(arguments)
                ])))
                .WithTriviaFrom(node);
        }
    }

    private sealed class PortableParamsRewriter(SemanticModel model) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            InvocationExpressionSyntax visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (model.GetOperation(node) is not IInvocationOperation invocation) return visited;
            IArgumentOperation? expanded = invocation.Arguments.FirstOrDefault(argument => argument.ArgumentKind == ArgumentKind.ParamCollection);
            if (expanded?.Parameter?.Type is not INamedTypeSymbol collection || collection.TypeArguments.Length != 1
                || collection.ContainingNamespace.ToDisplayString() != "System" || collection.Name is not ("Span" or "ReadOnlySpan")
                || expanded.Value is not ICollectionExpressionOperation elements) return visited;

            int first = node.ArgumentList.Arguments.Count;
            foreach (IOperation element in elements.Elements)
            {
                for (int index = 0; index < node.ArgumentList.Arguments.Count; index++)
                    if (node.ArgumentList.Arguments[index].Expression.Span.Contains(element.Syntax.Span)) first = Math.Min(first, index);
            }
            var values = visited.ArgumentList.Arguments.Skip(first).Select(argument => argument.Expression);
            ExpressionSyntax array = CreateArray(collection.TypeArguments[0], values);
            ArgumentSyntax packed = SyntaxFactory.Argument(SyntaxFactory.CastExpression(
                SyntaxFactory.ParseTypeName(collection.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)), array));
            if (visited.ArgumentList.Arguments.Any(argument => argument.NameColon is not null))
                packed = packed.WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(expanded.Parameter.Name)));
            return visited.WithArgumentList(visited.ArgumentList.WithArguments(
                SyntaxFactory.SeparatedList(visited.ArgumentList.Arguments.Take(first).Append(packed))));
        }

        public override SyntaxNode? VisitCollectionExpression(CollectionExpressionSyntax node)
        {
            CollectionExpressionSyntax visited = (CollectionExpressionSyntax)base.VisitCollectionExpression(node)!;
            if (model.GetTypeInfo(node).ConvertedType is not INamedTypeSymbol type || type.TypeArguments.Length != 1
                || type.ContainingNamespace.ToDisplayString() != "System" || type.Name is not ("Span" or "ReadOnlySpan")
                || visited.Elements.Any(element => element is SpreadElementSyntax)) return visited;
            return CreateArray(type.TypeArguments[0], visited.Elements.Cast<ExpressionElementSyntax>().Select(element => element.Expression)).WithTriviaFrom(node);
        }

        private static ArrayCreationExpressionSyntax CreateArray(ITypeSymbol elementType, IEnumerable<ExpressionSyntax> values)
            => SyntaxFactory.ArrayCreationExpression(SyntaxFactory.ArrayType(
                    SyntaxFactory.ParseTypeName(elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                .WithRankSpecifiers(SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(
                    SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.OmittedArraySizeExpression())))))
                .WithInitializer(SyntaxFactory.InitializerExpression(SyntaxKind.ArrayInitializerExpression, SyntaxFactory.SeparatedList(values)));
    }

}
