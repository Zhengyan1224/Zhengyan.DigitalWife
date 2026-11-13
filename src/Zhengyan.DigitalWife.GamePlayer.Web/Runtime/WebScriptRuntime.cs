using System.Reflection;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebScriptLoadDiagnostic(string ScriptId, string Message, bool IsError);

public sealed record WebLoadedScript(
    WebScriptEntry Manifest,
    Assembly Assembly,
    IWebGameScript? Module,
    WebScriptContext Context)
{
    public Task UpdateTask { get; set; } = Task.CompletedTask;
    public Task EventTask { get; set; } = Task.CompletedTask;
}

public sealed class WebScriptRuntime : IDisposable, IAsyncDisposable
{
    private bool _disposed;
    public Func<WebScriptEntry,string,IWebGameScript>? PythonFactory { get; set; }
    private readonly List<WebLoadedScript> _scripts = [];

    public IReadOnlyList<WebLoadedScript> Scripts => _scripts;
    public IReadOnlyList<WebScriptLoadDiagnostic> Diagnostics => _diagnostics;
    private readonly List<WebScriptLoadDiagnostic> _diagnostics = [];

    public async Task LoadAsync(
        IWebAssetStore assets,
        WebAssetManifest manifest,
        RuntimeScene scene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(scene);

        manifest.Validate();
        if (!WebManifestSignature.Verify(manifest))
        {
            _diagnostics.Add(new WebScriptLoadDiagnostic("manifest", "Web manifest signature is invalid.", true));
            return;
        }

        ReportMissingBindings(manifest, scene);

        IReadOnlyList<WebScriptEntry> orderedEntries;
        try
        {
            orderedEntries = OrderEntries(manifest.Scripts).ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            _diagnostics.Add(new WebScriptLoadDiagnostic("manifest", $"Web script dependency graph is invalid: {exception.Message}", true));
            return;
        }

        foreach (WebScriptEntry entry in orderedEntries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Scene) && !string.Equals(entry.Scene, scene.ScenePath, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(entry.Entity) && scene.GetEntity(entry.Entity) is null) continue;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!string.Equals(entry.ApiVersion, manifest.ApiVersion, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Script API version {entry.ApiVersion} is incompatible with runtime {manifest.ApiVersion}.");
                byte[] bytes = await assets.ReadAllBytesAsync(entry.Assembly, cancellationToken);
                if(entry.Language.Equals("python",StringComparison.OrdinalIgnoreCase))
                {
                    if(PythonFactory is null)throw new NotSupportedException("Python runtime is not configured.");
                    var pythonModule=PythonFactory(entry,System.Text.Encoding.UTF8.GetString(bytes));
                    _scripts.Add(new(entry,typeof(WebScriptRuntime).Assembly,pythonModule,new(scene,scene.GetEntity(entry.Entity))));continue;
                }
                Assembly assembly = Assembly.Load(bytes);
                WebScriptCompatibilityReport compatibility = WebScriptCompatibility.Analyze(assembly);
                if (!compatibility.IsCompatible)
                    throw new InvalidOperationException(compatibility.Message);
                IWebGameScript? module = CreateModule(assembly, entry);
                RuntimeEntity? entity = string.IsNullOrWhiteSpace(entry.Entity)
                    ? null
                    : scene.GetEntity(entry.Entity);
                WebLoadedScript loaded = new(entry, assembly, module, new WebScriptContext(scene, entity));
                _scripts.Add(loaded);
                if (module is null)
                {
                    _diagnostics.Add(new WebScriptLoadDiagnostic(
                        entry.Id,
                        "The assembly loaded, but its type/factory does not implement IWebGameScript.",
                        IsError: false));
                }
            }
            catch (Exception exception) when (exception is BadImageFormatException
                or FileLoadException
                or FileNotFoundException
                or InvalidOperationException
                or MissingMethodException
                or NotSupportedException
                or ReflectionTypeLoadException
                or TargetInvocationException
                or TypeLoadException)
            {
                _diagnostics.Add(new WebScriptLoadDiagnostic(
                    entry.Id,
                    $"Precompiled Web script could not be loaded: {exception.Message}",
                    IsError: true));
            }
        }
    }

    public void Start()
    {
        foreach (WebLoadedScript script in _scripts)
        {
            if (script.Module is null) continue;
            try
            {
                script.Module.Start(script.Context);
            }
            catch (Exception exception)
            {
                _diagnostics.Add(new WebScriptLoadDiagnostic(
                    script.Manifest.Id,
                    $"Start failed: {exception.Message}",
                    IsError: true));
            }
        }
    }

    public async Task StartAsync()
    {
        foreach (var script in _scripts)
        {
            if (script.Module is null) continue;
            try { await script.Module.StartAsync(script.Context); }
            catch (Exception exception) { _diagnostics.Add(new(script.Manifest.Id, "Start failed: " + exception.Message, true)); }
        }
    }

    public void Update(float deltaSeconds, double elapsedSeconds, RuntimeCameraInput input)
    {
        if(_disposed)return;
        foreach (WebLoadedScript script in _scripts)
        {
            if (script.Module is null) continue;
            if (!script.UpdateTask.IsCompleted || !script.EventTask.IsCompleted) continue;
            script.Context.UpdateFrame(elapsedSeconds, input);
            script.UpdateTask = RunAsync();
            async Task RunAsync()
            {
                try { await script.Module.UpdateAsync(script.Context, deltaSeconds); }
                catch (Exception exception) { _diagnostics.Add(new(script.Manifest.Id, "Update failed: " + exception.Message, true)); }
            }
        }
    }

    public IReadOnlyList<WebAudioCommand> DrainAudioCommands()
    {
        List<WebAudioCommand> commands = [];
        foreach (WebLoadedScript script in _scripts)
        {
            commands.AddRange(script.Context.DrainAudioCommands());
        }

        return commands;
    }

    public void DispatchEvent(WebScriptEvent gameEvent, string? targetEntityId = null)
    {
        if(_disposed)return;
        ArgumentNullException.ThrowIfNull(gameEvent);
        foreach (WebLoadedScript script in _scripts)
        {
            if (script.Module is null) continue;
            if (!string.IsNullOrWhiteSpace(targetEntityId)
                && !string.Equals(script.Context.Entity?.Id, targetEntityId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Task previous = script.EventTask;
            script.EventTask = RunAsync();
            async Task RunAsync()
            {
                await previous; await script.UpdateTask;
                if(_disposed)return;
                script.Context.SetEvent(gameEvent);
                try { await script.Module.OnEventAsync(script.Context, gameEvent); }
                catch (Exception exception) { _diagnostics.Add(new(script.Manifest.Id, $"Event '{gameEvent.Name}' failed: {exception.Message}", true)); }
                finally { script.Context.SetEvent(null); }
            }
        }
    }

    public void Dispose() { _disposed=true; }
    public async ValueTask DisposeAsync()
    {
        _disposed=true;
        foreach(var script in _scripts)
            if(script.Module is IAsyncDisposable asyncDisposable)await asyncDisposable.DisposeAsync();
            else if(script.Module is IDisposable disposable)disposable.Dispose();
        _scripts.Clear();
    }

    private static IEnumerable<WebScriptEntry> OrderEntries(IReadOnlyList<WebScriptEntry> entries)
    {
        Dictionary<string, WebScriptEntry> byId = entries.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        HashSet<string> visiting = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        List<WebScriptEntry> ordered = [];
        foreach (WebScriptEntry entry in entries)
        {
            Visit(entry);
        }
        return ordered;

        void Visit(WebScriptEntry entry)
        {
            if (visited.Contains(entry.Id)) return;
            if (!visiting.Add(entry.Id)) throw new InvalidDataException($"Circular Web script dependency: {entry.Id}");
            foreach (string dependency in entry.Dependencies)
            {
                if (!byId.TryGetValue(dependency, out WebScriptEntry? dependencyEntry))
                    throw new InvalidDataException($"Web script dependency is missing: {entry.Id} -> {dependency}");
                Visit(dependencyEntry);
            }
            visiting.Remove(entry.Id);
            visited.Add(entry.Id);
            ordered.Add(entry);
        }
    }

    private void ReportMissingBindings(WebAssetManifest manifest, RuntimeScene scene)
    {
        IEnumerable<(ScriptBinding Binding, string Entity)> bindings =
            scene.Definition.LoadingScripts.Select(binding => (binding, string.Empty))
                .Concat(scene.Entities.SelectMany(entity => entity.Definition.Scripts.Select(binding => (binding, entity.Id))));
        foreach (var (binding, entity) in bindings.Where(item => item.Binding.Enabled))
        {
            string source = binding.Path.Replace('\\', '/');
            if (source.StartsWith("project:", StringComparison.OrdinalIgnoreCase)) source = source[8..];
            bool python = binding.Language.Equals("python", StringComparison.OrdinalIgnoreCase);
            bool csharp = new[] { "csharp", "cs", "csx" }.Contains(binding.Language, StringComparer.OrdinalIgnoreCase)
                || source.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith(".csx", StringComparison.OrdinalIgnoreCase);
            if (!python && !csharp) continue;
            string assembly = python ? source : $"compiled/web/{Path.ChangeExtension(source, ".dll")}";
            if (manifest.Scripts.Any(entry =>
                (string.IsNullOrWhiteSpace(entry.Scene) || string.Equals(entry.Scene, scene.ScenePath, StringComparison.OrdinalIgnoreCase))
                && string.Equals(entry.Entity, entity, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Assembly.Replace('\\', '/'), assembly, StringComparison.OrdinalIgnoreCase))) continue;

            _diagnostics.Add(new(
                $"{scene.ScenePath}:{entity}:{source}",
                $"Enabled script '{source}' is missing from web.manifest.json (expected '{assembly}'). "
                    + "Recompile the game scripts and export the Web game again; see compiled/web/manifest.json for compilation errors.",
                IsError: true));
        }
    }

    private static IWebGameScript? CreateModule(Assembly assembly, WebScriptEntry entry)
    {
        Type? type = null;
        if (!string.IsNullOrWhiteSpace(entry.Type))
        {
            type = assembly.GetType(entry.Type, throwOnError: false, ignoreCase: false);
        }
        else if (!string.IsNullOrWhiteSpace(entry.Factory))
        {
            int separator = entry.Factory.LastIndexOf('.');
            if (separator > 0)
            {
                type = assembly.GetType(entry.Factory[..separator], throwOnError: false, ignoreCase: false);
            }
        }

        if (type is null)
        {
            type = assembly.GetTypes().FirstOrDefault(candidate =>
                typeof(IWebGameScript).IsAssignableFrom(candidate)
                && !candidate.IsAbstract
                && candidate.GetConstructor(Type.EmptyTypes) is not null);
        }

        if (type is null || !typeof(IWebGameScript).IsAssignableFrom(type)) return null;
        if (!string.IsNullOrWhiteSpace(entry.Factory))
        {
            int separator = entry.Factory.LastIndexOf('.');
            if (separator > 0)
            {
                string methodName = entry.Factory[(separator + 1)..];
                MethodInfo? factory = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (factory?.Invoke(null, null) is IWebGameScript created) return created;
            }
        }
        return Activator.CreateInstance(type) as IWebGameScript;
    }
}

