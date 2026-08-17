using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed record RuntimeSceneChange(RuntimeScene? Previous, RuntimeScene? Current, string ScenePath);
public sealed record RuntimeSceneLoadFailure(string ScenePath, Exception Error);

public sealed class RuntimeSceneManager : IDisposable
{
    private readonly GameProject _project;
    private readonly string _projectDirectory;
    private string? _pendingScene;
    private bool _disposed;

    public RuntimeSceneManager(GameProject project, string projectDirectory)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _projectDirectory = Path.GetFullPath(projectDirectory ?? throw new ArgumentNullException(nameof(projectDirectory)));
        GameProjectStore.NormalizeScenes(_project);
    }

    public event Action<RuntimeSceneChange>? SceneChanged;
    public event Action<RuntimeSceneLoadFailure>? SceneLoadFailed;

    public RuntimeScene? Current { get; private set; }
    public string CurrentScenePath => Current?.ScenePath ?? string.Empty;
    public IReadOnlyList<string> ScenePaths => _project.Scenes;

    public bool LoadInitial()
    {
        string path = ResolveScenePath(_project.DefaultScene)
            ?? _project.Scenes.FirstOrDefault()
            ?? string.Empty;
        return !string.IsNullOrWhiteSpace(path) && LoadScene(path);
    }

    public void RequestSceneChange(string scenePath)
    {
        ThrowIfDisposed();
        _pendingScene = scenePath;
    }

    public bool LoadScene(string scenePath)
    {
        ThrowIfDisposed();
        string? resolved = ResolveScenePath(scenePath);
        if (resolved is null)
        {
            SceneLoadFailed?.Invoke(new RuntimeSceneLoadFailure(scenePath,
                new FileNotFoundException($"Scene is not registered in the project: {scenePath}")));
            return false;
        }

        try
        {
            GameProjectScene definition = GameProjectStore.LoadScene(_projectDirectory, resolved);
            RuntimeScene next = new(resolved, definition, path => GameProjectPath.ToAbsolute(_projectDirectory, path));
            RuntimeScene? previous = Current;
            Current = next;
            _project.Scene = definition;
            _project.DefaultScene = resolved;
            SceneChanged?.Invoke(new RuntimeSceneChange(previous, next, resolved));
            previous?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            SceneLoadFailed?.Invoke(new RuntimeSceneLoadFailure(resolved, ex));
            return false;
        }
    }

    public void Update(float deltaSeconds)
    {
        ThrowIfDisposed();
        if (!string.IsNullOrWhiteSpace(_pendingScene))
        {
            string pending = _pendingScene;
            _pendingScene = null;
            LoadScene(pending);
        }
        Current?.Update(deltaSeconds);
    }

    public void Unload()
    {
        RuntimeScene? previous = Current;
        Current = null;
        if (previous is null) return;
        SceneChanged?.Invoke(new RuntimeSceneChange(previous, null, string.Empty));
        previous.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unload();
        _disposed = true;
    }

    private string? ResolveScenePath(string? scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath)) return null;
        string normalized = scenePath.Replace('\\', '/').Trim();
        return _project.Scenes.FirstOrDefault(path => string.Equals(path.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase))
            ?? _project.Scenes.FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), Path.GetFileNameWithoutExtension(normalized), StringComparison.OrdinalIgnoreCase));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
