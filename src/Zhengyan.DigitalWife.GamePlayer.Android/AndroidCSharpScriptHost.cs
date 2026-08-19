using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Android.Util;
using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidCSharpScriptHost : IDisposable
{
    private readonly string _projectDirectory;
    private readonly Action<string> _requestSceneChange;
    private readonly Func<RuntimeScene, string, bool> _playAudio;
    private readonly Func<string, bool> _stopAudio;
    private readonly Dictionary<string, ScriptRunner<object?>> _runners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _started = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public AndroidCSharpScriptHost(
        string projectDirectory,
        Action<string> requestSceneChange,
        Func<RuntimeScene, string, bool> playAudio,
        Func<string, bool> stopAudio)
    {
        _projectDirectory = projectDirectory;
        _requestSceneChange = requestSceneChange;
        _playAudio = playAudio;
        _stopAudio = stopAudio;
    }

    public void Start(RuntimeScene scene)
    {
        foreach (RuntimeEntity entity in scene.Entities)
        {
            foreach (ScriptBinding binding in entity.Definition.Scripts.Where(IsSupported))
            {
                Execute(binding, CreateGlobals(scene, entity, 0.0f, true));
            }
        }
    }

    public void Update(RuntimeScene scene, float deltaSeconds)
    {
        foreach (RuntimeEntity entity in scene.Entities)
        {
            foreach (ScriptBinding binding in entity.Definition.Scripts.Where(IsSupported))
            {
                Execute(binding, CreateGlobals(scene, entity, deltaSeconds, false));
            }
        }
    }

    public void DispatchEvent(RuntimeScene scene, AndroidRuntimeEvent runtimeEvent)
    {
        if (_disposed)
        {
            return;
        }

        RuntimeEntity? target = scene.GetEntity(runtimeEvent.TargetEntity);
        IEnumerable<RuntimeEntity> entities = target is null ? scene.Entities : [target];
        foreach (RuntimeEntity entity in entities)
        {
            foreach (ScriptBinding binding in entity.Definition.Scripts.Where(IsSupported))
            {
                Execute(binding, CreateGlobals(scene, entity, 0.0f, false, runtimeEvent));
            }
        }
    }

    private AndroidScriptGlobals CreateGlobals(
        RuntimeScene scene,
        RuntimeEntity entity,
        float deltaSeconds,
        bool isStart,
        AndroidRuntimeEvent? runtimeEvent = null)
    {
        AndroidScriptServices services = new(
            scene,
            _requestSceneChange,
            name => _playAudio(scene, name),
            _stopAudio);
        return new AndroidScriptGlobals(scene, entity, deltaSeconds, isStart, runtimeEvent, services);
    }

    private void Execute(ScriptBinding binding, AndroidScriptGlobals globals)
    {
        string path = GameProjectPath.ToAbsolute(_projectDirectory, binding.Path);
        if (!File.Exists(path)) return;
        try
        {
            if (!_runners.TryGetValue(path, out ScriptRunner<object?>? runner))
            {
                ScriptOptions options = ScriptOptions.Default
                    .WithFilePath(path)
                    .WithReferences(typeof(RuntimeScene).Assembly, typeof(GameProject).Assembly)
                    .WithImports("System", "System.Numerics", "Zhengyan.DigitalWife.GameProjects", "Zhengyan.DigitalWife.GamePlayer.Runtime");
                runner = CSharpScript.Create<object?>(File.ReadAllText(path), options, typeof(AndroidScriptGlobals)).CreateDelegate();
                _runners[path] = runner;
            }

            runner(globals).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("ZhengyanGamePlayer", $"Android C# script failed '{path}': {ex.GetBaseException().Message}");
        }
    }

    private static bool IsSupported(ScriptBinding binding)
    {
        return binding.Enabled
            && (string.Equals(binding.Language, "csharp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(binding.Language, "csx", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runners.Clear();
        _started.Clear();
    }
}

public sealed record AndroidRuntimeEvent(
    string Type,
    string Id,
    string EventName,
    Vector2 Position,
    string Text = "",
    string TargetEntity = "");

public sealed class AndroidScriptGlobals
{
    public AndroidScriptGlobals(
        RuntimeScene scene,
        RuntimeEntity entity,
        float deltaSeconds,
        bool isStart,
        AndroidRuntimeEvent? runtimeEvent = null,
        AndroidScriptServices? services = null)
    {
        Scene = scene;
        Entity = entity;
        DeltaSeconds = deltaSeconds;
        IsStart = isStart;
        Event = runtimeEvent;
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public RuntimeScene Scene { get; }
    public RuntimeEntity Entity { get; }
    public float DeltaSeconds { get; }
    public bool IsStart { get; }

    /// <summary>非 null 时表示一次 GUI/Sprite/触摸事件；Start/Update 时为 null。</summary>
    public AndroidRuntimeEvent? Event { get; }

    public bool IsEvent => Event is not null;

    public AndroidScriptServices Services { get; }
}

public sealed class AndroidScriptServices
{
    private readonly RuntimeScene _scene;
    private readonly Action<string> _requestSceneChange;
    private readonly Func<string, bool> _playAudio;
    private readonly Func<string, bool> _stopAudio;

    internal AndroidScriptServices(
        RuntimeScene scene,
        Action<string> requestSceneChange,
        Func<string, bool> playAudio,
        Func<string, bool> stopAudio)
    {
        _scene = scene;
        _requestSceneChange = requestSceneChange;
        _playAudio = playAudio;
        _stopAudio = stopAudio;
    }

    public RuntimeEntity? FindEntity(string idOrName) => _scene.GetEntity(idOrName);
    public RuntimeEntity AddPointLight(string name, Vector3 position, Vector3 color, float intensity = 1, float range = 8)
        => _scene.AddPointLight(name, position, color, intensity, range);
    public RuntimeEntity AddSpotLight(string name, Vector3 position, Vector3 rotation, Vector3 color, float intensity = 1, float range = 12)
        => _scene.AddSpotLight(name, position, rotation, color, intensity, range);
    public bool RemoveEntity(string idOrName) => _scene.RemoveEntity(idOrName);
    public void ChangeScene(string path) => _requestSceneChange(path);
    public bool PlayAudio(string idOrName) => _playAudio(idOrName);
    public bool StopAudio(string idOrName) => _stopAudio(idOrName);
}
