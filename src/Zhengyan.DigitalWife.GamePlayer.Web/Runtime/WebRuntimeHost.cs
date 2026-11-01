using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

/// <summary>Browser host for the shared scene, PMX pose evaluator and WebGL2 renderer.</summary>
public sealed class WebRuntimeHost(IJSRuntime jsRuntime, WebProjectLoader projectLoader) : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private WebProjectLoader _projectLoader = projectLoader;
    private IJSObjectReference? _module;
    private DotNetObjectReference<WebRuntimeHost>? _dotNetReference;
    private bool _started;
    private bool _disposed;
    private double _lastTimestampMilliseconds = double.NaN;
    private WebLoadedProject? _loadedProject;
    private RuntimeCameraInput _pendingInput;
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public WebRuntimeSnapshot Snapshot { get; private set; } = WebRuntimeSnapshot.Initial;
    public event Action? Changed;

    public async Task LoadDwGameAsync(byte[] packageBytes, ElementReference canvas, ElementReference overlayCanvas, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(packageBytes);
        if (_started) await StopAsync();
        WebPackageLoadResult package = await WebDwGamePackageLoader.LoadAsync(packageBytes, password);
        _projectLoader = new WebProjectLoader(package.Store);
        await StartAsync(canvas, overlayCanvas);
    }

    public async ValueTask StartAsync(ElementReference canvas, ElementReference overlayCanvas)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;

        try
        {
            _module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/web-runtime.js");
            _dotNetReference = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("start", canvas, overlayCanvas, _dotNetReference);
            await _module.InvokeVoidAsync("registerServiceWorker", "./sw.js");
            _started = true;
            WebGl2Info rendererInfo = await _module.InvokeAsync<WebGl2Info>("enableWebGl2");
            await _module.InvokeVoidAsync("log", "info", $"WebGL2 renderer initialized: {rendererInfo.Renderer}");
            WebGpuCapability gpu = await _module.InvokeAsync<WebGpuCapability>("detectWebGpu");
            await _module.InvokeVoidAsync("log", "info", gpu.Available
                ? $"WebGPU capability detected: {gpu.Name}"
                : $"WebGPU unavailable: {gpu.Reason}");

            Progress<WebLoadProgress> progress = new(value => SetSnapshot(Snapshot with
            {
                LoadingProgress = value.Value,
                LoadingStage = value.Stage,
                LoadingMessage = value.Message,
                Status = value.Message
            }));
            WebLoadedProject loadedProject = await _projectLoader.LoadAsync(progress);
            _loadedProject = loadedProject;
            WebPmxAsset? firstAsset = loadedProject.PmxAssets.Values.FirstOrDefault();
            if (loadedProject.PmxAssets.Count > 0)
            {
                WebPmxMeshInstance[] meshes = loadedProject.PmxAssets.Values
                    .Select(asset => new WebPmxMeshInstance(asset.Entity.Id, asset.Mesh))
                    .ToArray();
                await _module.InvokeVoidAsync("loadPmxMeshes", meshes);
                await UpdateRendererStateAsync(loadedProject);
            }
            await _module.InvokeVoidAsync("setOverlayState", new { controls = Array.Empty<object>(), sprites = Array.Empty<object>(), bubbles = Array.Empty<object>() });

            SetSnapshot(Snapshot with
            {
                IsRunning = true,
                RendererReady = true,
                RendererName = rendererInfo.Renderer,
                SceneReady = true,
                SceneName = loadedProject.RuntimeScene.Name,
                SceneEntityCount = loadedProject.RuntimeScene.Entities.Count(),
                PmxEntityCount = loadedProject.PmxAssets.Count,
                PmxMeshReady = firstAsset is not null,
                PmxModelName = firstAsset?.Mesh.ModelName ?? "Not loaded",
                PmxVertexCount = firstAsset?.Mesh.Positions.Length / 3 ?? 0,
                PmxIndexCount = firstAsset?.Mesh.Indices.Length ?? 0,
                LoadingProgress = 1.0f,
                LoadingStage = "ready",
                LoadingMessage = "Web scene ready",
                ScriptDiagnosticCount = loadedProject.Scripts.Diagnostics.Count,
                PhysicsBackend = firstAsset?.PhysicsBackend ?? "disabled",
                Status = "Web runtime running",
                LastError = null
            });
        }
        catch (Exception exception)
        {
            if (_started) await StopAsync();
            await SetErrorAsync("Web runtime startup failed", exception);
        }
    }

    public async ValueTask StopAsync()
    {
        if (!_started) return;
        try
        {
            if (_module is not null) await _module.InvokeVoidAsync("stop");
        }
        catch (JSDisconnectedException) { }
        finally
        {
            _started = false;
            _lastTimestampMilliseconds = double.NaN;
            if (_loadedProject is not null)
            {
                foreach (WebPmxAsset asset in _loadedProject.PmxAssets.Values) asset.Dispose();
                _loadedProject.RuntimeScene.Dispose();
            }
            _loadedProject = null;
            _pendingInput = RuntimeCameraInput.None;
            _keys.Clear();
            SetSnapshot(Snapshot with
            {
                IsRunning = false,
                RendererReady = false,
                RendererName = "Not initialized",
                SceneReady = false,
                SceneName = "Not loaded",
                SceneEntityCount = 0,
                PmxEntityCount = 0,
                PmxMeshReady = false,
                PmxModelName = "Not loaded",
                PmxVertexCount = 0,
                PmxIndexCount = 0,
                LoadingProgress = 0.0f,
                LoadingStage = "idle",
                LoadingMessage = "Web runtime stopped",
                ScriptDiagnosticCount = 0,
                PhysicsBackend = "disabled",
                Status = "Web runtime stopped"
            });
        }
    }

    [JSInvokable]
    public async Task OnAnimationFrame(double timestampMilliseconds)
    {
        if (_disposed || !_started || _loadedProject is null) return;
        double deltaSeconds = double.IsNaN(_lastTimestampMilliseconds)
            ? 0.0
            : Math.Clamp((timestampMilliseconds - _lastTimestampMilliseconds) / 1000.0, 0.0, 0.25);
        _lastTimestampMilliseconds = timestampMilliseconds;
        RuntimeCameraInput input = ConsumeInput();
        ApplyKeyboardCameraMotion((float)deltaSeconds);
        _loadedProject.RuntimeScene.Update((float)deltaSeconds, input);
        _loadedProject.Scripts.Update((float)deltaSeconds, timestampMilliseconds / 1000.0, input);

        WebPmxAsset[] assets = _loadedProject.PmxAssets.Values.ToArray();
        List<WebPmxMeshUpdateInstance> changedMeshes = [];
        foreach (WebPmxAsset asset in assets)
        {
            if (asset.Update(timestampMilliseconds / 1000.0, (float)deltaSeconds))
                changedMeshes.Add(new WebPmxMeshUpdateInstance(asset.Entity.Id, asset.Mesh.CreateUpdate()));
        }
        if (changedMeshes.Count > 0)
            await _module!.InvokeVoidAsync("updatePmxMeshes", changedMeshes);
        WebPmxAsset? firstAsset = assets.FirstOrDefault();
        await UpdateRendererStateAsync(_loadedProject);

        WebRuntimeSnapshot next = Snapshot with
        {
            FrameCount = Snapshot.FrameCount + 1,
            ElapsedSeconds = timestampMilliseconds / 1000.0,
            LastDeltaSeconds = deltaSeconds,
            ScriptDiagnosticCount = _loadedProject.Scripts.Diagnostics.Count,
            PhysicsBackend = firstAsset?.PhysicsBackend ?? "disabled",
            Status = Snapshot.RendererReady ? "WebGL2 renderer running" : Snapshot.Status
        };
        Snapshot = next;
        if (next.FrameCount == 1 || next.FrameCount % 10 == 0) Changed?.Invoke();
    }

    [JSInvokable]
    public Task OnPointerInput(float lookX, float lookY, float panX, float panY, float zoom, string pointerType, bool pressed)
    {
        _pendingInput = new RuntimeCameraInput(
            _pendingInput.LookDelta + new Vector2(lookX, lookY),
            _pendingInput.PanDelta + new Vector2(panX, panY),
            _pendingInput.ZoomDelta + zoom);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnKeyInput(string key, bool pressed)
    {
        if (string.IsNullOrWhiteSpace(key)) return Task.CompletedTask;
        if (pressed) _keys.Add(key); else _keys.Remove(key);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task SetGuiInputCapture(bool captured)
    {
        if (_module is not null) await _module.InvokeVoidAsync("setInputCapture", captured);
    }

    public async Task SetInputCaptureAsync(bool captured) => await SetGuiInputCapture(captured);

    [JSInvokable]
    public Task OnCanvasResized(int width, int height, double devicePixelRatio)
    {
        if (_disposed) return Task.CompletedTask;
        SetSnapshot(Snapshot with
        {
            CanvasWidth = Math.Max(width, 0),
            CanvasHeight = Math.Max(height, 0),
            DevicePixelRatio = Math.Max(devicePixelRatio, 1.0),
            Status = _started ? Snapshot.Status : "Canvas resized"
        });
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnBrowserError(string message)
    {
        SetSnapshot(Snapshot with
        {
            RendererReady = false,
            RendererName = "Unavailable",
            Status = "Browser or WebGL2 error",
            LastError = string.IsNullOrWhiteSpace(message) ? "Unknown browser error" : message
        });
        return Task.CompletedTask;
    }

    private RuntimeCameraInput ConsumeInput()
    {
        RuntimeCameraInput value = _pendingInput;
        _pendingInput = RuntimeCameraInput.None;
        return value;
    }

    private void ApplyKeyboardCameraMotion(float deltaSeconds)
    {
        if (_loadedProject is null || deltaSeconds <= 0) return;
        RuntimeCamera camera = _loadedProject.RuntimeScene.MainCamera;
        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        if (forward.LengthSquared() < 1e-8f) forward = -Vector3.UnitZ;
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        if (right.LengthSquared() < 1e-8f) right = Vector3.UnitX;
        Vector3 move = Vector3.Zero;
        if (_keys.Contains("KeyW") || _keys.Contains("ArrowUp")) move += forward;
        if (_keys.Contains("KeyS") || _keys.Contains("ArrowDown")) move -= forward;
        if (_keys.Contains("KeyA") || _keys.Contains("ArrowLeft")) move -= right;
        if (_keys.Contains("KeyD") || _keys.Contains("ArrowRight")) move += right;
        if (move.LengthSquared() <= 1e-8f) return;
        move = Vector3.Normalize(move) * camera.Settings.MoveSpeed * deltaSeconds;
        camera.Position += move;
        camera.Target += move;
    }

    private async Task UpdateRendererStateAsync(WebLoadedProject project)
    {
        if (_module is null) return;
        (RuntimeEntity Entity, WebPmxAsset Asset)[] models = project.PmxAssets.Values
            .Select(asset => (Entity: project.RuntimeScene.GetEntity(asset.Entity.Id), Asset: asset))
            .Where(item => item.Entity is not null)
            .Select(item => (item.Entity!, item.Asset))
            .ToArray();
        WebRenderFrame frame = WebRenderFrame.FromScene(project.RuntimeScene, models, Snapshot.CanvasWidth, Snapshot.CanvasHeight, Snapshot.ElapsedSeconds);
        await _module.InvokeVoidAsync("setFrameState", frame);
    }

    private async ValueTask SetErrorAsync(string status, Exception exception)
    {
        string message = exception.GetBaseException().Message;
        SetSnapshot(Snapshot with
        {
            IsRunning = false,
            RendererReady = false,
            RendererName = "Unavailable",
            Status = status,
            LastError = message
        });
        try
        {
            if (_module is not null) await _module.InvokeVoidAsync("log", "error", $"{status}: {message}");
        }
        catch (JSDisconnectedException) { }
    }

    private void SetSnapshot(WebRuntimeSnapshot snapshot)
    {
        Snapshot = snapshot;
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        _dotNetReference?.Dispose();
        _dotNetReference = null;
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
        _module = null;
    }
}
