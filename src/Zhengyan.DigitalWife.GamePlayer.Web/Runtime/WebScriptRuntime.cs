using System.Reflection;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebScriptLoadDiagnostic(string ScriptId, string Message, bool IsError);

public sealed record WebLoadedScript(
    WebScriptEntry Manifest,
    Assembly Assembly,
    IWebGameScript? Module,
    WebScriptContext Context);

public sealed class WebScriptRuntime
{
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
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!string.Equals(entry.ApiVersion, manifest.ApiVersion, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Script API version {entry.ApiVersion} is incompatible with runtime {manifest.ApiVersion}.");
                byte[] bytes = await assets.ReadAllBytesAsync(entry.Assembly, cancellationToken);
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

    public void Update(float deltaSeconds, double elapsedSeconds, RuntimeCameraInput input)
    {
        foreach (WebLoadedScript script in _scripts)
        {
            if (script.Module is null) continue;
            script.Context.UpdateFrame(elapsedSeconds, input);
            try
            {
                script.Module.Update(script.Context, deltaSeconds);
            }
            catch (Exception exception)
            {
                _diagnostics.Add(new WebScriptLoadDiagnostic(
                    script.Manifest.Id,
                    $"Update failed: {exception.Message}",
                    IsError: true));
            }
        }
    }

    public void DispatchEvent(WebScriptEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        foreach (WebLoadedScript script in _scripts)
        {
            if (script.Module is null) continue;
            try { script.Module.OnEvent(script.Context, gameEvent); }
            catch (Exception exception)
            {
                _diagnostics.Add(new WebScriptLoadDiagnostic(script.Manifest.Id, $"Event '{gameEvent.Name}' failed: {exception.Message}", true));
            }
        }
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

