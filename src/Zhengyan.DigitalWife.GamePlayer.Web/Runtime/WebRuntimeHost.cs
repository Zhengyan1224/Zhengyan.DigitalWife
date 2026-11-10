using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

/// <summary>Browser host for the shared scene, PMX pose evaluator and WebGL2 renderer.</summary>
public sealed class WebRuntimeHost(
    IJSRuntime jsRuntime,
    WebProjectLoader projectLoader,
    WebAudioService audioService,
    WebSpeechService speechService,
    WebPmxPhysicsService? physicsService = null) : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private WebProjectLoader _projectLoader = projectLoader;
    private readonly WebAudioService _audioService = audioService;
    private readonly WebSpeechService _speechService = speechService;
    private readonly WebPmxPhysicsService _physicsService = physicsService ?? new WebPmxPhysicsService(jsRuntime);
    private readonly bool _ownsPhysicsService = physicsService is null;
    private IJSObjectReference? _module;
    private DotNetObjectReference<WebRuntimeHost>? _dotNetReference;
    private bool _started;
    private bool _disposed;
    private double _lastTimestampMilliseconds = double.NaN;
    private WebLoadedProject? _loadedProject;
    private RuntimeCameraInput _pendingInput;
    private Vector2 _pointerPosition;
    private Vector2 _pointerDelta;
    private int _pointerButtons;
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(string EntityId, RuntimeSpeechRequest Request)> _speechRequests = [];
    private readonly Dictionary<string, Action> _speechCompletions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _audioVolumes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _audioLoops = new(StringComparer.OrdinalIgnoreCase);
    private int _reportedScriptDiagnosticCount;

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
            _projectLoader.SpeechRequestHandler = QueueSpeechRequest;
            _projectLoader.PhysicsFactory = _physicsService.CreateAsync;
            SetSnapshot(Snapshot with { LastError = null, Status = "Initializing WebGL2" });
            _module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", WebRuntimeModuleUrl.Resolve("web-runtime.js"));
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

            IProgress<WebLoadProgress> progress = new LoadProgressReporter(value => SetSnapshot(Snapshot with
            {
                LoadingProgress = value.Value,
                LoadingStage = value.Stage,
                LoadingMessage = value.Message,
                Status = value.Message
            }));
            WebLoadedProject loadedProject = await _projectLoader.LoadAsync(progress);
            _loadedProject = loadedProject;
            foreach (WebAudioAsset audio in loadedProject.AudioAssets.Values.DistinctBy(asset => asset.Alias))
                await _audioService.PrepareAsync(audio.Alias, audio.Uri);
            await ProcessScriptCommandsAsync();
            WebPmxAsset? firstAsset = loadedProject.PmxAssets.Values.FirstOrDefault();
            if (loadedProject.PmxAssets.Count > 0 || loadedProject.SceneMeshes.Count > 0)
            {
                WebPmxMeshInstance[] meshes = loadedProject.PmxAssets.Values
                    .Select(asset => new WebPmxMeshInstance(asset.Entity.Id, asset.Mesh))
                    .Concat(loadedProject.SceneMeshes.Select(item => new WebPmxMeshInstance(item.Key, item.Value)))
                    .ToArray();
                // A reference-type array is also object[]: without this cast
                // params expands it into separate JS arguments, so the JS
                // loader receives only the first model as its meshes argument.
                await _module.InvokeVoidAsync("loadPmxMeshes", (object)meshes);
                await UpdateRendererStateAsync(loadedProject);
            }
            await UpdateOverlayStateAsync(loadedProject);

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
                Status = "Web runtime running"
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
            _speechRequests.Clear();
            _speechCompletions.Clear();
            _audioVolumes.Clear();
            _audioLoops.Clear();
            _reportedScriptDiagnosticCount = 0;
            try { await _audioService.StopAllAsync(); }
            catch (JSDisconnectedException) { }
            if (_loadedProject is not null)
            {
                foreach (WebPmxAsset asset in _loadedProject.PmxAssets.Values) asset.Dispose();
                _loadedProject.RuntimeScene.Dispose();
            }
            _loadedProject = null;
            _pendingInput = RuntimeCameraInput.None;
            _pointerPosition = Vector2.Zero;
            _pointerDelta = Vector2.Zero;
            _pointerButtons = 0;
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
    public Task OnAnimationFrame(double timestampMilliseconds) => OnAnimationFrameWithInput(timestampMilliseconds, null);

    [JSInvokable]
    public async Task OnAnimationFrameWithInput(double timestampMilliseconds, WebPointerFrameInput? pointerInput)
    {
        if (_disposed || !_started || !Snapshot.IsRunning || _loadedProject is null) return;
        if (pointerInput is not null)
        {
            AccumulatePointerInput(pointerInput.LookX, pointerInput.LookY, pointerInput.PanX, pointerInput.PanY,
                pointerInput.Zoom, pointerInput.PointerX, pointerInput.PointerY, pointerInput.Buttons);
        }
        double frameIntervalSeconds = double.IsNaN(_lastTimestampMilliseconds)
            ? 0.0
            : Math.Max((timestampMilliseconds - _lastTimestampMilliseconds) / 1000.0, 0.0);
        double deltaSeconds = Math.Min(frameIntervalSeconds, 0.25);
        _lastTimestampMilliseconds = timestampMilliseconds;
        RuntimeCameraInput input = ConsumeInput();
        ApplyKeyboardCameraMotion((float)deltaSeconds);
        _loadedProject.RuntimeScene.Update((float)deltaSeconds, input);
        _loadedProject.Scripts.Update((float)deltaSeconds, timestampMilliseconds / 1000.0, input);
        await ProcessScriptCommandsAsync();

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
        await UpdateOverlayStateAsync(_loadedProject);

        WebRuntimeSnapshot next = Snapshot with
        {
            FrameCount = Snapshot.FrameCount + 1,
            ElapsedSeconds = timestampMilliseconds / 1000.0,
            LastDeltaSeconds = frameIntervalSeconds,
            ScriptDiagnosticCount = _loadedProject.Scripts.Diagnostics.Count,
            PhysicsBackend = firstAsset?.PhysicsBackend ?? "disabled",
            Status = Snapshot.RendererReady ? "WebGL2 renderer running" : Snapshot.Status
        };
        Snapshot = next;
        if (next.FrameCount == 1 || next.FrameCount % 10 == 0) Changed?.Invoke();
    }

    [JSInvokable]
    public Task OnPointerInput(
        float lookX,
        float lookY,
        float panX,
        float panY,
        float zoom,
        string pointerType,
        bool pressed,
        float pointerX = 0.0f,
        float pointerY = 0.0f,
        int buttons = 0)
    {
        AccumulatePointerInput(lookX, lookY, panX, panY, zoom, pointerX, pointerY, buttons);
        return Task.CompletedTask;
    }

    private void AccumulatePointerInput(float lookX, float lookY, float panX, float panY, float zoom, float pointerX, float pointerY, int buttons)
    {
        _pendingInput = new RuntimeCameraInput(
            _pendingInput.LookDelta + new Vector2(lookX, lookY),
            _pendingInput.PanDelta + new Vector2(panX, panY),
            _pendingInput.ZoomDelta + zoom);
        _pointerDelta += new Vector2(lookX + panX, lookY + panY);
        _pointerPosition = new Vector2(pointerX, pointerY);
        _pointerButtons = buttons;
    }

    [JSInvokable]
    public Task OnKeyInput(string key, bool pressed)
    {
        if (string.IsNullOrWhiteSpace(key)) return Task.CompletedTask;
        if (pressed) _keys.Add(key); else _keys.Remove(key);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OnGuiPointer(float x, float y, string phase, int button)
    {
        if (_loadedProject is null
            || button != 0
            || !string.Equals(phase, "up", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        GuiControlSettings? control = ResolveGuiControl(x, y);
        if (control is null)
        {
            return;
        }

        string type = NormalizeGuiType(control.Type);
        if (type == "checkbox")
        {
            control.Checked = !control.Checked;
        }
        else if (type == "dropdown" && control.Items.Count > 0)
        {
            control.SelectedIndex = (control.SelectedIndex + 1) % control.Items.Count;
        }

        string eventName = string.IsNullOrWhiteSpace(control.EventName)
            ? type is "checkbox" or "dropdown" ? "changed" : "clicked"
            : control.EventName;
        string? targetEntityId = ResolveGuiTargetEntityId(control);
        _loadedProject.Scripts.DispatchEvent(
            new WebScriptEvent(
                "gui",
                PayloadJson: $"{{\"button\":{Math.Max(button, 0)}}}",
                EntityId: targetEntityId ?? string.Empty,
                ControlId: control.Id,
                ControlName: control.Name,
                EventName: eventName),
            targetEntityId);
        await ProcessScriptCommandsAsync();
        await UpdateOverlayStateAsync(_loadedProject);
    }

    [JSInvokable]
    public async Task OnSpritePointer(float x, float y, string phase, int button)
    {
        if (_loadedProject is null
            || button != 0
            || !string.Equals(phase, "up", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        float actualWidth = Math.Max(Snapshot.CanvasWidth, 1);
        float actualHeight = Math.Max(Snapshot.CanvasHeight, 1);
        float referenceWidth = Math.Max(_loadedProject.Project.Window.Width, 1);
        float referenceHeight = Math.Max(_loadedProject.Project.Window.Height, 1);
        SpriteSettings? sprite = _loadedProject.SceneDefinition.Sprites
            .Where(item => item.Visible && _loadedProject.SpriteUris.ContainsKey(item.Id))
            .OrderByDescending(item => item.DrawOrder)
            .ThenByDescending(item => _loadedProject.SceneDefinition.Sprites.IndexOf(item))
            .FirstOrDefault(item => SpriteLayoutResolver.ContainsPoint(
                item,
                x,
                y,
                actualWidth,
                actualHeight,
                referenceWidth,
                referenceHeight));
        if (sprite is null || string.IsNullOrWhiteSpace(sprite.TargetEntity))
        {
            return;
        }

        string? targetEntityId = _loadedProject.RuntimeScene.GetEntity(sprite.TargetEntity)?.Id;
        if (string.IsNullOrWhiteSpace(targetEntityId)) return;
        _loadedProject.Scripts.DispatchEvent(
            new WebScriptEvent(
                "sprite",
                PayloadJson: $"{{\"button\":{Math.Max(button, 0)}}}",
                EntityId: targetEntityId,
                SpriteId: sprite.Id,
                SpriteName: sprite.Name,
                SpriteEventName: "clicked"),
            targetEntityId);
        await ProcessScriptCommandsAsync();
    }

    [JSInvokable]
    public async Task OnSpeechCompleted(string entityId, string callbackName)
    {
        if (!string.IsNullOrWhiteSpace(callbackName)
            && _speechCompletions.Remove(callbackName, out Action? completion))
        {
            completion();
            return;
        }

        if (_loadedProject is null || string.IsNullOrWhiteSpace(callbackName))
        {
            return;
        }

        _loadedProject.Scripts.DispatchEvent(
            new WebScriptEvent("speech", EntityId: entityId ?? string.Empty, CallbackName: callbackName),
            entityId);
        await ProcessScriptCommandsAsync();
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
            IsRunning = false,
            RendererReady = false,
            RendererName = "Unavailable",
            Status = "Browser or WebGL2 error",
            LastError = string.IsNullOrWhiteSpace(message) ? "Unknown browser error" : message
        });
        return Task.CompletedTask;
    }

    private void QueueSpeechRequest(string entityId, RuntimeSpeechRequest request)
    {
        _speechRequests.Enqueue((entityId, request));
    }

    [JSInvokable]
    public Task OnBrowserWarning(string message)
    {
        SetSnapshot(Snapshot with { LastError = message });
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnGraphicsQualityApplied(WebGraphicsApplied quality)
    {
        if (_loadedProject is not { } project || quality.ScenePath != project.RuntimeScene.ScenePath) return Task.CompletedTask;
        project.RuntimeScene.Graphics.ReportApplied(quality.Revision,quality.SceneSamples,quality.DirectionalResolution,quality.LocalLightResolution);
        foreach (var entity in project.RuntimeScene.Entities)
            if(entity.IsTexturedPlane || entity.IsWaterSurface)
                entity.ReportReflectionSamples(quality.ReflectionSamples?.GetValueOrDefault(entity.Id) ?? 0);
        return Task.CompletedTask;
    }

    private async Task ProcessScriptCommandsAsync()
    {
        if (_loadedProject is null)
        {
            return;
        }

        while (_reportedScriptDiagnosticCount < _loadedProject.Scripts.Diagnostics.Count)
        {
            WebScriptLoadDiagnostic diagnostic = _loadedProject.Scripts.Diagnostics[_reportedScriptDiagnosticCount++];
            string message = $"Web script {diagnostic.ScriptId}: {diagnostic.Message}";
            if (_module is not null) await _module.InvokeVoidAsync("log", diagnostic.IsError ? "error" : "warn", message);
            if (diagnostic.IsError) SetSnapshot(Snapshot with { LastError = message });
        }

        foreach (WebAudioCommand command in _loadedProject.Scripts.DrainAudioCommands())
        {
            if (!_loadedProject.AudioAssets.TryGetValue(command.NameOrPath, out WebAudioAsset? asset))
            {
                continue;
            }

            string key = asset.Alias;
            float volume = Math.Clamp(
                command.Operation == "set_volume"
                    ? command.Volume
                    : _audioVolumes.GetValueOrDefault(key, asset.Volume),
                0.0f,
                1.0f);
            bool loop = command.Operation == "set_loop"
                ? command.Loop
                : _audioLoops.GetValueOrDefault(key, asset.Loop);
            _audioVolumes[key] = volume;
            _audioLoops[key] = loop;

            try
            {
                switch (command.Operation)
                {
                    case "play":
                        await _audioService.PlayAsync(key, asset.Uri, loop, volume);
                        break;
                    case "play_one_shot":
                        await _audioService.PlayOneShotAsync(asset.Uri, Math.Clamp(command.VolumeScale * volume, 0.0f, 1.0f));
                        break;
                    case "pause":
                        await _audioService.PauseAsync(key);
                        break;
                    case "stop":
                        await _audioService.StopAsync(key);
                        break;
                    case "set_volume":
                        await _audioService.SetVolumeAsync(key, volume);
                        break;
                    case "set_loop":
                        await _audioService.SetLoopAsync(key, loop);
                        break;
                }
            }
            catch (Exception exception)
            {
                SetSnapshot(Snapshot with { LastError = $"Web audio command failed: {exception.GetBaseException().Message}" });
            }
        }

        while (_speechRequests.Count > 0)
        {
            (string entityId, RuntimeSpeechRequest request) = _speechRequests.Dequeue();
            string callbackName = request.CallbackName;
            if (request.Completion is not null && string.IsNullOrWhiteSpace(callbackName))
            {
                callbackName = $"__completion_{Guid.NewGuid():N}";
                _speechCompletions[callbackName] = request.Completion;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(callbackName) && _dotNetReference is not null)
                {
                    await _speechService.SpeakWithCallbackAsync(
                        request.Text,
                        "zh-CN",
                        request.Speed,
                        1.0f,
                        request.Volume,
                        _dotNetReference,
                        entityId,
                        callbackName);
                }
                else
                {
                    await _speechService.SpeakAsync(request.Text, "zh-CN", request.Speed, 1.0f, request.Volume);
                }
            }
            catch (Exception exception)
            {
                SetSnapshot(Snapshot with { LastError = $"Web speech command failed: {exception.GetBaseException().Message}" });
            }
        }
    }

    private GuiControlSettings? ResolveGuiControl(float x, float y)
    {
        if (_loadedProject is null) return null;
        float actualWidth = Math.Max(Snapshot.CanvasWidth, 1);
        float actualHeight = Math.Max(Snapshot.CanvasHeight, 1);
        float referenceWidth = Math.Max(_loadedProject.Project.Window.Width, 1);
        float referenceHeight = Math.Max(_loadedProject.Project.Window.Height, 1);
        return _loadedProject.SceneDefinition.GuiControls
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(control =>
            {
                if (!control.Visible) return false;
                LayoutRect rect = LayoutResolver.Resolve(
                    control.LayoutMode,
                    control.X,
                    control.Y,
                    control.Width,
                    control.Height,
                    actualWidth,
                    actualHeight,
                    referenceWidth,
                    referenceHeight);
                return x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
            });
    }

    private string? ResolveGuiTargetEntityId(GuiControlSettings control)
    {
        if (_loadedProject is null) return null;
        if (!string.IsNullOrWhiteSpace(control.TargetEntity)
            && _loadedProject.RuntimeScene.GetEntity(control.TargetEntity) is RuntimeEntity configured)
        {
            return configured.Id;
        }

        return _loadedProject.SceneDefinition.Entities
            .Where(entity => entity.Scripts.Any(binding => binding.Enabled && IsCSharp(binding.Language, binding.Path)))
            .OrderByDescending(entity => string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase))
            .Select(entity => entity.Id)
            .FirstOrDefault();
    }

    private async Task UpdateOverlayStateAsync(WebLoadedProject project)
    {
        if (_module is null) return;
        float actualWidth = Math.Max(Snapshot.CanvasWidth, 1);
        float actualHeight = Math.Max(Snapshot.CanvasHeight, 1);
        float referenceWidth = Math.Max(project.Project.Window.Width, 1);
        float referenceHeight = Math.Max(project.Project.Window.Height, 1);
        object[] controls = project.SceneDefinition.GuiControls.Select(control =>
        {
            LayoutRect rect = LayoutResolver.Resolve(
                control.LayoutMode,
                control.X,
                control.Y,
                control.Width,
                control.Height,
                actualWidth,
                actualHeight,
                referenceWidth,
                referenceHeight);
            float fontSize = LayoutResolver.ResolveFontSize(
                control.LayoutMode,
                control.Style.FontSize,
                actualWidth,
                actualHeight,
                referenceWidth,
                referenceHeight);
            return (object)new
            {
                id = control.Id,
                name = control.Name,
                type = NormalizeGuiType(control.Type),
                text = control.Text,
                x = rect.X,
                y = rect.Y,
                width = rect.Width,
                height = rect.Height,
                visible = control.Visible,
                checkedValue = control.Checked,
                progress = control.Progress,
                selectedIndex = control.SelectedIndex,
                items = control.Items,
                background = ToCssColor(control.Style.BackgroundColor),
                border = ToCssColor(control.Style.BorderColor),
                color = ToCssColor(control.Style.TextColor),
                fontSize
            };
        }).ToArray();
        object[] sprites = project.SceneDefinition.Sprites
            .Where(sprite => project.SpriteUris.ContainsKey(sprite.Id))
            .Select(sprite =>
            {
                LayoutRect rect = SpriteLayoutResolver.Resolve(
                    sprite,
                    actualWidth,
                    actualHeight,
                    referenceWidth,
                    referenceHeight);
                return (object)new
                {
                    id = sprite.Id,
                    uri = project.SpriteUris[sprite.Id],
                    x = rect.X,
                    y = rect.Y,
                    width = rect.Width,
                    height = rect.Height,
                    rotation = sprite.RotationDegrees,
                    opacity = Math.Clamp(sprite.Opacity, 0.0f, 1.0f),
                    visible = sprite.Visible,
                    drawOrder = sprite.DrawOrder,
                    sourceX = sprite.SourceX,
                    sourceY = sprite.SourceY,
                    sourceWidth = sprite.SourceWidth,
                    sourceHeight = sprite.SourceHeight
                };
            })
            .ToArray();
        await _module.InvokeVoidAsync("setOverlayState", new { controls, sprites, bubbles = Array.Empty<object>() });
    }

    private static string NormalizeGuiType(string? type)
        => (type ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
        {
            "check_box" => "checkbox",
            "combo" => "dropdown",
            "progress" => "progress_bar",
            _ => (type ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_')
        };

    private static string ToCssColor(Vector4Dto color)
        => $"rgba({Math.Clamp((int)MathF.Round(color.X * 255.0f), 0, 255)},{Math.Clamp((int)MathF.Round(color.Y * 255.0f), 0, 255)},{Math.Clamp((int)MathF.Round(color.Z * 255.0f), 0, 255)},{Math.Clamp(color.W, 0.0f, 1.0f):0.###})";

    private static bool IsCSharp(string? language, string? path)
        => string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "cs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "csx", StringComparison.OrdinalIgnoreCase)
            || (path?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
            || (path?.EndsWith(".csx", StringComparison.OrdinalIgnoreCase) == true);

    private RuntimeCameraInput ConsumeInput()
    {
        RuntimeCameraInput value = new(
            _pendingInput.LookDelta,
            _pendingInput.PanDelta,
            _pendingInput.ZoomDelta,
            _pointerPosition,
            _pointerDelta,
            (_pointerButtons & 1) != 0,
            (_pointerButtons & 2) != 0,
            (_pointerButtons & 4) != 0,
            _keys.Contains);
        _pendingInput = RuntimeCameraInput.None;
        _pointerDelta = Vector2.Zero;
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
        (RuntimeEntity Entity, WebPmxMesh Mesh)[] models = project.PmxAssets.Values
            .Select(asset => (Entity: project.RuntimeScene.GetEntity(asset.Entity.Id), Asset: asset))
            .Where(item => item.Entity is not null)
            .Select(item => (item.Entity!, item.Asset.Mesh))
            .Concat(project.SceneMeshes
                .Select(item => (Entity: project.RuntimeScene.GetEntity(item.Key), Mesh: item.Value))
                .Where(item => item.Entity is not null)
                .Select(item => (item.Entity!, item.Mesh)))
            .ToArray();
        WebRenderFrame frame = WebRenderFrame.FromScene(project.RuntimeScene, models, Snapshot.CanvasWidth, Snapshot.CanvasHeight,
            Snapshot.ElapsedSeconds, project.Project.Window.AntiAliasingSamples, project.Project.Shadows, project.RuntimeScene.Graphics.Capture());
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

    private sealed class LoadProgressReporter(Action<WebLoadProgress> report) : IProgress<WebLoadProgress>
    {
        public void Report(WebLoadProgress value) => report(value);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        if (_ownsPhysicsService) await _physicsService.DisposeAsync();
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
