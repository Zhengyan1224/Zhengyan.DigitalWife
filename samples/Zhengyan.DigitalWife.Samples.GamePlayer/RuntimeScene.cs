using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeScene
{
    private readonly GameProjectScene _scene;
    private readonly IReadOnlyDictionary<string, RuntimeEntity> _entitiesById;
    private readonly IReadOnlyDictionary<string, RuntimeEntity> _entitiesByName;
    private readonly RuntimeWindowControl _window;
    private readonly RuntimeCamera _camera;
    private readonly Action<string> _requestSceneChange;
    private readonly Action<RuntimeEntity, string> _dispatchSpeechEvent;

    internal RuntimeScene(
        GameProjectScene scene,
        IReadOnlyDictionary<string, RuntimeEntity> entitiesById,
        IReadOnlyDictionary<string, RuntimeEntity> entitiesByName,
        RuntimeWindowControl window,
        RuntimeCamera camera,
        Action<RuntimeEntity, string> dispatchSpeechEvent,
        Action<string> requestSceneChange)
    {
        _scene = scene;
        _entitiesById = entitiesById;
        _entitiesByName = entitiesByName;
        _window = window;
        _camera = camera;
        _dispatchSpeechEvent = dispatchSpeechEvent;
        _requestSceneChange = requestSceneChange;
    }

    public string Name => _scene.Name;

    public IEnumerable<RuntimeEntity> Entities => _entitiesById.Values;

    public IEnumerable<RuntimeGuiControl> GuiControls => _scene.GuiControls.Select(control => new RuntimeGuiControl(control));

    public IEnumerable<RuntimeSpriteControl> Sprites => _scene.Sprites.Select(sprite => new RuntimeSpriteControl(sprite));

    public RuntimeWindowControl Window => _window;

    public RuntimeCamera Camera => _camera;

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
