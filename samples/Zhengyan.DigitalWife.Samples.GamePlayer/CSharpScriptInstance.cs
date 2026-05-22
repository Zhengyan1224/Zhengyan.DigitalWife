using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System.Reflection;

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
                "System.Numerics",
                "Zhengyan.DigitalWife.Samples.GamePlayer");
    }

    public void Start(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: true, isUpdate: false, isGuiEvent: false, isLoadingEvent: false, isSpeechEvent: false, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty);
    }

    public void Update(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, double deltaSeconds)
    {
        Execute(entity, scene, input, audio, deltaSeconds, isStart: false, isUpdate: true, isGuiEvent: false, isLoadingEvent: false, isSpeechEvent: false, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, string.Empty);
    }

    public void GuiEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string controlId, string eventName)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: true, isLoadingEvent: false, isSpeechEvent: false, controlId, eventName, string.Empty, 0.0f, string.Empty, string.Empty);
    }

    public void LoadingEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string eventName, float progress, string message)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isLoadingEvent: true, isSpeechEvent: false, string.Empty, string.Empty, eventName, progress, message, string.Empty);
    }

    public void SpeechEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string callbackName)
    {
        Execute(entity, scene, input, audio, 0.0, isStart: false, isUpdate: false, isGuiEvent: false, isLoadingEvent: false, isSpeechEvent: true, string.Empty, string.Empty, string.Empty, 0.0f, string.Empty, callbackName);
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
        string guiControlId,
        string guiEventName,
        string loadingEventName,
        float loadingProgress,
        string loadingMessage,
        string speechCallbackName)
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
            GuiControlId = guiControlId,
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
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location));
    }
}
