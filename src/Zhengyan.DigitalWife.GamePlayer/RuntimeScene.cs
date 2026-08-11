using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeScene
{
    private readonly GameProjectScene _scene;
    private readonly IReadOnlyDictionary<string, RuntimeEntity> _entitiesById;
    private readonly IReadOnlyDictionary<string, RuntimeEntity> _entitiesByName;
    private readonly RuntimeWindowControl _window;
    private readonly RuntimeProjectControl _runtime;
    private readonly RuntimeCamera _camera;
    private readonly RuntimeScenePhysics _physics;
    private readonly RuntimeSceneNavigation _navigation;
    private readonly RuntimeDebug _debug;
    private readonly RuntimeSaveStore _save;
    private readonly RuntimeLlm _llm;
    private readonly RuntimeAsr _asr;
    private readonly RuntimeRealtimeVoice _realtimeVoice;
    private readonly RuntimeDialogueBubbleManager _bubble;
    private readonly RuntimeNetwork _network;
    private readonly RuntimePerformance _performance;
    private readonly RuntimePointLightCollection _pointLights;
    private readonly RuntimeSpotLightCollection _spotLights;
    private readonly RuntimeLighting _lighting;
    private readonly Action<string> _requestSceneChange;
    private readonly Action<RuntimeEntity, string> _dispatchSpeechEvent;
    private readonly Func<RuntimeEntity, string, RuntimeLlmToolCall, Task<string?>> _invokeLlmTool;

    internal RuntimeScene(
        GameProjectScene scene,
        IReadOnlyDictionary<string, RuntimeEntity> entitiesById,
        IReadOnlyDictionary<string, RuntimeEntity> entitiesByName,
        RuntimeWindowControl window,
        RuntimeProjectControl runtime,
        RuntimeCamera camera,
        RuntimeScenePhysics physics,
        RuntimeSceneNavigation navigation,
        RuntimeDebug debug,
        RuntimeSaveStore save,
        RuntimeLlm llm,
        RuntimeAsr asr,
        RuntimeRealtimeVoice realtimeVoice,
        RuntimeDialogueBubbleManager bubble,
        RuntimeNetwork network,
        RuntimePerformance performance,
        Action lightingChanged,
        Func<string?, string, System.Numerics.Vector3, System.Numerics.Vector3, float, float, bool, RuntimeEntity> addPointLight,
        Func<string, bool> removePointLight,
        Func<string?, string, System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3, float, float, float, float, bool, RuntimeEntity> addSpotLight,
        Func<string, bool> removeSpotLight,
        Action<RuntimeEntity, string> dispatchSpeechEvent,
        Func<RuntimeEntity, string, RuntimeLlmToolCall, Task<string?>> invokeLlmTool,
        Action<string> requestSceneChange)
    {
        _scene = scene;
        _entitiesById = entitiesById;
        _entitiesByName = entitiesByName;
        _window = window;
        _runtime = runtime;
        _camera = camera;
        _physics = physics;
        _navigation = navigation;
        _debug = debug;
        _save = save;
        _llm = llm;
        _asr = asr;
        _realtimeVoice = realtimeVoice;
        _bubble = bubble;
        _network = network;
        _performance = performance;
        _lighting = new RuntimeLighting(scene.Lighting, lightingChanged);
        _pointLights = new RuntimePointLightCollection(() => _entitiesById.Values, addPointLight, removePointLight);
        _spotLights = new RuntimeSpotLightCollection(() => _entitiesById.Values, addSpotLight, removeSpotLight);
        _dispatchSpeechEvent = dispatchSpeechEvent;
        _invokeLlmTool = invokeLlmTool;
        _requestSceneChange = requestSceneChange;
    }

    public string Name => _scene.Name;

    public IEnumerable<RuntimeEntity> Entities => _entitiesById.Values;

    public IEnumerable<RuntimeGuiControl> GuiControls => _scene.GuiControls.Select(control => new RuntimeGuiControl(control));

    public IEnumerable<RuntimeSpriteControl> Sprites => _scene.Sprites.Select(sprite => new RuntimeSpriteControl(sprite, _window));

    public RuntimeWindowControl Window => _window;

    public RuntimeProjectControl Runtime => _runtime;

    public RuntimeCamera Camera => _camera;

    public RuntimeScenePhysics Physics => _physics;

    public RuntimeSceneNavigation Navigation => _navigation;

    public RuntimeDebug Debug => _debug;

    public RuntimeSaveStore Save => _save;

    public RuntimeLlm Llm => _llm;

    public RuntimeAsr Asr => _asr;

    public RuntimeRealtimeVoice RealtimeVoice => _realtimeVoice;

    public RuntimeDialogueBubbleManager Bubble => _bubble;

    public RuntimeNetwork Network => _network;

    public RuntimePerformance Performance => _performance;

    public RuntimePointLightCollection PointLights => _pointLights;

    public RuntimeSpotLightCollection SpotLights => _spotLights;

    public RuntimeLighting Lighting => _lighting;

    public double Fps => _performance.Fps;

    public double RawFps => _performance.RawFps;

    public double DeltaSeconds => _performance.DeltaSeconds;

    public long FrameCount => _performance.FrameCount;

    public string RenderTexture(string renderTextureName) => _camera.RenderTexture(renderTextureName);

    public RuntimeEntity? GetEntity(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return null;
        }

        return _entitiesById.TryGetValue(idOrName, out RuntimeEntity? byId)
            ? byId
            : _entitiesByName.TryGetValue(idOrName, out RuntimeEntity? byName)
                ? byName
                : null;
    }

    public RuntimeGuiControl? GetGuiControl(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return null;
        }

        GuiControlSettings? control = _scene.GuiControls.FirstOrDefault(item =>
            string.Equals(item.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase));
        return control is null ? null : new RuntimeGuiControl(control);
    }

    public RuntimeSpriteControl? GetSprite(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return null;
        }

        SpriteSettings? sprite = _scene.Sprites.FirstOrDefault(item =>
            string.Equals(item.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase));
        return sprite is null ? null : new RuntimeSpriteControl(sprite, _window);
    }

    public void LoadScene(string scenePath)
    {
        _requestSceneChange(scenePath);
    }

    internal void DispatchSpeechEvent(RuntimeEntity entity, string callbackName)
    {
        _dispatchSpeechEvent(entity, callbackName);
    }

    internal Task<string?> InvokeLlmToolAsync(RuntimeEntity entity, string callbackName, RuntimeLlmToolCall toolCall)
    {
        return _invokeLlmTool(entity, callbackName, toolCall);
    }
}
