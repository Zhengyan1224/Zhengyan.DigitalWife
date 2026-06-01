using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeScene
{
    private readonly GameProjectScene _scene;
    private readonly IReadOnlyDictionary<string, RuntimeEntity> _entitiesById;
    private readonly IReadOnlyDictionary<string, RuntimeEntity> _entitiesByName;
    private readonly RuntimeWindowControl _window;
    private readonly RuntimeProjectControl _runtime;
    private readonly RuntimeCamera _camera;
    private readonly RuntimeDebug _debug;
    private readonly RuntimeSaveStore _save;
    private readonly RuntimeLlm _llm;
    private readonly RuntimeNetwork _network;
    private readonly RuntimePerformance _performance;
    private readonly Action<string> _requestSceneChange;
    private readonly Action<RuntimeEntity, string> _dispatchSpeechEvent;

    internal RuntimeScene(
        GameProjectScene scene,
        IReadOnlyDictionary<string, RuntimeEntity> entitiesById,
        IReadOnlyDictionary<string, RuntimeEntity> entitiesByName,
        RuntimeWindowControl window,
        RuntimeProjectControl runtime,
        RuntimeCamera camera,
        RuntimeDebug debug,
        RuntimeSaveStore save,
        RuntimeLlm llm,
        RuntimeNetwork network,
        RuntimePerformance performance,
        Action<RuntimeEntity, string> dispatchSpeechEvent,
        Action<string> requestSceneChange)
    {
        _scene = scene;
        _entitiesById = entitiesById;
        _entitiesByName = entitiesByName;
        _window = window;
        _runtime = runtime;
        _camera = camera;
        _debug = debug;
        _save = save;
        _llm = llm;
        _network = network;
        _performance = performance;
        _dispatchSpeechEvent = dispatchSpeechEvent;
        _requestSceneChange = requestSceneChange;
    }

    public string Name => _scene.Name;

    public IEnumerable<RuntimeEntity> Entities => _entitiesById.Values;

    public IEnumerable<RuntimeGuiControl> GuiControls => _scene.GuiControls.Select(control => new RuntimeGuiControl(control));

    public IEnumerable<RuntimeSpriteControl> Sprites => _scene.Sprites.Select(sprite => new RuntimeSpriteControl(sprite));

    public RuntimeWindowControl Window => _window;

    public RuntimeProjectControl Runtime => _runtime;

    public RuntimeCamera Camera => _camera;

    public RuntimeDebug Debug => _debug;

    public RuntimeSaveStore Save => _save;

    public RuntimeLlm Llm => _llm;

    public RuntimeNetwork Network => _network;

    public RuntimePerformance Performance => _performance;

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
        return sprite is null ? null : new RuntimeSpriteControl(sprite);
    }

    public void LoadScene(string scenePath)
    {
        _requestSceneChange(scenePath);
    }

    internal void DispatchSpeechEvent(RuntimeEntity entity, string callbackName)
    {
        _dispatchSpeechEvent(entity, callbackName);
    }
}
