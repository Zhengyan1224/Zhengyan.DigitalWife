using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Zhengyan.DigitalWife.Mmd.Game.Speech;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

public readonly struct MotionLayerDefinition
{
    public MotionLayerDefinition(string motionPath, float weight = 1.0f)
        : this(motionPath, weight, null)
    {
    }

    public MotionLayerDefinition(string motionPath, float weight, bool? resetPhysicsOnLoop)
    {
        MotionPath = motionPath;
        Weight = weight;
        ResetPhysicsOnLoop = resetPhysicsOnLoop;
    }

    public string MotionPath { get; }

    public float Weight { get; }

    public bool? ResetPhysicsOnLoop { get; }
}

public readonly struct MotionLayerInfo
{
    public MotionLayerInfo(string motionPath, float weight, float timeSeconds, int durationFrames)
        : this(motionPath, weight, timeSeconds, durationFrames, true, true)
    {
    }

    public MotionLayerInfo(string motionPath, float weight, float timeSeconds, int durationFrames, bool resetPhysicsOnLoop, bool isPlaying)
    {
        MotionPath = motionPath;
        Weight = weight;
        TimeSeconds = timeSeconds;
        DurationFrames = durationFrames;
        ResetPhysicsOnLoop = resetPhysicsOnLoop;
        IsPlaying = isPlaying;
    }

    public string MotionPath { get; }

    public float Weight { get; }

    public float TimeSeconds { get; }

    public int DurationFrames { get; }

    public bool ResetPhysicsOnLoop { get; }

    public bool IsPlaying { get; }
}

public readonly struct PmxNodeState
{
    public PmxNodeState(
        string name,
        Vector3 translate,
        Quaternion rotate,
        Vector3 scale,
        Vector3 animTranslate,
        Quaternion animRotate,
        Vector3 baseAnimTranslate,
        Quaternion baseAnimRotate)
    {
        Name = name;
        Translate = translate;
        Rotate = rotate;
        Scale = scale;
        AnimTranslate = animTranslate;
        AnimRotate = animRotate;
        BaseAnimTranslate = baseAnimTranslate;
        BaseAnimRotate = baseAnimRotate;
    }

    public string Name { get; }

    public Vector3 Translate { get; }

    public Quaternion Rotate { get; }

    public Vector3 Scale { get; }

    public Vector3 AnimTranslate { get; }

    public Quaternion AnimRotate { get; }

    public Vector3 BaseAnimTranslate { get; }

    public Quaternion BaseAnimRotate { get; }
}

public unsafe class PmxModelComponent : DrawableGameComponent
{
    private const float MotionWeightEpsilon = 0.0001f;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    [Flags]
    private enum DirtyFlags
    {
        None = 0,
        Pose = 1 << 0,
        Uv = 1 << 1,
        Material = 1 << 2
    }

    private readonly record struct MotionLayerConfig(string MotionPath, float Weight, bool ResetPhysicsOnLoop);

    private sealed class MotionLayerState : IDisposable
    {
        public MotionLayerState(string motionPath, Zhengyan.DigitalWife.Mmd.VmdAnimation animation, float weight, bool resetPhysicsOnLoop)
        {
            MotionPath = motionPath;
            Animation = animation;
            Weight = weight;
            ResetPhysicsOnLoop = resetPhysicsOnLoop;
            TimeSeconds = 0.0f;
        }

        public string MotionPath { get; }

        public Zhengyan.DigitalWife.Mmd.VmdAnimation Animation { get; }

        public float Weight { get; set; }

        public bool ResetPhysicsOnLoop { get; set; }

        public float TimeSeconds { get; set; }

        public bool IsPlaying { get; set; } = true;

        public void Dispose()
        {
            Animation.Dispose();
        }
    }

    private readonly Dictionary<Zhengyan.DigitalWife.Mmd.MMDMaterial, MaterialTextures> _materials = [];
    private readonly Dictionary<(string Path, GLEnum WrapMode), ITexture2D> _textures = [];
    private readonly Dictionary<int, string> _materialTextureOverrides = [];
    private readonly Dictionary<string, float> _manualMorphWeights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> _manualNodeTranslateOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Quaternion> _manualNodeRotateOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> _manualNodeScaleOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> _manualNodeAnimTranslateOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Quaternion> _manualNodeAnimRotateOverrides = new(StringComparer.Ordinal);
    private readonly TransformUpdaterManager _transformUpdaters = new();
    private readonly List<MotionLayerConfig> _initialMotionLayers = [];

    private string _initialModelPath;

    private PmxEdgeShader? _edgeShader;
    private PmxGroundShadowShader? _groundShadowShader;
    private PmxShadowDepthShader? _shadowDepthShader;
    private EmbeddedToonTextureLibrary? _toonTextures;
    private ITexture2D? _defaultTexture;
    private PmxGpuResources? _gpuResources;
    private IPmxMainPassRenderer? _mainPassRenderer;

    private Zhengyan.DigitalWife.Mmd.MMDModel? _model;
    private readonly List<MotionLayerState> _motionLayers = [];
    private Zhengyan.DigitalWife.Mmd.MMDMesh[] _meshes = [];
    private bool _hasUvMorphs;
    private DirtyFlags _dirtyFlags = DirtyFlags.Pose | DirtyFlags.Uv | DirtyFlags.Material;
    private bool _loaded;
    private int _lastOpaqueMeshDrawCount;
    private int _lastEdgeMeshDrawCount;
    private int _lastShadowMeshDrawCount;
    private Vector3 _boundsMin;
    private Vector3 _boundsMax;

    private uint _positionBuffer;
    private uint _normalBuffer;
    private uint _uvBuffer;
    private uint _indexBuffer;
    private uint _edgeVao;
    private uint _groundShadowVao;
    private uint _shadowDepthVao;
    private uint _customShaderVao;
    private float _animationTime;
    private bool _isPlaying = true;
    private bool _enablePhysical = true;
    private bool _skipPhysicsOnNextPlayFrame;
    private bool _resetPhysicsOnNextPoseUpdate;
    private bool _defaultResetPhysicsOnMotionLoop = true;
    private Vector3 _physicsGravity = MMDPhysics.DefaultGravity;
    private double _lastPoseSolveTimeSeconds = double.NegativeInfinity;
    private Vector3[]? _resetPositions;
    private Vector3[]? _resetNormals;
    private Vector2[]? _resetUVs;
    private Vector3[] _blendNodeTranslations = [];
    private Vector4[] _blendNodeRotations = [];
    private bool[] _blendNodeRotationValid = [];
    private float[] _blendMorphWeights = [];
    private float[] _blendIkEnabledWeights = [];
    private float[] _blendIkTotalWeights = [];
    private CustomShaderProgram? _customShader;
    private readonly Dictionary<string, CustomShaderUniformValue> _customShaderUniforms = new(StringComparer.Ordinal);

    public PmxModelComponent(string modelPath, string? motionPath = null)
    {
        _initialModelPath = NormalizePath(modelPath);
        string? normalizedMotionPath = NormalizeOptionalPath(motionPath);
        _initialMotionLayers.Clear();
        if (!string.IsNullOrWhiteSpace(normalizedMotionPath))
        {
            _initialMotionLayers.Add(new MotionLayerConfig(normalizedMotionPath, 1.0f, _defaultResetPhysicsOnMotionLoop));
        }

        UpdateMotionMetadataFromInitialConfig();
    }

    public OrbitCamera? Camera { get; set; }

    public string? ModelPath { get; private set; }

    public string? MotionPath { get; private set; }

    public bool IsLoaded => _loaded && _model is not null;

    public bool HasAnimation => _motionLayers.Count != 0;

    public Zhengyan.DigitalWife.Mmd.MMDModel? Model => _model;

    public bool IsUsingOpenCL => _model is Zhengyan.DigitalWife.Mmd.PmxModel pmxModel && pmxModel.IsUsingOpenCL;

    public string ComputeBackend => _model is Zhengyan.DigitalWife.Mmd.PmxModel pmxModel ? pmxModel.ComputeBackend : "CPU";

    public IReadOnlyList<string> LoadWarnings => _model is Zhengyan.DigitalWife.Mmd.PmxModel pmxModel ? pmxModel.LoadWarnings : [];

    public Zhengyan.DigitalWife.Mmd.VmdAnimation? Animation => _motionLayers.Count == 0 ? null : _motionLayers[0].Animation;

    public int MotionLayerCount => _motionLayers.Count;

    public int MeshCount => _meshes.Length;

    public int MaterialCount => _materials.Count;

    public IReadOnlyList<string> MaterialNames => _model?.GetMaterials()
        .Select((material, index) => string.IsNullOrWhiteSpace(material.Name) ? $"Material {index}" : material.Name)
        .ToArray() ?? [];

    public IReadOnlyList<string> MorphNames => _model?.GetMorphs()
        .Select(morph => morph.Name)
        .ToArray() ?? [];

    public IReadOnlyList<string> NodeNames => _model?.GetNodes()
        .Select(node => node.Name)
        .ToArray() ?? [];

    public IReadOnlyDictionary<string, float> MorphWeights => GetMorphWeightMap(static morph => morph.Weight);

    public IReadOnlyDictionary<string, float> MorphSaveAnimWeights => GetMorphWeightMap(static morph => morph.SaveAnimWeight);

    public IRuntimeTextureProvider? RuntimeTextureProvider { get; set; }

    public Func<PmxModelComponent, bool>? ShouldUpdatePoseEvaluator { get; set; }

    public float OffscreenPoseUpdateIntervalSeconds { get; set; } = 0.12f;

    public int VertexCount => _model?.GetVertexCount() ?? 0;

    public Vector3 BoundsMin => _boundsMin;

    public Vector3 BoundsMax => _boundsMax;

    public int LastOpaqueMeshDrawCount => _lastOpaqueMeshDrawCount;

    public int LastEdgeMeshDrawCount => _lastEdgeMeshDrawCount;

    public int LastShadowMeshDrawCount => _lastShadowMeshDrawCount;

    public float AnimationTimeSeconds
    {
        get => _animationTime;
        set
        {
            _animationTime = MathF.Max(0.0f, value);
            foreach (MotionLayerState layer in _motionLayers)
            {
                layer.TimeSeconds = _animationTime;
            }

            MarkPoseDirty(includeMaterial: false);
        }
    }

    public Vector3 Position { get; set; } = Vector3.Zero;

    public Vector3 Scale { get; set; } = Vector3.One;

    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    public Vector3 LightColor { get; set; } = Vector3.One;

    public Vector3 AmbientLightColor { get; set; } = Vector3.Zero;

    public float AmbientLightStrength { get; set; } = 0.2f;

    public Vector4 ShadowColor { get; set; } = new(0.17f, 0.17f, 0.17f, 0.7f);

    public Vector3 LightDirection { get; set; } = new(-0.5f, -1.0f, -0.5f);

    public float GroundShadowPlaneHeight { get; set; }

    public ShadowMapBinding? ShadowMap { get; set; }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (!_isPlaying && value)
            {
                _skipPhysicsOnNextPlayFrame = true;
            }

            _isPlaying = value;
        }
    }

    public float PlaybackSpeed { get; set; } = 1.0f;

    public bool LoopMotion { get; set; } = true;

    public bool ResetPhysicsOnMotionLoop
    {
        get
        {
            if (_motionLayers.Count != 0)
            {
                return AreAllMotionLayersResetPhysicsOnLoopEnabled(_motionLayers);
            }

            if (_initialMotionLayers.Count != 0)
            {
                return AreAllMotionLayersResetPhysicsOnLoopEnabled(_initialMotionLayers);
            }

            return _defaultResetPhysicsOnMotionLoop;
        }
        set
        {
            _defaultResetPhysicsOnMotionLoop = value;
            ApplyResetPhysicsOnLoopToAllMotionLayers(value);
        }
    }

    public bool EnablePhysical
    {
        get => _enablePhysical;
        set
        {
            if (_enablePhysical == value)
            {
                return;
            }

            _enablePhysical = value;
            if (_loaded && _model is not null)
            {
                _resetPhysicsOnNextPoseUpdate = true;
                _skipPhysicsOnNextPlayFrame = true;
                MarkPoseDirty(includeMaterial: false);
            }
        }
    }

    public Vector3 PhysicsGravity
    {
        get => _model is PmxModel pmxModel ? pmxModel.PhysicsGravity : _physicsGravity;
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Physics gravity components must be finite.");
            }

            _physicsGravity = value;
            if (_model is PmxModel pmxModel)
            {
                pmxModel.PhysicsGravity = value;
            }
        }
    }

    public bool EnableEdge { get; set; } = true;

    public bool EnableShadow { get; set; } = true;

    public bool DrawShadowInMainPass { get; set; } = true;

    public Matrix4x4 World => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    public bool HasCustomShader => _customShader is not null;

    private bool IsPoseDirty => (_dirtyFlags & DirtyFlags.Pose) != 0;

    private bool IsUvDirty => (_dirtyFlags & DirtyFlags.Uv) != 0;

    private bool IsMaterialDirty => (_dirtyFlags & DirtyFlags.Material) != 0;

    public bool ReloadForCurrentOpenClSetting()
    {
        if (Game is null || !_loaded || _model is null || string.IsNullOrWhiteSpace(ModelPath))
        {
            return false;
        }

        Dictionary<string, float> manualMorphWeights = new(_manualMorphWeights, StringComparer.Ordinal);
        Dictionary<string, Vector3> manualNodeTranslateOverrides = new(_manualNodeTranslateOverrides, StringComparer.Ordinal);
        Dictionary<string, Quaternion> manualNodeRotateOverrides = new(_manualNodeRotateOverrides, StringComparer.Ordinal);
        Dictionary<string, Vector3> manualNodeScaleOverrides = new(_manualNodeScaleOverrides, StringComparer.Ordinal);
        Dictionary<string, Vector3> manualNodeAnimTranslateOverrides = new(_manualNodeAnimTranslateOverrides, StringComparer.Ordinal);
        Dictionary<string, Quaternion> manualNodeAnimRotateOverrides = new(_manualNodeAnimRotateOverrides, StringComparer.Ordinal);
        Dictionary<string, (float TimeSeconds, bool IsPlaying)> motionState = _motionLayers.ToDictionary(
            layer => layer.MotionPath,
            layer => (layer.TimeSeconds, layer.IsPlaying),
            PathComparer);
        bool isPlaying = IsPlaying;
        float animationTime = _animationTime;

        List<MotionLayerConfig> currentMotionLayers = CloneCurrentMotionLayerConfigs();
        LoadResources(GetOpenGl(), ModelPath, currentMotionLayers);
        _manualMorphWeights.Clear();
        foreach ((string key, float value) in manualMorphWeights)
        {
            _manualMorphWeights[key] = value;
        }

        _manualNodeTranslateOverrides.Clear();
        foreach ((string key, Vector3 value) in manualNodeTranslateOverrides)
        {
            _manualNodeTranslateOverrides[key] = value;
        }

        _manualNodeRotateOverrides.Clear();
        foreach ((string key, Quaternion value) in manualNodeRotateOverrides)
        {
            _manualNodeRotateOverrides[key] = value;
        }

        _manualNodeScaleOverrides.Clear();
        foreach ((string key, Vector3 value) in manualNodeScaleOverrides)
        {
            _manualNodeScaleOverrides[key] = value;
        }

        _manualNodeAnimTranslateOverrides.Clear();
        foreach ((string key, Vector3 value) in manualNodeAnimTranslateOverrides)
        {
            _manualNodeAnimTranslateOverrides[key] = value;
        }

        _manualNodeAnimRotateOverrides.Clear();
        foreach ((string key, Quaternion value) in manualNodeAnimRotateOverrides)
        {
            _manualNodeAnimRotateOverrides[key] = value;
        }

        for (int i = 0; i < _motionLayers.Count; i++)
        {
            MotionLayerState layer = _motionLayers[i];
            if (motionState.TryGetValue(layer.MotionPath, out (float TimeSeconds, bool IsPlaying) state))
            {
                layer.TimeSeconds = state.TimeSeconds;
                layer.IsPlaying = state.IsPlaying;
                layer.Animation.ResetPlaybackCursor();
            }
        }

        _animationTime = animationTime;
        IsPlaying = isPlaying;
        MarkPoseDirty();
        return true;
    }

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        GL? gl = GetOpenGl();
        if (gl is not null)
        {
            _edgeShader = new PmxEdgeShader(gl);
            _groundShadowShader = new PmxGroundShadowShader(gl);
            _shadowDepthShader = new PmxShadowDepthShader(gl);
        }
        _toonTextures = new EmbeddedToonTextureLibrary(Game.GraphicsDevice);

        try
        {
            Load(_initialModelPath, _initialMotionLayers);
        }
        catch
        {
            DisposeSharedResources();
            throw;
        }
    }

    public void Load(string modelPath, string? motionPath = null)
    {
        string normalizedModelPath = NormalizePath(modelPath);
        List<MotionLayerConfig> normalizedMotionLayers = CreateSingleMotionConfigList(
            NormalizeOptionalPath(motionPath),
            _defaultResetPhysicsOnMotionLoop);
        Load(normalizedModelPath, normalizedMotionLayers);
    }

    public void Load(string modelPath, IEnumerable<MotionLayerDefinition> motionLayers)
    {
        string normalizedModelPath = NormalizePath(modelPath);
        List<MotionLayerConfig> normalizedMotionLayers = NormalizeMotionLayerDefinitions(
            motionLayers,
            _defaultResetPhysicsOnMotionLoop);
        Load(normalizedModelPath, normalizedMotionLayers);
    }

    public IReadOnlyList<MotionLayerInfo> GetMotionLayers()
    {
        MotionLayerInfo[] result = new MotionLayerInfo[_motionLayers.Count];
        for (int i = 0; i < _motionLayers.Count; i++)
        {
            MotionLayerState layer = _motionLayers[i];
            result[i] = new MotionLayerInfo(
                layer.MotionPath,
                layer.Weight,
                layer.TimeSeconds,
                layer.Animation.MaxKeyTime,
                layer.ResetPhysicsOnLoop,
                layer.IsPlaying);
        }

        return result;
    }

    public void ApplyMotion(string? motionPath)
    {
        string? normalizedMotionPath = NormalizeOptionalPath(motionPath);
        SetMotionLayersCore(CreateSingleMotionConfigList(normalizedMotionPath, _defaultResetPhysicsOnMotionLoop));
    }

    public void SetMotionLayers(IEnumerable<MotionLayerDefinition> motionLayers)
    {
        List<MotionLayerConfig> normalizedMotionLayers = NormalizeMotionLayerDefinitions(
            motionLayers,
            _defaultResetPhysicsOnMotionLoop);
        SetMotionLayersCore(normalizedMotionLayers);
    }

    public void AddMotionLayer(string motionPath, float weight = 1.0f)
    {
        AddMotionLayer(motionPath, weight, null);
    }

    public void AddMotionLayer(string motionPath, float weight, bool? resetPhysicsOnLoop)
    {
        string normalizedMotionPath = NormalizeMotionPathRequired(motionPath);
        float clampedWeight = ClampMotionWeight(weight);
        bool effectiveResetPhysicsOnLoop = resetPhysicsOnLoop ?? _defaultResetPhysicsOnMotionLoop;

        if (Game is null || !_loaded || _model is null)
        {
            int existingInitialIndex = FindMotionLayerConfigIndex(_initialMotionLayers, normalizedMotionPath);
            if (existingInitialIndex >= 0)
            {
                MotionLayerConfig existingLayer = _initialMotionLayers[existingInitialIndex];
                _initialMotionLayers[existingInitialIndex] = new MotionLayerConfig(
                    normalizedMotionPath,
                    clampedWeight,
                    resetPhysicsOnLoop ?? existingLayer.ResetPhysicsOnLoop);
            }
            else
            {
                _initialMotionLayers.Add(new MotionLayerConfig(normalizedMotionPath, clampedWeight, effectiveResetPhysicsOnLoop));
            }

            UpdateMotionMetadataFromInitialConfig();
            MarkPoseDirty(includeMaterial: false);
            return;
        }

        int existingRuntimeIndex = FindMotionLayerStateIndex(_motionLayers, normalizedMotionPath);
        if (existingRuntimeIndex >= 0)
        {
            _motionLayers[existingRuntimeIndex].Weight = clampedWeight;
            if (resetPhysicsOnLoop.HasValue)
            {
                _motionLayers[existingRuntimeIndex].ResetPhysicsOnLoop = resetPhysicsOnLoop.Value;
            }

            SyncInitialMotionLayersFromRuntime();
            MarkPoseDirty(includeMaterial: false);
            return;
        }

        List<MotionLayerState> newLayer = [];
        try
        {
            newLayer = CreateMotionLayers(_model, [new MotionLayerConfig(normalizedMotionPath, clampedWeight, effectiveResetPhysicsOnLoop)]);
            if (newLayer.Count == 0)
            {
                return;
            }

            _motionLayers.Add(newLayer[0]);
            newLayer.Clear();
            SetIkSolversEnabled(_model, true);
            if (_motionLayers.Count == 1)
            {
                IsPlaying = true;
                _skipPhysicsOnNextPlayFrame = true;
            }

            UpdateMotionMetadataFromRuntimeLayers();
            SyncInitialMotionLayersFromRuntime();
            MarkPoseDirty(includeMaterial: false);
        }
        catch
        {
            DisposeMotionLayers(newLayer);
            throw;
        }
    }

    public bool RemoveMotionLayer(string motionPath, bool skipPhysicsOnNextPlayFrame = true)
    {
        string normalizedMotionPath = NormalizeMotionLookupPath(motionPath);

        if (Game is null || !_loaded || _model is null)
        {
            int existingInitialIndex = FindMotionLayerConfigIndex(_initialMotionLayers, normalizedMotionPath);
            if (existingInitialIndex < 0)
            {
                return false;
            }

            _initialMotionLayers.RemoveAt(existingInitialIndex);
            UpdateMotionMetadataFromInitialConfig();
            MarkPoseDirty(includeMaterial: false);
            return true;
        }

        int existingRuntimeIndex = FindMotionLayerStateIndex(_motionLayers, normalizedMotionPath);
        if (existingRuntimeIndex < 0)
        {
            return false;
        }

        MotionLayerState removedLayer = _motionLayers[existingRuntimeIndex];
        _motionLayers.RemoveAt(existingRuntimeIndex);
        removedLayer.Dispose();

        SetIkSolversEnabled(_model, _motionLayers.Count != 0);
        if (_motionLayers.Count == 0)
        {
            MotionPath = null;
            _animationTime = 0.0f;
            IsPlaying = false;
        }
        else
        {
            UpdateMotionMetadataFromRuntimeLayers();
        }

        SyncInitialMotionLayersFromRuntime();
        if (skipPhysicsOnNextPlayFrame)
        {
            _skipPhysicsOnNextPlayFrame = true;
        }
        MarkPoseDirty(includeMaterial: false);
        return true;
    }

    public bool TrySetMotionLayerWeight(string motionPath, float weight)
    {
        string normalizedMotionPath = NormalizeMotionLookupPath(motionPath);
        float clampedWeight = ClampMotionWeight(weight);

        if (Game is null || !_loaded || _model is null)
        {
            int existingInitialIndex = FindMotionLayerConfigIndex(_initialMotionLayers, normalizedMotionPath);
            if (existingInitialIndex < 0)
            {
                return false;
            }

            MotionLayerConfig existingLayer = _initialMotionLayers[existingInitialIndex];
            _initialMotionLayers[existingInitialIndex] = new MotionLayerConfig(
                normalizedMotionPath,
                clampedWeight,
                existingLayer.ResetPhysicsOnLoop);
            UpdateMotionMetadataFromInitialConfig();
            MarkPoseDirty(includeMaterial: false);
            return true;
        }

        int existingRuntimeIndex = FindMotionLayerStateIndex(_motionLayers, normalizedMotionPath);
        if (existingRuntimeIndex < 0)
        {
            return false;
        }

        _motionLayers[existingRuntimeIndex].Weight = clampedWeight;
        SyncInitialMotionLayersFromRuntime();
        MarkPoseDirty(includeMaterial: false);
        return true;
    }

    public bool TrySetMotionLayerPlaying(string motionPath, bool isPlaying)
    {
        string normalizedMotionPath = NormalizeMotionLookupPath(motionPath);

        if (Game is null || !_loaded || _model is null)
        {
            return FindMotionLayerConfigIndex(_initialMotionLayers, normalizedMotionPath) >= 0;
        }

        int existingRuntimeIndex = FindMotionLayerStateIndex(_motionLayers, normalizedMotionPath);
        if (existingRuntimeIndex < 0)
        {
            return false;
        }

        _motionLayers[existingRuntimeIndex].IsPlaying = isPlaying;
        MarkPoseDirty(includeMaterial: false);
        return true;
    }

    public void SetMotionLayerPlaying(string motionPath, bool isPlaying)
    {
        if (!TrySetMotionLayerPlaying(motionPath, isPlaying))
        {
            throw new KeyNotFoundException($"Motion layer not found: {motionPath}");
        }
    }

    public bool TrySetMotionLayerResetPhysicsOnLoop(string motionPath, bool resetPhysicsOnLoop)
    {
        string normalizedMotionPath = NormalizeMotionLookupPath(motionPath);

        if (Game is null || !_loaded || _model is null)
        {
            int existingInitialIndex = FindMotionLayerConfigIndex(_initialMotionLayers, normalizedMotionPath);
            if (existingInitialIndex < 0)
            {
                return false;
            }

            MotionLayerConfig existingLayer = _initialMotionLayers[existingInitialIndex];
            _initialMotionLayers[existingInitialIndex] = new MotionLayerConfig(existingLayer.MotionPath, existingLayer.Weight, resetPhysicsOnLoop);
            MarkPoseDirty(includeMaterial: false);
            return true;
        }

        int existingRuntimeIndex = FindMotionLayerStateIndex(_motionLayers, normalizedMotionPath);
        if (existingRuntimeIndex < 0)
        {
            return false;
        }

        _motionLayers[existingRuntimeIndex].ResetPhysicsOnLoop = resetPhysicsOnLoop;
        SyncInitialMotionLayersFromRuntime();
        return true;
    }

    public void SetMotionLayerResetPhysicsOnLoop(string motionPath, bool resetPhysicsOnLoop)
    {
        if (!TrySetMotionLayerResetPhysicsOnLoop(motionPath, resetPhysicsOnLoop))
        {
            throw new KeyNotFoundException($"Motion layer not found: {motionPath}");
        }
    }

    public void SetMotionLayerWeight(string motionPath, float weight)
    {
        if (!TrySetMotionLayerWeight(motionPath, weight))
        {
            throw new KeyNotFoundException($"Motion layer not found: {motionPath}");
        }
    }

    public bool TrySetMotionLayerTime(string motionPath, float timeSeconds)
    {
        string normalizedMotionPath = NormalizeMotionLookupPath(motionPath);
        float clamped = MathF.Max(0.0f, timeSeconds);

        if (Game is null || !_loaded || _model is null)
        {
            return FindMotionLayerConfigIndex(_initialMotionLayers, normalizedMotionPath) >= 0;
        }

        int existingRuntimeIndex = FindMotionLayerStateIndex(_motionLayers, normalizedMotionPath);
        if (existingRuntimeIndex < 0)
        {
            return false;
        }

        MotionLayerState layer = _motionLayers[existingRuntimeIndex];
        float durationSeconds = GetMotionDurationSeconds(layer);
        layer.TimeSeconds = durationSeconds > 0.0f ? MathF.Min(clamped, durationSeconds) : clamped;
        layer.Animation.ResetPlaybackCursor();
        MarkPoseDirty(includeMaterial: false);
        UpdateMotionMetadataFromRuntimeLayers();
        return true;
    }

    public void SetMotionLayerTime(string motionPath, float timeSeconds)
    {
        if (!TrySetMotionLayerTime(motionPath, timeSeconds))
        {
            throw new KeyNotFoundException($"Motion layer not found: {motionPath}");
        }
    }

    public bool TrySetMotionLayerFrame(string motionPath, float frame)
    {
        return TrySetMotionLayerTime(motionPath, MathF.Max(0.0f, frame) / 30.0f);
    }

    public void SetMotionLayerFrame(string motionPath, float frame)
    {
        if (!TrySetMotionLayerFrame(motionPath, frame))
        {
            throw new KeyNotFoundException($"Motion layer not found: {motionPath}");
        }
    }

    public bool TryGetMorphWeight(string morphName, out float weight)
    {
        weight = 0.0f;
        Zhengyan.DigitalWife.Mmd.MMDMorph? morph = FindMorphByName(morphName);
        if (morph is null)
        {
            return false;
        }

        weight = morph.Weight;
        return true;
    }

    public float GetMorphWeight(string morphName)
    {
        if (!TryGetMorphWeight(morphName, out float weight))
        {
            throw new KeyNotFoundException($"Morph not found: {morphName}");
        }

        return weight;
    }

    public bool TrySetMorphWeight(string morphName, float weight, bool overrideAnimation = true)
    {
        Zhengyan.DigitalWife.Mmd.MMDMorph? morph = FindMorphByName(morphName);
        if (morph is null)
        {
            return false;
        }

        float normalizedWeight = NormalizeMorphWeight(weight);
        morph.Weight = normalizedWeight;
        if (overrideAnimation)
        {
            _manualMorphWeights[morph.Name] = normalizedWeight;
        }

        switch (morph.Kind)
        {
            case Zhengyan.DigitalWife.Mmd.MMDMorphKind.UV:
                MarkUvDirty();
                break;
            case Zhengyan.DigitalWife.Mmd.MMDMorphKind.Material:
                MarkMaterialDirty();
                break;
            default:
                MarkPoseDirty();
                break;
        }

        return true;
    }

    public void SetMorphWeight(string morphName, float weight, bool overrideAnimation = true)
    {
        if (!TrySetMorphWeight(morphName, weight, overrideAnimation))
        {
            throw new KeyNotFoundException($"Morph not found: {morphName}");
        }
    }

    public bool TryGetMorphSaveAnimWeight(string morphName, out float weight)
    {
        weight = 0.0f;
        Zhengyan.DigitalWife.Mmd.MMDMorph? morph = FindMorphByName(morphName);
        if (morph is null)
        {
            return false;
        }

        weight = morph.SaveAnimWeight;
        return true;
    }

    public float GetMorphSaveAnimWeight(string morphName)
    {
        if (!TryGetMorphSaveAnimWeight(morphName, out float weight))
        {
            throw new KeyNotFoundException($"Morph not found: {morphName}");
        }

        return weight;
    }

    public bool TrySetMorphSaveAnimWeight(string morphName, float weight)
    {
        Zhengyan.DigitalWife.Mmd.MMDMorph? morph = FindMorphByName(morphName);
        if (morph is null)
        {
            return false;
        }

        morph.SaveAnimWeight = NormalizeMorphWeight(weight);
        MarkPoseDirty();
        return true;
    }

    public void SetMorphSaveAnimWeight(string morphName, float weight)
    {
        if (!TrySetMorphSaveAnimWeight(morphName, weight))
        {
            throw new KeyNotFoundException($"Morph not found: {morphName}");
        }
    }

    public bool SaveMorphAnimWeight(string morphName)
    {
        Zhengyan.DigitalWife.Mmd.MMDMorph? morph = FindMorphByName(morphName);
        if (morph is null)
        {
            return false;
        }

        morph.SaveBaseAnimation();
        MarkPoseDirty();
        return true;
    }

    public bool SaveAnimWeight(string morphName)
    {
        return SaveMorphAnimWeight(morphName);
    }

    public bool LoadMorphAnimWeight(string morphName)
    {
        Zhengyan.DigitalWife.Mmd.MMDMorph? morph = FindMorphByName(morphName);
        if (morph is null)
        {
            return false;
        }

        morph.LoadBaseAnimation();
        MarkPoseDirty();
        return true;
    }

    public bool ClearMorphAnimWeight(string morphName)
    {
        Zhengyan.DigitalWife.Mmd.MMDMorph? morph = FindMorphByName(morphName);
        if (morph is null)
        {
            return false;
        }

        morph.ClearBaseAnimation();
        MarkPoseDirty();
        return true;
    }

    public bool ClearMorphWeightOverride(string morphName)
    {
        Zhengyan.DigitalWife.Mmd.MMDMorph? morph = FindMorphByName(morphName);
        bool removed = morph is not null
            ? _manualMorphWeights.Remove(morph.Name)
            : _manualMorphWeights.Remove(morphName);
        if (removed)
        {
            MarkPoseDirty();
        }

        return removed;
    }

    public void ClearMorphWeightOverrides()
    {
        if (_manualMorphWeights.Count == 0)
        {
            return;
        }

        _manualMorphWeights.Clear();
        MarkPoseDirty();
    }

    public void SaveBaseAnimation()
    {
        _model?.SaveBaseAnimation();
        MarkPoseDirty();
    }

    public void LoadBaseAnimation()
    {
        _model?.LoadBaseAnimation();
        MarkPoseDirty();
    }

    public void ClearBaseAnimation()
    {
        _model?.ClearBaseAnimation();
        MarkPoseDirty();
    }

    public bool TryGetNodeState(string nodeName, out PmxNodeState state)
    {
        state = default;
        Zhengyan.DigitalWife.Mmd.MMDNode? node = FindNodeByName(nodeName);
        if (node is null)
        {
            return false;
        }

        state = CreateNodeState(node);
        return true;
    }

    public bool TryGetNodeWorld(string nodeName, out Matrix4x4 world)
    {
        world = default;
        Zhengyan.DigitalWife.Mmd.MMDNode? node = FindNodeByName(nodeName);
        if (node is null)
        {
            return false;
        }

        world = node.Global * World;
        return true;
    }

    public PmxNodeState GetNodeState(string nodeName)
    {
        if (!TryGetNodeState(nodeName, out PmxNodeState state))
        {
            throw new KeyNotFoundException($"Node not found: {nodeName}");
        }

        return state;
    }

    public bool TrySetNodeTranslate(string nodeName, Vector3 translate, bool overrideAnimation = true)
    {
        Zhengyan.DigitalWife.Mmd.MMDNode? node = FindNodeByName(nodeName);
        if (node is null)
        {
            return false;
        }

        Vector3 value = NormalizeVector(translate, Vector3.Zero);
        node.Translate = value;
        node.InitTranslate = value;
        if (overrideAnimation)
        {
            _manualNodeTranslateOverrides[node.Name] = value;
        }

        MarkPoseDirty(includeMaterial: false);
        return true;
    }

    public void SetNodeTranslate(string nodeName, Vector3 translate, bool overrideAnimation = true)
    {
        if (!TrySetNodeTranslate(nodeName, translate, overrideAnimation))
        {
            throw new KeyNotFoundException($"Node not found: {nodeName}");
        }
    }

    public bool TrySetNodeRotate(string nodeName, Quaternion rotate, bool overrideAnimation = true)
    {
        Zhengyan.DigitalWife.Mmd.MMDNode? node = FindNodeByName(nodeName);
        if (node is null)
        {
            return false;
        }

        Quaternion value = NormalizeQuaternion(rotate);
        node.Rotate = value;
        node.InitRotate = value;
        if (overrideAnimation)
        {
            _manualNodeRotateOverrides[node.Name] = value;
        }

        MarkPoseDirty(includeMaterial: false);
        return true;
    }

    public void SetNodeRotate(string nodeName, Quaternion rotate, bool overrideAnimation = true)
    {
        if (!TrySetNodeRotate(nodeName, rotate, overrideAnimation))
        {
            throw new KeyNotFoundException($"Node not found: {nodeName}");
        }
    }

    public bool TrySetNodeScale(string nodeName, Vector3 scale, bool overrideAnimation = true)
    {
        Zhengyan.DigitalWife.Mmd.MMDNode? node = FindNodeByName(nodeName);
        if (node is null)
        {
            return false;
        }

        Vector3 value = NormalizeVector(scale, Vector3.One);
        node.Scale = value;
        node.InitScale = value;
        if (overrideAnimation)
        {
            _manualNodeScaleOverrides[node.Name] = value;
        }

        MarkPoseDirty(includeMaterial: false);
        return true;
    }

    public void SetNodeScale(string nodeName, Vector3 scale, bool overrideAnimation = true)
    {
        if (!TrySetNodeScale(nodeName, scale, overrideAnimation))
        {
            throw new KeyNotFoundException($"Node not found: {nodeName}");
        }
    }

    public bool TrySetNodeAnimTranslate(string nodeName, Vector3 translate, bool overrideAnimation = true)
    {
        Zhengyan.DigitalWife.Mmd.MMDNode? node = FindNodeByName(nodeName);
        if (node is null)
        {
            return false;
        }

        Vector3 value = NormalizeVector(translate, Vector3.Zero);
        node.AnimTranslate = value;
        if (overrideAnimation)
        {
            _manualNodeAnimTranslateOverrides[node.Name] = value;
        }

        MarkPoseDirty(includeMaterial: false);
        return true;
    }

    public void SetNodeAnimTranslate(string nodeName, Vector3 translate, bool overrideAnimation = true)
    {
        if (!TrySetNodeAnimTranslate(nodeName, translate, overrideAnimation))
        {
            throw new KeyNotFoundException($"Node not found: {nodeName}");
        }
    }

    public bool TrySetNodeAnimRotate(string nodeName, Quaternion rotate, bool overrideAnimation = true)
    {
        Zhengyan.DigitalWife.Mmd.MMDNode? node = FindNodeByName(nodeName);
        if (node is null)
        {
            return false;
        }

        Quaternion value = NormalizeQuaternion(rotate);
        node.AnimRotate = value;
        if (overrideAnimation)
        {
            _manualNodeAnimRotateOverrides[node.Name] = value;
        }

        MarkPoseDirty(includeMaterial: false);
        return true;
    }

    public void SetNodeAnimRotate(string nodeName, Quaternion rotate, bool overrideAnimation = true)
    {
        if (!TrySetNodeAnimRotate(nodeName, rotate, overrideAnimation))
        {
            throw new KeyNotFoundException($"Node not found: {nodeName}");
        }
    }

    public bool SaveNodeBaseAnimation(string nodeName)
    {
        Zhengyan.DigitalWife.Mmd.MMDNode? node = FindNodeByName(nodeName);
        if (node is null)
        {
            return false;
        }

        node.SaveBaseAnimation();
        MarkPoseDirty(includeMaterial: false);
        return true;
    }

    public bool LoadNodeBaseAnimation(string nodeName)
    {
        Zhengyan.DigitalWife.Mmd.MMDNode? node = FindNodeByName(nodeName);
        if (node is null)
        {
            return false;
        }

        node.LoadBaseAnimation();
        MarkPoseDirty(includeMaterial: false);
        return true;
    }

    public bool ClearNodeBaseAnimation(string nodeName)
    {
        Zhengyan.DigitalWife.Mmd.MMDNode? node = FindNodeByName(nodeName);
        if (node is null)
        {
            return false;
        }

        node.ClearBaseAnimation();
        MarkPoseDirty(includeMaterial: false);
        return true;
    }

    public bool ClearNodeOverrides(string nodeName)
    {
        Zhengyan.DigitalWife.Mmd.MMDNode? node = FindNodeByName(nodeName);
        string key = node?.Name ?? nodeName;
        bool removed = _manualNodeTranslateOverrides.Remove(key);
        removed |= _manualNodeRotateOverrides.Remove(key);
        removed |= _manualNodeScaleOverrides.Remove(key);
        removed |= _manualNodeAnimTranslateOverrides.Remove(key);
        removed |= _manualNodeAnimRotateOverrides.Remove(key);
        if (removed)
        {
            MarkPoseDirty(includeMaterial: false);
        }

        return removed;
    }

    public void ClearAllNodeOverrides()
    {
        if (_manualNodeTranslateOverrides.Count == 0
            && _manualNodeRotateOverrides.Count == 0
            && _manualNodeScaleOverrides.Count == 0
            && _manualNodeAnimTranslateOverrides.Count == 0
            && _manualNodeAnimRotateOverrides.Count == 0)
        {
            return;
        }

        ClearAllNodeOverrideDictionaries();
        MarkPoseDirty(includeMaterial: false);
    }

    public void ClearMotion()
    {
        SetMotionLayersCore([]);
    }

    public void PauseMotion()
    {
        IsPlaying = false;
    }

    public void PlayMotion()
    {
        if (_motionLayers.Count == 0 && _initialMotionLayers.Count == 0)
        {
            return;
        }

        IsPlaying = true;
        _skipPhysicsOnNextPlayFrame = true;
    }

    public void StopMotion()
    {
        ResetAnimation();
    }

    public void SeekMotionTime(float timeSeconds)
    {
        float clamped = MathF.Max(0.0f, timeSeconds);
        AnimationTimeSeconds = clamped;
        for (int i = 0; i < _motionLayers.Count; i++)
        {
            _motionLayers[i].Animation.ResetPlaybackCursor();
        }

        _skipPhysicsOnNextPlayFrame = true;
        UpdateMotionMetadataFromRuntimeLayers();
    }

    public void SeekMotionFrame(float frame)
    {
        SeekMotionTime(MathF.Max(0.0f, frame) / 30.0f);
    }

    private void Load(string normalizedModelPath, IReadOnlyList<MotionLayerConfig> normalizedMotionLayers)
    {
        _initialModelPath = normalizedModelPath;
        _initialMotionLayers.Clear();
        _initialMotionLayers.AddRange(normalizedMotionLayers);
        UpdateMotionMetadataFromInitialConfig();

        if (Game is null)
        {
            ModelPath = normalizedModelPath;
            IsPlaying = normalizedMotionLayers.Count != 0;
            _animationTime = 0.0f;
            MarkPoseDirty();
            return;
        }

        LoadResources(GetOpenGl(), normalizedModelPath, normalizedMotionLayers);
    }

    private void SetMotionLayersCore(IReadOnlyList<MotionLayerConfig> normalizedMotionLayers)
    {
        _initialMotionLayers.Clear();
        _initialMotionLayers.AddRange(normalizedMotionLayers);
        UpdateMotionMetadataFromInitialConfig();

        if (Game is null)
        {
            IsPlaying = normalizedMotionLayers.Count != 0;
            _animationTime = 0.0f;
            MarkPoseDirty();
            return;
        }

        ApplyMotionResources(GetOpenGl(), normalizedMotionLayers);
    }

    public TransformUpdaterManager TransformUpdaters => _transformUpdaters;

    public void AddTransformUpdater(ITransformUpdater updater)
    {
        _transformUpdaters.Add(updater);
    }

    public bool RemoveTransformUpdater(ITransformUpdater updater)
    {
        return _transformUpdaters.Remove(updater);
    }

    public void ClearTransformUpdaters()
    {
        _transformUpdaters.Clear();
    }

    public RelationTransformUpdater CreateRelationTransformUpdater(PmxModelComponent relationComponent, bool bindComponentTransform = true)
    {
        RelationTransformUpdater updater = new(relationComponent, bindComponentTransform);
        AddTransformUpdater(updater);
        return updater;
    }

    public SpeechTransformUpdater CreateSpeechTransformUpdater(
        KanaDictionary kanaDictionary,
        VowelDictionary vowelDictionary,
        IReadOnlyDictionary<string, string>? vowelMorphMap = null,
        string? noMatchFallbackVowel = null)
    {
        SpeechTransformUpdater updater = new(kanaDictionary, vowelDictionary, vowelMorphMap, noMatchFallbackVowel);
        AddTransformUpdater(updater);
        return updater;
    }

    public void ResetAnimation()
    {
        IsPlaying = false;
        _animationTime = 0.0f;
        _skipPhysicsOnNextPlayFrame = true;

        // For active motions, fully reload model+motion to clear any stale IK/physics state
        // while still landing at frame 0 pose after reset.
        if (Game is not null && _loaded && !string.IsNullOrWhiteSpace(ModelPath) && _motionLayers.Count != 0)
        {
            List<MotionLayerConfig> currentMotionLayers = CloneCurrentMotionLayerConfigs();
            LoadResources(GetOpenGl(), ModelPath, currentMotionLayers);
            IsPlaying = false;
            _animationTime = 0.0f;
            _skipPhysicsOnNextPlayFrame = true;
            return;
        }

        if (_model is not null)
        {
            _model.LoadBaseAnimation();
            ApplyManualNodeBaseOverrides(_model);
            ApplyManualMorphWeights(_model);
            ApplyManualNodeAnimationOverrides(_model);
            foreach (MotionLayerState layer in _motionLayers)
            {
                layer.Animation.ResetPlaybackCursor();
                layer.TimeSeconds = 0.0f;
            }

            // Rebuild deterministic frame-0 pose first, then resync physics to that pose.
            RebuildPose(_model, _motionLayers);
            _model.ResetPhysics();
            if (_motionLayers.Count != 0 && EnablePhysical)
            {
                SyncPhysicsAtZero(_model, _motionLayers);
                RebuildPose(_model, _motionLayers);
            }

            _model.Update();
        }

        if (Game is not null && _model is not null)
        {
            UploadVertexBuffers(true);
            ClearDirty(DirtyFlags.Pose | DirtyFlags.Uv | DirtyFlags.Material);
            return;
        }

        MarkPoseDirty();
    }

    public void ResetPhysics()
    {
        if (!_loaded || _model is null)
        {
            return;
        }

        _model.ResetPhysics();
        _skipPhysicsOnNextPlayFrame = true;
        MarkPoseDirty(includeMaterial: false);
    }

    public override void Update(GameTime gameTime)
    {
        if (!_loaded || _model is null)
        {
            return;
        }

        float rawElapsed = Math.Max(0.0f, (float)gameTime.ElapsedSeconds);
        float frameLimitedElapsed = MathF.Min(rawElapsed, 1.0f / 30.0f);
        AnimationTimingMode timingMode = Game?.Options.AnimationTimingMode ?? AnimationTimingMode.FrameRateDependent;
        float playbackElapsed = timingMode == AnimationTimingMode.TimeSynchronized ? rawElapsed : frameLimitedElapsed;
        float updaterElapsed = playbackElapsed;
        float physicsElapsed = frameLimitedElapsed;

        bool resetPhysicsOnLoopThisFrame = false;
        if (IsPlaying && _motionLayers.Count != 0)
        {
            for (int i = 0; i < _motionLayers.Count; i++)
            {
                resetPhysicsOnLoopThisFrame |= AdvanceMotionLayerTime(_motionLayers[i], playbackElapsed);
            }

            UpdateMotionMetadataFromRuntimeLayers();
        }

        bool animatedPose = (IsPlaying && MotionLayersAffectPose()) || _transformUpdaters.HasEnabledUpdaters;
        bool animatedUv = IsPlaying && MotionLayersAffectUv();
        bool animatedMaterial = IsPlaying && MotionLayersAffectMaterial();
        bool manualPose = ManualMorphsAffectPose();
        bool manualUv = ManualMorphsAffectUv();
        bool manualMaterial = ManualMorphsAffectMaterial();
        bool poseDirty = IsPoseDirty || animatedPose || manualPose;
        bool uvDirty = IsUvDirty || animatedUv || manualUv || poseDirty;
        bool materialDirty = IsMaterialDirty || animatedMaterial || manualMaterial || poseDirty;
        bool shouldSimulatePhysics = IsPlaying && EnablePhysical && !_skipPhysicsOnNextPlayFrame && _motionLayers.Count != 0;
        bool requiresFullRatePose = poseDirty || shouldSimulatePhysics;
        bool isVisibleForPose = ShouldUpdatePoseEvaluator?.Invoke(this) ?? true;
        bool shouldUpdatePose = requiresFullRatePose;
        if (!isVisibleForPose && !poseDirty)
        {
            double nowSeconds = gameTime.TotalSeconds;
            shouldUpdatePose = nowSeconds - _lastPoseSolveTimeSeconds >= Math.Max(0.01f, OffscreenPoseUpdateIntervalSeconds);
        }

        if (!shouldUpdatePose && !uvDirty && !materialDirty)
        {
            return;
        }

        _model.BeginAnimation();
        ApplyManualNodeBaseOverrides(_model);
        EvaluateMotionLayers(_model, _motionLayers);
        ApplyManualMorphWeights(_model);
        ApplyManualNodeAnimationOverrides(_model);

        _transformUpdaters.UpdateStage(TransformUpdaterStage.PreAnimation, this, updaterElapsed);
        _model.UpdateMorphAnimation();
        _model.UpdateNodeAnimation(false);
        if (_resetPhysicsOnNextPoseUpdate)
        {
            _model.ResetPhysics();
            _resetPhysicsOnNextPoseUpdate = false;
        }
        else if (resetPhysicsOnLoopThisFrame && EnablePhysical)
        {
            _model.ResetPhysics();
        }
        else if (shouldSimulatePhysics)
        {
            _model.UpdatePhysicsAnimation(physicsElapsed);
        }
        _model.UpdateNodeAnimation(true);
        _transformUpdaters.UpdateStage(TransformUpdaterStage.PostAnimation, this, updaterElapsed);
        _model.EndAnimation();

        if (poseDirty || shouldUpdatePose)
        {
            _model.Update();
            UploadVertexBuffers(uploadUv: uvDirty);
            _lastPoseSolveTimeSeconds = gameTime.TotalSeconds;
        }
        else if (uvDirty)
        {
            UpdateUvsOnly(_model);
            UploadUvBuffer();
        }

        ClearDirty(DirtyFlags.Pose | DirtyFlags.Uv | DirtyFlags.Material);
        _skipPhysicsOnNextPlayFrame = false;
    }

    public override void Draw(GameTime gameTime)
    {
        if (!_loaded || Game is null || Camera is null || _model is null || _defaultTexture is null || _gpuResources is null)
        {
            return;
        }

        _lastOpaqueMeshDrawCount = 0;
        _lastEdgeMeshDrawCount = 0;
        _lastShadowMeshDrawCount = 0;

        if (Game.GraphicsDevice.Backend == GraphicsBackend.Vulkan)
        {
            _lastOpaqueMeshDrawCount = _mainPassRenderer?.Draw(
                _gpuResources,
                _meshes,
                _materials,
                World,
                Camera.View,
                Camera.Projection,
                LightColor,
                LightDirection,
                AmbientLightColor,
                AmbientLightStrength,
                EnableShadow,
                ShadowMap,
                resolveOpenGlOverrideTexture: null) ?? 0;
            return;
        }

        if (_mainPassRenderer is null || _edgeShader is null || _groundShadowShader is null)
        {
            return;
        }

        GL gl = Game.GraphicsDevice.Gl;
        Vector2 screenSize = new(Game.GraphicsDevice.BackBufferSize.X, Game.GraphicsDevice.BackBufferSize.Y);

        Matrix4x4 transform = World;
        Matrix4x4 worldView = transform * Camera.View;
        Matrix4x4 worldViewProjection = worldView * Camera.Projection;

        gl.Enable(GLEnum.DepthTest);
        gl.Enable(GLEnum.Blend);
        gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);

        if (_customShader is not null && _customShaderVao != 0)
        {
            DrawCustomShaderPass(gl, gameTime, transform, worldView, worldViewProjection);
        }
        else
        {
            _lastOpaqueMeshDrawCount = _mainPassRenderer.Draw(
                _gpuResources,
                _meshes,
                _materials,
                transform,
                Camera.View,
                Camera.Projection,
                LightColor,
                LightDirection,
                AmbientLightColor,
                AmbientLightStrength,
                EnableShadow,
                ShadowMap,
                ResolveMaterialOverrideTextureId);
        }

        if (EnableEdge)
        {
            gl.Enable(GLEnum.CullFace);
            gl.CullFace(GLEnum.Front);
            gl.UseProgram(_edgeShader.Id);
            gl.BindVertexArray(_edgeVao);
            gl.SetUniform(_edgeShader.UniWVP, worldViewProjection);
            gl.SetUniform(_edgeShader.UniWV, worldView);
            gl.SetUniform(_edgeShader.UniScreenSize, screenSize);

            foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in _meshes)
            {
                Zhengyan.DigitalWife.Mmd.MMDMaterial mmdMaterial = mesh.Material;
                if (mmdMaterial.EdgeFlag == 0 || mmdMaterial.Alpha == 0.0f)
                {
                    continue;
                }

                gl.SetUniform(_edgeShader.UniEdgeSize, mmdMaterial.EdgeSize);
                gl.SetUniform(_edgeShader.UniEdgeColor, mmdMaterial.EdgeColor);
                gl.DrawElements(GLEnum.Triangles, mesh.VertexCount, GLEnum.UnsignedInt, (void*)(mesh.BeginIndex * sizeof(uint)));
                _lastEdgeMeshDrawCount++;
            }

            gl.BindVertexArray(0);
            gl.UseProgram(0);
        }

        if (DrawShadowInMainPass && ShadowMap is not { TextureId: not 0 })
        {
            DrawGroundShadowPassCore(gl, transform);
        }

        gl.Disable(GLEnum.StencilTest);
        gl.Disable(GLEnum.PolygonOffsetFill);
        gl.Disable(GLEnum.Blend);
        gl.Disable(GLEnum.DepthTest);
    }

    public void DrawGroundShadowPass()
    {
        if (!CanRenderGroundShadow())
        {
            return;
        }

        GL gl = Game!.GraphicsDevice.Gl;
        gl.Enable(GLEnum.DepthTest);
        DrawGroundShadowPassCore(gl, World);
        gl.Disable(GLEnum.StencilTest);
        gl.Disable(GLEnum.PolygonOffsetFill);
        gl.DepthMask(true);
        gl.Disable(GLEnum.Blend);
        gl.Disable(GLEnum.DepthTest);
    }

    public void DrawShadowDepthPass(Matrix4x4 lightViewProjection)
    {
        if (!CanRenderShadowDepth())
        {
            return;
        }

        GL gl = Game!.GraphicsDevice.Gl;
        Matrix4x4 worldLightViewProjection = World * lightViewProjection;

        gl.Enable(GLEnum.DepthTest);
        gl.DepthMask(true);
        gl.Disable(GLEnum.Blend);
        gl.Enable(GLEnum.CullFace);
        gl.CullFace(GLEnum.Back);
        gl.UseProgram(_shadowDepthShader!.Id);
        gl.BindVertexArray(_shadowDepthVao);
        gl.SetUniform(_shadowDepthShader.UniWorldLightViewProjection, worldLightViewProjection);

        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in _meshes)
        {
            Zhengyan.DigitalWife.Mmd.MMDMaterial mmdMaterial = mesh.Material;
            if (!mmdMaterial.ShadowCaster || mmdMaterial.Alpha <= 0.01f)
            {
                continue;
            }

            if (mmdMaterial.BothFace)
            {
                gl.Disable(GLEnum.CullFace);
            }
            else
            {
                gl.Enable(GLEnum.CullFace);
                gl.CullFace(GLEnum.Back);
            }

            gl.DrawElements(GLEnum.Triangles, mesh.VertexCount, GLEnum.UnsignedInt, (void*)(mesh.BeginIndex * sizeof(uint)));
        }

        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.Disable(GLEnum.CullFace);
    }

    private bool CanRenderShadowDepth()
    {
        return _loaded
            && Game is not null
            && _model is not null
            && _shadowDepthShader is not null
            && _shadowDepthVao != 0
            && EnableShadow;
    }

    private bool CanRenderGroundShadow()
    {
        return _loaded
            && Game is not null
            && Camera is not null
            && _model is not null
            && _groundShadowShader is not null
            && EnableShadow
            && ShadowColor.W > 0.0f;
    }

    private void DrawGroundShadowPassCore(GL gl, Matrix4x4 transform)
    {
        if (!CanRenderGroundShadow())
        {
            return;
        }

        gl.Enable(GLEnum.PolygonOffsetFill);
        gl.PolygonOffset(-1.0f, -1.0f);

        Matrix4x4 shadow = Matrix4x4.CreateShadow(-LightDirection, new Plane(0.0f, 1.0f, 0.0f, -GroundShadowPlaneHeight));
        Matrix4x4 shadowMatrix = transform * shadow * Camera!.View * Camera.Projection;
        // Ground shadows are projected overlays. Keep depth testing for occlusion,
        // but never write depth so later transparent surfaces (e.g. water) are not cut out.
        gl.DepthMask(false);

        if (ShadowColor.W < 1.0f)
        {
            gl.Enable(GLEnum.Blend);
            gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
            gl.Enable(GLEnum.StencilTest);
            gl.StencilFuncSeparate(GLEnum.FrontAndBack, GLEnum.Notequal, 1, 1);
            gl.StencilOp(GLEnum.Keep, GLEnum.Keep, GLEnum.Replace);
        }
        else
        {
            gl.Disable(GLEnum.Blend);
        }

        gl.Disable(GLEnum.CullFace);
        gl.UseProgram(_groundShadowShader!.Id);
        gl.BindVertexArray(_groundShadowVao);
        gl.SetUniform(_groundShadowShader.UniWVP, shadowMatrix);
        gl.SetUniform(_groundShadowShader.UniShadowColor, ShadowColor);

        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in _meshes)
        {
            Zhengyan.DigitalWife.Mmd.MMDMaterial mmdMaterial = mesh.Material;
            if (!mmdMaterial.GroundShadow || mmdMaterial.Alpha == 0.0f)
            {
                continue;
            }

            gl.DrawElements(GLEnum.Triangles, mesh.VertexCount, GLEnum.UnsignedInt, (void*)(mesh.BeginIndex * sizeof(uint)));
            _lastShadowMeshDrawCount++;
        }

        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.DepthMask(true);
    }

    private void DrawCustomShaderPass(GL gl, GameTime gameTime, Matrix4x4 transform, Matrix4x4 worldView, Matrix4x4 worldViewProjection)
    {
        if (_customShader is null || Camera is null || _defaultTexture is null)
        {
            return;
        }

        CustomShaderProgram shader = _customShader;
        gl.UseProgram(shader.Id);
        gl.BindVertexArray(_customShaderVao);
        gl.DepthMask(true);

        Vector3 viewSpaceLightDirection = Vector3.Normalize(Vector3.TransformNormal(LightDirection, Camera.View));
        shader.SetUniform("u_World", transform);
        shader.SetUniform("u_View", Camera.View);
        shader.SetUniform("u_Projection", Camera.Projection);
        shader.SetUniform("u_WV", worldView);
        shader.SetUniform("u_WVP", worldViewProjection);
        shader.SetUniform("u_Time", (float)gameTime.TotalSeconds);
        shader.SetUniform("u_DeltaTime", (float)gameTime.ElapsedSeconds);
        shader.SetUniform("u_FrameCount", (int)Math.Min(int.MaxValue, gameTime.FrameCount));
        shader.SetUniform("u_Texture", 0);
        shader.SetUniform("u_Tex", 0);
        shader.SetUniform("u_SphereTex", 1);
        shader.SetUniform("u_ToonTex", 2);
        shader.SetUniform("u_LightColor", LightColor);
        shader.SetUniform("u_LightDir", viewSpaceLightDirection);
        shader.SetUniform("u_AmbientLightColor", AmbientLightColor);
        shader.SetUniform("u_AmbientLightStrength", AmbientLightStrength);
        ApplyCustomShaderShadowUniforms(shader, transform);
        shader.ApplyUniforms(_customShaderUniforms);

        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in _meshes)
        {
            Zhengyan.DigitalWife.Mmd.MMDMaterial mmdMaterial = mesh.Material;
            if (!_materials.TryGetValue(mmdMaterial, out MaterialTextures? materialTextures) || mmdMaterial.Alpha == 0.0f)
            {
                continue;
            }

            int materialIndex = GetMaterialIndex(mmdMaterial);
            BindCustomShaderMaterialTextures(gl, materialTextures, materialIndex);
            shader.SetUniform("u_MaterialIndex", materialIndex);
            shader.SetUniform("u_Ambient", mmdMaterial.Ambient);
            shader.SetUniform("u_Diffuse", mmdMaterial.Diffuse);
            shader.SetUniform("u_Specular", mmdMaterial.Specular);
            shader.SetUniform("u_SpecularPower", mmdMaterial.SpecularPower);
            shader.SetUniform("u_Alpha", mmdMaterial.Alpha);
            shader.SetUniform("u_MaterialAmbient", mmdMaterial.Ambient);
            shader.SetUniform("u_MaterialDiffuse", mmdMaterial.Diffuse);
            shader.SetUniform("u_MaterialSpecular", mmdMaterial.Specular);
            shader.SetUniform("u_MaterialSpecularPower", mmdMaterial.SpecularPower);
            shader.SetUniform("u_MaterialAlpha", mmdMaterial.Alpha);

            if (mmdMaterial.BothFace)
            {
                gl.Disable(GLEnum.CullFace);
            }
            else
            {
                gl.Enable(GLEnum.CullFace);
                gl.CullFace(GLEnum.Back);
            }

            gl.DrawElements(GLEnum.Triangles, mesh.VertexCount, GLEnum.UnsignedInt, (void*)(mesh.BeginIndex * sizeof(uint)));
            _lastOpaqueMeshDrawCount++;
        }

        gl.ActiveTexture(TextureUnit.Texture6);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture5);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture4);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture3);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
    }

    private void BindCustomShaderMaterialTextures(GL gl, MaterialTextures materialTextures, int materialIndex)
    {
        gl.ActiveTexture(TextureUnit.Texture0);
        uint overrideTextureId = ResolveMaterialOverrideTextureId(materialIndex);
        if (overrideTextureId != 0)
        {
            gl.BindTexture(GLEnum.Texture2D, overrideTextureId);
        }
        else if (materialTextures.Texture is not null)
        {
            gl.BindTexture(GLEnum.Texture2D, materialTextures.Texture.LegacyTextureId);
        }
        else
        {
            gl.BindTexture(GLEnum.Texture2D, _defaultTexture?.LegacyTextureId ?? 0);
        }

        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(GLEnum.Texture2D, materialTextures.SphereTexture?.LegacyTextureId ?? _defaultTexture?.LegacyTextureId ?? 0);
        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(GLEnum.Texture2D, materialTextures.ToonTexture?.LegacyTextureId ?? _defaultTexture?.LegacyTextureId ?? 0);
    }

    private void ApplyCustomShaderShadowUniforms(CustomShaderProgram shader, Matrix4x4 transform)
    {
        if (!EnableShadow || ShadowMap is not { TextureId: not 0 } shadowMap)
        {
            shader.SetUniform("u_ShadowMapEnabled", 0);
            return;
        }

        Matrix4x4 lightWvp = transform * shadowMap.LightViewProjection;
        shader.SetUniform("u_ShadowMapEnabled", 1);
        shader.SetUniform("u_ShadowMapStrength", Math.Clamp(shadowMap.Strength, 0.0f, 1.0f));
        shader.SetUniform("u_ShadowMapBias", Math.Max(0.0f, shadowMap.Bias));
        shader.SetUniform("u_LightWVP", lightWvp);
        shader.SetUniform("u_LightViewProjection", shadowMap.LightViewProjection);
        shader.SetUniform("u_ShadowMap0", 3);
        shader.SetUniform("u_ShadowMap1", 4);
        shader.SetUniform("u_ShadowMap2", 5);
        shader.SetUniform("u_ShadowMap3", 6);

        BindShadowTexture(Game!.GraphicsDevice.Gl, shadowMap.TextureId, TextureUnit.Texture3);
        BindShadowTexture(Game.GraphicsDevice.Gl, shadowMap.TextureId, TextureUnit.Texture4);
        BindShadowTexture(Game.GraphicsDevice.Gl, shadowMap.TextureId, TextureUnit.Texture5);
        BindShadowTexture(Game.GraphicsDevice.Gl, shadowMap.TextureId, TextureUnit.Texture6);
    }

    private static void BindShadowTexture(GL gl, uint textureId, TextureUnit unit)
    {
        gl.ActiveTexture(unit);
        gl.BindTexture(GLEnum.Texture2D, textureId);
    }

    public bool SetMaterialTexture(int materialIndex, string textureReference)
    {
        if (_model is null || materialIndex < 0 || materialIndex >= _model.GetMaterials().Count())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(textureReference))
        {
            _materialTextureOverrides.Remove(materialIndex);
            return true;
        }

        _materialTextureOverrides[materialIndex] = textureReference.Trim();
        return true;
    }

    public bool SetMaterialTexture(string materialName, string textureReference)
    {
        if (_model is null || string.IsNullOrWhiteSpace(materialName))
        {
            return false;
        }

        int index = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMaterial material in _model.GetMaterials())
        {
            if (string.Equals(material.Name, materialName, StringComparison.OrdinalIgnoreCase))
            {
                return SetMaterialTexture(index, textureReference);
            }

            index++;
        }

        return false;
    }

    public void ClearMaterialTextureOverride(int materialIndex)
    {
        _materialTextureOverrides.Remove(materialIndex);
    }

    public void ClearMaterialTextureOverrides()
    {
        _materialTextureOverrides.Clear();
    }

    public void SetCustomShader(string vertexShaderPath, string fragmentShaderPath)
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        GL gl = Game.GraphicsDevice.Gl;
        CustomShaderProgram nextShader = new(gl, vertexShaderPath, fragmentShaderPath);
        _customShader?.Dispose();
        _customShader = nextShader;
        RebuildCustomShaderVao(gl);
    }

    public void ClearCustomShader()
    {
        if (Game is not null)
        {
            DeleteCustomShaderVao(Game.GraphicsDevice.Gl);
        }

        _customShader?.Dispose();
        _customShader = null;
        _customShaderUniforms.Clear();
    }

    public void SetCustomShaderFloat(string name, float value)
    {
        _customShaderUniforms[NormalizeUniformName(name)] = CustomShaderUniformValue.FromFloat(value);
    }

    public void SetCustomShaderInt(string name, int value)
    {
        _customShaderUniforms[NormalizeUniformName(name)] = CustomShaderUniformValue.FromInt(value);
    }

    public void SetCustomShaderVector2(string name, float x, float y)
    {
        _customShaderUniforms[NormalizeUniformName(name)] = CustomShaderUniformValue.FromVector2(x, y);
    }

    public void SetCustomShaderVector3(string name, float x, float y, float z)
    {
        _customShaderUniforms[NormalizeUniformName(name)] = CustomShaderUniformValue.FromVector3(x, y, z);
    }

    public void SetCustomShaderVector4(string name, float x, float y, float z, float w)
    {
        _customShaderUniforms[NormalizeUniformName(name)] = CustomShaderUniformValue.FromVector4(x, y, z, w);
    }

    public void ClearCustomShaderUniform(string name)
    {
        _customShaderUniforms.Remove(NormalizeUniformName(name));
    }

    public void ClearCustomShaderUniforms()
    {
        _customShaderUniforms.Clear();
    }

    private uint ResolveMaterialOverrideTextureId(int materialIndex)
    {
        if (!_materialTextureOverrides.TryGetValue(materialIndex, out string? textureReference)
            || string.IsNullOrWhiteSpace(textureReference))
        {
            return 0;
        }

        if (RuntimeTextureProvider is not null && RuntimeTextureProvider.TryGetTexture(textureReference, out uint runtimeTextureId))
        {
            return runtimeTextureId;
        }

        if (Game is null || !File.Exists(textureReference))
        {
            return 0;
        }

        return GetTexture(textureReference, GLEnum.Repeat).LegacyTextureId;
    }

    private int GetMaterialIndex(Zhengyan.DigitalWife.Mmd.MMDMaterial material)
    {
        if (_model is null)
        {
            return -1;
        }

        int index = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMaterial candidate in _model.GetMaterials())
        {
            if (ReferenceEquals(candidate, material))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    public override void Dispose()
    {
        if (Game is not null)
        {
            DisposeModelResources(GetOpenGl());
        }

        DisposeSharedResources();
        base.Dispose();
    }

    private GL? GetOpenGl()
    {
        return Game?.GraphicsDevice.Renderer is OpenGlRenderer renderer ? renderer.Gl : null;
    }

    private void LoadResources(GL? gl, string pmxPath, IReadOnlyList<MotionLayerConfig> motionLayers)
    {
        DisposeModelResources(gl);

        try
        {
            LoadModel(pmxPath, motionLayers);
            Setup(gl);

            ModelPath = pmxPath;
            IsPlaying = motionLayers.Count != 0;
            _animationTime = 0.0f;
            _loaded = true;
            UpdateMotionMetadataFromRuntimeLayers();
        }
        catch
        {
            DisposeModelResources(gl);
            ModelPath = null;
            MotionPath = null;
            throw;
        }
    }

    private void ApplyMotionResources(GL? gl, IReadOnlyList<MotionLayerConfig> motionLayers)
    {
        if (_model is null || !_loaded)
        {
            throw new InvalidOperationException("Model must be loaded before applying motion.");
        }

        List<MotionLayerState> previousMotionLayers = [.. _motionLayers];
        List<MotionLayerState> nextMotionLayers = [];
        string? previousMotionPath = MotionPath;
        float previousAnimationTime = _animationTime;
        bool previousIsPlaying = IsPlaying;
        bool previousSkipPhysicsOnNextPlayFrame = _skipPhysicsOnNextPlayFrame;
        DirtyFlags previousDirtyFlags = _dirtyFlags;

        try
        {
            nextMotionLayers = CreateMotionLayers(_model, motionLayers);
            bool hasMotion = nextMotionLayers.Count != 0;
            SetIkSolversEnabled(_model, hasMotion);

            DirtyFlags nextDirtyFlags;
            if (RestoreResetSnapshot(gl))
            {
                if (!hasMotion)
                {
                    _model.LoadBaseAnimation();
                    RebuildPose(_model, nextMotionLayers);
                }

                nextDirtyFlags = DirtyFlags.None;
            }
            else
            {
                _model.LoadBaseAnimation();
                RebuildPose(_model, nextMotionLayers);
                _model.Update();
                nextDirtyFlags = DirtyFlags.Pose | DirtyFlags.Uv | DirtyFlags.Material;
            }

            _motionLayers.Clear();
            _motionLayers.AddRange(nextMotionLayers);
            nextMotionLayers.Clear();
            DisposeMotionLayers(previousMotionLayers);

            IsPlaying = hasMotion;
            _animationTime = 0.0f;
            _skipPhysicsOnNextPlayFrame = true;
            _dirtyFlags = nextDirtyFlags;
            UpdateMotionMetadataFromRuntimeLayers();
        }
        catch
        {
            DisposeMotionLayers(nextMotionLayers);
            MotionPath = previousMotionPath;
            IsPlaying = previousIsPlaying;
            SetIkSolversEnabled(_model, previousMotionLayers.Count != 0);
            _animationTime = previousAnimationTime;
            _skipPhysicsOnNextPlayFrame = previousSkipPhysicsOnNextPlayFrame;
            _dirtyFlags = previousDirtyFlags;
            throw;
        }
    }

    private void DisposeSharedResources()
    {
        _defaultTexture?.Dispose();
        _defaultTexture = null;

        _toonTextures?.Dispose();
        _toonTextures = null;

        _edgeShader?.Dispose();
        _edgeShader = null;

        _groundShadowShader?.Dispose();
        _groundShadowShader = null;

        _shadowDepthShader?.Dispose();
        _shadowDepthShader = null;

        _customShader?.Dispose();
        _customShader = null;
        _customShaderUniforms.Clear();
    }

    private void DisposeModelResources(GL? gl)
    {
        if (gl is not null)
        {
            DeleteCustomShaderVao(gl);
        }

        foreach (ITexture2D texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
        _materials.Clear();
        _meshes = [];

        _mainPassRenderer?.Dispose();
        _mainPassRenderer = null;
        _gpuResources?.Dispose();
        _gpuResources = null;
        _defaultTexture = null;
        if (gl is not null)
        {
            gl.DeleteVertexArray(_edgeVao);
            gl.DeleteVertexArray(_groundShadowVao);
            gl.DeleteVertexArray(_shadowDepthVao);
        }

        _positionBuffer = 0;
        _normalBuffer = 0;
        _uvBuffer = 0;
        _indexBuffer = 0;
        _edgeVao = 0;
        _groundShadowVao = 0;
        _shadowDepthVao = 0;
        _customShaderVao = 0;

        DisposeMotionLayers(_motionLayers);
        _motionLayers.Clear();
        _manualMorphWeights.Clear();
        ClearAllNodeOverrideDictionaries();

        _model?.Dispose();
        _model = null;

        _resetPositions = null;
        _resetNormals = null;
        _resetUVs = null;

        _loaded = false;
        _hasUvMorphs = false;
        MarkPoseDirty();
        _animationTime = 0.0f;
        _lastOpaqueMeshDrawCount = 0;
        _lastEdgeMeshDrawCount = 0;
        _lastShadowMeshDrawCount = 0;
        _boundsMin = Vector3.Zero;
        _boundsMax = Vector3.Zero;
    }

    private void RebuildCustomShaderVao(GL gl)
    {
        DeleteCustomShaderVao(gl);
        if (_customShader is null || _positionBuffer == 0 || _indexBuffer == 0)
        {
            return;
        }

        _customShaderVao = gl.GenVertexArray();
        gl.BindVertexArray(_customShaderVao);

        if (_customShader.InPos >= 0)
        {
            gl.BindBuffer(GLEnum.ArrayBuffer, _positionBuffer);
            gl.VertexAttribPointer((uint)_customShader.InPos, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
            gl.EnableVertexAttribArray((uint)_customShader.InPos);
        }

        if (_customShader.InNor >= 0)
        {
            gl.BindBuffer(GLEnum.ArrayBuffer, _normalBuffer);
            gl.VertexAttribPointer((uint)_customShader.InNor, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
            gl.EnableVertexAttribArray((uint)_customShader.InNor);
        }

        int uvAttribute = _customShader.InUV >= 0 ? _customShader.InUV : _customShader.InUv;
        if (uvAttribute >= 0)
        {
            gl.BindBuffer(GLEnum.ArrayBuffer, _uvBuffer);
            gl.VertexAttribPointer((uint)uvAttribute, 2, GLEnum.Float, false, (uint)sizeof(Vector2), (void*)0);
            gl.EnableVertexAttribArray((uint)uvAttribute);
        }

        gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBuffer);
        gl.BindVertexArray(0);
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, 0);
    }

    private void DeleteCustomShaderVao(GL gl)
    {
        if (_customShaderVao == 0)
        {
            return;
        }

        gl.DeleteVertexArray(_customShaderVao);
        _customShaderVao = 0;
    }

    private void LoadModel(string pmxPath, IReadOnlyList<MotionLayerConfig> motionLayers)
    {
        Zhengyan.DigitalWife.Mmd.PmxModel model = new();
        List<MotionLayerState> layers = [];

        try
        {
            string modelDirectory = Path.GetDirectoryName(pmxPath) ?? string.Empty;
            if (!model.Load(pmxPath, modelDirectory))
            {
                throw new InvalidDataException($"Unsupported or invalid PMX header: {pmxPath}");
            }

            model.PhysicsGravity = _physicsGravity;
            model.InitializeAnimation();

            layers = CreateMotionLayers(model, motionLayers);
            SetIkSolversEnabled(model, layers.Count != 0);
            RebuildPose(model, layers);

            model.Update();

            _model = model;
            _motionLayers.Clear();
            _motionLayers.AddRange(layers);
            layers.Clear();
            _hasUvMorphs = model.HasUvMorphs;
            MarkPoseDirty();
            ComputeBounds(model);
        }
        catch
        {
            DisposeMotionLayers(layers);
            model.Dispose();
            throw;
        }
    }

    private static Zhengyan.DigitalWife.Mmd.VmdAnimation? CreateAnimation(Zhengyan.DigitalWife.Mmd.MMDModel model, string? vmdPath)
    {
        if (string.IsNullOrWhiteSpace(vmdPath))
        {
            return null;
        }

        Zhengyan.DigitalWife.Mmd.VmdAnimation animation = new();
        if (!animation.Load(vmdPath, model))
        {
            animation.Dispose();
            throw new InvalidDataException($"Unsupported VMD format: {vmdPath}");
        }

        animation.SyncPhysics(0.0f);
        return animation;
    }

    private static void DisposeMotionLayers(IEnumerable<MotionLayerState> motionLayers)
    {
        foreach (MotionLayerState layer in motionLayers)
        {
            layer.Dispose();
        }
    }

    private static List<MotionLayerState> CreateMotionLayers(Zhengyan.DigitalWife.Mmd.MMDModel model, IReadOnlyList<MotionLayerConfig> motionLayers)
    {
        List<MotionLayerState> result = [];
        try
        {
            for (int i = 0; i < motionLayers.Count; i++)
            {
                MotionLayerConfig config = motionLayers[i];
                model.LoadBaseAnimation();
                Zhengyan.DigitalWife.Mmd.VmdAnimation? animation = CreateAnimation(model, config.MotionPath);
                if (animation is null)
                {
                    continue;
                }

                result.Add(new MotionLayerState(
                    config.MotionPath,
                    animation,
                    ClampMotionWeight(config.Weight),
                    config.ResetPhysicsOnLoop));
            }

            model.LoadBaseAnimation();
            return result;
        }
        catch
        {
            DisposeMotionLayers(result);
            throw;
        }
    }

    private static void SetIkSolversEnabled(Zhengyan.DigitalWife.Mmd.MMDModel model, bool enabled)
    {
        foreach (Zhengyan.DigitalWife.Mmd.MMDIkSolver ikSolver in model.GetIkSolvers())
        {
            ikSolver.Enable = enabled;
        }
    }

    private IReadOnlyDictionary<string, float> GetMorphWeightMap(Func<Zhengyan.DigitalWife.Mmd.MMDMorph, float> selector)
    {
        Dictionary<string, float> weights = new(StringComparer.Ordinal);
        if (_model is null)
        {
            return weights;
        }

        foreach (Zhengyan.DigitalWife.Mmd.MMDMorph morph in _model.GetMorphs())
        {
            weights[morph.Name] = selector(morph);
        }

        return weights;
    }

    private Zhengyan.DigitalWife.Mmd.MMDMorph? FindMorphByName(string morphName)
    {
        if (_model is null || string.IsNullOrWhiteSpace(morphName))
        {
            return null;
        }

        string trimmedName = morphName.Trim();
        return _model.FindMorph(morph => string.Equals(morph.Name, trimmedName, StringComparison.Ordinal))
            ?? _model.FindMorph(morph => string.Equals(morph.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    private Zhengyan.DigitalWife.Mmd.MMDNode? FindNodeByName(string nodeName)
    {
        if (_model is null || string.IsNullOrWhiteSpace(nodeName))
        {
            return null;
        }

        string trimmedName = nodeName.Trim();
        return _model.FindNode(node => string.Equals(node.Name, trimmedName, StringComparison.Ordinal))
            ?? _model.FindNode(node => string.Equals(node.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyManualMorphWeights(Zhengyan.DigitalWife.Mmd.MMDModel model)
    {
        if (_manualMorphWeights.Count == 0)
        {
            return;
        }

        foreach (Zhengyan.DigitalWife.Mmd.MMDMorph morph in model.GetMorphs())
        {
            if (_manualMorphWeights.TryGetValue(morph.Name, out float weight))
            {
                morph.Weight = weight;
            }
        }
    }

    private void ApplyManualNodeBaseOverrides(Zhengyan.DigitalWife.Mmd.MMDModel model)
    {
        if (_manualNodeTranslateOverrides.Count == 0
            && _manualNodeRotateOverrides.Count == 0
            && _manualNodeScaleOverrides.Count == 0)
        {
            return;
        }

        foreach (Zhengyan.DigitalWife.Mmd.MMDNode node in model.GetNodes())
        {
            if (_manualNodeTranslateOverrides.TryGetValue(node.Name, out Vector3 translate))
            {
                node.Translate = translate;
                node.InitTranslate = translate;
            }

            if (_manualNodeRotateOverrides.TryGetValue(node.Name, out Quaternion rotate))
            {
                node.Rotate = rotate;
                node.InitRotate = rotate;
            }

            if (_manualNodeScaleOverrides.TryGetValue(node.Name, out Vector3 scale))
            {
                node.Scale = scale;
                node.InitScale = scale;
            }
        }
    }

    private void ApplyManualNodeAnimationOverrides(Zhengyan.DigitalWife.Mmd.MMDModel model)
    {
        if (_manualNodeAnimTranslateOverrides.Count == 0 && _manualNodeAnimRotateOverrides.Count == 0)
        {
            return;
        }

        foreach (Zhengyan.DigitalWife.Mmd.MMDNode node in model.GetNodes())
        {
            if (_manualNodeAnimTranslateOverrides.TryGetValue(node.Name, out Vector3 animTranslate))
            {
                node.AnimTranslate = animTranslate;
            }

            if (_manualNodeAnimRotateOverrides.TryGetValue(node.Name, out Quaternion animRotate))
            {
                node.AnimRotate = animRotate;
            }
        }
    }

    private static PmxNodeState CreateNodeState(Zhengyan.DigitalWife.Mmd.MMDNode node)
    {
        return new PmxNodeState(
            node.Name,
            node.Translate,
            node.Rotate,
            node.Scale,
            node.AnimTranslate,
            node.AnimRotate,
            node.BaseAnimTranslate,
            node.BaseAnimRotate);
    }

    private void ClearAllNodeOverrideDictionaries()
    {
        _manualNodeTranslateOverrides.Clear();
        _manualNodeRotateOverrides.Clear();
        _manualNodeScaleOverrides.Clear();
        _manualNodeAnimTranslateOverrides.Clear();
        _manualNodeAnimRotateOverrides.Clear();
    }

    private static float NormalizeMorphWeight(float weight)
    {
        return float.IsNaN(weight) || float.IsInfinity(weight) ? 0.0f : weight;
    }

    private static Vector3 NormalizeVector(Vector3 value, Vector3 fallback)
    {
        return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z) ? value : fallback;
    }

    private static Quaternion NormalizeQuaternion(Quaternion value)
    {
        if (!IsFinite(value.X) || !IsFinite(value.Y) || !IsFinite(value.Z) || !IsFinite(value.W))
        {
            return Quaternion.Identity;
        }

        return value.LengthSquared() <= MotionWeightEpsilon ? Quaternion.Identity : Quaternion.Normalize(value);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string NormalizeUniformName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Uniform name cannot be empty.", nameof(name));
        }

        return name.Trim();
    }

    private void MarkDirty(DirtyFlags flags)
    {
        _dirtyFlags |= flags;
    }

    private void ClearDirty(DirtyFlags flags)
    {
        _dirtyFlags &= ~flags;
    }

    private void MarkPoseDirty(bool includeMaterial = true)
    {
        MarkDirty(DirtyFlags.Pose | DirtyFlags.Uv | (includeMaterial ? DirtyFlags.Material : DirtyFlags.None));
    }

    private void MarkUvDirty()
    {
        MarkDirty(DirtyFlags.Uv);
    }

    private void MarkMaterialDirty()
    {
        MarkDirty(DirtyFlags.Material);
    }

    private bool MotionLayersAffectPose()
    {
        for (int i = 0; i < _motionLayers.Count; i++)
        {
            MotionLayerState layer = _motionLayers[i];
            if (!layer.IsPlaying || ClampMotionWeight(layer.Weight) <= MotionWeightEpsilon)
            {
                continue;
            }

            if (layer.Animation.HasNodeAnimation || layer.Animation.HasIkAnimation || layer.Animation.HasVertexMorphAnimation)
            {
                return true;
            }
        }

        return false;
    }

    private bool MotionLayersAffectUv()
    {
        if (!_hasUvMorphs)
        {
            return false;
        }

        for (int i = 0; i < _motionLayers.Count; i++)
        {
            MotionLayerState layer = _motionLayers[i];
            if (!layer.IsPlaying || ClampMotionWeight(layer.Weight) <= MotionWeightEpsilon)
            {
                continue;
            }

            if (layer.Animation.HasUvMorphAnimation || layer.Animation.HasVertexMorphAnimation)
            {
                return true;
            }
        }

        return false;
    }

    private bool MotionLayersAffectMaterial()
    {
        for (int i = 0; i < _motionLayers.Count; i++)
        {
            MotionLayerState layer = _motionLayers[i];
            if (!layer.IsPlaying || ClampMotionWeight(layer.Weight) <= MotionWeightEpsilon)
            {
                continue;
            }

            if (layer.Animation.HasMaterialMorphAnimation)
            {
                return true;
            }
        }

        return false;
    }

    private bool ManualMorphsAffectPose()
    {
        if (_manualMorphWeights.Count == 0 || _model is null)
        {
            return false;
        }

        foreach (Zhengyan.DigitalWife.Mmd.MMDMorph morph in _model.GetMorphs())
        {
            if (!_manualMorphWeights.ContainsKey(morph.Name))
            {
                continue;
            }

            if (morph.Kind is Zhengyan.DigitalWife.Mmd.MMDMorphKind.Position
                or Zhengyan.DigitalWife.Mmd.MMDMorphKind.Bone
                or Zhengyan.DigitalWife.Mmd.MMDMorphKind.Group
                or Zhengyan.DigitalWife.Mmd.MMDMorphKind.Unknown)
            {
                return true;
            }
        }

        return false;
    }

    private bool ManualMorphsAffectUv()
    {
        if (!_hasUvMorphs || _manualMorphWeights.Count == 0 || _model is null)
        {
            return false;
        }

        foreach (Zhengyan.DigitalWife.Mmd.MMDMorph morph in _model.GetMorphs())
        {
            if (!_manualMorphWeights.ContainsKey(morph.Name))
            {
                continue;
            }

            if (morph.Kind is Zhengyan.DigitalWife.Mmd.MMDMorphKind.UV
                or Zhengyan.DigitalWife.Mmd.MMDMorphKind.Group
                or Zhengyan.DigitalWife.Mmd.MMDMorphKind.Unknown)
            {
                return true;
            }
        }

        return false;
    }

    private bool ManualMorphsAffectMaterial()
    {
        if (_manualMorphWeights.Count == 0 || _model is null)
        {
            return false;
        }

        foreach (Zhengyan.DigitalWife.Mmd.MMDMorph morph in _model.GetMorphs())
        {
            if (!_manualMorphWeights.ContainsKey(morph.Name))
            {
                continue;
            }

            if (morph.Kind is Zhengyan.DigitalWife.Mmd.MMDMorphKind.Material
                or Zhengyan.DigitalWife.Mmd.MMDMorphKind.Group
                or Zhengyan.DigitalWife.Mmd.MMDMorphKind.Unknown)
            {
                return true;
            }
        }

        return false;
    }

    private void RebuildPose(Zhengyan.DigitalWife.Mmd.MMDModel model, IReadOnlyList<MotionLayerState> motionLayers)
    {
        model.BeginAnimation();
        ApplyManualNodeBaseOverrides(model);
        EvaluateMotionLayers(model, motionLayers);
        ApplyManualMorphWeights(model);
        ApplyManualNodeAnimationOverrides(model);
        model.UpdateMorphAnimation();
        model.UpdateNodeAnimation(false);
        model.UpdateNodeAnimation(true);
        model.EndAnimation();
    }

    private void SyncPhysicsAtZero(Zhengyan.DigitalWife.Mmd.MMDModel model, IReadOnlyList<MotionLayerState> motionLayers)
    {
        if (motionLayers.Count == 0)
        {
            return;
        }

        model.SaveBaseAnimation();

        const int warmupFrameCount = 30;
        for (int i = 0; i < warmupFrameCount; i++)
        {
            float warmupWeight = (1.0f + i) / warmupFrameCount;
            model.BeginAnimation();
            ApplyManualNodeBaseOverrides(model);
            EvaluateMotionLayers(model, motionLayers, warmupWeight, forceFrame: 0.0f);
            ApplyManualMorphWeights(model);
            ApplyManualNodeAnimationOverrides(model);
            model.UpdateMorphAnimation();
            model.UpdateNodeAnimation(false);
            model.UpdatePhysicsAnimation(1.0f / 30.0f);
            model.UpdateNodeAnimation(true);
            model.EndAnimation();
        }
    }

    private void EvaluateMotionLayers(
        Zhengyan.DigitalWife.Mmd.MMDModel model,
        IReadOnlyList<MotionLayerState> motionLayers,
        float blendScale = 1.0f,
        float? forceFrame = null)
    {
        Zhengyan.DigitalWife.Mmd.MMDNode[] nodes = model.GetNodes();
        Zhengyan.DigitalWife.Mmd.MMDMorph[] morphs = model.GetMorphs();
        Zhengyan.DigitalWife.Mmd.MMDIkSolver[] ikSolvers = model.GetIkSolvers();

        EnsureBlendBuffers(nodes.Length, morphs.Length, ikSolvers.Length);
        Array.Clear(_blendNodeTranslations);
        Array.Clear(_blendNodeRotations);
        Array.Clear(_blendNodeRotationValid);
        Array.Clear(_blendMorphWeights);
        Array.Clear(_blendIkEnabledWeights);
        Array.Clear(_blendIkTotalWeights);

        float totalWeight = 0.0f;
        for (int layerIndex = 0; layerIndex < motionLayers.Count; layerIndex++)
        {
            MotionLayerState layer = motionLayers[layerIndex];
            float layerWeight = ClampMotionWeight(layer.Weight) * MathF.Max(0.0f, blendScale);
            if (layerWeight <= MotionWeightEpsilon)
            {
                continue;
            }

            model.LoadBaseAnimation();
            float frame = forceFrame ?? MathF.Max(0.0f, layer.TimeSeconds * 30.0f);
            layer.Animation.Evaluate(frame);
            totalWeight += layerWeight;

            for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                Zhengyan.DigitalWife.Mmd.MMDNode node = nodes[nodeIndex];
                _blendNodeTranslations[nodeIndex] += node.AnimTranslate * layerWeight;

                Vector4 sampledRotation = new(node.AnimRotate.X, node.AnimRotate.Y, node.AnimRotate.Z, node.AnimRotate.W);
                if (_blendNodeRotationValid[nodeIndex] && Vector4.Dot(_blendNodeRotations[nodeIndex], sampledRotation) < 0.0f)
                {
                    sampledRotation = -sampledRotation;
                }

                _blendNodeRotations[nodeIndex] += sampledRotation * layerWeight;
                _blendNodeRotationValid[nodeIndex] = true;
            }

            for (int morphIndex = 0; morphIndex < morphs.Length; morphIndex++)
            {
                _blendMorphWeights[morphIndex] += morphs[morphIndex].Weight * layerWeight;
            }

            for (int ikIndex = 0; ikIndex < ikSolvers.Length; ikIndex++)
            {
                _blendIkTotalWeights[ikIndex] += layerWeight;
                if (ikSolvers[ikIndex].Enable)
                {
                    _blendIkEnabledWeights[ikIndex] += layerWeight;
                }
            }
        }

        if (totalWeight <= MotionWeightEpsilon)
        {
            model.LoadBaseAnimation();
            return;
        }

        float invTotalWeight = 1.0f / totalWeight;
        for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            Zhengyan.DigitalWife.Mmd.MMDNode node = nodes[nodeIndex];
            node.AnimTranslate = _blendNodeTranslations[nodeIndex] * invTotalWeight;
            if (!_blendNodeRotationValid[nodeIndex])
            {
                node.AnimRotate = Quaternion.Identity;
                continue;
            }

            Vector4 rotation = _blendNodeRotations[nodeIndex] * invTotalWeight;
            Quaternion blendedRotation = new(rotation.X, rotation.Y, rotation.Z, rotation.W);
            node.AnimRotate = blendedRotation.LengthSquared() <= MotionWeightEpsilon
                ? Quaternion.Identity
                : Quaternion.Normalize(blendedRotation);
        }

        for (int morphIndex = 0; morphIndex < morphs.Length; morphIndex++)
        {
            morphs[morphIndex].Weight = _blendMorphWeights[morphIndex] * invTotalWeight;
        }

        for (int ikIndex = 0; ikIndex < ikSolvers.Length; ikIndex++)
        {
            float layerWeight = _blendIkTotalWeights[ikIndex];
            if (layerWeight <= MotionWeightEpsilon)
            {
                ikSolvers[ikIndex].Enable = ikSolvers[ikIndex].BaseAnimEnable;
                continue;
            }

            ikSolvers[ikIndex].Enable = _blendIkEnabledWeights[ikIndex] >= (layerWeight * 0.5f);
        }
    }

    private void EnsureBlendBuffers(int nodeCount, int morphCount, int ikCount)
    {
        if (_blendNodeTranslations.Length != nodeCount)
        {
            _blendNodeTranslations = new Vector3[nodeCount];
            _blendNodeRotations = new Vector4[nodeCount];
            _blendNodeRotationValid = new bool[nodeCount];
        }

        if (_blendMorphWeights.Length != morphCount)
        {
            _blendMorphWeights = new float[morphCount];
        }

        if (_blendIkEnabledWeights.Length != ikCount)
        {
            _blendIkEnabledWeights = new float[ikCount];
            _blendIkTotalWeights = new float[ikCount];
        }
    }

    private void Setup(GL? gl)
    {
        if (_model is null || Game is null)
        {
            return;
        }

        _meshes = _model.GetMeshes();

        _gpuResources?.Dispose();
        _gpuResources = new PmxGpuResources(Game!.GraphicsDevice, _model);
        _defaultTexture = _gpuResources.DefaultTexture;
        _positionBuffer = _gpuResources.PositionBuffer.LegacyBufferId;
        _normalBuffer = _gpuResources.NormalBuffer.LegacyBufferId;
        _uvBuffer = _gpuResources.UvBuffer.LegacyBufferId;
        _indexBuffer = _gpuResources.IndexBuffer.LegacyBufferId;

        if (gl is not null)
        {
            if (_edgeShader is null || _groundShadowShader is null || _shadowDepthShader is null)
            {
                throw new InvalidOperationException("The OpenGL PMX shaders have not been initialized.");
            }

            _edgeVao = gl.GenVertexArray();
            gl.BindVertexArray(_edgeVao);
            gl.BindBuffer(GLEnum.ArrayBuffer, _positionBuffer);
            gl.VertexAttribPointer(_edgeShader.InPos, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
            gl.EnableVertexAttribArray(_edgeShader.InPos);
            gl.BindBuffer(GLEnum.ArrayBuffer, _normalBuffer);
            gl.VertexAttribPointer(_edgeShader.InNor, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
            gl.EnableVertexAttribArray(_edgeShader.InNor);
            gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBuffer);
            gl.BindVertexArray(0);

            _groundShadowVao = gl.GenVertexArray();
            gl.BindVertexArray(_groundShadowVao);
            gl.BindBuffer(GLEnum.ArrayBuffer, _positionBuffer);
            gl.VertexAttribPointer(_groundShadowShader.InPos, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
            gl.EnableVertexAttribArray(_groundShadowShader.InPos);
            gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBuffer);
            gl.BindVertexArray(0);

            _shadowDepthVao = gl.GenVertexArray();
            gl.BindVertexArray(_shadowDepthVao);
            gl.BindBuffer(GLEnum.ArrayBuffer, _positionBuffer);
            gl.VertexAttribPointer(_shadowDepthShader.InPos, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
            gl.EnableVertexAttribArray(_shadowDepthShader.InPos);
            gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBuffer);
            gl.BindVertexArray(0);

            if (_customShader is not null)
            {
                RebuildCustomShaderVao(gl);
            }
        }
        else if (Game.GraphicsDevice.Backend != GraphicsBackend.Vulkan)
        {
            throw new NotSupportedException($"PMX rendering is not implemented for {Game.GraphicsDevice.Backend}.");
        }

        foreach (Zhengyan.DigitalWife.Mmd.MMDMaterial mmdMaterial in _model.GetMaterials())
        {
            MaterialTextures textures = new();

            if (!string.IsNullOrEmpty(mmdMaterial.Texture))
            {
                textures.Texture = GetTexture(mmdMaterial.Texture, GLEnum.Repeat);
            }

            if (!string.IsNullOrEmpty(mmdMaterial.SpTexture))
            {
                textures.SphereTexture = GetTexture(mmdMaterial.SpTexture, GLEnum.Repeat);
            }

            if (!string.IsNullOrEmpty(mmdMaterial.ToonTexture))
            {
                textures.ToonTexture = GetTexture(mmdMaterial.ToonTexture, GLEnum.ClampToEdge);
            }

            textures.DescriptorSet = _gpuResources?.CreateMaterialDescriptorSet(
                textures.Texture,
                textures.SphereTexture,
                textures.ToonTexture);
            _materials.Add(mmdMaterial, textures);
        }

        _mainPassRenderer?.Dispose();
        _mainPassRenderer = PmxMainPassRendererFactory.Create(Game.GraphicsDevice, _gpuResources!);

        UploadVertexBuffers(true);
        CaptureResetSnapshot();
        _dirtyFlags = DirtyFlags.None;
    }

    private ITexture2D GetTexture(string texturePath, GLEnum wrapMode)
    {
        if (!File.Exists(texturePath) && _toonTextures is not null && _toonTextures.TryGetTexture(texturePath, out ITexture2D toonTexture))
        {
            return toonTexture;
        }

        (string Path, GLEnum WrapMode) cacheKey = (texturePath, wrapMode);
        if (!_textures.TryGetValue(cacheKey, out ITexture2D? texture))
        {
            texture = Game!.GraphicsDevice.CreateTexture2D();
            texture.LoadFromFile(texturePath);
            _textures.Add(cacheKey, texture);
        }

        return texture;
    }

    private void UploadVertexBuffers(bool uploadUv)
    {
        if (_model is null || _gpuResources is null)
        {
            return;
        }

        _gpuResources.UploadPose(_model, uploadUv);
    }

    private void UploadUvBuffer()
    {
        if (_model is null || _gpuResources is null)
        {
            return;
        }

        _gpuResources.UploadUv(_model);
    }

    private static void UpdateUvsOnly(Zhengyan.DigitalWife.Mmd.MMDModel model)
    {
        int vertexCount = model.GetVertexCount();
        if (vertexCount <= 0)
        {
            return;
        }

        Vector2* sourceUvs = model.GetUVs();
        Vector2* updateUvs = model.GetUpdateUVs();
        Vector2* currentUvs = sourceUvs;
        Vector2* nextUvs = updateUvs;

        for (int i = 0; i < vertexCount; i++)
        {
            *nextUvs = *currentUvs;
            currentUvs++;
            nextUvs++;
        }
    }

    private bool ShouldUploadUvThisFrame()
    {
        if (!_hasUvMorphs)
        {
            return false;
        }

        if (_motionLayers.Count != 0)
        {
            return true;
        }

        if (_manualMorphWeights.Count != 0)
        {
            return true;
        }

        return false;
    }

    private void CaptureResetSnapshot()
    {
        if (_model is null)
        {
            _resetPositions = null;
            _resetNormals = null;
            _resetUVs = null;
            return;
        }

        int vertexCount = _model.GetVertexCount();
        _resetPositions = new ReadOnlySpan<Vector3>(_model.GetUpdatePositions(), vertexCount).ToArray();
        _resetNormals = new ReadOnlySpan<Vector3>(_model.GetUpdateNormals(), vertexCount).ToArray();
        _resetUVs = new ReadOnlySpan<Vector2>(_model.GetUpdateUVs(), vertexCount).ToArray();
    }

    private bool RestoreResetSnapshot(GL? gl)
    {
        if (_resetPositions is null || _resetNormals is null || _resetUVs is null || _gpuResources is null)
        {
            return false;
        }

        _gpuResources.PositionBuffer.Update<Vector3>(_resetPositions);
        _gpuResources.NormalBuffer.Update<Vector3>(_resetNormals);
        _gpuResources.UvBuffer.Update<Vector2>(_resetUVs);
        return true;
    }

    private List<MotionLayerConfig> CloneCurrentMotionLayerConfigs()
    {
        List<MotionLayerConfig> result = new(_motionLayers.Count);
        for (int i = 0; i < _motionLayers.Count; i++)
        {
            MotionLayerState layer = _motionLayers[i];
            result.Add(new MotionLayerConfig(layer.MotionPath, layer.Weight, layer.ResetPhysicsOnLoop));
        }

        return result;
    }

    private static List<MotionLayerConfig> CreateSingleMotionConfigList(string? motionPath, bool defaultResetPhysicsOnLoop)
    {
        if (string.IsNullOrWhiteSpace(motionPath))
        {
            return [];
        }

        return [new MotionLayerConfig(motionPath, 1.0f, defaultResetPhysicsOnLoop)];
    }

    private static List<MotionLayerConfig> NormalizeMotionLayerDefinitions(
        IEnumerable<MotionLayerDefinition> motionLayers,
        bool defaultResetPhysicsOnLoop)
    {
        ArgumentNullException.ThrowIfNull(motionLayers);

        List<MotionLayerConfig> result = [];
        foreach (MotionLayerDefinition layer in motionLayers)
        {
            string normalizedMotionPath = NormalizeMotionPathRequired(layer.MotionPath);
            float weight = ClampMotionWeight(layer.Weight);
            bool resetPhysicsOnLoop = layer.ResetPhysicsOnLoop ?? defaultResetPhysicsOnLoop;
            int existingIndex = FindMotionLayerConfigIndex(result, normalizedMotionPath);
            if (existingIndex >= 0)
            {
                result[existingIndex] = new MotionLayerConfig(normalizedMotionPath, weight, resetPhysicsOnLoop);
            }
            else
            {
                result.Add(new MotionLayerConfig(normalizedMotionPath, weight, resetPhysicsOnLoop));
            }
        }

        return result;
    }

    private static int FindMotionLayerConfigIndex(IReadOnlyList<MotionLayerConfig> motionLayers, string normalizedMotionPath)
    {
        for (int i = 0; i < motionLayers.Count; i++)
        {
            if (PathComparer.Equals(motionLayers[i].MotionPath, normalizedMotionPath))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindMotionLayerStateIndex(IReadOnlyList<MotionLayerState> motionLayers, string normalizedMotionPath)
    {
        for (int i = 0; i < motionLayers.Count; i++)
        {
            if (PathComparer.Equals(motionLayers[i].MotionPath, normalizedMotionPath))
            {
                return i;
            }
        }

        return -1;
    }

    private static float ClampMotionWeight(float weight)
    {
        if (!float.IsFinite(weight))
        {
            return 0.0f;
        }

        return Math.Clamp(weight, 0.0f, 1.0f);
    }

    private static string NormalizeMotionPathRequired(string motionPath)
    {
        string? normalized = NormalizeOptionalPath(motionPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Motion path is required.", nameof(motionPath));
        }

        return normalized;
    }

    private static string NormalizeMotionLookupPath(string motionPath)
    {
        if (string.IsNullOrWhiteSpace(motionPath))
        {
            throw new ArgumentException("Motion path is required.", nameof(motionPath));
        }

        return Path.GetFullPath(motionPath);
    }

    private void UpdateMotionMetadataFromInitialConfig()
    {
        MotionPath = _initialMotionLayers.Count == 0 ? null : _initialMotionLayers[0].MotionPath;
    }

    private void UpdateMotionMetadataFromRuntimeLayers()
    {
        if (_motionLayers.Count == 0)
        {
            MotionPath = null;
            _animationTime = 0.0f;
            return;
        }

        MotionPath = _motionLayers[0].MotionPath;
        _animationTime = _motionLayers[0].TimeSeconds;
    }

    private void SyncInitialMotionLayersFromRuntime()
    {
        _initialMotionLayers.Clear();
        for (int i = 0; i < _motionLayers.Count; i++)
        {
            MotionLayerState layer = _motionLayers[i];
            _initialMotionLayers.Add(new MotionLayerConfig(layer.MotionPath, layer.Weight, layer.ResetPhysicsOnLoop));
        }
    }

    private bool AdvanceMotionLayerTime(MotionLayerState layer, float playbackElapsed)
    {
        if (!layer.IsPlaying)
        {
            return false;
        }

        layer.TimeSeconds = MathF.Max(0.0f, layer.TimeSeconds + (playbackElapsed * PlaybackSpeed));

        if (layer.Animation.MaxKeyTime <= 0)
        {
            return false;
        }

        float durationSeconds = layer.Animation.MaxKeyTime / 30.0f;
        if (durationSeconds <= 0.0f)
        {
            return false;
        }

        if (LoopMotion)
        {
            if (layer.TimeSeconds >= durationSeconds)
            {
                layer.TimeSeconds %= durationSeconds;
                layer.Animation.ResetPlaybackCursor();
                return layer.ResetPhysicsOnLoop;
            }
        }
        else if (layer.TimeSeconds > durationSeconds)
        {
            layer.TimeSeconds = durationSeconds;
        }

        return false;
    }

    private static float GetMotionDurationSeconds(MotionLayerState layer)
    {
        return layer.Animation.MaxKeyTime <= 0 ? 0.0f : layer.Animation.MaxKeyTime / 30.0f;
    }

    private void ApplyResetPhysicsOnLoopToAllMotionLayers(bool resetPhysicsOnLoop)
    {
        for (int i = 0; i < _initialMotionLayers.Count; i++)
        {
            MotionLayerConfig layer = _initialMotionLayers[i];
            _initialMotionLayers[i] = new MotionLayerConfig(layer.MotionPath, layer.Weight, resetPhysicsOnLoop);
        }

        for (int i = 0; i < _motionLayers.Count; i++)
        {
            _motionLayers[i].ResetPhysicsOnLoop = resetPhysicsOnLoop;
        }
    }

    private static bool AreAllMotionLayersResetPhysicsOnLoopEnabled(IReadOnlyList<MotionLayerConfig> motionLayers)
    {
        for (int i = 0; i < motionLayers.Count; i++)
        {
            if (!motionLayers[i].ResetPhysicsOnLoop)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreAllMotionLayersResetPhysicsOnLoopEnabled(IReadOnlyList<MotionLayerState> motionLayers)
    {
        for (int i = 0; i < motionLayers.Count; i++)
        {
            if (!motionLayers[i].ResetPhysicsOnLoop)
            {
                return false;
            }
        }

        return true;
    }

    private static Vector3 SanitizeVertex(Vector3 value)
    {
        return new Vector3(
            float.IsFinite(value.X) ? value.X : 0.0f,
            float.IsFinite(value.Y) ? value.Y : 0.0f,
            float.IsFinite(value.Z) ? value.Z : 0.0f);
    }

    private void ComputeBounds(Zhengyan.DigitalWife.Mmd.MMDModel model)
    {
        int vertexCount = model.GetVertexCount();
        if (vertexCount <= 0)
        {
            _boundsMin = Vector3.Zero;
            _boundsMax = Vector3.Zero;
            return;
        }

        Vector3* positions = model.GetUpdatePositions();
        Vector3 min = SanitizeVertex(positions[0]);
        Vector3 max = min;

        for (int i = 1; i < vertexCount; i++)
        {
            Vector3 position = SanitizeVertex(positions[i]);
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        _boundsMin = min;
        _boundsMax = max;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Model path is required.", nameof(path));
        }

        string normalized = Path.GetFullPath(path);
        if (!File.Exists(normalized))
        {
            throw new FileNotFoundException($"PMX file not found: {normalized}", normalized);
        }

        return normalized;
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalized = Path.GetFullPath(path);
        if (!File.Exists(normalized))
        {
            throw new FileNotFoundException($"VMD file not found: {normalized}", normalized);
        }

        return normalized;
    }
}

