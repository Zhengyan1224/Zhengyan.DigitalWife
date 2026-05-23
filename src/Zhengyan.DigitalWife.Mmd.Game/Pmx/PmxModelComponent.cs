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
        : this(motionPath, weight, timeSeconds, durationFrames, true)
    {
    }

    public MotionLayerInfo(string motionPath, float weight, float timeSeconds, int durationFrames, bool resetPhysicsOnLoop)
    {
        MotionPath = motionPath;
        Weight = weight;
        TimeSeconds = timeSeconds;
        DurationFrames = durationFrames;
        ResetPhysicsOnLoop = resetPhysicsOnLoop;
    }

    public string MotionPath { get; }

    public float Weight { get; }

    public float TimeSeconds { get; }

    public int DurationFrames { get; }

    public bool ResetPhysicsOnLoop { get; }
}

public unsafe class PmxModelComponent : DrawableGameComponent
{
    private const float MotionWeightEpsilon = 0.0001f;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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

        public void Dispose()
        {
            Animation.Dispose();
        }
    }

    private readonly Dictionary<Zhengyan.DigitalWife.Mmd.MMDMaterial, MaterialTextures> _materials = [];
    private readonly Dictionary<(string Path, GLEnum WrapMode), Texture2D> _textures = [];
    private readonly Dictionary<int, string> _materialTextureOverrides = [];
    private readonly TransformUpdaterManager _transformUpdaters = new();
    private readonly List<MotionLayerConfig> _initialMotionLayers = [];

    private string _initialModelPath;

    private PmxShader? _mmdShader;
    private PmxEdgeShader? _edgeShader;
    private PmxGroundShadowShader? _groundShadowShader;
    private EmbeddedToonTextureLibrary? _toonTextures;
    private Texture2D? _defaultTexture;

    private Zhengyan.DigitalWife.Mmd.MMDModel? _model;
    private readonly List<MotionLayerState> _motionLayers = [];
    private Zhengyan.DigitalWife.Mmd.MMDMesh[] _meshes = [];
    private bool _hasUvMorphs;
    private bool _vertexBuffersDirty = true;
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
    private uint _modelVao;
    private uint _edgeVao;
    private uint _groundShadowVao;
    private float _animationTime;
    private bool _isPlaying = true;
    private bool _skipPhysicsOnNextPlayFrame;
    private bool _defaultResetPhysicsOnMotionLoop = true;
    private Vector3[]? _resetPositions;
    private Vector3[]? _resetNormals;
    private Vector2[]? _resetUVs;
    private Vector3[] _blendNodeTranslations = [];
    private Vector4[] _blendNodeRotations = [];
    private bool[] _blendNodeRotationValid = [];
    private float[] _blendMorphWeights = [];
    private float[] _blendIkEnabledWeights = [];
    private float[] _blendIkTotalWeights = [];

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

    public Zhengyan.DigitalWife.Mmd.VmdAnimation? Animation => _motionLayers.Count == 0 ? null : _motionLayers[0].Animation;

    public int MotionLayerCount => _motionLayers.Count;

    public int MeshCount => _meshes.Length;

    public int MaterialCount => _materials.Count;

    public IReadOnlyList<string> MaterialNames => _model?.GetMaterials()
        .Select((material, index) => string.IsNullOrWhiteSpace(material.Name) ? $"Material {index}" : material.Name)
        .ToArray() ?? [];

    public IRuntimeTextureProvider? RuntimeTextureProvider { get; set; }

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

            _vertexBuffersDirty = true;
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

    public bool EnablePhysical { get; set; } = true;

    public bool EnableEdge { get; set; } = true;

    public bool EnableShadow { get; set; } = true;

    public bool DrawShadowInMainPass { get; set; } = true;

    public Matrix4x4 World => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        GL gl = Game.GraphicsDevice.Gl;
        _mmdShader = new PmxShader(gl);
        _edgeShader = new PmxEdgeShader(gl);
        _groundShadowShader = new PmxGroundShadowShader(gl);
        _toonTextures = new EmbeddedToonTextureLibrary(gl);
        _defaultTexture = new Texture2D(gl);
        _defaultTexture.Fill(255, 255, 255, 255);

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
                layer.ResetPhysicsOnLoop);
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
            _vertexBuffersDirty = true;
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
            _vertexBuffersDirty = true;
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
            _vertexBuffersDirty = true;
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
            _vertexBuffersDirty = true;
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
        _vertexBuffersDirty = true;
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
            _vertexBuffersDirty = true;
            return true;
        }

        int existingRuntimeIndex = FindMotionLayerStateIndex(_motionLayers, normalizedMotionPath);
        if (existingRuntimeIndex < 0)
        {
            return false;
        }

        _motionLayers[existingRuntimeIndex].Weight = clampedWeight;
        SyncInitialMotionLayersFromRuntime();
        _vertexBuffersDirty = true;
        return true;
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
            _vertexBuffersDirty = true;
            return true;
        }

        int existingRuntimeIndex = FindMotionLayerStateIndex(_motionLayers, normalizedMotionPath);
        if (existingRuntimeIndex < 0)
        {
            return false;
        }

        _motionLayers[existingRuntimeIndex].ResetPhysicsOnLoop = resetPhysicsOnLoop;
        SyncInitialMotionLayersFromRuntime();
        _vertexBuffersDirty = true;
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

    public void ClearMotion()
    {
        SetMotionLayersCore([]);
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
            _vertexBuffersDirty = true;
            return;
        }

        LoadResources(Game.GraphicsDevice.Gl, normalizedModelPath, normalizedMotionLayers);
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
            _vertexBuffersDirty = true;
            return;
        }

        ApplyMotionResources(Game.GraphicsDevice.Gl, normalizedMotionLayers);
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
        IReadOnlyDictionary<string, string>? vowelMorphMap = null)
    {
        SpeechTransformUpdater updater = new(kanaDictionary, vowelDictionary, vowelMorphMap);
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
            LoadResources(Game.GraphicsDevice.Gl, ModelPath, currentMotionLayers);
            IsPlaying = false;
            _animationTime = 0.0f;
            _skipPhysicsOnNextPlayFrame = true;
            return;
        }

        if (_model is not null)
        {
            _model.LoadBaseAnimation();
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
            UploadVertexBuffers(Game.GraphicsDevice.Gl, true);
            _vertexBuffersDirty = false;
            return;
        }

        _vertexBuffersDirty = true;
    }

    public void ResetPhysics()
    {
        if (!_loaded || _model is null)
        {
            return;
        }

        _model.ResetPhysics();
        _skipPhysicsOnNextPlayFrame = true;
        _vertexBuffersDirty = true;
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

        bool shouldSimulatePhysics = IsPlaying && EnablePhysical && !_skipPhysicsOnNextPlayFrame && _motionLayers.Count != 0;
        bool shouldUpdatePose = _vertexBuffersDirty || IsPlaying || shouldSimulatePhysics || _transformUpdaters.HasEnabledUpdaters;
        if (!shouldUpdatePose)
        {
            return;
        }

        _model.BeginAnimation();
        EvaluateMotionLayers(_model, _motionLayers);

        _transformUpdaters.UpdateStage(TransformUpdaterStage.PreAnimation, this, updaterElapsed);
        _model.UpdateMorphAnimation();
        _model.UpdateNodeAnimation(false);
        if (resetPhysicsOnLoopThisFrame && EnablePhysical)
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

        _model.Update();
        UploadVertexBuffers(Game!.GraphicsDevice.Gl, _vertexBuffersDirty || _hasUvMorphs);
        _vertexBuffersDirty = false;
        _skipPhysicsOnNextPlayFrame = false;
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        if (!_loaded || Game is null || Camera is null || _model is null || _mmdShader is null || _edgeShader is null || _groundShadowShader is null || _defaultTexture is null)
        {
            return;
        }

        GL gl = Game.GraphicsDevice.Gl;
        Vector2 screenSize = new(Game.GraphicsDevice.BackBufferSize.X, Game.GraphicsDevice.BackBufferSize.Y);
        _lastOpaqueMeshDrawCount = 0;
        _lastEdgeMeshDrawCount = 0;
        _lastShadowMeshDrawCount = 0;

        Matrix4x4 transform = World;
        Matrix4x4 worldView = transform * Camera.View;
        Matrix4x4 worldViewProjection = worldView * Camera.Projection;

        gl.Enable(GLEnum.DepthTest);
        gl.Enable(GLEnum.Blend);
        gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);

        gl.UseProgram(_mmdShader.Id);
        gl.BindVertexArray(_modelVao);
        gl.SetUniform(_mmdShader.UniWVP, worldViewProjection);
        gl.SetUniform(_mmdShader.UniWV, worldView);
        gl.SetUniform(_mmdShader.UniTex, 0);
        gl.SetUniform(_mmdShader.UniSphereTex, 1);
        gl.SetUniform(_mmdShader.UniToonTex, 2);
        gl.SetUniform(_mmdShader.UniLightColor, LightColor);
        gl.SetUniform(_mmdShader.UniLightDir, LightDirection);
        gl.SetUniform(_mmdShader.UniAmbientLightColor, AmbientLightColor);
        gl.SetUniform(_mmdShader.UniAmbientLightStrength, AmbientLightStrength);
        gl.SetUniform(_mmdShader.UniShadowMapEnabled, 0);
        gl.SetUniform(_mmdShader.UniShadowMap0, 3);
        gl.SetUniform(_mmdShader.UniShadowMap1, 4);
        gl.SetUniform(_mmdShader.UniShadowMap2, 5);
        gl.SetUniform(_mmdShader.UniShadowMap3, 6);

        gl.DepthMask(true);
        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in _meshes)
        {
            Zhengyan.DigitalWife.Mmd.MMDMaterial mmdMaterial = mesh.Material;
            if (!_materials.TryGetValue(mmdMaterial, out MaterialTextures? materialTextures) || mmdMaterial.Alpha == 0.0f)
            {
                continue;
            }

            DrawMesh(gl, mesh, mmdMaterial, materialTextures, GetMaterialIndex(mmdMaterial));

            _lastOpaqueMeshDrawCount++;
        }

        gl.BindVertexArray(0);
        gl.UseProgram(0);

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

        if (DrawShadowInMainPass)
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

    private void DrawMesh(GL gl, Zhengyan.DigitalWife.Mmd.MMDMesh mesh, Zhengyan.DigitalWife.Mmd.MMDMaterial mmdMaterial, MaterialTextures materialTextures, int materialIndex)
    {
        if (_mmdShader is null || _defaultTexture is null)
        {
            return;
        }

        gl.SetUniform(_mmdShader.UniAmbient, mmdMaterial.Ambient);
        gl.SetUniform(_mmdShader.UniDiffuse, mmdMaterial.Diffuse);
        gl.SetUniform(_mmdShader.UniSpecular, mmdMaterial.Specular);
        gl.SetUniform(_mmdShader.UniSpecularPower, mmdMaterial.SpecularPower);
        gl.SetUniform(_mmdShader.UniAlpha, mmdMaterial.Alpha);

        gl.ActiveTexture(TextureUnit.Texture0);
        uint overrideTextureId = ResolveMaterialOverrideTextureId(materialIndex);
        if (overrideTextureId != 0)
        {
            gl.SetUniform(_mmdShader.UniTexMode, 1);
            gl.SetUniform(_mmdShader.UniTexMulFactor, mmdMaterial.TextureMulFactor);
            gl.SetUniform(_mmdShader.UniTexAddFactor, mmdMaterial.TextureAddFactor);
            gl.BindTexture(GLEnum.Texture2D, overrideTextureId);
        }
        else if (materialTextures.Texture is not null)
        {
            int texMode = materialTextures.Texture.AlphaMode switch
            {
                TextureAlphaMode.Blend => 2,
                TextureAlphaMode.ColorMask => 3,
                TextureAlphaMode.BlendMaskColor => 4,
                _ => 1
            };
            gl.SetUniform(_mmdShader.UniTexMode, texMode);
            gl.SetUniform(_mmdShader.UniTexMulFactor, mmdMaterial.TextureMulFactor);
            gl.SetUniform(_mmdShader.UniTexAddFactor, mmdMaterial.TextureAddFactor);
            gl.BindTexture(GLEnum.Texture2D, materialTextures.Texture.Id);
        }
        else
        {
            gl.SetUniform(_mmdShader.UniTexMode, 0);
            gl.BindTexture(GLEnum.Texture2D, _defaultTexture.Id);
        }

        gl.ActiveTexture(TextureUnit.Texture1);
        if (materialTextures.SphereTexture is not null)
        {
            gl.SetUniform(_mmdShader.UniSphereTexMode, mmdMaterial.SpTextureMode switch
            {
                Zhengyan.DigitalWife.Mmd.SphereTextureMode.Mul => 1,
                Zhengyan.DigitalWife.Mmd.SphereTextureMode.Add => 2,
                _ => 0
            });
            gl.SetUniform(_mmdShader.UniSphereTexMulFactor, mmdMaterial.SpTextureMulFactor);
            gl.SetUniform(_mmdShader.UniSphereTexAddFactor, mmdMaterial.SpTextureAddFactor);
            gl.BindTexture(GLEnum.Texture2D, materialTextures.SphereTexture.Id);
        }
        else
        {
            gl.SetUniform(_mmdShader.UniSphereTexMode, 0);
            gl.BindTexture(GLEnum.Texture2D, _defaultTexture.Id);
        }

        gl.ActiveTexture(TextureUnit.Texture2);
        if (materialTextures.ToonTexture is not null)
        {
            gl.SetUniform(_mmdShader.UniToonTexMode, 1);
            gl.SetUniform(_mmdShader.UniToonTexMulFactor, mmdMaterial.ToonTextureMulFactor);
            gl.SetUniform(_mmdShader.UniToonTexAddFactor, mmdMaterial.ToonTextureAddFactor);
            gl.BindTexture(GLEnum.Texture2D, materialTextures.ToonTexture.Id);
        }
        else
        {
            gl.SetUniform(_mmdShader.UniToonTexMode, 0);
            gl.BindTexture(GLEnum.Texture2D, _defaultTexture.Id);
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

        return GetTexture(Game.GraphicsDevice.Gl, textureReference, GLEnum.Repeat).Id;
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
            DisposeModelResources(Game.GraphicsDevice.Gl);
        }

        DisposeSharedResources();
        base.Dispose();
    }

    private void LoadResources(GL gl, string pmxPath, IReadOnlyList<MotionLayerConfig> motionLayers)
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

    private void ApplyMotionResources(GL gl, IReadOnlyList<MotionLayerConfig> motionLayers)
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
        bool previousVertexBuffersDirty = _vertexBuffersDirty;

        try
        {
            nextMotionLayers = CreateMotionLayers(_model, motionLayers);
            bool hasMotion = nextMotionLayers.Count != 0;
            SetIkSolversEnabled(_model, hasMotion);

            bool vertexBuffersDirty;
            if (RestoreResetSnapshot(gl))
            {
                if (!hasMotion)
                {
                    _model.LoadBaseAnimation();
                    RebuildPose(_model, nextMotionLayers);
                }

                vertexBuffersDirty = false;
            }
            else
            {
                _model.LoadBaseAnimation();
                RebuildPose(_model, nextMotionLayers);
                _model.Update();
                vertexBuffersDirty = true;
            }

            _motionLayers.Clear();
            _motionLayers.AddRange(nextMotionLayers);
            nextMotionLayers.Clear();
            DisposeMotionLayers(previousMotionLayers);

            IsPlaying = hasMotion;
            _animationTime = 0.0f;
            _skipPhysicsOnNextPlayFrame = true;
            _vertexBuffersDirty = vertexBuffersDirty;
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
            _vertexBuffersDirty = previousVertexBuffersDirty;
            throw;
        }
    }

    private void DisposeSharedResources()
    {
        _defaultTexture?.Dispose();
        _defaultTexture = null;

        _toonTextures?.Dispose();
        _toonTextures = null;

        _mmdShader?.Dispose();
        _mmdShader = null;

        _edgeShader?.Dispose();
        _edgeShader = null;

        _groundShadowShader?.Dispose();
        _groundShadowShader = null;
    }

    private void DisposeModelResources(GL gl)
    {
        foreach (Texture2D texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
        _materials.Clear();
        _meshes = [];

        gl.DeleteBuffer(_positionBuffer);
        gl.DeleteBuffer(_normalBuffer);
        gl.DeleteBuffer(_uvBuffer);
        gl.DeleteBuffer(_indexBuffer);
        gl.DeleteVertexArray(_modelVao);
        gl.DeleteVertexArray(_edgeVao);
        gl.DeleteVertexArray(_groundShadowVao);

        _positionBuffer = 0;
        _normalBuffer = 0;
        _uvBuffer = 0;
        _indexBuffer = 0;
        _modelVao = 0;
        _edgeVao = 0;
        _groundShadowVao = 0;

        DisposeMotionLayers(_motionLayers);
        _motionLayers.Clear();

        _model?.Dispose();
        _model = null;

        _resetPositions = null;
        _resetNormals = null;
        _resetUVs = null;

        _loaded = false;
        _hasUvMorphs = false;
        _vertexBuffersDirty = true;
        _animationTime = 0.0f;
        _lastOpaqueMeshDrawCount = 0;
        _lastEdgeMeshDrawCount = 0;
        _lastShadowMeshDrawCount = 0;
        _boundsMin = Vector3.Zero;
        _boundsMax = Vector3.Zero;
    }

    private void LoadModel(string pmxPath, IReadOnlyList<MotionLayerConfig> motionLayers)
    {
        Zhengyan.DigitalWife.Mmd.PmxModel model = new();
        List<MotionLayerState> layers = [];

        try
        {
            string modelDirectory = Path.GetDirectoryName(pmxPath) ?? string.Empty;
            model.Load(pmxPath, modelDirectory);
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
            _vertexBuffersDirty = true;
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

    private void RebuildPose(Zhengyan.DigitalWife.Mmd.MMDModel model, IReadOnlyList<MotionLayerState> motionLayers)
    {
        model.BeginAnimation();
        EvaluateMotionLayers(model, motionLayers);
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
            EvaluateMotionLayers(model, motionLayers, warmupWeight, forceFrame: 0.0f);
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

    private void Setup(GL gl)
    {
        if (_model is null || _mmdShader is null || _edgeShader is null || _groundShadowShader is null)
        {
            return;
        }

        _meshes = _model.GetMeshes();

        int vertexCount = _model.GetVertexCount();
        _positionBuffer = gl.GenBuffer();
        gl.BindBuffer(GLEnum.ArrayBuffer, _positionBuffer);
        gl.BufferData(GLEnum.ArrayBuffer, (uint)(sizeof(Vector3) * vertexCount), null, GLEnum.DynamicDraw);

        _normalBuffer = gl.GenBuffer();
        gl.BindBuffer(GLEnum.ArrayBuffer, _normalBuffer);
        gl.BufferData(GLEnum.ArrayBuffer, (uint)(sizeof(Vector3) * vertexCount), null, GLEnum.DynamicDraw);

        _uvBuffer = gl.GenBuffer();
        gl.BindBuffer(GLEnum.ArrayBuffer, _uvBuffer);
        gl.BufferData(GLEnum.ArrayBuffer, (uint)(sizeof(Vector2) * vertexCount), null, GLEnum.DynamicDraw);
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);

        int indexCount = _model.GetIndexCount();
        _indexBuffer = gl.GenBuffer();
        gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBuffer);
        gl.BufferData(GLEnum.ElementArrayBuffer, (uint)(sizeof(uint) * indexCount), _model.GetIndices(), GLEnum.StaticDraw);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, 0);

        _modelVao = gl.GenVertexArray();
        gl.BindVertexArray(_modelVao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _positionBuffer);
        gl.VertexAttribPointer(_mmdShader.InPos, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
        gl.EnableVertexAttribArray(_mmdShader.InPos);
        gl.BindBuffer(GLEnum.ArrayBuffer, _normalBuffer);
        gl.VertexAttribPointer(_mmdShader.InNor, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
        gl.EnableVertexAttribArray(_mmdShader.InNor);
        gl.BindBuffer(GLEnum.ArrayBuffer, _uvBuffer);
        gl.VertexAttribPointer(_mmdShader.InUV, 2, GLEnum.Float, false, (uint)sizeof(Vector2), (void*)0);
        gl.EnableVertexAttribArray(_mmdShader.InUV);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBuffer);
        gl.BindVertexArray(0);

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

        foreach (Zhengyan.DigitalWife.Mmd.MMDMaterial mmdMaterial in _model.GetMaterials())
        {
            MaterialTextures textures = new();

            if (!string.IsNullOrEmpty(mmdMaterial.Texture))
            {
                textures.Texture = GetTexture(gl, mmdMaterial.Texture, GLEnum.Repeat);
            }

            if (!string.IsNullOrEmpty(mmdMaterial.SpTexture))
            {
                textures.SphereTexture = GetTexture(gl, mmdMaterial.SpTexture, GLEnum.Repeat);
            }

            if (!string.IsNullOrEmpty(mmdMaterial.ToonTexture))
            {
                textures.ToonTexture = GetTexture(gl, mmdMaterial.ToonTexture, GLEnum.ClampToEdge);
            }

            _materials.Add(mmdMaterial, textures);
        }

        UploadVertexBuffers(gl, true);
        CaptureResetSnapshot();
        _vertexBuffersDirty = false;
    }

    private Texture2D GetTexture(GL gl, string texturePath, GLEnum wrapMode)
    {
        if (!File.Exists(texturePath) && _toonTextures is not null && _toonTextures.TryGetTexture(texturePath, out Texture2D toonTexture))
        {
            return toonTexture;
        }

        (string Path, GLEnum WrapMode) cacheKey = (texturePath, wrapMode);
        if (!_textures.TryGetValue(cacheKey, out Texture2D? texture))
        {
            texture = new Texture2D(gl, wrapMode);
            texture.LoadFromFile(texturePath);
            _textures.Add(cacheKey, texture);
        }

        return texture;
    }

    private void UploadVertexBuffers(GL gl, bool uploadUv)
    {
        if (_model is null)
        {
            return;
        }

        int vertexCount = _model.GetVertexCount();

        gl.BindBuffer(GLEnum.ArrayBuffer, _positionBuffer);
        gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(sizeof(Vector3) * vertexCount), _model.GetUpdatePositions());
        gl.BindBuffer(GLEnum.ArrayBuffer, _normalBuffer);
        gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(sizeof(Vector3) * vertexCount), _model.GetUpdateNormals());

        if (uploadUv)
        {
            gl.BindBuffer(GLEnum.ArrayBuffer, _uvBuffer);
            gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(sizeof(Vector2) * vertexCount), _model.GetUpdateUVs());
        }

        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
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

    private bool RestoreResetSnapshot(GL gl)
    {
        if (_resetPositions is null || _resetNormals is null || _resetUVs is null)
        {
            return false;
        }

        fixed (Vector3* positions = _resetPositions)
        {
            gl.BindBuffer(GLEnum.ArrayBuffer, _positionBuffer);
            gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(sizeof(Vector3) * _resetPositions.Length), positions);
        }

        fixed (Vector3* normals = _resetNormals)
        {
            gl.BindBuffer(GLEnum.ArrayBuffer, _normalBuffer);
            gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(sizeof(Vector3) * _resetNormals.Length), normals);
        }

        fixed (Vector2* uvs = _resetUVs)
        {
            gl.BindBuffer(GLEnum.ArrayBuffer, _uvBuffer);
            gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(sizeof(Vector2) * _resetUVs.Length), uvs);
        }

        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
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

