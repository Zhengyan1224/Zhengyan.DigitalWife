using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Android.Util;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidCSharpScriptHost : IDisposable
{
    private readonly string _projectDirectory;
    private readonly Dictionary<string, ScriptRunner<object?>> _runners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _started = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public AndroidCSharpScriptHost(string projectDirectory)
    {
        _projectDirectory = projectDirectory;
    }

    public void Start(RuntimeScene scene)
    {
        foreach (RuntimeEntity entity in scene.Entities)
        {
            foreach (ScriptBinding binding in entity.Definition.Scripts.Where(IsSupported))
            {
                Execute(binding, new AndroidScriptGlobals(scene, entity, 0.0f, true));
            }
        }
    }

    public void Update(RuntimeScene scene, float deltaSeconds)
    {
        foreach (RuntimeEntity entity in scene.Entities)
        {
            foreach (ScriptBinding binding in entity.Definition.Scripts.Where(IsSupported))
            {
                Execute(binding, new AndroidScriptGlobals(scene, entity, deltaSeconds, false));
            }
        }
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

public sealed class AndroidScriptGlobals
{
    public AndroidScriptGlobals(RuntimeScene scene, RuntimeEntity entity, float deltaSeconds, bool isStart)
    {
        Scene = scene;
        Entity = entity;
        DeltaSeconds = deltaSeconds;
        IsStart = isStart;
    }

    public RuntimeScene Scene { get; }
    public RuntimeEntity Entity { get; }
    public float DeltaSeconds { get; }
    public bool IsStart { get; }
}
