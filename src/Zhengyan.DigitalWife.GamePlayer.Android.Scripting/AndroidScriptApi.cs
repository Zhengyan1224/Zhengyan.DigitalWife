using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed record AndroidRuntimeEvent(
    string Type,
    string Id,
    string EventName,
    Vector2 Position,
    string Text = "",
    string TargetEntity = "",
    RuntimeLlmToolCall? ToolCall = null,
    string ToolResult = "",
    string Error = "",
    float Progress = 0.0f,
    string CallbackName = "",
    string AccumulatedText = "",
    bool IsFinal = false,
    string WakeWord = "",
    string RecognizedText = "")
{
    internal static AndroidRuntimeEvent FromLlm(AndroidScriptEntity entity, RuntimeLlmScriptEvent value)
        => new("llm", value.RequestId, value.EventName, Vector2.Zero, value.Delta,
            entity.Id, value.ToolCall, value.ToolResult ?? string.Empty, value.Error,
            CallbackName: value.CallbackName, AccumulatedText: value.AccumulatedText, IsFinal: value.IsFinal);
}

public sealed record AndroidRenderTextureInfo(
    string Id,
    string Name,
    int Width,
    int Height,
    string RefreshMode,
    float RefreshIntervalSeconds,
    bool HasRendered,
    double LastRenderedSeconds);

public sealed record AndroidQualityBudgetInfo(
    string Profile,
    int TargetFrameRate,
    int TextureMemoryBudgetMb,
    int RenderTargetMemoryBudgetMb,
    int DrawCallBudget,
    long EstimatedGpuBytes,
    double LastFrameGpuEstimateMs,
    int AdaptiveParticleLimit,
    bool ReflectionsEnabled);

public sealed class AndroidScriptScene
{
    private readonly RuntimeScene _scene;
    private readonly Action<string> _requestSceneChange;
    private readonly Func<double> _getFps;

    internal AndroidScriptScene(RuntimeScene scene, string projectDirectory, Action<string> requestSceneChange, Func<double> getFps, AndroidScriptServices services)
    {
        _scene = scene;
        _requestSceneChange = requestSceneChange;
        _getFps = getFps;
        Services = services;
        Camera = new AndroidScriptCamera(scene, projectDirectory);
    }

    public string Name => _scene.Name;
    public double Fps => _getFps();
    public double RawFps => Fps;
    public double DeltaSeconds => 0.0;
    public long FrameCount => 0;
    public AndroidScriptCamera Camera { get; }
    internal AndroidScriptServices Services { get; }
    public RuntimeLlm Llm => Services.Llm;
    public IAndroidScriptAsr Asr => Services.Asr;
    public IAndroidScriptRealtimeVoice RealtimeVoice => Services.RealtimeVoice;
    public AndroidScriptBubbleManager Bubble => Services.Bubble;
    public RuntimeScenePhysics Physics => _scene.Physics;
    public RuntimeSceneNavigation Navigation => _scene.Navigation;
    public RuntimeDebug Debug => _scene.Debug;
    public IEnumerable<RuntimeGuiControl> GuiControls => _scene.Definition.GuiControls.Select(control => new RuntimeGuiControl(control));
    public RuntimeGuiControl? GetGuiControl(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        GuiControlSettings? control = _scene.Definition.GuiControls.FirstOrDefault(item =>
            string.Equals(item.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase));
        return control is null ? null : new RuntimeGuiControl(control);
    }
    public void LoadScene(string scenePath) => _requestSceneChange(scenePath);
    internal Task<string?> InvokeLlmToolAsync(AndroidScriptEntity entity, string callbackName, RuntimeLlmToolCall toolCall)
        => entity.InvokeLlmToolAsync(callbackName, toolCall);
    public RuntimeEntity? GetEntity(string idOrName) => _scene.GetEntity(idOrName);
    public IEnumerable<AndroidScriptSprite> Sprites => _scene.Definition.Sprites.Select(sprite => new AndroidScriptSprite(sprite));
    public AndroidScriptSprite? GetSprite(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        SpriteSettings? sprite = _scene.Definition.Sprites.FirstOrDefault(item =>
            string.Equals(item.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase));
        return sprite is null ? null : new AndroidScriptSprite(sprite);
    }
}

public sealed class RuntimeGuiControl
{
    private readonly GuiControlSettings _control;
    internal RuntimeGuiControl(GuiControlSettings control) => _control = control;
    public string Id => _control.Id;
    public string Name { get => _control.Name; set => _control.Name = value ?? string.Empty; }
    public string Type { get => _control.Type; set => _control.Type = value ?? string.Empty; }
    public string Text { get => _control.Text; set => _control.Text = value ?? string.Empty; }
    public string Value { get => Text; set => Text = value; }
    public bool Visible { get => _control.Visible; set => _control.Visible = value; }
    public float X { get => _control.X; set => _control.X = Math.Max(0, value); }
    public float Y { get => _control.Y; set => _control.Y = Math.Max(0, value); }
    public float Width { get => _control.Width; set => _control.Width = Math.Max(1, value); }
    public float Height { get => _control.Height; set => _control.Height = Math.Max(1, value); }
    public float Progress { get => _control.Progress; set => _control.Progress = Math.Clamp(value, 0, 1); }
    public bool Checked { get => _control.Checked; set => _control.Checked = value; }
    public void SetValue(string value) => Text = value;
    public void SetText(string value) => Text = value;
    public void SetPosition(float x, float y) { X = x; Y = y; }
    public void SetSize(float width, float height) { Width = width; Height = height; }
    public void Show() => Visible = true;
    public void Hide() => Visible = false;
}

public sealed class AndroidScriptSprite
{
    private readonly SpriteSettings _sprite;

    internal AndroidScriptSprite(SpriteSettings sprite) => _sprite = sprite;

    public string Id => _sprite.Id;
    public string Name { get => _sprite.Name; set => _sprite.Name = value ?? string.Empty; }
    public bool Visible { get => _sprite.Visible; set => _sprite.Visible = value; }
    public float X { get => _sprite.X; set => _sprite.X = value; }
    public float Y { get => _sprite.Y; set => _sprite.Y = value; }
    public float Width { get => _sprite.Width; set => _sprite.Width = Math.Max(1.0f, value); }
    public float Height { get => _sprite.Height; set => _sprite.Height = Math.Max(1.0f, value); }
    public float RotationDegrees { get => _sprite.RotationDegrees; set => _sprite.RotationDegrees = value; }
    public float Opacity { get => _sprite.Opacity; set => _sprite.Opacity = Math.Clamp(value, 0.0f, 1.0f); }
    public int DrawOrder { get => _sprite.DrawOrder; set => _sprite.DrawOrder = value; }
    public string Texture { get => _sprite.Path; set => _sprite.Path = value ?? string.Empty; }
    public string Path { get => _sprite.Path; set => _sprite.Path = value ?? string.Empty; }
    public string LayoutMode { get => _sprite.LayoutMode; set => _sprite.LayoutMode = value ?? "absolute"; }
    public float SourceX { get => _sprite.SourceX; set => _sprite.SourceX = Math.Max(0.0f, value); }
    public float SourceY { get => _sprite.SourceY; set => _sprite.SourceY = Math.Max(0.0f, value); }
    public float SourceWidth { get => _sprite.SourceWidth; set => _sprite.SourceWidth = Math.Max(0.0f, value); }
    public float SourceHeight { get => _sprite.SourceHeight; set => _sprite.SourceHeight = Math.Max(0.0f, value); }

    public void SetPosition(float x, float y) { X = x; Y = y; }
    public void SetSize(float width, float height) { Width = width; Height = height; }
    public void SetSourceRect(float x, float y, float width, float height)
    {
        SourceX = x; SourceY = y; SourceWidth = width; SourceHeight = height;
    }
    public void ResetSourceRect() => SetSourceRect(0.0f, 0.0f, 0.0f, 0.0f);
    public void Show() => Visible = true;
    public void Hide() => Visible = false;
}

public sealed class AndroidScriptEntity
{
    private readonly RuntimeEntity _entity;
    private readonly Action<string> _applyMotion;
    private readonly Action<float?, bool?> _setMotionState;
    private readonly Func<PmxModelComponent?> _resolvePmxModel;
    private readonly Func<string, string> _resolveAssetPath;
    private readonly Action<string, string> _speakWithCallback;
    private readonly Action _stopSpeaking;
    private readonly Action<string, int, float, float, Action?, string> _speak;
    private readonly Func<string, RuntimeLlmToolCall, Task<string?>> _invokeLlmTool;

    internal AndroidScriptEntity(
        RuntimeEntity entity,
        Action<string> applyMotion,
        Action<float?, bool?> setMotionState,
        Func<PmxModelComponent?> resolvePmxModel,
        Func<string, string>? resolveAssetPath = null,
        Action<string, string>? speakWithCallback = null,
        Action? stopSpeaking = null,
        Func<string, RuntimeLlmToolCall, Task<string?>>? invokeLlmTool = null,
        Action<string, int, float, float, Action?, string>? speak = null)
    {
        _entity = entity;
        _applyMotion = applyMotion;
        _setMotionState = setMotionState;
        _resolvePmxModel = resolvePmxModel;
        _resolveAssetPath = resolveAssetPath ?? (path => path);
        _speakWithCallback = speakWithCallback ?? ((_, _) => { });
        _stopSpeaking = stopSpeaking ?? (() => { });
        _invokeLlmTool = invokeLlmTool ?? ((_, _) => Task.FromResult<string?>(null));
        _speak = speak ?? ((text, _, _, _, _, callback) => _speakWithCallback(text, callback));
    }

    public string Id => _entity.Id;
    public string Name { get => _entity.Name; set => _entity.Name = value; }
    public Vector3 Position { get => _entity.Position; set => _entity.Position = value; }
    public Vector3 RotationDegrees { get => _entity.RotationDegrees; set => _entity.RotationDegrees = value; }
    public Vector3 Scale { get => _entity.Scale; set => _entity.Scale = value; }
    public string ReceiveShadowMode { get => _entity.ReceiveShadowMode; set => _entity.ReceiveShadowMode = value; }
    public bool IsPmxModel => _entity.IsPmxModel;
    public CollisionSettings Collision => _entity.Collision;
    public IList<ColliderSettings> Colliders => _entity.Colliders;
    public bool CollisionEnabled { get => _entity.CollisionEnabled; set => _entity.CollisionEnabled = value; }
    public string CollisionShape => _entity.CollisionShape;
    public Vector3 ColliderPosition { get => _entity.ColliderPosition; set => _entity.ColliderPosition = value; }
    public float ColliderRadius { get => _entity.ColliderRadius; set => _entity.ColliderRadius = value; }
    public float ColliderHeight { get => _entity.ColliderHeight; set => _entity.ColliderHeight = value; }
    public string ColliderAxis { get => _entity.ColliderAxis; set => _entity.ColliderAxis = value; }
    public int MotionLayerCount => Pmx?.MotionLayerCount ?? 0;
    public IReadOnlyList<string> MaterialNames => Pmx?.MaterialNames ?? [];
    public IReadOnlyList<string> MorphNames => Pmx?.MorphNames ?? [];
    public IReadOnlyList<string> NodeNames => Pmx?.NodeNames ?? [];
    public IReadOnlyDictionary<string, float> MorphWeights => Pmx?.MorphWeights ?? new Dictionary<string, float>();
    public IReadOnlyDictionary<string, float> MorphSaveAnimWeights => Pmx?.MorphSaveAnimWeights ?? new Dictionary<string, float>();
    public bool PhysicsEnabled
    {
        get => _entity.Definition.EnablePhysics;
        set { _entity.Definition.EnablePhysics = value; if (Pmx is { } model) model.EnablePhysical = value; }
    }
    public Vector3 PhysicsGravity
    {
        get => Pmx?.PhysicsGravity ?? _entity.Definition.PhysicsGravity;
        set { _entity.Definition.PhysicsGravity = value; if (Pmx is { } model) model.PhysicsGravity = value; }
    }
    public Vector3 PhysicsGravityDirection
    {
        get => _entity.Definition.PhysicsGravityDirection.ToVector3();
        set { _entity.Definition.PhysicsGravityDirection = Vector3Dto.FromVector3(value); if (Pmx is { } model) model.PhysicsGravity = _entity.Definition.PhysicsGravity; }
    }
    public float PhysicsGravityMagnitude
    {
        get => _entity.Definition.PhysicsGravityMagnitude;
        set { _entity.Definition.PhysicsGravityMagnitude = Math.Max(value, 0.0f); if (Pmx is { } model) model.PhysicsGravity = _entity.Definition.PhysicsGravity; }
    }
    public bool EnableShadow
    {
        get => _entity.Definition.EnableShadow;
        set { _entity.Definition.EnableShadow = value; if (Pmx is { } model) model.EnableShadow = value; }
    }
    public bool ReceiveShadow
    {
        get => _entity.Definition.ReceiveShadow;
        set { _entity.Definition.ReceiveShadow = value; if (Pmx is { } model) model.ReceiveShadow = value; }
    }
    public bool DrawShadowInMainPass
    {
        get => _entity.Definition.DrawShadowInMainPass;
        set { _entity.Definition.DrawShadowInMainPass = value; if (Pmx is { } model) model.DrawShadowInMainPass = value; }
    }
    public bool LoopMotion
    {
        get => _entity.Definition.LoopMotion;
        set { _entity.Definition.LoopMotion = value; if (Pmx is { } model) model.LoopMotion = value; }
    }
    public bool ResetPhysicsOnMotionLoop
    {
        get => _entity.Definition.ResetPhysicsOnMotionLoop;
        set { _entity.Definition.ResetPhysicsOnMotionLoop = value; if (Pmx is { } model) model.ResetPhysicsOnMotionLoop = value; }
    }
    public float PlaybackSpeed
    {
        get => _entity.Definition.PlaybackSpeed;
        set { _entity.Definition.PlaybackSpeed = Math.Max(value, 0.0f); if (Pmx is { } model) model.PlaybackSpeed = _entity.Definition.PlaybackSpeed; }
    }

    public void SetPosition(float x, float y, float z) => Position = new Vector3(x, y, z);
    public void SetPosition(Vector3 position) => Position = position;
    public void SetScale(float x, float y, float z) => Scale = new Vector3(x, y, z);
    public void LookAt(float targetX, float targetY, float targetZ) => LookAt(new Vector3(targetX, targetY, targetZ));
    public void LookAt(Vector3 target)
    {
        Vector3 direction = target - Position;
        if (direction.LengthSquared() < 1e-8f) return;
        direction = Vector3.Normalize(direction);
        RotationDegrees = new Vector3(
            MathF.Atan2(direction.Y, MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z)) * 180.0f / MathF.PI,
            MathF.Atan2(direction.X, -direction.Z) * 180.0f / MathF.PI,
            0.0f);
    }
    public void ApplyMotion(string motionPath)
    {
        if (Pmx is { } model)
        {
            model.ApplyMotion(_resolveAssetPath(motionPath));
            _entity.Definition.IsPlaying = true;
        }
        else _applyMotion(motionPath);
    }
    public void PlayMotion(bool restart = false)
    {
        if (Pmx is { } model)
        {
            if (restart) model.ResetAnimation();
            model.PlayMotion();
            _entity.Definition.IsPlaying = true;
            return;
        }
        _setMotionState(restart ? 0.0f : null, true);
    }
    public void PauseMotion()
    {
        if (Pmx is { } model) { model.PauseMotion(); _entity.Definition.IsPlaying = false; }
        else _setMotionState(null, false);
    }
    public void StopMotion()
    {
        if (Pmx is { } model) { model.StopMotion(); _entity.Definition.IsPlaying = false; }
        else _setMotionState(0.0f, false);
    }
    public void SeekMotionFrame(float frame)
    {
        if (Pmx is { } model) model.SeekMotionFrame(Math.Max(frame, 0.0f));
        else _setMotionState(Math.Max(frame, 0.0f), null);
    }
    public bool TryResetPhysics() => _entity.TryResetPhysics();
    public void ResetPhysics() => _entity.ResetPhysics();
    public void ResetMotionPhysics() => ResetPhysics();
    public void SetCapsuleCollider(float radius, float height, float centerX = 0, float centerY = 1, float centerZ = 0, string axis = "y") => _entity.SetCapsuleCollider(radius, height, centerX, centerY, centerZ, axis);
    public string AddCapsuleCollider(string name, float radius, float height, float centerX = 0, float centerY = 1, float centerZ = 0, string axis = "y", float rotationX = 0, float rotationY = 0, float rotationZ = 0) => _entity.AddCapsuleCollider(name, radius, height, centerX, centerY, centerZ, axis, rotationX, rotationY, rotationZ);
    public string AddBoxCollider(string name, float sizeX, float sizeY, float sizeZ, float centerX = 0, float centerY = .5f, float centerZ = 0, float rotationX = 0, float rotationY = 0, float rotationZ = 0) => _entity.AddBoxCollider(name, sizeX, sizeY, sizeZ, centerX, centerY, centerZ, rotationX, rotationY, rotationZ);
    public string AddMeshCollider(string name, bool walkable = true, float maxSlopeDegrees = 55) => _entity.AddMeshCollider(name, walkable, maxSlopeDegrees);
    public bool RemoveCollider(string idOrName) => _entity.RemoveCollider(idOrName);
    public void ClearColliders() => _entity.ClearColliders();
    public void DisableCollider() => _entity.DisableCollider();
    public bool TryGetCapsule(out RuntimeCapsule capsule) => _entity.TryGetCapsule(out capsule);
    public bool Raycast(RuntimeRay ray, out float distance, out Vector3 point) => _entity.Raycast(ray, out distance, out point);
    public bool CheckCollision(AndroidScriptEntity other) => _entity.CheckCollision(other._entity);
    public float DistanceToCollider(AndroidScriptEntity other) => _entity.DistanceToCollider(other._entity);
    public void AddMotionLayer(string motionPath, float weight = 1.0f)
        => RequirePmx().AddMotionLayer(_resolveAssetPath(motionPath), weight);
    public void AddMotionLayer(string motionPath, float weight, bool resetPhysicsOnLoop)
        => RequirePmx().AddMotionLayer(_resolveAssetPath(motionPath), weight, resetPhysicsOnLoop);
    public void SetMotionLayers(IEnumerable<MotionLayerDefinition> motionLayers)
        => RequirePmx().SetMotionLayers(motionLayers.Select(layer => new MotionLayerDefinition(
            _resolveAssetPath(layer.MotionPath), layer.Weight, layer.ResetPhysicsOnLoop)));
    public void ClearMotion() => RequirePmx().ClearMotion();
    public void RemoveMotionLayer(string motionPath) => RequirePmx().RemoveMotionLayer(_resolveAssetPath(motionPath));
    public IReadOnlyList<MotionLayerInfo> GetMotionLayers() => Pmx?.GetMotionLayers() ?? [];
    public MotionLayerInfo? GetMotionLayer(string motionPath)
        => Pmx?.GetMotionLayers().FirstOrDefault(layer =>
            string.Equals(layer.MotionPath, _resolveAssetPath(motionPath), StringComparison.OrdinalIgnoreCase));
    public void SeekMotionTime(float timeSeconds) => RequirePmx().SeekMotionTime(Math.Max(timeSeconds, 0.0f));
    public void ResetMotion() { RequirePmx().ResetAnimation(); _entity.Definition.IsPlaying = false; }
    public bool PlayMotionLayer(string motionPath) => TrySetMotionLayerPlaying(motionPath, true);
    public bool PauseMotionLayer(string motionPath) => TrySetMotionLayerPlaying(motionPath, false);
    public void SetMotionLayerPlaying(string motionPath, bool isPlaying) => RequirePmx().SetMotionLayerPlaying(_resolveAssetPath(motionPath), isPlaying);
    public void SetMotionLayerTime(string motionPath, float timeSeconds) => RequirePmx().SetMotionLayerTime(_resolveAssetPath(motionPath), Math.Max(timeSeconds, 0.0f));
    public void SetMotionLayerFrame(string motionPath, float frame) => RequirePmx().SetMotionLayerFrame(_resolveAssetPath(motionPath), Math.Max(frame, 0.0f));
    public void SetMotionLayerWeight(string motionPath, float weight) => RequirePmx().SetMotionLayerWeight(_resolveAssetPath(motionPath), weight);
    public void SetMotionLayerResetPhysicsOnLoop(string motionPath, bool reset) => RequirePmx().SetMotionLayerResetPhysicsOnLoop(_resolveAssetPath(motionPath), reset);
    public bool TrySetMotionLayerPlaying(string motionPath, bool isPlaying) => Pmx?.TrySetMotionLayerPlaying(_resolveAssetPath(motionPath), isPlaying) == true;
    public bool TrySetMotionLayerTime(string motionPath, float timeSeconds) => Pmx?.TrySetMotionLayerTime(_resolveAssetPath(motionPath), timeSeconds) == true;
    public bool TrySetMotionLayerFrame(string motionPath, float frame) => Pmx?.TrySetMotionLayerFrame(_resolveAssetPath(motionPath), frame) == true;
    public bool TrySetMotionLayerWeight(string motionPath, float weight) => Pmx?.TrySetMotionLayerWeight(_resolveAssetPath(motionPath), weight) == true;
    public bool TrySetMotionLayerResetPhysicsOnLoop(string motionPath, bool reset) => Pmx?.TrySetMotionLayerResetPhysicsOnLoop(_resolveAssetPath(motionPath), reset) == true;
    public bool TryGetMorphWeight(string morphName, out float weight) { weight = 0.0f; return Pmx?.TryGetMorphWeight(morphName, out weight) == true; }
    public float GetMorphWeight(string morphName) => RequirePmx().GetMorphWeight(morphName);
    public bool TrySetMorphWeight(string morphName, float weight, bool overrideAnimation = true) => Pmx?.TrySetMorphWeight(morphName, weight, overrideAnimation) == true;
    public void SetMorphWeight(string morphName, float weight, bool overrideAnimation = true) => RequirePmx().SetMorphWeight(morphName, weight, overrideAnimation);
    public bool TryGetMorphSaveAnimWeight(string morphName, out float weight) { weight = 0.0f; return Pmx?.TryGetMorphSaveAnimWeight(morphName, out weight) == true; }
    public float GetMorphSaveAnimWeight(string morphName) => RequirePmx().GetMorphSaveAnimWeight(morphName);
    public bool TrySetMorphSaveAnimWeight(string morphName, float weight) => Pmx?.TrySetMorphSaveAnimWeight(morphName, weight) == true;
    public void SetMorphSaveAnimWeight(string morphName, float weight) => RequirePmx().SetMorphSaveAnimWeight(morphName, weight);
    public bool SaveMorphAnimWeight(string morphName) => Pmx?.SaveMorphAnimWeight(morphName) == true;
    public bool SaveAnimWeight(string morphName) => SaveMorphAnimWeight(morphName);
    public bool LoadMorphAnimWeight(string morphName) => Pmx?.LoadMorphAnimWeight(morphName) == true;
    public bool ClearMorphAnimWeight(string morphName) => Pmx?.ClearMorphAnimWeight(morphName) == true;
    public bool ClearMorphWeightOverride(string morphName) => Pmx?.ClearMorphWeightOverride(morphName) == true;
    public void ClearMorphWeightOverrides() => Pmx?.ClearMorphWeightOverrides();
    public void SaveBaseAnimation() => Pmx?.SaveBaseAnimation();
    public void LoadBaseAnimation() => Pmx?.LoadBaseAnimation();
    public void ClearBaseAnimation() => Pmx?.ClearBaseAnimation();

    public bool TryGetNodeState(string nodeName, out PmxNodeState state) { state = default; return Pmx?.TryGetNodeState(nodeName, out state) == true; }
    public bool TryGetNodeWorld(string nodeName, out Matrix4x4 world) { world = default; return Pmx?.TryGetNodeWorld(nodeName, out world) == true; }
    public PmxNodeState GetNodeState(string nodeName) => RequirePmx().GetNodeState(nodeName);
    public bool TrySetNodeTranslate(string nodeName, Vector3 value, bool overrideAnimation = true) => Pmx?.TrySetNodeTranslate(nodeName, value, overrideAnimation) == true;
    public void SetNodeTranslate(string nodeName, Vector3 value, bool overrideAnimation = true) => RequirePmx().SetNodeTranslate(nodeName, value, overrideAnimation);
    public void SetNodeTranslate(string nodeName, float x, float y, float z, bool overrideAnimation = true) => SetNodeTranslate(nodeName, new Vector3(x, y, z), overrideAnimation);
    public bool TrySetNodeRotate(string nodeName, Quaternion value, bool overrideAnimation = true) => Pmx?.TrySetNodeRotate(nodeName, value, overrideAnimation) == true;
    public void SetNodeRotate(string nodeName, Quaternion value, bool overrideAnimation = true) => RequirePmx().SetNodeRotate(nodeName, value, overrideAnimation);
    public void SetNodeRotateEuler(string nodeName, float x, float y, float z, bool overrideAnimation = true) => SetNodeRotate(nodeName, Quaternion.CreateFromYawPitchRoll(y * MathF.PI / 180.0f, x * MathF.PI / 180.0f, z * MathF.PI / 180.0f), overrideAnimation);
    public bool TrySetNodeScale(string nodeName, Vector3 value, bool overrideAnimation = true) => Pmx?.TrySetNodeScale(nodeName, value, overrideAnimation) == true;
    public void SetNodeScale(string nodeName, Vector3 value, bool overrideAnimation = true) => RequirePmx().SetNodeScale(nodeName, value, overrideAnimation);
    public void SetNodeScale(string nodeName, float x, float y, float z, bool overrideAnimation = true) => SetNodeScale(nodeName, new Vector3(x, y, z), overrideAnimation);
    public bool TrySetNodeAnimTranslate(string nodeName, Vector3 value, bool overrideAnimation = true) => Pmx?.TrySetNodeAnimTranslate(nodeName, value, overrideAnimation) == true;
    public void SetNodeAnimTranslate(string nodeName, Vector3 value, bool overrideAnimation = true) => RequirePmx().SetNodeAnimTranslate(nodeName, value, overrideAnimation);
    public void SetNodeAnimTranslate(string nodeName, float x, float y, float z, bool overrideAnimation = true) => SetNodeAnimTranslate(nodeName, new Vector3(x, y, z), overrideAnimation);
    public bool TrySetNodeAnimRotate(string nodeName, Quaternion value, bool overrideAnimation = true) => Pmx?.TrySetNodeAnimRotate(nodeName, value, overrideAnimation) == true;
    public void SetNodeAnimRotate(string nodeName, Quaternion value, bool overrideAnimation = true) => RequirePmx().SetNodeAnimRotate(nodeName, value, overrideAnimation);
    public void SetNodeAnimRotateEuler(string nodeName, float x, float y, float z, bool overrideAnimation = true) => SetNodeAnimRotate(nodeName, Quaternion.CreateFromYawPitchRoll(y * MathF.PI / 180.0f, x * MathF.PI / 180.0f, z * MathF.PI / 180.0f), overrideAnimation);
    public bool SaveNodeBaseAnimation(string nodeName) => Pmx?.SaveNodeBaseAnimation(nodeName) == true;
    public bool LoadNodeBaseAnimation(string nodeName) => Pmx?.LoadNodeBaseAnimation(nodeName) == true;
    public bool ClearNodeBaseAnimation(string nodeName) => Pmx?.ClearNodeBaseAnimation(nodeName) == true;
    public bool ClearNodeOverrides(string nodeName) => Pmx?.ClearNodeOverrides(nodeName) == true;
    public void ClearAllNodeOverrides() => Pmx?.ClearAllNodeOverrides();

    public bool SetMaterialTexture(int materialIndex, string textureReference) => Pmx?.SetMaterialTexture(materialIndex, ResolveTextureReference(textureReference)) == true;
    public bool SetMaterialTexture(string materialName, string textureReference) => Pmx?.SetMaterialTexture(materialName, ResolveTextureReference(textureReference)) == true;
    public bool SetMaterialRenderTexture(int materialIndex, string renderTextureName) => SetMaterialTexture(materialIndex, "rt:" + renderTextureName);
    public bool SetMaterialRenderTexture(string materialName, string renderTextureName) => SetMaterialTexture(materialName, "rt:" + renderTextureName);
    public void ClearMaterialTextureOverride(int materialIndex) => Pmx?.ClearMaterialTextureOverride(materialIndex);
    public void ClearMaterialTextureOverrides() => Pmx?.ClearMaterialTextureOverrides();
    public bool SetCustomShader(string vertexShaderPath, string fragmentShaderPath)
    {
        if (Pmx is not { } model) return false;
        model.SetCustomShader(_resolveAssetPath(vertexShaderPath), _resolveAssetPath(fragmentShaderPath));
        return true;
    }
    public bool SetCustomShader(string openGlVertexShaderPath, string openGlFragmentShaderPath, string vulkanVertexSpirvPath, string vulkanFragmentSpirvPath)
    {
        if (Pmx is not { } model) return false;
        model.SetCustomShader(
            _resolveAssetPath(openGlVertexShaderPath),
            _resolveAssetPath(openGlFragmentShaderPath),
            _resolveAssetPath(vulkanVertexSpirvPath),
            _resolveAssetPath(vulkanFragmentSpirvPath));
        return true;
    }
    public void SetCustomShaderFloat(string name, float value) => Pmx?.SetCustomShaderFloat(name, value);
    public void SetCustomShaderInt(string name, int value) => Pmx?.SetCustomShaderInt(name, value);
    public void SetCustomShaderVector2(string name, float x, float y) => Pmx?.SetCustomShaderVector2(name, x, y);
    public void SetCustomShaderVector3(string name, float x, float y, float z) => Pmx?.SetCustomShaderVector3(name, x, y, z);
    public void SetCustomShaderVector4(string name, float x, float y, float z, float w) => Pmx?.SetCustomShaderVector4(name, x, y, z, w);
    public void SetCustomShaderColor(string name, float r, float g, float b, float a = 1.0f) => SetCustomShaderVector4(name, r, g, b, a);
    public void ClearCustomShaderUniform(string name) => Pmx?.ClearCustomShaderUniform(name);
    public void ClearCustomShaderUniforms() => Pmx?.ClearCustomShaderUniforms();
    public void ClearCustomShader() => Pmx?.ClearCustomShader();

    public void Speak(string text, Action? onCompleted = null) => _speak(text, 0, 1.0f, 1.0f, onCompleted, string.Empty);
    public void Speak(string text, int speakerId = 0, float speed = 1.0f, float volume = 1.0f, Action? onCompleted = null)
        => _speak(text, speakerId, speed, volume, onCompleted, string.Empty);
    public void SpeakWithCallback(string text, string callbackName) => _speak(text, 0, 1.0f, 1.0f, null, callbackName);
    public void SpeakWithCallback(string text, int speakerId, float speed, float volume, string callbackName) => _speak(text, speakerId, speed, volume, null, callbackName);
    public void StopSpeaking() => _stopSpeaking();

    internal Task<string?> InvokeLlmToolAsync(string callbackName, RuntimeLlmToolCall toolCall)
        => _invokeLlmTool(callbackName, toolCall);

    private PmxModelComponent? Pmx => _resolvePmxModel();
    private PmxModelComponent RequirePmx() => Pmx ?? throw new InvalidOperationException("Entity is not a PMX model or its renderer does not expose PMX controls.");
    private string ResolveTextureReference(string value)
    {
        string normalized = (value ?? string.Empty).Trim();
        return normalized.StartsWith("rt:", StringComparison.OrdinalIgnoreCase) ? normalized : _resolveAssetPath(normalized);
    }
}

public sealed class AndroidScriptAudio
{
    private readonly Func<string, bool> _play;
    private readonly Func<string, bool> _pause;
    private readonly Func<string, bool> _stop;
    private readonly Func<string, float, bool> _setVolume;
    private readonly Func<string, bool, bool> _setLoop;
    private readonly Func<string, bool> _isPlaying;

    internal AndroidScriptAudio(Func<string, bool> play, Func<string, bool> pause, Func<string, bool> stop, Func<string, float, bool>? setVolume = null, Func<string, bool, bool>? setLoop = null, Func<string, bool>? isPlaying = null)
    {
        _play = play;
        _pause = pause;
        _stop = stop;
        _setVolume = setVolume ?? ((_, _) => false);
        _setLoop = setLoop ?? ((_, _) => false);
        _isPlaying = isPlaying ?? (_ => false);
    }

    public bool Play(string idOrName) => _play(idOrName);
    public bool Pause(string idOrName) => _pause(idOrName);
    public bool Stop(string idOrName) => _stop(idOrName);
    public bool SetVolume(string idOrName, float volume) => _setVolume(idOrName, volume);
    public bool SetLoop(string idOrName, bool loop) => _setLoop(idOrName, loop);
    public bool IsPlaying(string idOrName) => _isPlaying(idOrName);
}

public sealed class AndroidScriptCamera
{
    private readonly RuntimeScene _scene;
    private readonly string _projectDirectory;

    internal AndroidScriptCamera(RuntimeScene scene, string projectDirectory)
    {
        _scene = scene;
        _projectDirectory = projectDirectory;
    }

    public void SetCameraVmd(string cameraName, string path, bool loop = true, float playbackSpeed = 1.0f, bool play = true)
    {
        RuntimeCamera? camera = Find(cameraName);
        if (camera is null)
        {
            Console.Error.WriteLine($"Android script camera was not found: {cameraName}");
            return;
        }
        string absolutePath = GameProjectPath.ToAbsolute(_projectDirectory, path);
        if (!File.Exists(absolutePath))
        {
            Console.Error.WriteLine($"Android script camera VMD asset was not found: {absolutePath}");
            return;
        }
        camera.SetVmd(path, loop, playbackSpeed, play);
        camera.Settings.ControlMode = "vmd";
        Console.WriteLine($"Android script applied camera VMD '{path}' to '{camera.Name}'.");
    }
    public void PlayCameraVmd(string cameraName, bool restart = false) => Find(cameraName)?.PlayVmd(restart);
    public void PauseCameraVmd(string cameraName) => Find(cameraName)?.PauseVmd();
    public void SeekCameraVmd(string cameraName, float frame) => Find(cameraName)?.SeekVmd(frame);
    public void ClearCameraVmd(string cameraName)
    {
        RuntimeCamera? camera = Find(cameraName);
        if (camera is null) return;
        camera.SetVmd(string.Empty, play: false);
        camera.Settings.ControlMode = "custom";
    }
    public void UseEditorOrbitMode()
    {
        _scene.MainCamera.Settings.ControlMode = "editor";
    }

    public RuntimeRay ScreenPointToRay(float screenX, float screenY)
    {
        CameraSettings settings = _scene.MainCamera.Settings;
        float width = Math.Max(_scene.MainCamera.Definition.Viewport.Width, 1.0f);
        float height = Math.Max(_scene.MainCamera.Definition.Viewport.Height, 1.0f);
        Vector3 position = settings.Position.ToVector3();
        Vector3 target = settings.Target.ToVector3();
        if (Vector3.DistanceSquared(position, target) < 1e-8f) target = position - Vector3.UnitZ;
        Vector3 forward = Vector3.Normalize(target - position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
        float aspect = width / height;
        float tan = MathF.Tan(Math.Clamp(settings.Fov, 1, 179) * MathF.PI / 360.0f);
        float nx = (screenX / width * 2.0f - 1.0f) * aspect * tan;
        float ny = (1.0f - screenY / height * 2.0f) * tan;
        return new RuntimeRay(position, Vector3.Normalize(forward + right * nx + up * ny));
    }

    public bool RaycastEntity(RuntimeRay ray, out RuntimeRaycastHit hit, float fallbackRadius = 0.5f)
        => _scene.Physics.Raycast(ray, out hit, float.MaxValue);

    private RuntimeCamera? Find(string idOrName) => _scene.Cameras.FirstOrDefault(camera =>
        string.Equals(camera.Id, idOrName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(camera.Name, idOrName, StringComparison.OrdinalIgnoreCase));
}

public sealed class AndroidScriptGlobals : AndroidScriptGlobalsContract
{
    public AndroidScriptGlobals(
        AndroidScriptScene scene,
        AndroidScriptEntity entity,
        AndroidScriptInputApi input,
        AndroidScriptAudio audio,
        float deltaSeconds,
        bool isStart,
        bool isUpdate,
        AndroidRuntimeEvent? runtimeEvent = null,
        AndroidScriptServices? services = null)
    {
        Scene = scene;
        Entity = entity;
        Input = input;
        Audio = audio;
        DeltaSeconds = deltaSeconds;
        IsStart = isStart;
        IsUpdate = isUpdate;
        Event = runtimeEvent;
        Services = services ?? throw new ArgumentNullException(nameof(services));
        base.Scene = scene;
        base.Entity = entity;
        base.Input = input;
        base.Audio = audio;
        base.Network = Services.Network;
        base.Save = Services.Save;
        base.Llm = Services.Llm;
        base.Tts = Services.Tts;
        base.Asr = Services.Asr;
        base.Realtime = Services.Realtime;
        base.Event = runtimeEvent;
        base.Services = Services;
        base.DeltaSeconds = deltaSeconds;
        base.IsStart = isStart;
        base.IsUpdate = isUpdate;
        base.IsGuiEvent = IsGuiEvent;
        base.IsSpriteEvent = IsSpriteEvent;
        base.IsTrayMenuEvent = IsTrayMenuEvent;
        base.IsLoadingEvent = IsLoadingEvent;
        base.IsSpeechEvent = IsSpeechEvent;
        base.IsLlmEvent = IsLlmEvent;
        base.IsAsrEvent = IsAsrEvent;
        base.IsRealtimeVoiceEvent = IsRealtimeVoiceEvent;
        base.LlmEvent = LlmEvent;
        base.LlmRequestId = LlmRequestId;
        base.LlmEventName = LlmEventName;
        base.LlmDelta = LlmDelta;
        base.LlmText = LlmText;
        base.LlmIsFinal = LlmIsFinal;
        base.LlmError = LlmError;
        base.LlmCallbackName = LlmCallbackName;
        base.LlmToolCall = LlmToolCall;
        base.LlmToolCallId = LlmToolCallId;
        base.LlmToolName = LlmToolName;
        base.LlmToolArgumentsJson = LlmToolArgumentsJson;
        base.LlmToolResult = LlmToolResult;
        base.LoadingEventName = LoadingEventName;
        base.LoadingProgress = LoadingProgress;
        base.LoadingMessage = LoadingMessage;
        base.AsrRequestId = AsrRequestId;
        base.AsrEventName = AsrEventName;
        base.AsrText = AsrText;
        base.AsrIsFinal = AsrIsFinal;
        base.AsrError = AsrError;
        base.AsrCallbackName = AsrCallbackName;
        base.RealtimeVoiceRequestId = RealtimeVoiceRequestId;
        base.RealtimeVoiceEventName = RealtimeVoiceEventName;
        base.RealtimeVoiceText = RealtimeVoiceText;
        base.RealtimeVoiceDelta = RealtimeVoiceDelta;
        base.RealtimeVoiceAccumulatedText = RealtimeVoiceAccumulatedText;
        base.RealtimeVoiceIsFinal = RealtimeVoiceIsFinal;
        base.RealtimeVoiceError = RealtimeVoiceError;
        base.RealtimeVoiceCallbackName = RealtimeVoiceCallbackName;
        base.SpriteId = SpriteId;
        base.SpriteName = SpriteName;
        base.SpriteEventName = SpriteEventName;
        base.TrayMenuItemId = TrayMenuItemId;
        base.TrayMenuItemText = TrayMenuItemText;
        base.TrayMenuEventName = TrayMenuEventName;
        base.GuiControlId = GuiControlId;
        base.GuiControlName = GuiControlName;
        base.GuiEventName = GuiEventName;
        base.SpeechCallbackName = SpeechCallbackName;
    }

    public new AndroidScriptScene Scene { get; }
    public new AndroidScriptEntity Entity { get; }
    public new AndroidScriptInputApi Input { get; }
    public new AndroidScriptAudio Audio { get; }
    public new AndroidScriptNetwork Network => Services.Network;
    public new AndroidScriptSaveStore Save => Services.Save;
    public new RuntimeLlm Llm => Services.Llm;
    public new IAndroidScriptTts Tts => Services.Tts;
    public new IAndroidScriptAsr Asr => Services.Asr;
    public new IAndroidScriptRealtime Realtime => Services.Realtime;
    public new float DeltaSeconds { get; private set; }
    public new bool IsStart { get; private set; }
    public new bool IsUpdate { get; private set; }

    /// <summary>非 null 时表示一次 GUI/Sprite/触摸事件；Start/Update 时为 null。</summary>
    public new AndroidRuntimeEvent? Event { get; private set; }

    public new bool IsEvent => Event is not null;
    public new bool IsGuiEvent => string.Equals(Event?.Type, "gui", StringComparison.OrdinalIgnoreCase);
    public new bool IsSpriteEvent => string.Equals(Event?.Type, "sprite", StringComparison.OrdinalIgnoreCase);
    public new bool IsTrayMenuEvent => string.Equals(Event?.Type, "tray", StringComparison.OrdinalIgnoreCase);
    public new bool IsLoadingEvent => string.Equals(Event?.Type, "loading", StringComparison.OrdinalIgnoreCase);
    public new bool IsSpeechEvent => string.Equals(Event?.Type, "speech", StringComparison.OrdinalIgnoreCase);
    public new bool IsLlmEvent => string.Equals(Event?.Type, "llm", StringComparison.OrdinalIgnoreCase);
    public new bool IsAsrEvent => string.Equals(Event?.Type, "asr", StringComparison.OrdinalIgnoreCase);
    public new bool IsRealtimeVoiceEvent => string.Equals(Event?.Type, "realtime_voice", StringComparison.OrdinalIgnoreCase);
    public new RuntimeLlmScriptEvent? LlmEvent => IsLlmEvent
        ? new(LlmRequestId, LlmEventName, LlmDelta, LlmText, LlmIsFinal, LlmError, LlmCallbackName, Event!.ToolCall, Event.ToolResult)
        : null;
    public dynamic? AsrEvent => IsAsrEvent ? Event : null;
    public dynamic? RealtimeVoiceEvent => IsRealtimeVoiceEvent ? Event : null;
    public new string LlmRequestId => IsLlmEvent ? Event!.Id : string.Empty;
    public new string LlmEventName => IsLlmEvent ? Event!.EventName : string.Empty;
    public new string LlmDelta => IsLlmEvent ? Event!.Text : string.Empty;
    public new string LlmText => IsLlmEvent ? Event!.AccumulatedText : string.Empty;
    public new bool LlmIsFinal => IsLlmEvent && Event!.IsFinal;
    public new string LlmError => IsLlmEvent ? Event!.Error : string.Empty;
    public new string LlmCallbackName => IsLlmEvent ? Event!.CallbackName : string.Empty;
    public new RuntimeLlmToolCall? LlmToolCall => IsLlmEvent ? Event!.ToolCall : null;
    public new string LlmToolCallId => Event?.ToolCall?.Id ?? string.Empty;
    public new string LlmToolName => Event?.ToolCall?.Name ?? string.Empty;
    public new string LlmToolArgumentsJson => Event?.ToolCall?.ArgumentsJson ?? string.Empty;
    public new string LlmToolResult => IsLlmEvent ? Event!.ToolResult : string.Empty;
    public new string AsrRequestId => IsAsrEvent ? Event!.Id : string.Empty;
    public new string AsrEventName => IsAsrEvent ? Event!.EventName : string.Empty;
    public new string AsrText => IsAsrEvent ? Event!.Text : string.Empty;
    public new bool AsrIsFinal => IsAsrEvent && Event!.IsFinal;
    public new string AsrError => IsAsrEvent ? Event!.Error : string.Empty;
    public new string AsrCallbackName => IsAsrEvent ? Event!.CallbackName : string.Empty;
    public double AsrOffsetSeconds => 0.0;
    public string AsrWakeWord => IsAsrEvent ? Event!.WakeWord : string.Empty;
    public string AsrRecognizedText => IsAsrEvent ? Event!.RecognizedText : string.Empty;
    public new string RealtimeVoiceRequestId => IsRealtimeVoiceEvent ? Event!.Id : string.Empty;
    public new string RealtimeVoiceEventName => IsRealtimeVoiceEvent ? Event!.EventName : string.Empty;
    public new string RealtimeVoiceText => IsRealtimeVoiceEvent ? Event!.Text : string.Empty;
    public new string RealtimeVoiceDelta => RealtimeVoiceText;
    public new string RealtimeVoiceAccumulatedText => RealtimeVoiceText;
    public new bool RealtimeVoiceIsFinal => IsRealtimeVoiceEvent && (RealtimeVoiceEventName is "voice_done" or "voice_transcribed");
    public new string RealtimeVoiceError => IsRealtimeVoiceEvent ? Event!.Error : string.Empty;
    public new string RealtimeVoiceCallbackName => IsRealtimeVoiceEvent
        ? (string.IsNullOrEmpty(Event!.CallbackName) ? Event.EventName : Event.CallbackName) : string.Empty;
    public string RealtimeVoiceWakeWord => IsRealtimeVoiceEvent ? Event!.WakeWord : string.Empty;
    public string RealtimeVoiceRecognizedText => IsRealtimeVoiceEvent ? Event!.RecognizedText : string.Empty;
    public new string GuiControlId => IsGuiEvent ? Event!.Id : string.Empty;
    public new string GuiControlName => IsGuiEvent ? Event!.Text : string.Empty;
    public new string GuiEventName => IsGuiEvent ? Event!.EventName : string.Empty;
    public new string SpeechCallbackName => IsSpeechEvent ? Event!.EventName : string.Empty;
    public new string LoadingEventName => IsLoadingEvent ? Event!.EventName : string.Empty;
    public new float LoadingProgress => IsLoadingEvent ? Event!.Progress : 0.0f;
    public new string LoadingMessage => IsLoadingEvent ? Event!.Text : string.Empty;
    public new string SpriteId => IsSpriteEvent ? Event!.Id : string.Empty;
    public new string SpriteName => IsSpriteEvent ? Event!.Text : string.Empty;
    public new string SpriteEventName => IsSpriteEvent ? Event!.EventName : string.Empty;
    public new string TrayMenuItemId => IsTrayMenuEvent ? Event!.Id : string.Empty;
    public new string TrayMenuItemText => IsTrayMenuEvent ? Event!.Text : string.Empty;
    public new string TrayMenuEventName => IsTrayMenuEvent ? Event!.EventName : string.Empty;

    public new AndroidScriptServices Services { get; }

    internal void Update(float deltaSeconds, bool isStart, AndroidRuntimeEvent? runtimeEvent)
    {
        DeltaSeconds = deltaSeconds;
        IsStart = isStart;
        IsUpdate = !isStart && runtimeEvent is null;
        Event = runtimeEvent;
        base.Event = runtimeEvent;
        base.DeltaSeconds = DeltaSeconds;
        base.IsStart = IsStart;
        base.IsUpdate = IsUpdate;
        base.IsGuiEvent = IsGuiEvent;
        base.IsSpriteEvent = IsSpriteEvent;
        base.IsSpeechEvent = IsSpeechEvent;
        base.GuiControlId = GuiControlId;
        base.GuiControlName = GuiControlName;
        base.GuiEventName = GuiEventName;
        base.SpeechCallbackName = SpeechCallbackName;
        base.IsTrayMenuEvent = IsTrayMenuEvent;
        base.IsLoadingEvent = IsLoadingEvent;
        base.IsLlmEvent = IsLlmEvent;
        base.IsAsrEvent = IsAsrEvent;
        base.IsRealtimeVoiceEvent = IsRealtimeVoiceEvent;
        base.LlmEvent = LlmEvent;
        base.LlmRequestId = LlmRequestId;
        base.LlmEventName = LlmEventName;
        base.LlmDelta = LlmDelta;
        base.LlmText = LlmText;
        base.LlmIsFinal = LlmIsFinal;
        base.LlmError = LlmError;
        base.LlmCallbackName = LlmCallbackName;
        base.LlmToolCall = LlmToolCall;
        base.LlmToolCallId = LlmToolCallId;
        base.LlmToolName = LlmToolName;
        base.LlmToolArgumentsJson = LlmToolArgumentsJson;
        base.LlmToolResult = LlmToolResult;
        base.LoadingEventName = LoadingEventName;
        base.LoadingProgress = LoadingProgress;
        base.LoadingMessage = LoadingMessage;
        base.AsrRequestId = AsrRequestId;
        base.AsrEventName = AsrEventName;
        base.AsrText = AsrText;
        base.AsrIsFinal = AsrIsFinal;
        base.AsrError = AsrError;
        base.AsrCallbackName = AsrCallbackName;
        base.RealtimeVoiceRequestId = RealtimeVoiceRequestId;
        base.RealtimeVoiceEventName = RealtimeVoiceEventName;
        base.RealtimeVoiceText = RealtimeVoiceText;
        base.RealtimeVoiceDelta = RealtimeVoiceDelta;
        base.RealtimeVoiceAccumulatedText = RealtimeVoiceAccumulatedText;
        base.RealtimeVoiceIsFinal = RealtimeVoiceIsFinal;
        base.RealtimeVoiceError = RealtimeVoiceError;
        base.RealtimeVoiceCallbackName = RealtimeVoiceCallbackName;
        base.SpriteId = SpriteId;
        base.SpriteName = SpriteName;
        base.SpriteEventName = SpriteEventName;
        base.TrayMenuItemId = TrayMenuItemId;
        base.TrayMenuItemText = TrayMenuItemText;
        base.TrayMenuEventName = TrayMenuEventName;
    }
}

public sealed class AndroidScriptServices : IDisposable
{
    private readonly RuntimeScene _scene;
    private readonly Action<string> _requestSceneChange;
    private readonly Func<string, bool> _playAudio;
    private readonly Func<string, bool> _pauseAudio;
    private readonly Func<string, bool> _stopAudio;
    private readonly Func<string, bool> _refreshRenderTexture;
    private readonly Func<string, string, float, bool> _configureRenderTexture;
    private readonly Func<string, AndroidRenderTextureInfo?> _getRenderTexture;
    private readonly Func<IReadOnlyList<AndroidRenderTextureInfo>> _listRenderTextures;
    public AndroidScriptNetwork Network { get; }
    public AndroidScriptSaveStore Save { get; }
    public RuntimeLlm Llm { get; }
    public IAndroidScriptTts Tts { get; }
    public IAndroidScriptRealtime Realtime { get; }
    public IAndroidScriptAsr Asr { get; }
    public IAndroidScriptRealtimeVoice RealtimeVoice { get; }
    public AndroidScriptBubbleManager Bubble { get; }

    internal AndroidScriptServices(
        RuntimeScene scene,
        string projectDirectory,
        Action<string> requestSceneChange,
        Func<string, bool> playAudio,
        Func<string, bool> pauseAudio,
        Func<string, bool> stopAudio,
        Func<string, bool> refreshRenderTexture,
        Func<string, string, float, bool> configureRenderTexture,
        Func<string, AndroidRenderTextureInfo?> getRenderTexture,
        Func<IReadOnlyList<AndroidRenderTextureInfo>> listRenderTextures,
        Action<AndroidRuntimeEvent> dispatchEvent,
        GameProjectLlmSettings llmSettings,
        string saveDirectory,
        MainThreadDispatcher dispatcher,
        IAndroidScriptTts tts,
        IAndroidScriptRealtime realtime,
        IAndroidScriptAsr asr,
        IAndroidScriptRealtimeVoice realtimeVoice)
    {
        _scene = scene;
        _requestSceneChange = requestSceneChange;
        _playAudio = playAudio;
        _pauseAudio = pauseAudio;
        _stopAudio = stopAudio;
        _refreshRenderTexture = refreshRenderTexture;
        _configureRenderTexture = configureRenderTexture;
        _getRenderTexture = getRenderTexture;
        _listRenderTextures = listRenderTextures;
        Network = new AndroidScriptNetwork();
        Save = new AndroidScriptSaveStore(saveDirectory);
        Llm = new RuntimeLlm(llmSettings, projectDirectory, saveDirectory, dispatcher,
            (entity, value) => dispatchEvent(AndroidRuntimeEvent.FromLlm(entity, value)));
        Tts = tts;
        Realtime = realtime;
        RealtimeVoice = realtimeVoice;
        Bubble = AndroidScriptBubbleManager.Shared;
        Asr = asr;
    }

    public void Dispose() => Llm.Dispose();

    public RuntimeEntity? FindEntity(string idOrName) => _scene.GetEntity(idOrName);
    public RuntimeEntity AddPointLight(string name, Vector3 position, Vector3 color, float intensity = 1, float range = 8)
        => _scene.AddPointLight(name, position, color, intensity, range);
    public RuntimeEntity AddSpotLight(string name, Vector3 position, Vector3 rotation, Vector3 color, float intensity = 1, float range = 12)
        => _scene.AddSpotLight(name, position, rotation, color, intensity, range);
    public bool RemoveEntity(string idOrName) => _scene.RemoveEntity(idOrName);
    public void ChangeScene(string path) => _requestSceneChange(path);
    public bool PlayAudio(string idOrName) => _playAudio(idOrName);
    public bool PauseAudio(string idOrName) => _pauseAudio(idOrName);
    public bool StopAudio(string idOrName) => _stopAudio(idOrName);

    /// <summary>使指定 RenderTexture 在下一帧强制刷新。</summary>
    public bool RefreshRenderTexture(string idOrName) => _refreshRenderTexture(idOrName);

    /// <summary>修改 RenderTexture 的刷新模式：every_frame、interval 或 manual。</summary>
    public bool ConfigureRenderTexture(string idOrName, string refreshMode, float intervalSeconds = 0.1f)
        => _configureRenderTexture(idOrName, refreshMode, intervalSeconds);

    /// <summary>查询一个 RenderTexture 的尺寸、刷新模式和最近绘制时间。</summary>
    public AndroidRenderTextureInfo? GetRenderTexture(string idOrName) => _getRenderTexture(idOrName);

    /// <summary>列出当前场景全部可用 RenderTexture。</summary>
    public IReadOnlyList<AndroidRenderTextureInfo> GetRenderTextures() => _listRenderTextures();
}
