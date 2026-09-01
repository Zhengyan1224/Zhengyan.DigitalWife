using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class CSharpScriptInstance : IScriptInstance
{
    private readonly string _scriptPath;
    private readonly string _projectDirectory;
    private readonly ScriptOptions _options;
    private ScriptRunner<object?>? _runner;
    private MethodInfo? _precompiledFactory;
    private bool _precompiledChecked;

    public CSharpScriptInstance(string scriptPath, string projectDirectory)
    {
        _scriptPath = scriptPath;
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _options = ScriptOptions.Default
            .WithFilePath(scriptPath)
            .WithSourceResolver(new SourceFileResolver([], Path.GetDirectoryName(scriptPath)!))
            .WithReferences(GetScriptReferences())
            .WithImports(
                "System",
                "System.Collections.Generic",
                "System.Globalization",
                "System.IO",
                "System.Linq",
                "System.Net",
                "System.Net.Http",
                "System.Net.Sockets",
                "System.Numerics",
                "System.Text",
                "System.Text.Json",
                "System.Text.RegularExpressions",
                "System.Threading",
                "System.Threading.Tasks",
                "Zhengyan.DigitalWife.Mmd.Game.Pmx",
                "Zhengyan.DigitalWife.GamePlayer");
    }

    public void Start(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: true, isUpdate: false, isGuiEvent: false, isSpriteEvent: false, isTrayMenuEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, null, null, null);
    }

    public void Update(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, double deltaSeconds)
    {
        Execute(entity, scene, input, audio, deltaSeconds, isStart: false, isUpdate: true, isGuiEvent: false, isSpriteEvent: false, isTrayMenuEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, null, null, null);
    }

    public void GuiEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string controlId, string controlName, string eventName)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: true, isSpriteEvent: false, isTrayMenuEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, controlId, controlName, eventName, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, null, null, null);
    }

    public void SpriteEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string spriteId, string spriteName, string eventName)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isSpriteEvent: true, isTrayMenuEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, spriteId, spriteName, eventName, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, null, null, null);
    }

    public void TrayMenuEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string itemId, string itemText, string eventName)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isSpriteEvent: false, isTrayMenuEvent: true, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, itemId, itemText, eventName, string.Empty, 0.0f, string.Empty, string.Empty, null, null, null);
    }

    public void LoadingEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string eventName, float progress, string message)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isSpriteEvent: false, isTrayMenuEvent: false, isLoadingEvent: true, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, eventName, progress, message, string.Empty, null, null, null);
    }

    public void SpeechEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string callbackName)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isSpriteEvent: false, isTrayMenuEvent: false, isLoadingEvent: false, isSpeechEvent: true, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, callbackName, null, null, null);
    }

    public void LlmEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeLlmScriptEvent llmEvent)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isSpriteEvent: false, isTrayMenuEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: true, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, llmEvent, null, null);
    }

    public string? InvokeLlmTool(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeLlmScriptEvent llmEvent)
    {
        object? result = Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isSpriteEvent: false, isTrayMenuEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: true, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, llmEvent, null, null);
        return result switch
        {
            null => null,
            string text => text,
            _ => JsonSerializer.Serialize(result)
        };
    }

    public void AsrEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeAsrScriptEvent asrEvent)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isSpriteEvent: false, isTrayMenuEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: true, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, null, asrEvent, null);
    }

    public void RealtimeVoiceEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeRealtimeVoiceScriptEvent realtimeVoiceEvent)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isSpriteEvent: false, isTrayMenuEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: true, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, null, null, realtimeVoiceEvent);
    }

    public void Dispose()
    {
        _runner = null;
        _precompiledFactory = null;
    }

    private object? Execute(
        RuntimeEntity entity,
        RuntimeScene scene,
        RuntimeInput input,
        RuntimeAudio audio,
        double deltaSeconds,
        bool isStart,
        bool isUpdate,
        bool isGuiEvent,
        bool isSpriteEvent,
        bool isTrayMenuEvent,
        bool isLoadingEvent,
        bool isSpeechEvent,
        bool isLlmEvent,
        bool isAsrEvent,
        bool isRealtimeVoiceEvent,
        string guiControlId,
        string guiControlName,
        string guiEventName,
        string spriteId,
        string spriteName,
        string spriteEventName,
        string trayMenuItemId,
        string trayMenuItemText,
        string trayMenuEventName,
        string loadingEventName,
        float loadingProgress,
        string loadingMessage,
        string speechCallbackName,
        RuntimeLlmScriptEvent? llmEvent,
        RuntimeAsrScriptEvent? asrEvent,
        RuntimeRealtimeVoiceScriptEvent? realtimeVoiceEvent)
    {
        CSharpScriptGlobals globals = new()
        {
            Entity = entity,
            Scene = scene,
            Input = input,
            Audio = audio,
            DeltaSeconds = deltaSeconds,
            IsStart = isStart,
            IsUpdate = isUpdate,
            IsGuiEvent = isGuiEvent,
            IsSpriteEvent = isSpriteEvent,
            IsTrayMenuEvent = isTrayMenuEvent,
            IsLoadingEvent = isLoadingEvent,
            IsSpeechEvent = isSpeechEvent,
            IsLlmEvent = isLlmEvent,
            IsAsrEvent = isAsrEvent,
            IsRealtimeVoiceEvent = isRealtimeVoiceEvent,
            LlmEvent = llmEvent,
            AsrEvent = asrEvent,
            RealtimeVoiceEvent = realtimeVoiceEvent,
            GuiControlId = guiControlId,
            GuiControlName = guiControlName,
            GuiEventName = guiEventName,
            SpriteId = spriteId,
            SpriteName = spriteName,
            SpriteEventName = spriteEventName,
            TrayMenuItemId = trayMenuItemId,
            TrayMenuItemText = trayMenuItemText,
            TrayMenuEventName = trayMenuEventName,
            LoadingEventName = loadingEventName,
            LoadingProgress = loadingProgress,
            LoadingMessage = loadingMessage,
            SpeechCallbackName = speechCallbackName
        };

        if (!_precompiledChecked)
        {
            _precompiledChecked = true;
            _precompiledFactory = TryLoadPrecompiledFactory();
        }

        if (_precompiledFactory is not null)
        {
            object?[] submissionArray = [globals, null];
            Task<object?> task = (Task<object?>)_precompiledFactory.Invoke(null, [submissionArray])!;
            return task.GetAwaiter().GetResult();
        }

        _runner ??= CSharpScript
            .Create<object?>(File.ReadAllText(_scriptPath), _options, typeof(CSharpScriptGlobals))
            .CreateDelegate();

        return _runner(globals).GetAwaiter().GetResult();
    }

    private MethodInfo? TryLoadPrecompiledFactory()
    {
        string relative = Path.GetRelativePath(_projectDirectory, _scriptPath);
        if (relative.StartsWith("..", StringComparison.Ordinal)) return null;
        string assemblyPath = Path.Combine(_projectDirectory, "compiled", "desktop", Path.ChangeExtension(relative, ".dll"));
        if (!File.Exists(assemblyPath)) return null;

        try
        {
            Assembly assembly = Assembly.Load(File.ReadAllBytes(assemblyPath));
            string currentSourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(_scriptPath))));
            string expectedAssemblyName = "DesktopScript_" + currentSourceSha256;
            if (!string.Equals(assembly.GetName().Name, expectedAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[GamePlayer] Precompiled desktop C# script is stale; compiling source instead: '{_scriptPath}'.");
                return null;
            }

            Type? scriptType = assembly.GetType("Script");
            MethodInfo? factory = scriptType?.GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (factory is null)
            {
                Console.Error.WriteLine($"[GamePlayer] Precompiled desktop C# script has no execution factory: '{assemblyPath}'.");
            }
            else
            {
                Console.WriteLine($"[GamePlayer] Loaded precompiled desktop C# script: '{assemblyPath}'.");
            }

            return factory;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GamePlayer] Precompiled desktop C# script could not be loaded '{assemblyPath}': {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<Assembly> GetScriptReferences()
    {
        Assembly[] commonAssemblies =
        [
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Enumerable).Assembly,
            typeof(List<>).Assembly,
            typeof(StringBuilder).Assembly,
            typeof(JsonSerializer).Assembly,
            typeof(Regex).Assembly,
            typeof(System.Net.IPAddress).Assembly,
            typeof(System.Net.Http.HttpClient).Assembly,
            typeof(System.Net.Sockets.TcpClient).Assembly,
            typeof(System.Numerics.Vector3).Assembly,
            typeof(Zhengyan.DigitalWife.Llm.OpenAI.OpenAiCompatibleLlmClient).Assembly,
            typeof(RuntimeEntity).Assembly
        ];

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().Concat(commonAssemblies))
        {
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
            {
                continue;
            }

            if (seen.Add(assembly.Location))
            {
                yield return assembly;
            }
        }
    }
}
