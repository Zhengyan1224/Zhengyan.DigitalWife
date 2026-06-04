using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed class CSharpScriptInstance : IScriptInstance
{
    private readonly string _scriptPath;
    private readonly ScriptOptions _options;
    private ScriptRunner<object?>? _runner;

    public CSharpScriptInstance(string scriptPath)
    {
        _scriptPath = scriptPath;
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
                "Zhengyan.DigitalWife.Samples.GamePlayer");
    }

    public void Start(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: true, isUpdate: false, isGuiEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, null, null, null);
    }

    public void Update(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, double deltaSeconds)
    {
        Execute(entity, scene, input, audio, deltaSeconds, isStart: false, isUpdate: true, isGuiEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, null, null, null);
    }

    public void GuiEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string controlId, string controlName, string eventName)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: true, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, controlId, controlName, eventName, string.Empty, 0.0f, string.Empty, string.Empty, null, null, null);
    }

    public void LoadingEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string eventName, float progress, string message)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isLoadingEvent: true, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, eventName, progress, message, string.Empty, null, null, null);
    }

    public void SpeechEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string callbackName)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isLoadingEvent: false, isSpeechEvent: true, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, callbackName, null, null, null);
    }

    public void LlmEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeLlmScriptEvent llmEvent)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: true, isAsrEvent: false, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, llmEvent, null, null);
    }

    public void AsrEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeAsrScriptEvent asrEvent)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: true, isRealtimeVoiceEvent: false, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, null, asrEvent, null);
    }

    public void RealtimeVoiceEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeRealtimeVoiceScriptEvent realtimeVoiceEvent)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isLoadingEvent: false, isSpeechEvent: false, isLlmEvent: false, isAsrEvent: false, isRealtimeVoiceEvent: true, string.Empty, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty, null, null, realtimeVoiceEvent);
    }

    public void Dispose()
    {
    }

    private void Execute(
        RuntimeEntity entity,
        RuntimeScene scene,
        RuntimeInput input,
        RuntimeAudio audio,
        double deltaSeconds,
        bool isStart,
        bool isUpdate,
        bool isGuiEvent,
        bool isLoadingEvent,
        bool isSpeechEvent,
        bool isLlmEvent,
        bool isAsrEvent,
        bool isRealtimeVoiceEvent,
        string guiControlId,
        string guiControlName,
        string guiEventName,
        string loadingEventName,
        float loadingProgress,
        string loadingMessage,
        string speechCallbackName,
        RuntimeLlmScriptEvent? llmEvent,
        RuntimeAsrScriptEvent? asrEvent,
        RuntimeRealtimeVoiceScriptEvent? realtimeVoiceEvent)
    {
        _runner ??= CSharpScript
            .Create<object?>(File.ReadAllText(_scriptPath), _options, typeof(CSharpScriptGlobals))
            .CreateDelegate();

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
            LoadingEventName = loadingEventName,
            LoadingProgress = loadingProgress,
            LoadingMessage = loadingMessage,
            SpeechCallbackName = speechCallbackName
        };

        _runner(globals).GetAwaiter().GetResult();
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
