using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed record RuntimeSceneChange(RuntimeScene? Previous, RuntimeScene? Current, string ScenePath);
public sealed record RuntimeSceneLoadFailure(string ScenePath, Exception Error);
public enum RuntimeSceneLoadState { Idle, Loading, Ready, Failed, Cancelled }
public sealed record RuntimeSceneLoadProgress(string ScenePath, RuntimeSceneLoadState State, float Value, string Message);

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
    public event Action<RuntimeSceneLoadProgress>? LoadProgressChanged;

    public RuntimeScene? Current { get; private set; }
    public string CurrentScenePath => Current?.ScenePath ?? string.Empty;
    public IReadOnlyList<string> ScenePaths => _project.Scenes;
    public RuntimeSceneLoadProgress LoadProgress { get; private set; } = new(string.Empty, RuntimeSceneLoadState.Idle, 0.0f, "Idle");

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
            FileNotFoundException error = new($"Scene is not registered in the project: {scenePath}");
            SetProgress(scenePath, RuntimeSceneLoadState.Failed, 0.0f, error.Message);
            SceneLoadFailed?.Invoke(new RuntimeSceneLoadFailure(scenePath, error));
            return false;
        }

        try
        {
            SetProgress(resolved, RuntimeSceneLoadState.Loading, 0.1f, "Reading scene");
            GameProjectScene definition = GameProjectStore.LoadScene(_projectDirectory, resolved);
            SetProgress(resolved, RuntimeSceneLoadState.Loading, 0.65f, "Creating runtime scene");
            RuntimeScene next = new(resolved, definition, path => GameProjectPath.ToAbsolute(_projectDirectory, path));
            CommitScene(resolved, definition, next);
            SetProgress(resolved, RuntimeSceneLoadState.Ready, 1.0f, "Scene ready");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            SetProgress(resolved, RuntimeSceneLoadState.Failed, 0.0f, ex.Message);
            SceneLoadFailed?.Invoke(new RuntimeSceneLoadFailure(resolved, ex));
            return false;
        }
    }

    public async Task<bool> LoadSceneAsync(string scenePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string? resolved = ResolveScenePath(scenePath);
        if (resolved is null)
        {
            FileNotFoundException error = new($"Scene is not registered in the project: {scenePath}");
            SetProgress(scenePath, RuntimeSceneLoadState.Failed, 0.0f, error.Message);
            SceneLoadFailed?.Invoke(new RuntimeSceneLoadFailure(scenePath, error));
            return false;
        }

        SetProgress(resolved, RuntimeSceneLoadState.Loading, 0.1f, "Reading scene");
        try
        {
            GameProjectScene definition = await Task.Run(
                () => GameProjectStore.LoadScene(_projectDirectory, resolved), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            SetProgress(resolved, RuntimeSceneLoadState.Loading, 0.65f, "Creating runtime scene");
            RuntimeScene next = new(resolved, definition, path => GameProjectPath.ToAbsolute(_projectDirectory, path));
            CommitScene(resolved, definition, next);
            SetProgress(resolved, RuntimeSceneLoadState.Ready, 1.0f, "Scene ready");
            return true;
        }
        catch (OperationCanceledException)
        {
            SetProgress(resolved, RuntimeSceneLoadState.Cancelled, 0.0f, "Scene load cancelled");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            SetProgress(resolved, RuntimeSceneLoadState.Failed, 0.0f, ex.Message);
            SceneLoadFailed?.Invoke(new RuntimeSceneLoadFailure(resolved, ex));
            return false;
        }
    }

    public void Update(float deltaSeconds) => Update(deltaSeconds, RuntimeCameraInput.None);

    public void Update(float deltaSeconds, RuntimeCameraInput input)
    {
        ThrowIfDisposed();
        if (!string.IsNullOrWhiteSpace(_pendingScene))
        {
            string pending = _pendingScene;
            _pendingScene = null;
            LoadScene(pending);
        }
        Current?.Update(deltaSeconds, input);
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

    private void CommitScene(string resolved, GameProjectScene definition, RuntimeScene next)
    {
        RuntimeScene? previous = Current;
        Current = next;
        _project.Scene = definition;
        _project.DefaultScene = resolved;
        SceneChanged?.Invoke(new RuntimeSceneChange(previous, next, resolved));
        previous?.Dispose();
    }

    private void SetProgress(string path, RuntimeSceneLoadState state, float value, string message)
    {
        LoadProgress = new RuntimeSceneLoadProgress(path, state, Math.Clamp(value, 0.0f, 1.0f), message);
        LoadProgressChanged?.Invoke(LoadProgress);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
