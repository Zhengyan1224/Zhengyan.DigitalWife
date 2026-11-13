extern alias AndroidScripting;
extern alias AndroidRuntime;

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Desktop = Zhengyan.DigitalWife.GamePlayer;
using Android = AndroidScripting::Zhengyan.DigitalWife.GamePlayer.Android;
using Portable = AndroidRuntime::Zhengyan.DigitalWife.GamePlayer.Runtime;

internal static class ScriptApiAudit
{
    private static readonly Assembly DesktopAssembly = typeof(Desktop.CSharpScriptGlobals).Assembly;
    private static readonly Assembly AndroidAssembly = typeof(Android.AndroidScriptGlobals).Assembly;
    private static readonly Assembly PortableAssembly = typeof(Portable.RuntimeScene).Assembly;

    internal static void Write(string path)
    {
        var types = DesktopAssembly.GetExportedTypes().Where(t => t.Name.StartsWith("Runtime") || t == typeof(Desktop.CSharpScriptGlobals)).OrderBy(t => t.Name);
        var rows = types.Select(type => new
        {
            Type = type.Name,
            AndroidType = Find(type, android: true)?.FullName,
            WebType = Find(type, android: false)?.FullName,
            Members = Members(type).Select(member => new
            {
                Name = member.Name,
                Signature = Signature(member),
                AndroidNamePresent = Find(type, true)?.GetMember(member.Name).Length > 0,
                WebNamePresent = Find(type, false)?.GetMember(member.Name).Length > 0,
                Android = Has(Find(type, true), member),
                Web = type==typeof(Desktop.CSharpScriptGlobals)?WebGlobalNames.Contains(member.Name):Has(Find(type, false), member)
            }).ToArray()
        }).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var row in rows)
            Console.WriteLine($"{row.Type}: Android missing {row.Members.Count(m => !m.Android)}/{row.Members.Length}, Web missing {row.Members.Count(m => !m.Web)}/{row.Members.Length}");
    }

    private static Type? Find(Type desktop, bool android)
    {
        if (desktop == typeof(Desktop.CSharpScriptGlobals)) return android ? typeof(Android.AndroidScriptGlobals) : null;
        string name = desktop.Name;
        string[] alternatives = name switch
        {
            "RuntimeInput" => ["AndroidScriptInputApi", "RuntimeCameraInput"],
            "RuntimeAudio" => ["AndroidScriptAudio", "WebScriptAudio"],
            "RuntimeSaveStore" => ["AndroidScriptSaveStore", name],
            "RuntimeGuiControl" => ["RuntimeGuiControl"],
            "RuntimeSpriteControl" => ["AndroidScriptSprite", name],
            "RuntimeDialogueBubbleManager" => ["AndroidScriptBubbleManager", name],
            "RuntimePointLightCollection" or "RuntimeSpotLightCollection" => ["RuntimeLightCollection"],
            _ => [name.Replace("Runtime", "AndroidScript"), name]
        };
        var available = (android ? AndroidAssembly.GetExportedTypes().Concat(PortableAssembly.GetExportedTypes()) : PortableAssembly.GetExportedTypes()).ToArray();
        foreach (string candidate in alternatives)
        {
            var match = available.FirstOrDefault(t => t.Name == candidate);
            if (match is not null) return match;
        }
        return null;
    }

    internal static void Verify()
    {
        if(!typeof(Portable.RuntimeAsr).GetMethods().Any(m=>m.Name=="StartWakeWordMonitoring"&&m.GetParameters().Count(p=>p.ParameterType==typeof(float))==3))
            throw new Exception("The original Web wake method binary signature was removed.");
        if(!typeof(Portable.RuntimeSpriteControl).GetConstructors().Any(c=>c.GetParameters().Length==1))
            throw new Exception("The original Web sprite constructor binary signature was removed.");
        var scopes=new (Type Type,string Expression)[]{
            (typeof(Desktop.RuntimeScene),"Scene"),(typeof(Desktop.RuntimeEntity),"Entity!"),
            (typeof(Desktop.RuntimeCamera),"Scene.Camera"),(typeof(Desktop.RuntimeInput),"Input"),
            (typeof(Desktop.RuntimeWindowControl),"Scene.Window"),(typeof(Desktop.RuntimeProjectControl),"Scene.Runtime"),
            (typeof(Desktop.RuntimeNetwork),"Scene.Network"),(typeof(Desktop.RuntimeLlm),"Scene.Llm"),
            (typeof(Desktop.RuntimeAsr),"Scene.Asr"),(typeof(Desktop.RuntimeRealtimeVoice),"Scene.RealtimeVoice"),
            (typeof(Desktop.RuntimeAudio),"Audio"),(typeof(Desktop.RuntimeGuiControl),"Scene.GetGuiControl(\"test\")!"),
            (typeof(Desktop.RuntimeSpriteControl),"Scene.GetSprite(\"test\")!"),
            (typeof(Desktop.RuntimeDialogueBubbleManager),"Scene.Bubble"),(typeof(Desktop.RuntimeDialogueBubble),"Scene.Bubble.GetOrCreate(\"test\")"),
            (typeof(Desktop.RuntimeLighting),"Scene.Lighting"),(typeof(Desktop.RuntimeSaveStore),"Scene.Save"),
            (typeof(Desktop.RuntimeScenePhysics),"Scene.Physics"),(typeof(Desktop.RuntimeSceneNavigation),"Scene.Navigation")};
        var code=new System.Text.StringBuilder("using Zhengyan.DigitalWife.GamePlayer;\nusing Zhengyan.DigitalWife.Mmd.Game.Pmx;\nif (!IsStart) {\nRuntimeScene typedScene=Scene;RuntimeCamera typedCamera=Scene.Camera;RuntimeEntity typedActor=Scene.GetEntity(\"actor\")!;RuntimeLlm typedLlm=Scene.Llm;Scene.Asr.StartStreamingRecognition(typedActor);\n");
        int index=0;
        foreach(var scope in scopes)
        foreach(var member in Members(scope.Type))
        {
            if(!Has(Find(scope.Type,true),member)||!Has(Find(scope.Type,false),member))throw new Exception("Missing portable script member: "+Signature(member));
            if(member is PropertyInfo property)
            {
                if(property.GetMethod?.IsPublic==true)code.AppendLine("var api_"+(index++)+" = "+scope.Expression+"."+property.Name+";");
                if(property.SetMethod?.IsPublic==true)code.AppendLine(scope.Expression+"."+property.Name+" = "+Argument(property.PropertyType)+";");
            }
            else if(member is MethodInfo method)
            {
                string name=method.Name+(method.IsGenericMethod?"<string>":"");
                foreach(bool named in new[]{false,true})
                {
                    string args=string.Join(", ",method.GetParameters().Select(p=>
                        (named?p.Name+": ":"")+(p.IsOut?"out var api_"+(index++):p.ParameterType.IsByRef?"ref api_ref":Argument(p.ParameterType))));
                    code.AppendLine(scope.Expression+"."+name+"("+args+");");
                }
            }
        }
        code.AppendLine("}\nvoid HelperWithReturn(){ if(IsStart)return; void Nested(){return;} Nested(); }\nHelperWithReturn();\nif (!IsStart) { Entity!.SetNodeTranslate(nodeName: \"root\", value: System.Numerics.Vector3.Zero); Entity.SetNodeRotateEuler(nodeName: \"root\", x: 0, y: 0, z: 0); Audio.Play(idOrName: \"audio\"); Scene.Asr.StartStreamingRecognition(entity: Entity); }\nreturn null;");
        string path=Path.GetFullPath("temp/script-api-compile-fixture.csx");Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,code.ToString());
        var compile=typeof(Zhengyan.DigitalWife.GameEditor.WebCSharpScriptPrecompiler).GetMethod("Compile",BindingFlags.NonPublic|BindingFlags.Static)!;
        string desktopSource=code.ToString();
        File.WriteAllText(path,desktopSource[..desktopSource.IndexOf("if (!IsStart) { Entity!")]+"return null;");
        try{compile.Invoke(null,[path]);}catch(TargetInvocationException error){throw error.InnerException!;}
        File.WriteAllText(path,desktopSource);
        Android.AndroidScriptCompiler.Compile(path);
        Console.WriteLine("PASS desktop API calls, setters, overloads and named parameters compile for Android and Web");
    }

    private static string Argument(Type type)
    {
        if(type==typeof(string))return "\"fixture\"";
        if(type==typeof(Desktop.RuntimeEntity))return "Entity!";
        if(type.IsGenericParameter)return "\"fixture\"";
        if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(IEnumerable<>))return "System.Array.Empty<"+FixtureType(type.GetGenericArguments()[0])+">()";
        return "default("+FixtureType(type)+")!";
    }
    private static string FixtureType(Type type)
    {
        if(type.IsGenericParameter)return "string";
        if(type.IsArray)return FixtureType(type.GetElementType()!)+"[]";
        if(type.IsGenericType)return type.FullName!.Split('`')[0]+"<"+string.Join(",",type.GetGenericArguments().Select(FixtureType))+">";
        if(type.Namespace==typeof(Desktop.RuntimeEntity).Namespace || type.Namespace=="Zhengyan.DigitalWife.Mmd.Game.Pmx")return type.Name;
        return type.FullName!;
    }

    private static IEnumerable<MemberInfo> Members(Type type) => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
        .Where(m => m.DeclaringType != typeof(object) && (m is PropertyInfo or FieldInfo || m is MethodInfo { IsSpecialName: false }))
        .OrderBy(m => m.Name).ThenBy(Signature);

    private static readonly HashSet<string> WebGlobalNames = new(Regex.Matches(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../src/Zhengyan.DigitalWife.GameEditor/WebCSharpScriptPrecompiler.cs"))),@"\b(\w+)\s*(?:=|,|\))").Select(m=>m.Groups[1].Value));
    private static bool Has(Type? target, MemberInfo member) => target is not null && Members(target).Any(candidate => Compatible(member,candidate));
    private static bool Compatible(MemberInfo desktop,MemberInfo target)
    {
        if(desktop.Name!=target.Name)return false;
        if(desktop is PropertyInfo p && target is PropertyInfo t)
        {
            bool init=p.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Any(m=>m.FullName=="System.Runtime.CompilerServices.IsExternalInit")==true;
            bool dynamic=t.CustomAttributes.Any(a=>a.AttributeType.FullName=="System.Runtime.CompilerServices.DynamicAttribute");
            bool compatibleType=TypeName(p.PropertyType)==TypeName(t.PropertyType)||(p.PropertyType==typeof(double)&&t.PropertyType==typeof(float))||dynamic;
            return compatibleType&&(p.GetMethod?.IsPublic!=true||t.GetMethod?.IsPublic==true)
                &&(p.SetMethod?.IsPublic!=true||init||t.SetMethod?.IsPublic==true);
        }
        if(desktop is MethodInfo method && target is MethodInfo other)
        {
            var expected=method.GetParameters();var actual=other.GetParameters();
            return actual.Length>=expected.Length&&actual.Skip(expected.Length).All(p=>p.IsOptional)
                &&actual.Take(expected.Length).Zip(expected).All(pair=>TypeName(pair.First.ParameterType)==TypeName(pair.Second.ParameterType)&&pair.First.Name==pair.Second.Name&&(!pair.Second.IsOptional||pair.First.IsOptional))
                &&(method.ReturnType==typeof(void)||TypeName(method.ReturnType)==TypeName(other.ReturnType));
        }
        return Signature(desktop)==Signature(target);
    }
    private static string Signature(MemberInfo member) => member switch
    {
        PropertyInfo p => $"{p.Name}: {TypeName(p.PropertyType)} {(p.GetMethod?.IsPublic == true ? "get" : "")} {(p.SetMethod?.IsPublic == true ? "set" : "")}",
        FieldInfo f => $"{f.Name}: {TypeName(f.FieldType)} field",
        MethodInfo m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => TypeName(p.ParameterType) + " " + p.Name + (p.IsOptional ? "?" : "")))}): {TypeName(m.ReturnType)}",
        _ => member.Name
    };

    private static string TypeName(Type type)
    {
        if (type.IsByRef) return "ref " + TypeName(type.GetElementType()!);
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter) return type.Name;
        string name = type.Name.Replace("AndroidScript", "").Replace("Runtime", "");
        name = name switch { "PointLightCollection" or "SpotLightCollection" or "LightCollection" => "Lights", "CameraInput" or "InputApi" => "Input", "WebScriptAudio" => "Audio", "Sprite"=>"SpriteControl","IAsr"=>"Asr","IRealtimeVoice"=>"RealtimeVoice","BubbleManager"=>"DialogueBubbleManager", _ => name };
        return type.IsGenericType ? name.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">" : name;
    }

    internal static void GenerateAndroidCamera(string path)
        => GenerateAndroidForwarders(typeof(Desktop.RuntimeCamera),typeof(Android.AndroidScriptCamera),typeof(Portable.RuntimeCamera),"_scene.MainCamera","AndroidScriptCamera",path);
    internal static void GenerateAndroidEntity(string path)
        => GenerateAndroidForwarders(typeof(Desktop.RuntimeEntity),typeof(Android.AndroidScriptEntity),typeof(Portable.RuntimeEntity),"_entity","AndroidScriptEntity",path);
    private static void GenerateAndroidForwarders(Type source,Type target,Type portable,string expression,string className,string path)
    {
        var output = new System.Text.StringBuilder("using System.Numerics;\nusing Zhengyan.DigitalWife.GamePlayer.Runtime;\n\nnamespace Zhengyan.DigitalWife.GamePlayer.Android;\n\npublic sealed partial class "+className+"\n{\n");
        foreach (var member in Members(source))
        {
            if (member is PropertyInfo p && target.GetProperty(p.Name) is null && portable.GetProperty(p.Name) is not null)
            {
                output.Append("    public ").Append(CsType(p.PropertyType)).Append(' ').Append(p.Name).Append(" { get => ").Append(expression).Append('.').Append(p.Name).Append(';');
                if (p.SetMethod?.IsPublic == true) output.Append(" set => ").Append(expression).Append('.').Append(p.Name).Append(" = value;");
                output.AppendLine(" }");
            }
            if (member is not MethodInfo m || portable.GetMethods().All(method => method.Name != m.Name)) continue;
            var parameters = m.GetParameters();
            var matching = target.GetMethods().Where(method => method.Name == m.Name).FirstOrDefault(method =>
                method.GetParameters().Length == parameters.Length && method.GetParameters().Zip(parameters).All(pair => TypeName(pair.First.ParameterType) == TypeName(pair.Second.ParameterType)));
            if (matching is not null) continue;
            string declaration = string.Join(", ", parameters.Select(p =>
                (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + CsType(p.ParameterType) + " " + p.Name + (p.IsOptional ? " = " + CsDefault(p) : "")));
            string args = string.Join(", ", parameters.Select(p => (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + p.Name));
            output.Append("    public ").Append(CsType(m.ReturnType)).Append(' ').Append(m.Name).Append('(').Append(declaration).Append(") => ").Append(expression).Append('.').Append(m.Name).Append('(').Append(args).AppendLine(");");
        }
        output.AppendLine("}");
        File.WriteAllText(path, output.ToString());
    }

    private static string CsType(Type type)
    {
        if (type.IsByRef) return CsType(type.GetElementType()!);
        if (type.IsArray) return CsType(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter) return type.Name;
        if (type == typeof(void)) return "void";
        if (type == typeof(Desktop.RuntimeInput)) return "AndroidScriptInputApi";
        if (type.Namespace == typeof(Desktop.RuntimeEntity).Namespace) return type.Name;
        return type.IsGenericType ? type.FullName!.Split('`')[0] + "<" + string.Join(",",type.GetGenericArguments().Select(CsType)) + ">" : type.FullName!;
    }

    internal static void NormalizeAndroidParameters(string apiPath,string aliasesPath)
    {
        var root=CSharpSyntaxTree.ParseText(File.ReadAllText(apiPath)).GetRoot();
        var aliases=new System.Text.StringBuilder("namespace Zhengyan.DigitalWife.GamePlayer.Android;\ninternal static partial class AndroidScriptParameterAliases\n{\n internal static readonly System.Collections.Generic.Dictionary<(string Type,string Method,string Parameter),string> Names=new()\n {\n");
        foreach(var pair in new[]{(typeof(Desktop.RuntimeEntity),typeof(Android.AndroidScriptEntity)),(typeof(Desktop.RuntimeCamera),typeof(Android.AndroidScriptCamera)),(typeof(Desktop.RuntimeAudio),typeof(Android.AndroidScriptAudio))})
        foreach(var method in pair.Item1.GetMethods().Where(m=>!m.IsSpecialName&&m.DeclaringType!=typeof(object)))
        {
            var expected=method.GetParameters();
            var target=pair.Item2.GetMethods().FirstOrDefault(m=>m.Name==method.Name&&m.GetParameters().Length==expected.Length&&m.GetParameters().Zip(expected).All(p=>TypeName(p.First.ParameterType)==TypeName(p.Second.ParameterType)));
            if(target is null)continue;
            var actual=target.GetParameters();var names=new Dictionary<string,string>();
            for(int i=0;i<actual.Length;i++)if(actual[i].Name!=expected[i].Name)names[actual[i].Name!]=expected[i].Name!;
            if(names.Count==0)continue;
            var declaration=root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(m=>m.Parent is ClassDeclarationSyntax c&&c.Identifier.ValueText==pair.Item2.Name&&m.Identifier.ValueText==target.Name&&m.ParameterList.Parameters.Select(p=>p.Identifier.ValueText).SequenceEqual(actual.Select(p=>p.Name)));
            if(declaration is not null)root=root.ReplaceNode(declaration,new ParameterRewriter(names).Visit(declaration)!);
            foreach(var name in names)aliases.AppendLine($"  {{({JsonSerializer.Serialize(pair.Item2.Name)},{JsonSerializer.Serialize(method.Name)},{JsonSerializer.Serialize(name.Key)}),{JsonSerializer.Serialize(name.Value)}}},");
        }
        aliases.AppendLine("  {(\"IAndroidScriptAsr\",\"StartStreamingRecognition\",\"entity\"),\"callbackTarget\"},\n  {(\"IAndroidScriptAsr\",\"StartWakeWordMonitoring\",\"entity\"),\"callbackTarget\"}\n };\n}");
        File.WriteAllText(apiPath,root.ToFullString());File.WriteAllText(aliasesPath,aliases.ToString());
    }
    private sealed class ParameterRewriter(Dictionary<string,string> names):CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitParameter(ParameterSyntax node)=>names.TryGetValue(node.Identifier.ValueText,out var name)?node.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(node.Identifier)):node;
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {if(node.Parent is MemberAccessExpressionSyntax member&&ReferenceEquals(member.Name,node))return node;return names.TryGetValue(node.Identifier.ValueText,out var name)?node.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(node.Identifier)):node;}
    }

    private static string CsDefault(ParameterInfo parameter)
    {
        object? value=parameter.DefaultValue;
        if(value is null)return "null";
        if(value is string text)return JsonSerializer.Serialize(text);
        if(value is bool boolean)return boolean?"true":"false";
        string number=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)!;
        return value is float ? number+"f" : number;
    }
}
