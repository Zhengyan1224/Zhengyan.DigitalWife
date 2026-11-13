using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Numerics;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

/// <summary>Browser host for the shared scene, PMX pose evaluator and WebGL2 renderer.</summary>
public sealed class WebRuntimeHost(
    IJSRuntime jsRuntime,
    WebProjectLoader projectLoader,
    WebAudioService audioService,
    WebSpeechService speechService,
    WebPmxPhysicsService? physicsService = null,
    HttpClient? httpClient = null) : IAsyncDisposable
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
    private bool _cursorVisible = true;
    private bool _switchingScene;
    private readonly ConcurrentQueue<(RuntimeScene Scene, RuntimeWindowCommand Command)> _windowCommands = new();
    private readonly WebSpeechLipSync _lipSync = new();
    private string? _speakingEntity;
    private string? _pressedGuiId;
    private string? _contextMenuTarget;
    private string? _hoveredSpriteId,_pressedSpriteId;
    private readonly Dictionary<string,WebPythonBridge> _pythonBridges=new();
    private string _offlineAssetIdentity = "";
    private readonly Dictionary<string,string> _offlineAssetUris=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string> _pythonProjectFiles=new(StringComparer.OrdinalIgnoreCase);
    private readonly WebSaveStore _saveStore = new(jsRuntime);
    private IJSObjectReference? _asrModule;
    private readonly ConcurrentQueue<(RuntimeScene Scene, WebScriptEvent Event)> _serviceEvents = new();
    private readonly ConcurrentQueue<(RuntimeScene Scene, RuntimeAsrCommand Command)> _asrCommands = new();
    private string _saveKey = "";
    private IJSObjectReference? _realtimeModule;
    private readonly ConcurrentQueue<(RuntimeScene Scene, RuntimeRealtimeVoiceCommand Command)> _realtimeCommands = new();

    public WebRuntimeSnapshot Snapshot { get; private set; } = WebRuntimeSnapshot.Initial;
    public event Action? Changed;
    public async Task<int> CacheOfflineGameAsync()=>_module is null?0:await _module.InvokeAsync<int>("cacheOfflineGame");

    public async Task LoadDwGameAsync(byte[] packageBytes, ElementReference canvas, ElementReference overlayCanvas, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(packageBytes);
        if (_started) await StopAsync();
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import",WebRuntimeModuleUrl.Resolve("web-runtime.js"));
        packageBytes=await _module.InvokeAsync<byte[]>("decryptGamePackage",packageBytes,password??"");
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
            _projectLoader.InitializeServices = InitializeSceneServicesAsync;
            _projectLoader.ConfigureLoadingScreen = ConfigureLoadingScreenAsync;
            SetSnapshot(Snapshot with { IsLoading = true, LastError = null, Status = "正在加载游戏", LoadingMessage = "正在初始化渲染器" });
            _module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", WebRuntimeModuleUrl.Resolve("web-runtime.js"));
            _dotNetReference = DotNetObjectReference.Create(this);
            _projectLoader.PythonFactory=(entry,source)=>new WebPythonScript(_jsRuntime,entry,source,_dotNetReference,(id,bridge)=>
            {
                if(bridge is null)_pythonBridges.Remove(id);else{bridge.ProjectFiles=_pythonProjectFiles;_pythonBridges[id]=bridge;}
            });
            await _module.InvokeVoidAsync("start", canvas, overlayCanvas, _dotNetReference);
            await _module.InvokeVoidAsync("setInputCapture", true);
            await _module.InvokeVoidAsync("nextPaint");
            await _module.InvokeVoidAsync("registerServiceWorker", "./sw.js");
            _started = true;
            WebGl2Info rendererInfo = await _module.InvokeAsync<WebGl2Info>("enableWebGl2");
            await _module.InvokeVoidAsync("log", "info", $"WebGL2 renderer initialized: {rendererInfo.Renderer}");
            WebGpuCapability gpu = await _module.InvokeAsync<WebGpuCapability>("detectWebGpu");
            await _module.InvokeVoidAsync("log", "info", gpu.Available
                ? $"WebGPU capability detected: {gpu.Name}"
                : $"WebGPU unavailable: {gpu.Reason}");

            IProgress<WebLoadProgress> progress = CreateLoadingProgress();
            WebLoadedProject loadedProject = await _projectLoader.LoadAsync(progress);
            _loadedProject = loadedProject;
            await _module.InvokeVoidAsync("configureGame", loadedProject.Project.Window.Width, loadedProject.Project.Window.Height, loadedProject.Project.Window.DesktopSpriteMode);
            await _module.InvokeVoidAsync("loadSceneTextures", (object)loadedProject.SpriteUris.Values.ToArray());
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
            }
            await UpdateRendererStateAsync(loadedProject);
            await UpdateOverlayStateAsync(loadedProject);
            await _module.InvokeVoidAsync("presentScene");
            await _module.InvokeVoidAsync("setInputCapture", false);

            SetSnapshot(Snapshot with
            {
                IsRunning = true,
                IsLoading = false,
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
            if (_loadedProject is not null) await PersistSaveAsync(_loadedProject.RuntimeScene);
            _lipSync.Stop();_speakingEntity=null;
            if (_asrModule is not null) await _asrModule.InvokeVoidAsync("stopAll");
            if (_realtimeModule is not null) await _realtimeModule.InvokeVoidAsync("stopAll");
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
                await _loadedProject.Scripts.DisposeAsync();
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
                IsLoading = false,
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
        if (_disposed || !_started || !Snapshot.IsRunning || _loadedProject is null || _switchingScene) return;
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
        if (pointerInput is not null) input = input with { Touches = pointerInput.Touches, TouchTransitions = pointerInput.TouchTransitions, IsTouchAvailable = pointerInput.TouchAvailable };
        ApplyKeyboardCameraMotion((float)deltaSeconds);
        _loadedProject.RuntimeScene.Update((float)deltaSeconds, input);
        _loadedProject.RuntimeScene.ReportFrameInterval(frameIntervalSeconds);
        _loadedProject.Scripts.Update((float)deltaSeconds, timestampMilliseconds / 1000.0, input);
        UpdateSpriteHover(input.PointerPosition);
        while (_serviceEvents.TryDequeue(out var item))
            if (ReferenceEquals(item.Scene, _loadedProject.RuntimeScene)) _loadedProject.Scripts.DispatchEvent(item.Event, item.Event.EntityId);
        await ProcessScriptCommandsAsync();

        if (!Snapshot.IsRunning) return;

        await SynchronizeSurfacesAsync(_loadedProject);

        WebPmxAsset[] assets = _loadedProject.PmxAssets.Values.ToArray();
        _lipSync.Update((float)deltaSeconds);
        HashSet<string> changedIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (WebPmxAsset asset in assets)
        {
            if (asset.Update(timestampMilliseconds / 1000.0, (float)deltaSeconds))
                changedIds.Add(asset.Entity.Id);
        }
        changedIds.UnionWith(WebPmxRelations.Apply(_loadedProject.RuntimeScene, _loadedProject.PmxAssets));
        _loadedProject.Simulation.Update(_loadedProject.RuntimeScene, (float)deltaSeconds);
        var changedMeshes = assets.Where(a => changedIds.Contains(a.Entity.Id))
            .Select(a => new WebPmxMeshUpdateInstance(a.Entity.Id, a.Mesh.CreateUpdate())).ToArray();
        if (changedMeshes.Length > 0)
            await _module!.InvokeVoidAsync("updatePmxMeshes", (object)changedMeshes);
        WebPmxAsset? firstAsset = assets.FirstOrDefault();
        await UpdateRendererStateAsync(_loadedProject);
        await UpdateOverlayStateAsync(_loadedProject);
        await _module!.InvokeVoidAsync("setCursorVisible", _cursorVisible);

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
        if (_loadedProject is null || Snapshot.IsLoading || button != 0)
        {
            return;
        }

        GuiControlSettings? control = ResolveGuiControl(x, y);
        if (phase == "down")
        {
            _pressedGuiId = control?.Id;
            if (control is not null) DispatchGuiEvent(control, "pressed", button);
            await ProcessScriptCommandsAsync(); return;
        }
        if (_pressedGuiId is string pressedId)
        {
            var pressed = _loadedProject.SceneDefinition.GuiControls.FirstOrDefault(c => c.Id == pressedId);
            if (pressed is not null) DispatchGuiEvent(pressed, "released", button);
            _pressedGuiId = null;
            if (control?.Id != pressedId || phase == "cancel") { await ProcessScriptCommandsAsync(); return; }
        }
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

    private void DispatchGuiEvent(GuiControlSettings control, string eventName, int button)
    {
        if (_loadedProject is null) return;
        string? target = ResolveGuiTargetEntityId(control);
        _loadedProject.Scripts.DispatchEvent(new("gui", EntityId:target??"",ControlId:control.Id,ControlName:control.Name,
            EventName:eventName,PayloadJson:$"{{\"button\":{button}}}"),target);
    }

    [JSInvokable]
    public async Task OnContextMenuRequest(float x,float y)
    {
        if (_loadedProject is null || _module is null) return;
        var scene = _loadedProject.RuntimeScene; ContextMenuSettings? selected = null; string? entityId = null;
        foreach (var menu in scene.Definition.ContextMenus.Where(m => m.Enabled).Reverse())
        {
            string type = menu.TargetType.Replace('-', '_').ToLowerInvariant();
            if (type == "entity")
            {
                var ray = scene.MainCamera.ScreenPointToRay(x,y,Snapshot.CanvasWidth,Snapshot.CanvasHeight);
                if (!scene.Physics.Raycast(ray,out var hit) || (menu.TargetId.Length>0 && hit.Entity.Id!=menu.TargetId && hit.Entity.Name!=menu.TargetId)
                    || (menu.TargetCollider.Length>0 && hit.ColliderId!=menu.TargetCollider && hit.ColliderName!=menu.TargetCollider)) continue;
                entityId=hit.Entity.Id;
            }
            else if (type is "gui_control" or "gui")
            {
                var control=ResolveGuiControl(x,y);if(control is null || (menu.TargetId.Length>0 && control.Id!=menu.TargetId && control.Name!=menu.TargetId))continue;
                entityId=ResolveGuiTargetEntityId(control);
            }
            else if (type=="sprite")
            {
                var sprite=scene.Definition.Sprites.LastOrDefault(s=>(menu.TargetId.Length==0 || s.Id==menu.TargetId || s.Name==menu.TargetId)
                    && SpriteLayoutResolver.ContainsPoint(s,x,y,Snapshot.CanvasWidth,Snapshot.CanvasHeight,_loadedProject.Project.Window.Width,_loadedProject.Project.Window.Height));
                if(sprite is null)continue;entityId=scene.GetEntity(sprite.TargetEntity)?.Id;
            }
            selected=menu;break;
        }
        if(selected is null)return;
        _contextMenuTarget=entityId??scene.SelectScriptTarget()?.Id;
        float scale=LayoutResolver.IsRelative(selected.LayoutMode)?Math.Min(Snapshot.CanvasWidth/(float)_loadedProject.Project.Window.Width,Snapshot.CanvasHeight/(float)_loadedProject.Project.Window.Height):1;
        float width=selected.Width*scale,itemHeight=selected.ItemHeight*scale,paddingY=selected.PaddingY*scale;
        await _module.InvokeVoidAsync("showContextMenu",new{id=selected.Id,x=Math.Clamp(x,0,Math.Max(0,Snapshot.CanvasWidth-width)),
            y=Math.Clamp(y,0,Math.Max(0,Snapshot.CanvasHeight-itemHeight*selected.Items.Count-paddingY*2)),width,itemHeight,paddingY,paddingX=selected.PaddingX*scale,
            rounding=selected.Style.Rounding*scale,fontSize=selected.Style.FontSize*scale,background=ToCssColor(selected.Style.BackgroundColor),
            border=ToCssColor(selected.Style.BorderColor),color=ToCssColor(selected.Style.TextColor),hover=ToCssColor(selected.Style.HoverColor),
            items=selected.Items.Select(i=>new{id=i.Id,text=i.Text,enabled=i.Enabled}).ToArray()});
    }

    [JSInvokable]
    public async Task OnContextMenuItem(string menuId,string itemId)
    {
        var menu=_loadedProject?.SceneDefinition.ContextMenus.FirstOrDefault(m=>m.Id==menuId && m.Enabled);
        var item=menu?.Items.FirstOrDefault(i=>i.Id==itemId && i.Enabled);if(item is null)return;
        _loadedProject!.Scripts.DispatchEvent(new("gui",EntityId:_contextMenuTarget??"",ControlId:menu!.Id,ControlName:menu.Name,
            EventName:string.IsNullOrWhiteSpace(item.EventName)?"context_menu_clicked":item.EventName),_contextMenuTarget);
        await ProcessScriptCommandsAsync();
    }

    [JSInvokable]
    public async Task OnSpritePointer(float x, float y, string phase, int button)
    {
        if (_loadedProject is null || button != 0)
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
        var hit=sprite;
        if(phase=="down")_pressedSpriteId=sprite?.Id;
        else if(_pressedSpriteId is not null)sprite=_loadedProject.SceneDefinition.Sprites.FirstOrDefault(s=>s.Id==_pressedSpriteId);
        bool clicked=phase=="up" && sprite is not null && hit?.Id==sprite.Id;
        if(phase!="down")_pressedSpriteId=null;
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
                SpriteEventName: phase == "down" ? "pressed" : "released"),
            targetEntityId);
        if (clicked) _loadedProject.Scripts.DispatchEvent(new("sprite", EntityId: targetEntityId,
            SpriteId: sprite.Id, SpriteName: sprite.Name, SpriteEventName: "clicked"), targetEntityId);
        await ProcessScriptCommandsAsync();
    }

    private void UpdateSpriteHover(Vector2 point)
    {
        if(_loadedProject is null)return;
        var scene=_loadedProject.RuntimeScene;
        var hovered=scene.Definition.Sprites.Where(s=>s.Visible && !string.IsNullOrWhiteSpace(s.TargetEntity))
            .OrderByDescending(s=>s.DrawOrder).FirstOrDefault(s=>SpriteLayoutResolver.ContainsPoint(s,point.X,point.Y,
                Snapshot.CanvasWidth,Snapshot.CanvasHeight,_loadedProject.Project.Window.Width,_loadedProject.Project.Window.Height));
        if(hovered?.Id==_hoveredSpriteId)return;
        foreach(var item in new[]{(_hoveredSpriteId,"exited"),(hovered?.Id,"entered")})
        {
            var sprite=scene.Definition.Sprites.FirstOrDefault(s=>s.Id==item.Item1);var entity=scene.GetEntity(sprite?.TargetEntity);
            if(sprite is not null && entity is not null)_loadedProject.Scripts.DispatchEvent(new("sprite",EntityId:entity.Id,SpriteId:sprite.Id,SpriteName:sprite.Name,SpriteEventName:item.Item2),entity.Id);
        }
        _hoveredSpriteId=hovered?.Id;
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
    public async Task OnCanvasResized(int width, int height, double devicePixelRatio)
    {
        if (_disposed) return;
        SetSnapshot(Snapshot with
        {
            CanvasWidth = Math.Max(width, 0),
            CanvasHeight = Math.Max(height, 0),
            DevicePixelRatio = Math.Max(devicePixelRatio, 1.0),
            Status = _started ? Snapshot.Status : "Canvas resized"
        });
        if (_loadedProject is not null && Snapshot.RendererReady)
        {
            _loadedProject.RuntimeScene.SetScreenSize(width,height,_loadedProject.Project.Window.Width,_loadedProject.Project.Window.Height);
            await UpdateRendererStateAsync(_loadedProject);
            await UpdateOverlayStateAsync(_loadedProject);
        }
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
        while (_windowCommands.TryDequeue(out var window))
            if (ReferenceEquals(window.Scene, _loadedProject?.RuntimeScene) && _module is not null)
                if (window.Command.Operation == "size")
                    await _module.InvokeVoidAsync("setGameSize", window.Command.Width, window.Command.Height);
                else if (window.Command.Operation == "fullscreen")
                    await _module.InvokeVoidAsync("setGameFullscreen", window.Command.Fullscreen);
        if (_loadedProject is not null)
        {
            while (_asrCommands.TryDequeue(out var item))
                if (ReferenceEquals(item.Scene, _loadedProject.RuntimeScene) && _asrModule is not null)
                    await _asrModule.InvokeVoidAsync("execute", item.Command, _dotNetReference);
            await PersistSaveAsync(_loadedProject.RuntimeScene);
            while (_realtimeCommands.TryDequeue(out var item))
                if (ReferenceEquals(item.Scene, _loadedProject.RuntimeScene) && _realtimeModule is not null)
                    await _realtimeModule.InvokeVoidAsync("execute", item.Command, _dotNetReference);
        }
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
                if (request.Stop) { _lipSync.Stop();_speakingEntity=null;await _speechService.StopAsync(); _speechCompletions.Clear(); continue; }
                if (_dotNetReference is not null)
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
        if (_loadedProject.RuntimeScene.ConsumeSceneRequest() is string scenePath && !_switchingScene)
            await SwitchSceneAsync(scenePath);
    }

    private async Task SwitchSceneAsync(string path)
    {
        if (_module is null || _loadedProject is null) return;
        _switchingScene = true;
        SetSnapshot(Snapshot with { IsLoading = true, IsRunning = false, LoadingProgress = 0,
            LoadingStage = "scene", LoadingMessage = "正在切换场景", LastError = null });
        try
        {
            await _module.InvokeVoidAsync("setInputCapture", true);
            await _module.InvokeVoidAsync("nextPaint");
            await PersistSaveAsync(_loadedProject.RuntimeScene);
            _speechCompletions.Clear(); _speechRequests.Clear();
            _lipSync.Stop();_speakingEntity=null;
            if (_asrModule is not null) await _asrModule.InvokeVoidAsync("stopAll");
            if (_realtimeModule is not null) await _realtimeModule.InvokeVoidAsync("stopAll");
            await _audioService.StopAllAsync();
            await _speechService.StopAsync();
            await _loadedProject.Scripts.DisposeAsync();
            var next = await _projectLoader.LoadAsync(CreateLoadingProgress(), scenePathOverride: path);
            var meshes = next.PmxAssets.Values.Select(a => new WebPmxMeshInstance(a.Entity.Id, a.Mesh))
                .Concat(next.SceneMeshes.Select(m => new WebPmxMeshInstance(m.Key, m.Value))).ToArray();
            await _module.InvokeVoidAsync("loadPmxMeshes", (object)meshes);
            foreach (var asset in _loadedProject.PmxAssets.Values) asset.Dispose();
            _loadedProject.RuntimeScene.Dispose();
            _loadedProject = next; _reportedScriptDiagnosticCount = 0;
            _lastTimestampMilliseconds = double.NaN;
            foreach (var audio in next.AudioAssets.Values.DistinctBy(a => a.Alias)) await _audioService.PrepareAsync(audio.Alias, audio.Uri);
            await _module.InvokeVoidAsync("loadSceneTextures", (object)next.SpriteUris.Values.ToArray());
            await UpdateRendererStateAsync(next); await UpdateOverlayStateAsync(next);
            await _module.InvokeVoidAsync("presentScene");
            SetSnapshot(Snapshot with { IsLoading = false, IsRunning = true, LoadingProgress = 1,
                LoadingStage = "ready", LoadingMessage = "场景加载完成", SceneName = next.RuntimeScene.Name, SceneEntityCount = next.RuntimeScene.Entities.Count(),
                PmxEntityCount = next.PmxAssets.Count });
        }
        catch (Exception exception) { SetSnapshot(Snapshot with { IsLoading = false, IsRunning = false, LastError = $"Scene '{path}' failed to load: {exception.GetBaseException().Message}" }); }
        finally { _switchingScene = false; await _module.InvokeVoidAsync("setInputCapture", false); }
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

        return _loadedProject.RuntimeScene.SelectScriptTarget()?.Id;
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
                fontSize,
                rounding = control.Style.Rounding,
                alignment = control.Style.HorizontalAlignment,
                verticalAlignment = control.Style.VerticalAlignment,
                borderThickness = control.Style.BorderThickness
            };
        }).ToArray();
        object[] sprites = project.SceneDefinition.Sprites
            .Where(sprite => sprite.Path.StartsWith("rt:", StringComparison.OrdinalIgnoreCase) || project.TextureUris.ContainsKey(sprite.Path) || project.SpriteUris.ContainsKey(sprite.Id))
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
                    uri = sprite.Path.StartsWith("rt:", StringComparison.OrdinalIgnoreCase) ? sprite.Path : project.TextureUris.GetValueOrDefault(sprite.Path) ?? project.SpriteUris[sprite.Id],
                    x = rect.X,
                    y = rect.Y,
                    width = rect.Width,
                    height = rect.Height,
                    rotation = sprite.RotationDegrees,
                    opacity = Math.Clamp(sprite.Opacity, 0.0f, 1.0f),
                    visible = sprite.Visible,
                    interactive = !string.IsNullOrWhiteSpace(sprite.TargetEntity),
                    drawOrder = sprite.DrawOrder,
                    sourceX = sprite.SourceX,
                    sourceY = sprite.SourceY,
                    sourceWidth = sprite.SourceWidth,
                    sourceHeight = sprite.SourceHeight
                };
            })
            .ToArray();
        object[] bubbles = project.RuntimeScene.Bubble.GetOrderedVisibleBubbles().Select(bubble =>
        {
            Vector2 anchor = bubble.ScreenPosition;
            if (bubble.AnchorMode is "entity" or "world")
            {
                RuntimeEntity? entity = project.RuntimeScene.GetEntity(bubble.AnchorEntity);
                Vector3 world = bubble.WorldPosition;
                if (entity is not null)
                {
                    world = entity.Position;
                    if (bubble.UseEntityTopAnchor && project.PmxAssets.TryGetValue(entity.Id, out var asset))
                        world = Vector3.Transform(new Vector3(0, asset.Mesh.BoundsMax.Y, 0), entity.TransformMatrix);
                }
                world += bubble.WorldOffset;
                var camera = project.RuntimeScene.MainCamera;
                var clip = Vector4.Transform(new Vector4(world, 1), camera.CreateView() * camera.CreateProjection(actualWidth / actualHeight));
                if (clip.W <= 0) return (object)new { visible = false };
                anchor = new((clip.X / clip.W * .5f + .5f) * actualWidth, (.5f - clip.Y / clip.W * .5f) * actualHeight);
            }
            else if (bubble.LayoutMode == "relative") anchor *= new Vector2(actualWidth / referenceWidth, actualHeight / referenceHeight);
            anchor += bubble.ScreenOffset;
            float scale = bubble.LayoutMode == "relative" ? Math.Min(actualWidth / referenceWidth, actualHeight / referenceHeight) : 1;
            return (object)new { x = anchor.X, y = anchor.Y, width = bubble.Width * scale,
                pivot = new[] { bubble.Pivot.X, bubble.Pivot.Y }, text = bubble.Text, header = bubble.HeaderText, footer = bubble.FooterText,
                fontSize = bubble.FontSize * scale, padding = new[] { bubble.PaddingX * scale, bubble.PaddingY * scale },
                background = ToCssColor(Vector4Dto.FromVector4(bubble.BackgroundColor)), border = ToCssColor(Vector4Dto.FromVector4(bubble.BorderColor)),
                color = ToCssColor(Vector4Dto.FromVector4(bubble.TextColor)), rounding = bubble.Rounding * scale, visible = true };
        }).ToArray();
        await _module.InvokeVoidAsync("setOverlayState", new { controls, sprites, bubbles });
    }

    [JSInvokable]
    public Task OnGuiValueChanged(string id, string value)
    {
        var control = _loadedProject?.RuntimeScene.GetGuiControl(id);
        if (control is not null) control.Value = value;
        return Task.CompletedTask;
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
        return value with { CursorVisibility = visible => _cursorVisible = visible,CursorState=()=>_cursorVisible,DevicePixelRatio=(float)Snapshot.DevicePixelRatio };
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
        frame = frame.WithSceneEffects(project, models, Snapshot.CanvasWidth, Snapshot.CanvasHeight);
        if (project.Project.Window.DesktopSpriteMode)
            frame = frame with { ClearColor = [0, 0, 0, 0], Sky = null, Features = frame.Features with { Skybox = false } };
        await _module.InvokeVoidAsync("setFrameState", frame);
    }

    private async Task InitializeSceneServicesAsync(RuntimeScene scene, GameProject project)
    {
        scene.Window.Configure(_loadedProject?.RuntimeScene.Window.Width ?? project.Window.Width,
            _loadedProject?.RuntimeScene.Window.Height ?? project.Window.Height);
        scene.Window.Command += command => _windowCommands.Enqueue((scene, command));
        scene.Window.ReportState((int)(Snapshot.CanvasWidth / Math.Max(1, Snapshot.DevicePixelRatio)),
            (int)(Snapshot.CanvasHeight / Math.Max(1, Snapshot.DevicePixelRatio)), _loadedProject?.RuntimeScene.Window.Fullscreen ?? false);
        if (_loadedProject is null && project.Window.Fullscreen) scene.Window.SetFullscreen(true);
        if(_module is not null)
        {
            if (project.Llm.Enabled && await _module.InvokeAsync<string?>("resolveLlmEndpoint", project.Llm.BaseUrl, project.Llm.ChatCompletionsPath) is string endpoint)
            {
                var uri = new Uri(endpoint);
                project.Llm.BaseUrl = uri.GetLeftPart(UriPartial.Authority);
                project.Llm.ChatCompletionsPath = uri.PathAndQuery;
            }
            var modelUris=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var paths=new[]{project.Voice.ModelPath,project.Voice.VocoderPath,project.Voice.TokensPath,project.Voice.LexiconPath,
                project.Asr.Sherpa.EncoderPath,project.Asr.Sherpa.DecoderPath,project.Asr.Sherpa.JoinerPath,project.Asr.Sherpa.TokensPath,project.Asr.Sherpa.ModelPath};
            var entries=_projectLoader.Assets.Manifest.Assets.Where(a=>paths.Contains(a.Path,StringComparer.OrdinalIgnoreCase)).OrderBy(a=>a.Path).ToArray();
            string identity=System.Text.Json.JsonSerializer.Serialize(new{project.Name,project.Web,project.Voice,project.Asr,Assets=entries.Select(a=>new{a.Path,a.Sha256})});
            if(identity!=_offlineAssetIdentity)
            {
                await _module.InvokeVoidAsync("disposeOffline");
                await _module.InvokeVoidAsync("clearOfflineAssetUrls");
                _offlineAssetUris.Clear();
                _offlineAssetIdentity=identity;
            }
            foreach(string path in paths.Where(p=>!string.IsNullOrWhiteSpace(p)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                if(_projectLoader.Assets.Manifest.Assets.Any(a=>a.Path.Equals(path,StringComparison.OrdinalIgnoreCase)))
                {
                    if(!_offlineAssetUris.TryGetValue(path,out var uri))
                        _offlineAssetUris[path]=uri=_projectLoader.Assets is WebMemoryAssetStore
                            ?await _module.InvokeAsync<string>("createOfflineAssetUrl",await _projectLoader.Assets.ReadAllBytesAsync(path))
                            :await _projectLoader.Assets.GetUriAsync(path);
                    modelUris[path]=uri;
                }
            await _module.InvokeVoidAsync("configureOffline",project,modelUris,identity);
            await _module.InvokeVoidAsync("preloadModels", _dotNetReference);
        }
        _pythonProjectFiles.Clear();
        if(_projectLoader.Assets.Manifest.Scripts.Any(s=>s.Language.Equals("python",StringComparison.OrdinalIgnoreCase)))
            foreach(var asset in _projectLoader.Assets.Manifest.Assets.Where(a=>a.Size<=4*1024*1024 &&
                new[]{".py",".json",".txt",".md"}.Contains(Path.GetExtension(a.Path),StringComparer.OrdinalIgnoreCase)))
                _pythonProjectFiles[asset.Path]=System.Text.Encoding.UTF8.GetString(await _projectLoader.Assets.ReadAllBytesAsync(asset.Path));
        scene.SetScreenSize(Snapshot.CanvasWidth,Snapshot.CanvasHeight,project.Window.Width,project.Window.Height);
        string kana="",vowels="";
        if(httpClient is not null && project.Voice.LipSync.Enabled)
        {
            string file=project.Voice.LipSync.DictionaryLanguage.Equals("Japanese",StringComparison.OrdinalIgnoreCase)?"kanadic.txt"
                :project.Voice.LipSync.DictionaryLanguage.Equals("English",StringComparison.OrdinalIgnoreCase)?"en_kanadic.txt":"zh_kanadic.txt";
            try { kana=await httpClient.GetStringAsync("engine/SpeechLipSyncDictionaries/"+file+"?v="+WebRuntimeModuleUrl.Version);vowels=await httpClient.GetStringAsync("engine/SpeechLipSyncDictionaries/voweldic.txt?v="+WebRuntimeModuleUrl.Version); }
            catch(Exception exception){SetSnapshot(Snapshot with{LastError="Lip-sync dictionary load failed: "+exception.Message});}
        }
        _lipSync.Configure(project.Voice.LipSync,kana,vowels);
        _saveKey = "game-save:" + project.Name;
        try { scene.Save.Initialize(await _saveStore.LoadAsync<Dictionary<string, string>>(_saveKey)); }
        catch (Exception exception) { SetSnapshot(Snapshot with { LastError = "Save load failed: " + exception.Message }); }
        scene.Llm.Configure(project.Llm);
        scene.Network.ConfigureStreamingRequest = request => request.SetBrowserResponseStreamingEnabled(true);
        scene.Llm.ScriptEvent += (entity, value) => _serviceEvents.Enqueue((scene, new("llm", EntityId: entity.Id) { Llm = value }));
        foreach (var asset in _projectLoader.Assets.Manifest.Assets.Where(a => a.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            && (a.Path.StartsWith("skills/", StringComparison.OrdinalIgnoreCase) || a.Path.StartsWith("memory/", StringComparison.OrdinalIgnoreCase))))
            scene.Llm.RegisterProjectText(asset.Path, System.Text.Encoding.UTF8.GetString(await _projectLoader.Assets.ReadAllBytesAsync(asset.Path)));
        scene.Asr.Configure(project.Asr);
        scene.RealtimeVoice.Configure(project.RealtimeVoice);
        scene.RealtimeVoice.Command += command => _realtimeCommands.Enqueue((scene, command));
        if (project.RealtimeVoice.Enabled)
        {
            _realtimeModule ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", WebRuntimeModuleUrl.Resolve("web-realtime.js"));
            await _realtimeModule.InvokeVoidAsync("configure", project.RealtimeVoice);
        }
        scene.Asr.Command += command => _asrCommands.Enqueue((scene, command));
        if (project.Asr.Enabled)
        {
            _asrModule ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", WebRuntimeModuleUrl.Resolve("web-asr.js"));
            scene.Asr.MicrophoneInputAvailable = await _asrModule.InvokeAsync<bool>("isAvailable");
            scene.Asr.MicrophoneUnavailableReason = scene.Asr.MicrophoneInputAvailable ? "" : "This browser does not provide speech recognition.";
        }
    }

    private IProgress<WebLoadProgress> CreateLoadingProgress() => new LoadProgressReporter(value => SetSnapshot(Snapshot with
    {
        LoadingProgress = value.Value, LoadingStage = value.Stage, LoadingMessage = value.Message, Status = value.Message
    }));

    private async Task ConfigureLoadingScreenAsync(GameProject project, GameProjectScene scene)
    {
        if (_module is not null && _loadedProject is null)
            await _module.InvokeVoidAsync("configureGame", project.Window.Width, project.Window.Height, project.Window.DesktopSpriteMode);
        string? uri = null;
        string path = scene.LoadingScreen.BackgroundImagePath ?? string.Empty;
        if (path.StartsWith("project:", StringComparison.OrdinalIgnoreCase)) path = path[8..];
        path = path.Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(path)
            && _projectLoader.Assets.Manifest.Assets.Any(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            uri = await _projectLoader.Assets.GetUriAsync(path);
        SetSnapshot(Snapshot with { LoadingScreen = scene.LoadingScreen, LoadingBackgroundUri = uri,
            ReferenceWidth = project.Window.Width, ReferenceHeight = project.Window.Height });
        if (_module is not null) await _module.InvokeVoidAsync("nextPaint");
    }

    [JSInvokable]
    public Task OnWindowStateChanged(int width, int height, bool fullscreen)
    {
        _loadedProject?.RuntimeScene.Window.ReportState(width, height, fullscreen);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnModelLoadingProgress(string kind, string message)
    {
        if (!Snapshot.IsLoading) return Task.CompletedTask;
        SetSnapshot(Snapshot with { LoadingStage = "models", LoadingProgress = kind == "tts" ? 0.80f : kind == "asr" ? 0.85f : 0.88f,
            LoadingMessage = message, Status = message });
        return Task.CompletedTask;
    }

    private async Task PersistSaveAsync(RuntimeScene scene)
    {
        if (!scene.Save.HasChanges) return;
        long revision = scene.Save.Revision;
        try { await _saveStore.SaveAsync(_saveKey, scene.Save.Snapshot()); scene.Save.MarkPersisted(revision); }
        catch (Exception exception) { SetSnapshot(Snapshot with { LastError = "Save write failed: " + exception.Message }); }
    }

    [JSInvokable]
    public Task OnAsrEvent(string entityId, RuntimeAsrScriptEvent value)
    {
        if (value.EventName == "error") SetSnapshot(Snapshot with { LastError = "语音识别失败：" + value.Error });
        if (_loadedProject is not null)
        {
            _loadedProject.RuntimeScene.Asr.Report(value);
            _serviceEvents.Enqueue((_loadedProject.RuntimeScene, new("asr", EntityId: entityId) { Asr = value }));
        }
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnRealtimeVoiceEvent(string entityId, RuntimeRealtimeVoiceScriptEvent value)
    {
        if (_loadedProject is not null && _loadedProject.RuntimeScene.GetEntity(entityId) is not null)
            _serviceEvents.Enqueue((_loadedProject.RuntimeScene,new("realtime_voice",EntityId:entityId){RealtimeVoice=value}));
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSpeechStarted(string entityId,string text,float rate)
    {
        if(_loadedProject?.PmxAssets.TryGetValue(entityId,out var asset)==true){_speakingEntity=entityId;_lipSync.Begin(asset,text,rate);}
        return Task.CompletedTask;
    }
    [JSInvokable]
    public Task OnSpeechBoundary(string entityId,int index,int length){if(entityId==_speakingEntity)_lipSync.Boundary(index,length);return Task.CompletedTask;}
    [JSInvokable]
    public Task OnSpeechFinished(string entityId){if(entityId==_speakingEntity){_lipSync.Stop();_speakingEntity=null;}return Task.CompletedTask;}
    [JSInvokable]
    public Task OnSpeechFailed(string entityId,string callbackName,string error)
    {
        OnSpeechFinished(entityId);_speechCompletions.Remove(callbackName);
        _loadedProject?.Scripts.DispatchEvent(new("speech",EntityId:entityId,CallbackName:callbackName,EventName:"error",PayloadJson:System.Text.Json.JsonSerializer.Serialize(new{error})),entityId);
        SetSnapshot(Snapshot with{LastError="Speech synthesis failed: "+error});return Task.CompletedTask;
    }

    [JSInvokable]
    public object? OnPythonCall(string scriptId,int handle,string operation,string name,string arguments,string value)
        => _pythonBridges[scriptId].Invoke(new(handle,operation,name,System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(arguments),System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value)));
    [JSInvokable]
    public Task<object?> OnPythonAwait(string scriptId,int handle)=>_pythonBridges[scriptId].Await(handle);
    [JSInvokable]
    public object OnPythonFiles(string scriptId)=>_pythonBridges[scriptId].Files();
    [JSInvokable]
    public void OnPythonSave(string scriptId,string files)=>_pythonBridges[scriptId].SaveFiles(files);

    private async Task SynchronizeSurfacesAsync(WebLoadedProject project)
    {
        if (_module is null || project.SceneMeshes is not Dictionary<string, WebPmxMesh> meshes) return;
        if (project.PmxAssets is Dictionary<string, WebPmxAsset> models)
        {
            string[] removedModels = models.Keys.Where(id => project.RuntimeScene.GetEntity(id)?.IsPmxModel != true).ToArray();
            foreach (string id in removedModels) { models[id].Dispose(); models.Remove(id); }
            if (removedModels.Length > 0) await _module.InvokeVoidAsync("removeMeshes", (object)removedModels);
            foreach (var entity in project.RuntimeScene.PmxModels.Where(e => !string.IsNullOrWhiteSpace(e.Definition.AssetPath)))
            {
                if (models.TryGetValue(entity.Id, out var existing) && existing.SourceAssetPath == entity.Definition.AssetPath) continue;
                if(project.ModelRetryAfter.TryGetValue(entity.Id,out var retry) && DateTimeOffset.UtcNow<retry)continue;
                try
                {
                    var asset = await _projectLoader.LoadRuntimeModelAsync(entity, project);
                    existing?.Dispose(); models[entity.Id] = asset;
                    project.ModelRetryAfter.Remove(entity.Id);
                    await _module.InvokeVoidAsync("upsertMeshes", (object)new[] { new WebPmxMeshInstance(entity.Id, asset.Mesh) });
                }
                catch (Exception exception) { project.ModelRetryAfter[entity.Id]=DateTimeOffset.UtcNow.AddSeconds(5);SetSnapshot(Snapshot with { LastError = "Model load failed: " + exception.Message }); }
            }
        }
        var entities = project.RuntimeScene.Entities.Where(e => e.IsTexturedPlane || e.IsWaterSurface).ToArray();
        var active = entities.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] removed = meshes.Keys.Where(id => !active.Contains(id)).ToArray();
        foreach (string id in removed) { meshes.Remove(id); project.SurfaceSignatures.Remove(id); }
        if (removed.Length > 0) await _module.InvokeVoidAsync("removeMeshes", (object)removed);
        List<WebPmxMeshInstance> changed = [];
        foreach (var entity in entities)
        {
            var plane = entity.Definition.Plane; var water = entity.Definition.Water;
            string signature = entity.IsWaterSurface ? $"{water.Size}:{water.GerstnerMeshResolution}"
                : System.Text.Json.JsonSerializer.Serialize(new { plane.Width, plane.Height, plane.TexturePath, plane.Tint, plane.Opacity, plane.ReceiveShadow });
            if (project.SurfaceSignatures.GetValueOrDefault(entity.Id) == signature) continue;
            string? texture = plane.TexturePath.StartsWith("rt:", StringComparison.OrdinalIgnoreCase) ? plane.TexturePath : project.TextureUris.GetValueOrDefault(plane.TexturePath);
            WebPmxMesh mesh = entity.IsWaterSurface ? WebSceneSimulation.CreateWaterMesh(water)
                : WebProjectLoader.CreateQuadMesh(Math.Max(.01f, plane.Width), Math.Max(.01f, plane.Height), plane.Tint, plane.Opacity, texture, plane.ReceiveShadow, true);
            meshes[entity.Id] = mesh; project.SurfaceSignatures[entity.Id] = signature; changed.Add(new(entity.Id, mesh));
        }
        if (changed.Count > 0) await _module.InvokeVoidAsync("upsertMeshes", (object)changed.ToArray());
    }

    private async ValueTask SetErrorAsync(string status, Exception exception)
    {
        string message = exception.GetBaseException().Message;
        SetSnapshot(Snapshot with
        {
            IsRunning = false,
            IsLoading = false,
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
        await _saveStore.DisposeAsync();
        if (_asrModule is not null) { await _asrModule.DisposeAsync(); _asrModule = null; }
        if (_realtimeModule is not null) { await _realtimeModule.InvokeVoidAsync("dispose");await _realtimeModule.DisposeAsync();_realtimeModule=null; }
        _dotNetReference?.Dispose();
        _dotNetReference = null;
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("disposeOffline");await _module.InvokeVoidAsync("clearOfflineAssetUrls");await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
        _module = null;
    }
}
