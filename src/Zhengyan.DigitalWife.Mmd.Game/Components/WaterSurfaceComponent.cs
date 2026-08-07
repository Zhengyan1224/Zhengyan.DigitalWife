using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Components;

public sealed unsafe class WaterSurfaceComponent : DrawableGameComponent
{
    private const int MaxRipples = 48;
    private const int MaxGerstnerWaves = 4;
    private const int DefaultMeshResolution = 96;
    private static readonly string[] DefaultNormalMapFileNames =
    [
        "Ocean0_N.dds",
        "Ocean1_N.dds",
        "Ocean2_N.dds",
        "Ocean3_N.dds"
    ];
    private const string DefaultSkyTextureFileName = "Sky.dds";

    private OrbitCamera _camera;
    private readonly string[] _normalMapPaths;
    private readonly string _skyTexturePath;
    private readonly float _surfaceSize;
    private readonly int _meshResolution;
    private Texture2D[] _normalMaps = [];
    private Texture2D? _skyTexture;
    private ITexture2D[] _backendNormalMaps = [];
    private ITexture2D? _backendSkyTexture;
    private VeldridWaterRenderer? _vulkanRenderer;

    private uint _program;
    private uint _vao;
    private uint _vertexBuffer;
    private uint _indexBuffer;
    private WaterVertex[] _vertices = [];
    private int _indexCount;
    private bool _uploadedGerstnerEnabled;
    private float _elapsedSeconds;
    private float _alpha = 0.55f;
    private float _animationSpeed = 0.03f;
    private float _skyReflectionStrength = 0.85f;
    private int _gerstnerWaveCount = MaxGerstnerWaves;
    private float _gerstnerAmplitude = 0.18f;
    private float _gerstnerWavelength = 8.0f;
    private float _gerstnerSpeed = 1.1f;
    private float _gerstnerSteepness = 0.45f;

    private int _uniformWorld = -1;
    private int _uniformView = -1;
    private int _uniformProjection = -1;
    private int _uniformEyePos = -1;
    private int _uniformTime = -1;
    private int _uniformTextureLerp = -1;
    private int _uniformAlpha = -1;
    private int _uniformDeepColor = -1;
    private int _uniformReflectionTint = -1;
    private int _uniformNormalTiling = -1;
    private int _uniformNormalTex = -1;
    private int _uniformNormalTex2 = -1;
    private int _uniformSkyTex = -1;
    private int _uniformPlanarReflectionTex = -1;
    private int _uniformSkyReflectionStrength = -1;
    private int _uniformMirrorReflectionEnabled = -1;
    private int _uniformPlanarReflectionEnabled = -1;
    private int _uniformReflectionViewProjection = -1;
    private int _uniformRippleCenters = -1;
    private int _uniformRippleTimes = -1;
    private int _uniformRippleRadii = -1;
    private int _uniformRippleStrengths = -1;
    private int _uniformRippleLifetime = -1;
    private int _uniformRippleWaveSpeed = -1;
    private int _uniformRippleFrequency = -1;
    private int _uniformRippleNormalStrength = -1;
    private uint _planarReflectionTextureId;
    private RuntimeTextureHandle? _planarReflectionTextureHandle;
    private Matrix4x4 _planarReflectionViewProjection = Matrix4x4.Identity;
    private readonly RippleState[] _ripples = new RippleState[MaxRipples];

    public WaterSurfaceComponent(
        OrbitCamera camera,
        float surfaceSize = 1000.0f,
        IReadOnlyList<string>? normalMapPaths = null,
        string? skyTexturePath = null,
        int meshResolution = DefaultMeshResolution)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        if (surfaceSize <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceSize), "Surface size must be greater than zero.");
        }

        _surfaceSize = surfaceSize;
        _meshResolution = Math.Clamp(meshResolution, 8, 256);
        _normalMapPaths = ResolveNormalMapPaths(normalMapPaths);
        _skyTexturePath = ResolveSkyTexturePath(skyTexturePath);
        DrawOrder = 100;
    }

    public Vector3 Position { get; set; } = Vector3.Zero;

    public OrbitCamera Camera
    {
        get => _camera;
        set => _camera = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    public Vector3 Scale { get; set; } = Vector3.One;

    public Matrix4x4 World => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    public float SurfaceSize => _surfaceSize;

    public int MeshResolution => _meshResolution;

    public IReadOnlyList<string> NormalMapPaths => _normalMapPaths;

    public string SkyTexturePath => _skyTexturePath;

    public float Alpha
    {
        get => _alpha;
        set => _alpha = Math.Clamp(value, 0.0f, 1.0f);
    }

    public float AnimationSpeed
    {
        get => _animationSpeed;
        set => _animationSpeed = Math.Max(0.0f, value);
    }

    public float NormalTiling { get; set; } = 100.0f;

    public Vector3 DeepColor { get; set; } = new(0.02f, 0.10f, 0.22f);

    public Vector3 ReflectionTint { get; set; } = new(0.56f, 0.70f, 0.90f);

    public float SkyReflectionStrength
    {
        get => _skyReflectionStrength;
        set => _skyReflectionStrength = Math.Clamp(value, 0.0f, 1.0f);
    }

    public bool MirrorReflectionEnabled { get; set; } = true;

    public bool GerstnerWavesEnabled { get; set; } = true;

    public int GerstnerWaveCount
    {
        get => _gerstnerWaveCount;
        set => _gerstnerWaveCount = Math.Clamp(value, 1, MaxGerstnerWaves);
    }

    public float GerstnerAmplitude
    {
        get => _gerstnerAmplitude;
        set => _gerstnerAmplitude = Math.Max(0.0f, value);
    }

    public float GerstnerWavelength
    {
        get => _gerstnerWavelength;
        set => _gerstnerWavelength = Math.Max(0.1f, value);
    }

    public float GerstnerSpeed
    {
        get => _gerstnerSpeed;
        set => _gerstnerSpeed = Math.Max(0.0f, value);
    }

    public float GerstnerSteepness
    {
        get => _gerstnerSteepness;
        set => _gerstnerSteepness = Math.Clamp(value, 0.0f, 1.0f);
    }

    public float GerstnerDirectionDegrees { get; set; } = 35.0f;

    public float RippleLifetimeSeconds { get; set; } = 1.8f;

    public float RippleWaveSpeed { get; set; } = 18.0f;

    public float RippleFrequency { get; set; } = 24.0f;

    public float RippleNormalStrength { get; set; } = 0.30f;

    public bool HasPlanarReflection => _planarReflectionTextureHandle is not null || _planarReflectionTextureId != 0;

    public void SetPlanarReflection(uint textureId, Matrix4x4 reflectionViewProjection, int width, int height)
    {
        _ = width;
        _ = height;
        _planarReflectionTextureId = textureId;
        _planarReflectionTextureHandle = textureId == 0 ? null : new RuntimeTextureHandle(GraphicsBackend.OpenGL, textureId);
        _planarReflectionViewProjection = reflectionViewProjection;
    }

    public void SetPlanarReflection(RuntimeTextureHandle texture, Matrix4x4 reflectionViewProjection, int width, int height)
    {
        _ = width;
        _ = height;
        _planarReflectionTextureHandle = texture;
        _planarReflectionTextureId = texture.Backend == GraphicsBackend.OpenGL ? texture.LegacyTextureId : 0;
        _planarReflectionViewProjection = reflectionViewProjection;
    }

    public void ClearPlanarReflection()
    {
        _planarReflectionTextureId = 0;
        _planarReflectionTextureHandle = null;
        _planarReflectionViewProjection = Matrix4x4.Identity;
    }

    public void AddRipple(Vector3 worldPosition, float radius = 0.8f, float strength = 0.8f, float mergeDistance = -1.0f)
    {
        float effectiveMergeDistance = mergeDistance < 0.0f
            ? MathF.Max(radius * 1.25f, 0.45f)
            : mergeDistance;

        if (effectiveMergeDistance > 0.0001f)
        {
            int nearestIndex = -1;
            float nearestDistanceSquared = float.MaxValue;
            for (int i = 0; i < _ripples.Length; i++)
            {
                if (!_ripples[i].Active)
                {
                    continue;
                }

                float distanceSquared = Vector3.DistanceSquared(_ripples[i].Center, worldPosition);
                if (distanceSquared <= effectiveMergeDistance * effectiveMergeDistance && distanceSquared < nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                    nearestIndex = i;
                }
            }

            if (nearestIndex >= 0)
            {
                ref RippleState ripple = ref _ripples[nearestIndex];
                ripple.Center = Vector3.Lerp(ripple.Center, worldPosition, 0.5f);
                ripple.Age = 0.0f;
                ripple.Radius = MathF.Max(ripple.Radius, MathF.Max(0.001f, radius));
                ripple.Strength = Math.Clamp(MathF.Max(ripple.Strength * 0.8f, strength), 0.0f, 4.0f);
                return;
            }
        }

        int targetIndex = 0;
        float oldestAge = float.MinValue;
        for (int i = 0; i < _ripples.Length; i++)
        {
            if (!_ripples[i].Active)
            {
                targetIndex = i;
                oldestAge = float.MaxValue;
                break;
            }

            if (_ripples[i].Age > oldestAge)
            {
                oldestAge = _ripples[i].Age;
                targetIndex = i;
            }
        }

        _ripples[targetIndex] = new RippleState
        {
            Active = true,
            Center = worldPosition,
            Age = 0.0f,
            Radius = MathF.Max(0.001f, radius),
            Strength = Math.Clamp(strength, 0.0f, 4.0f)
        };
    }

    public bool TryGetSurfaceHeight(Vector3 worldPosition, out float surfaceHeight)
    {
        surfaceHeight = Position.Y;
        if (!Matrix4x4.Invert(World, out Matrix4x4 inverseWorld))
        {
            return false;
        }

        Vector3 localPosition = Vector3.Transform(worldPosition, inverseWorld);
        if (MathF.Abs(localPosition.X) > _surfaceSize || MathF.Abs(localPosition.Z) > _surfaceSize)
        {
            return false;
        }

        Vector3 displacement = GerstnerWavesEnabled
            ? EvaluateGerstnerDisplacement(localPosition.X, localPosition.Z, _elapsedSeconds)
            : Vector3.Zero;
        Vector3 localSurfacePosition = new(localPosition.X + displacement.X, displacement.Y, localPosition.Z + displacement.Z);
        Vector3 worldSurfacePosition = Vector3.Transform(localSurfacePosition, World);
        surfaceHeight = worldSurfacePosition.Y;
        return true;
    }

    public bool TryGetSurfaceDepth(Vector3 worldPosition, out float surfaceDepth)
    {
        surfaceDepth = 0.0f;
        if (!TryGetSurfaceHeight(worldPosition, out float surfaceHeight))
        {
            return false;
        }

        surfaceDepth = surfaceHeight - worldPosition.Y;
        return surfaceDepth > 0.0f;
    }

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        if (Game.GraphicsDevice.Renderer is VulkanRenderer vulkan)
        {
            int resolution = Math.Clamp(_meshResolution, 1, 256);
            _vertices = new WaterVertex[(resolution + 1) * (resolution + 1)];
            uint[] vulkanIndices = CreateIndices(resolution);
            FillVertices(_vertices, GerstnerWavesEnabled, _elapsedSeconds);
            _indexCount = vulkanIndices.Length;
            _uploadedGerstnerEnabled = GerstnerWavesEnabled;
            _backendNormalMaps = LoadBackendTextures(Game.GraphicsDevice, _normalMapPaths);
            _backendSkyTexture = Game.GraphicsDevice.CreateTexture2D();
            _backendSkyTexture.LoadFromFile(_skyTexturePath);
            _vulkanRenderer = new VeldridWaterRenderer(
                vulkan,
                checked((uint)(_vertices.Length * sizeof(WaterVertex))),
                vulkanIndices);
            return;
        }

        GL gl = Game.GraphicsDevice.Gl;
        _program = gl.CreateShaderProgramFromSource(VertexShaderSource, FragmentShaderSource);
        _vao = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();
        _indexBuffer = gl.GenBuffer();

        int clampedResolution = Math.Clamp(_meshResolution, 1, 256);
        _vertices = new WaterVertex[(clampedResolution + 1) * (clampedResolution + 1)];
        uint[] indices = CreateIndices(clampedResolution);
        FillVertices(_vertices, GerstnerWavesEnabled, _elapsedSeconds);
        _indexCount = indices.Length;
        _uploadedGerstnerEnabled = GerstnerWavesEnabled;

        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        fixed (WaterVertex* vertexPtr = _vertices)
        {
            gl.BufferData(GLEnum.ArrayBuffer, (uint)(_vertices.Length * sizeof(WaterVertex)), vertexPtr, GLEnum.DynamicDraw);
        }

        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)sizeof(WaterVertex), (void*)0);
        gl.EnableVertexAttribArray(0);

        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)sizeof(WaterVertex), (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);

        gl.VertexAttribPointer(2, 3, GLEnum.Float, false, (uint)sizeof(WaterVertex), (void*)(5 * sizeof(float)));
        gl.EnableVertexAttribArray(2);

        gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBuffer);
        fixed (uint* indexPtr = indices)
        {
            gl.BufferData(GLEnum.ElementArrayBuffer, (uint)(indices.Length * sizeof(uint)), indexPtr, GLEnum.StaticDraw);
        }

        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindVertexArray(0);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, 0);

        _uniformWorld = gl.GetUniformLocation(_program, "u_World");
        _uniformView = gl.GetUniformLocation(_program, "u_View");
        _uniformProjection = gl.GetUniformLocation(_program, "u_Projection");
        _uniformEyePos = gl.GetUniformLocation(_program, "u_EyePos");
        _uniformTime = gl.GetUniformLocation(_program, "u_Time");
        _uniformTextureLerp = gl.GetUniformLocation(_program, "u_TextureLerp");
        _uniformAlpha = gl.GetUniformLocation(_program, "u_Alpha");
        _uniformDeepColor = gl.GetUniformLocation(_program, "u_DeepColor");
        _uniformReflectionTint = gl.GetUniformLocation(_program, "u_ReflectionTint");
        _uniformNormalTiling = gl.GetUniformLocation(_program, "u_NormalTiling");
        _uniformNormalTex = gl.GetUniformLocation(_program, "u_NormalTex");
        _uniformNormalTex2 = gl.GetUniformLocation(_program, "u_NormalTex2");
        _uniformSkyTex = gl.GetUniformLocation(_program, "u_SkyTex");
        _uniformPlanarReflectionTex = gl.GetUniformLocation(_program, "u_PlanarReflectionTex");
        _uniformSkyReflectionStrength = gl.GetUniformLocation(_program, "u_SkyReflectionStrength");
        _uniformMirrorReflectionEnabled = gl.GetUniformLocation(_program, "u_MirrorReflectionEnabled");
        _uniformPlanarReflectionEnabled = gl.GetUniformLocation(_program, "u_PlanarReflectionEnabled");
        _uniformReflectionViewProjection = gl.GetUniformLocation(_program, "u_ReflectionViewProjection");
        _uniformRippleCenters = gl.GetUniformLocation(_program, "u_RippleCenters");
        _uniformRippleTimes = gl.GetUniformLocation(_program, "u_RippleTimes");
        _uniformRippleRadii = gl.GetUniformLocation(_program, "u_RippleRadii");
        _uniformRippleStrengths = gl.GetUniformLocation(_program, "u_RippleStrengths");
        _uniformRippleLifetime = gl.GetUniformLocation(_program, "u_RippleLifetime");
        _uniformRippleWaveSpeed = gl.GetUniformLocation(_program, "u_RippleWaveSpeed");
        _uniformRippleFrequency = gl.GetUniformLocation(_program, "u_RippleFrequency");
        _uniformRippleNormalStrength = gl.GetUniformLocation(_program, "u_RippleNormalStrength");

        _normalMaps = LoadNormalMaps(gl, _normalMapPaths);
        _skyTexture = new Texture2D(gl, GLEnum.Repeat);
        _skyTexture.LoadFromFile(_skyTexturePath);
    }

    public override void Update(GameTime gameTime)
    {
        _elapsedSeconds += (float)gameTime.ElapsedSeconds;
        float deltaSeconds = (float)gameTime.ElapsedSeconds;
        for (int i = 0; i < _ripples.Length; i++)
        {
            if (!_ripples[i].Active)
            {
                continue;
            }

            _ripples[i].Age += deltaSeconds;
            if (_ripples[i].Age > Math.Max(0.05f, RippleLifetimeSeconds) || _ripples[i].Strength <= 0.001f)
            {
                _ripples[i].Active = false;
            }
        }
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        if (Game is null)
        {
            return;
        }

        if (_vulkanRenderer is not null && _backendNormalMaps.Length > 0 && _backendSkyTexture is not null)
        {
            int frame = ((int)_elapsedSeconds) % _backendNormalMaps.Length;
            int next = (frame + 1) % _backendNormalMaps.Length;
            float fraction = _elapsedSeconds - MathF.Floor(_elapsedSeconds);
            float lerp = (((fraction * 2.0f) - 1.0f) * 0.5f) + 0.5f;
            if (GerstnerWavesEnabled || _uploadedGerstnerEnabled != GerstnerWavesEnabled)
            {
                FillVertices(_vertices, GerstnerWavesEnabled, _elapsedSeconds);
                _uploadedGerstnerEnabled = GerstnerWavesEnabled;
            }
            Span<Vector4> rippleData = stackalloc Vector4[MaxRipples * 2 + 1];
            for (int i = 0; i < MaxRipples; i++)
            {
                RippleState ripple = _ripples[i];
                rippleData[i * 2] = new Vector4(
                    ripple.Center.X, ripple.Center.Z, ripple.Active ? ripple.Age : 999.0f, ripple.Radius);
                rippleData[i * 2 + 1] = new Vector4(ripple.Active ? ripple.Strength : 0.0f, 0, 0, 0);
            }
            rippleData[^1] = new Vector4(
                Math.Max(RippleLifetimeSeconds, .05f), RippleWaveSpeed, RippleFrequency, RippleNormalStrength);
            _vulkanRenderer.Draw<WaterVertex>(
                new ReadOnlySpan<WaterVertex>(_vertices), (uint)_indexCount,
                _backendNormalMaps[next], _backendNormalMaps[frame], _backendSkyTexture,
                _planarReflectionTextureHandle, rippleData, World, _camera.View, _camera.Projection,
                _planarReflectionViewProjection, _camera.Position, DeepColor, ReflectionTint,
                _elapsedSeconds * _animationSpeed, lerp, _alpha, NormalTiling,
                _skyReflectionStrength, MirrorReflectionEnabled);
            return;
        }

        if (_normalMaps.Length == 0 || _skyTexture is null) return;

        GL gl = Game.GraphicsDevice.Gl;
        int frameIndex = ((int)_elapsedSeconds) % _normalMaps.Length;
        int nextFrameIndex = (frameIndex + 1) % _normalMaps.Length;
        float frameLerpTime = _elapsedSeconds - MathF.Floor(_elapsedSeconds);
        float textureLerp = (((frameLerpTime * 2.0f) - 1.0f) * 0.5f) + 0.5f;

        UploadAnimatedVertices(gl);

        gl.Enable(GLEnum.DepthTest);
        gl.DepthFunc((GLEnum)0x0203); // GL_LEQUAL: let water survive near-coplanar floor/stair depth.
        gl.Enable(GLEnum.PolygonOffsetFill);
        gl.PolygonOffset(-1.0f, -1.0f);
        gl.Enable(GLEnum.Blend);
        gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
        gl.DepthMask(false);
        gl.Disable(GLEnum.CullFace);

        gl.UseProgram(_program);
        gl.BindVertexArray(_vao);

        gl.SetUniform(_uniformWorld, World);
        gl.SetUniform(_uniformView, _camera.View);
        gl.SetUniform(_uniformProjection, _camera.Projection);
        gl.SetUniform(_uniformEyePos, _camera.Position);
        gl.SetUniform(_uniformTime, _elapsedSeconds * _animationSpeed);
        gl.SetUniform(_uniformTextureLerp, textureLerp);
        gl.SetUniform(_uniformAlpha, _alpha);
        gl.SetUniform(_uniformDeepColor, DeepColor);
        gl.SetUniform(_uniformReflectionTint, ReflectionTint);
        gl.SetUniform(_uniformNormalTiling, NormalTiling);
        gl.SetUniform(_uniformNormalTex, 0);
        gl.SetUniform(_uniformNormalTex2, 1);
        gl.SetUniform(_uniformSkyTex, 2);
        gl.SetUniform(_uniformPlanarReflectionTex, 3);
        gl.SetUniform(_uniformSkyReflectionStrength, _skyReflectionStrength);
        gl.Uniform1(_uniformMirrorReflectionEnabled, MirrorReflectionEnabled ? 1.0f : 0.0f);
        gl.Uniform1(_uniformPlanarReflectionEnabled, MirrorReflectionEnabled && _planarReflectionTextureId != 0 ? 1.0f : 0.0f);
        gl.SetUniform(_uniformReflectionViewProjection, _planarReflectionViewProjection);
        gl.SetUniform(_uniformRippleLifetime, Math.Max(0.05f, RippleLifetimeSeconds));
        gl.SetUniform(_uniformRippleWaveSpeed, RippleWaveSpeed);
        gl.SetUniform(_uniformRippleFrequency, RippleFrequency);
        gl.SetUniform(_uniformRippleNormalStrength, RippleNormalStrength);
        UploadRipples(gl);

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(GLEnum.Texture2D, _normalMaps[nextFrameIndex].Id);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(GLEnum.Texture2D, _normalMaps[frameIndex].Id);
        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(GLEnum.Texture2D, _skyTexture.Id);
        gl.ActiveTexture(TextureUnit.Texture3);
        gl.BindTexture(GLEnum.Texture2D, _planarReflectionTextureId != 0 ? _planarReflectionTextureId : _skyTexture.Id);

        gl.DrawElements(GLEnum.Triangles, (uint)_indexCount, GLEnum.UnsignedInt, (void*)0);

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

        gl.DepthMask(true);
        gl.Disable(GLEnum.PolygonOffsetFill);
        gl.DepthFunc((GLEnum)0x0201); // GL_LESS
        gl.Disable(GLEnum.Blend);
    }

    public override void Dispose()
    {
        foreach (Texture2D normalMap in _normalMaps)
        {
            normalMap.Dispose();
        }

        _normalMaps = [];
        _skyTexture?.Dispose();
        _skyTexture = null;
        foreach (ITexture2D texture in _backendNormalMaps) texture.Dispose();
        _backendNormalMaps = [];
        _backendSkyTexture?.Dispose();
        _backendSkyTexture = null;
        _vulkanRenderer?.Dispose();
        _vulkanRenderer = null;

        if (Game is not null && Game.GraphicsDevice.Renderer is OpenGlRenderer)
        {
            GL gl = Game.GraphicsDevice.Gl;
            gl.DeleteBuffer(_vertexBuffer);
            gl.DeleteBuffer(_indexBuffer);
            gl.DeleteVertexArray(_vao);
            gl.DeleteProgram(_program);
        }

        base.Dispose();
    }

    private void UploadAnimatedVertices(GL gl)
    {
        if (_vertices.Length == 0)
        {
            return;
        }

        bool needsUpload = GerstnerWavesEnabled || _uploadedGerstnerEnabled != GerstnerWavesEnabled;
        if (!needsUpload)
        {
            return;
        }

        FillVertices(_vertices, GerstnerWavesEnabled, _elapsedSeconds);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        fixed (WaterVertex* vertexPtr = _vertices)
        {
            gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(_vertices.Length * sizeof(WaterVertex)), vertexPtr);
        }
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _uploadedGerstnerEnabled = GerstnerWavesEnabled;
    }

    private void FillVertices(WaterVertex[] vertices, bool gerstnerEnabled, float timeSeconds)
    {
        int clampedResolution = Math.Clamp(_meshResolution, 1, 256);
        int index = 0;
        float step = (_surfaceSize * 2.0f) / clampedResolution;

        for (int z = 0; z <= clampedResolution; z++)
        {
            float z0 = -_surfaceSize + (step * z);
            float v0 = (float)z / clampedResolution;

            for (int x = 0; x <= clampedResolution; x++)
            {
                float x0 = -_surfaceSize + (step * x);
                float u0 = (float)x / clampedResolution;

                vertices[index++] = CreateVertex(x0, z0, u0, v0, gerstnerEnabled, timeSeconds);
            }
        }
    }

    private static uint[] CreateIndices(int resolution)
    {
        int clampedResolution = Math.Clamp(resolution, 1, 256);
        int rowStride = clampedResolution + 1;
        uint[] indices = new uint[clampedResolution * clampedResolution * 6];
        int index = 0;

        for (int z = 0; z < clampedResolution; z++)
        {
            for (int x = 0; x < clampedResolution; x++)
            {
                uint topLeft = (uint)((z * rowStride) + x);
                uint topRight = topLeft + 1;
                uint bottomLeft = (uint)(((z + 1) * rowStride) + x);
                uint bottomRight = bottomLeft + 1;

                indices[index++] = topLeft;
                indices[index++] = topRight;
                indices[index++] = bottomLeft;
                indices[index++] = bottomLeft;
                indices[index++] = topRight;
                indices[index++] = bottomRight;
            }
        }

        return indices;
    }

    private WaterVertex CreateVertex(float x, float z, float u, float v, bool gerstnerEnabled, float timeSeconds)
    {
        Vector3 displacement = Vector3.Zero;
        Vector3 normal = Vector3.UnitY;
        if (gerstnerEnabled)
        {
            EvaluateGerstner(x, z, timeSeconds, out displacement, out normal);
        }

        return new WaterVertex(new Vector3(x, 0.0f, z) + displacement, new Vector2(u, v), normal);
    }

    private Vector2 GetGerstnerDirection()
    {
        float radians = GerstnerDirectionDegrees * (MathF.PI / 180.0f);
        Vector2 direction = new(MathF.Cos(radians), MathF.Sin(radians));
        return direction.LengthSquared() > 0.0001f ? Vector2.Normalize(direction) : Vector2.UnitX;
    }

    private Vector3 EvaluateGerstnerDisplacement(float x, float z, float timeSeconds)
    {
        EvaluateGerstner(x, z, timeSeconds, out Vector3 displacement, out _);
        return displacement;
    }

    private void EvaluateGerstner(float x, float z, float timeSeconds, out Vector3 displacement, out Vector3 normal)
    {
        Vector2 baseDirection = GetGerstnerDirection();
        displacement = Vector3.Zero;
        Vector2 gradient = Vector2.Zero;
        int waveCount = Math.Clamp(_gerstnerWaveCount, 1, MaxGerstnerWaves);

        for (int i = 0; i < MaxGerstnerWaves; i++)
        {
            if (i >= waveCount)
            {
                break;
            }

            GetGerstnerWaveParameters(i, baseDirection, out Vector2 direction, out float amplitude, out float wavelength, out float speed, out float steepness);
            float waveNumber = (2.0f * MathF.PI) / Math.Max(wavelength, 0.1f);
            float phase = waveNumber * ((direction.X * x) + (direction.Y * z) - (speed * timeSeconds));
            float sin = MathF.Sin(phase);
            float cos = MathF.Cos(phase);

            displacement.X += direction.X * steepness * amplitude * cos;
            displacement.Y += amplitude * sin;
            displacement.Z += direction.Y * steepness * amplitude * cos;
            gradient += direction * amplitude * waveNumber * cos;
        }

        normal = new Vector3(-gradient.X, 1.0f, -gradient.Y);
        normal = normal.LengthSquared() > 0.0001f ? Vector3.Normalize(normal) : Vector3.UnitY;
    }

    private void GetGerstnerWaveParameters(
        int index,
        Vector2 baseDirection,
        out Vector2 direction,
        out float amplitude,
        out float wavelength,
        out float speed,
        out float steepness)
    {
        float angle = (index - 1.5f) * 0.75f;
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        direction = new Vector2(
            (baseDirection.X * cos) - (baseDirection.Y * sin),
            (baseDirection.X * sin) + (baseDirection.Y * cos));
        if (direction.LengthSquared() <= 0.0001f)
        {
            direction = Vector2.UnitX;
        }
        else
        {
            direction = Vector2.Normalize(direction);
        }

        float amplitudeScale = MathF.Pow(0.55f, index);
        amplitude = _gerstnerAmplitude * amplitudeScale;
        wavelength = _gerstnerWavelength / (1.0f + (index * 0.55f));
        speed = _gerstnerSpeed * (1.0f + (index * 0.18f));
        steepness = _gerstnerSteepness;
    }

    private static Texture2D[] LoadNormalMaps(GL gl, IReadOnlyList<string> normalMapPaths)
    {
        Texture2D[] textures = new Texture2D[normalMapPaths.Count];
        try
        {
            for (int i = 0; i < normalMapPaths.Count; i++)
            {
                Texture2D texture = new(gl, GLEnum.Repeat);
                texture.LoadFromFile(normalMapPaths[i]);
                textures[i] = texture;
            }

            return textures;
        }
        catch
        {
            foreach (Texture2D texture in textures)
            {
                texture?.Dispose();
            }

            throw;
        }
    }

    private static ITexture2D[] LoadBackendTextures(GraphicsDevice graphicsDevice, IReadOnlyList<string> paths)
    {
        ITexture2D[] textures = new ITexture2D[paths.Count];
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                textures[i] = graphicsDevice.CreateTexture2D();
                textures[i].LoadFromFile(paths[i]);
            }
            return textures;
        }
        catch
        {
            foreach (ITexture2D? texture in textures) texture?.Dispose();
            throw;
        }
    }

    private static string[] ResolveNormalMapPaths(IReadOnlyList<string>? normalMapPaths)
    {
        if (normalMapPaths is { Count: > 0 })
        {
            string[] resolvedCustomPaths = new string[normalMapPaths.Count];
            for (int i = 0; i < normalMapPaths.Count; i++)
            {
                string candidate = normalMapPaths[i];
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    throw new ArgumentException("Normal map path cannot be empty.", nameof(normalMapPaths));
                }

                string fullPath = Path.GetFullPath(candidate);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"Normal map not found: {fullPath}", fullPath);
                }

                resolvedCustomPaths[i] = fullPath;
            }

            return resolvedCustomPaths;
        }

        string[] resolvedDefaultPaths = new string[DefaultNormalMapFileNames.Length];
        for (int i = 0; i < DefaultNormalMapFileNames.Length; i++)
        {
            string fileName = DefaultNormalMapFileNames[i];
            string? resolved = TryResolveBundledResourcePath("Resources", "Water", fileName);
            if (resolved is null)
            {
                throw new FileNotFoundException($"Bundled water normal map was not found: {fileName}");
            }

            resolvedDefaultPaths[i] = resolved;
        }

        return resolvedDefaultPaths;
    }

    private static string ResolveSkyTexturePath(string? skyTexturePath)
    {
        if (!string.IsNullOrWhiteSpace(skyTexturePath))
        {
            string customPath = Path.GetFullPath(skyTexturePath);
            if (!File.Exists(customPath))
            {
                throw new FileNotFoundException($"Sky texture not found: {customPath}", customPath);
            }

            return customPath;
        }

        string? bundledPath = TryResolveBundledResourcePath("Resources", "Water", DefaultSkyTextureFileName);
        if (bundledPath is null)
        {
            throw new FileNotFoundException($"Bundled sky texture was not found: {DefaultSkyTextureFileName}");
        }

        return bundledPath;
    }

    private void UploadRipples(GL gl)
    {
        Span<float> centers = stackalloc float[MaxRipples * 3];
        Span<float> times = stackalloc float[MaxRipples];
        Span<float> radii = stackalloc float[MaxRipples];
        Span<float> strengths = stackalloc float[MaxRipples];

        for (int i = 0; i < MaxRipples; i++)
        {
            RippleState ripple = _ripples[i];
            centers[i * 3 + 0] = ripple.Center.X;
            centers[i * 3 + 1] = ripple.Center.Y;
            centers[i * 3 + 2] = ripple.Center.Z;
            times[i] = ripple.Active ? ripple.Age : 999.0f;
            radii[i] = ripple.Radius;
            strengths[i] = ripple.Active ? ripple.Strength : 0.0f;
        }

        fixed (float* centersPtr = centers)
        fixed (float* timesPtr = times)
        fixed (float* radiiPtr = radii)
        fixed (float* strengthsPtr = strengths)
        {
            gl.Uniform3(_uniformRippleCenters, MaxRipples, centersPtr);
            gl.Uniform1(_uniformRippleTimes, MaxRipples, timesPtr);
            gl.Uniform1(_uniformRippleRadii, MaxRipples, radiiPtr);
            gl.Uniform1(_uniformRippleStrengths, MaxRipples, strengthsPtr);
        }
    }

    private struct RippleState
    {
        public bool Active;
        public Vector3 Center;
        public float Age;
        public float Radius;
        public float Strength;
    }

    private static string? TryResolveBundledResourcePath(params string[] segments)
    {
        return BundledAssetPathResolver.TryResolveFile(segments);
    }

    private readonly struct WaterVertex(Vector3 position, Vector2 uv, Vector3 normal)
    {
        public readonly Vector3 Position = position;
        public readonly Vector2 Uv = uv;
        public readonly Vector3 Normal = normal;
    }

    private const string VertexShaderSource = """
#version 300 es

precision highp float;

layout (location = 0) in vec3 in_Pos;
layout (location = 1) in vec2 in_Uv;
layout (location = 2) in vec3 in_Normal;

uniform mat4 u_World;
uniform mat4 u_View;
uniform mat4 u_Projection;
uniform float u_NormalTiling;
uniform mat4 u_ReflectionViewProjection;

out vec2 vs_Uv;
out vec3 vs_WorldPos;
out vec3 vs_SurfaceNormal;
out vec4 vs_ReflectionClipPos;

void main()
{
    vec4 worldPos = u_World * vec4(in_Pos, 1.0);
    vs_WorldPos = worldPos.xyz;
    vs_SurfaceNormal = normalize(mat3(u_World) * in_Normal);
    vs_Uv = in_Uv * u_NormalTiling;
    vs_ReflectionClipPos = u_ReflectionViewProjection * worldPos;
    gl_Position = u_Projection * u_View * worldPos;
}
""";

    private const string FragmentShaderSource = """
#version 300 es

precision highp float;

#define MAX_RIPPLES 48

in vec2 vs_Uv;
in vec3 vs_WorldPos;
in vec3 vs_SurfaceNormal;
in vec4 vs_ReflectionClipPos;

uniform sampler2D u_NormalTex;
uniform sampler2D u_NormalTex2;
uniform sampler2D u_SkyTex;
uniform sampler2D u_PlanarReflectionTex;
uniform vec3 u_EyePos;
uniform float u_Time;
uniform float u_TextureLerp;
uniform float u_Alpha;
uniform vec3 u_DeepColor;
uniform vec3 u_ReflectionTint;
uniform float u_SkyReflectionStrength;
uniform float u_MirrorReflectionEnabled;
uniform float u_PlanarReflectionEnabled;
uniform vec3 u_RippleCenters[MAX_RIPPLES];
uniform float u_RippleTimes[MAX_RIPPLES];
uniform float u_RippleRadii[MAX_RIPPLES];
uniform float u_RippleStrengths[MAX_RIPPLES];
uniform float u_RippleLifetime;
uniform float u_RippleWaveSpeed;
uniform float u_RippleFrequency;
uniform float u_RippleNormalStrength;

out vec4 out_Color;

const float PI = 3.14159265359;

vec2 DirectionToEquirectUv(vec3 dir)
{
    dir = normalize(dir);
    float u = atan(dir.z, dir.x) / (2.0 * PI) + 0.5;
    float v = 0.5 - asin(clamp(dir.y, -1.0, 1.0)) / PI;
    return vec2(fract(u), clamp(v, 0.0, 1.0));
}

void main()
{
    vec3 normalTextureA = texture(u_NormalTex,  vs_Uv * 0.1 + vec2( u_Time,  u_Time)).xyz;
    vec3 normalTextureB = texture(u_NormalTex2, vs_Uv * 0.1 + vec2( u_Time,  u_Time)).xyz;
    vec3 normalTexture = mix(normalTextureB, normalTextureA, u_TextureLerp);

    vec3 normalTextureDetailA = texture(u_NormalTex,  vs_Uv * 2.0 + vec2(-u_Time, -u_Time * 2.0)).xyz;
    vec3 normalTextureDetailB = texture(u_NormalTex2, vs_Uv * 2.0 + vec2(-u_Time, -u_Time * 2.0)).xyz;
    vec3 normalTextureDetail = mix(normalTextureDetailB, normalTextureDetailA, u_TextureLerp);

    vec3 textureNormal = normalize((((0.5 * normalTexture) + (0.5 * normalTextureDetail)) * 2.0) - 1.0);
    textureNormal = textureNormal.xzy;
    vec3 normal = normalize(vs_SurfaceNormal + ((textureNormal - vec3(0.0, 1.0, 0.0)) * 0.68));

    float ripple = 0.0;
    float rippleHighlight = 0.0;
    for (int i = 0; i < MAX_RIPPLES; i++)
    {
        float rippleDistance = distance(vs_WorldPos.xz, u_RippleCenters[i].xz);
        float rippleWave = sin((rippleDistance * u_RippleFrequency) - (u_RippleTimes[i] * u_RippleWaveSpeed));
        float rippleEnvelope = exp(-(u_RippleTimes[i] / max(u_RippleLifetime, 0.001)) * 2.4) * exp(-pow(rippleDistance / max(u_RippleRadii[i], 0.001), 2.0));
        ripple += rippleWave * rippleEnvelope * u_RippleStrengths[i];
        rippleHighlight += max(rippleWave, 0.0) * rippleEnvelope * u_RippleStrengths[i];
    }
    normal = normalize(normal + vec3(
        cos(vs_WorldPos.x * 8.0 + u_Time) * ripple * u_RippleNormalStrength,
        abs(ripple) * (u_RippleNormalStrength * 1.15),
        sin(vs_WorldPos.z * 8.0 + u_Time) * ripple * u_RippleNormalStrength));

    vec3 incident = normalize(vs_WorldPos - u_EyePos);
    vec3 reflected = normalize(reflect(incident, normal));
    float horizon = clamp(reflected.y * 0.5 + 0.5, 0.0, 1.0);

    float fresnel = pow(1.0 - max(dot(normalize(u_EyePos - vs_WorldPos), normal), 0.0), 5.0);
    vec3 gradientReflection = mix(u_DeepColor * 0.72, u_ReflectionTint, horizon);
    vec3 skyColor = texture(u_SkyTex, DirectionToEquirectUv(reflected)).rgb;
    float mirrorEnabled = clamp(u_MirrorReflectionEnabled, 0.0, 1.0);
    vec3 reflection = mix(gradientReflection, skyColor, clamp(u_SkyReflectionStrength, 0.0, 1.0) * mirrorEnabled);
    vec2 reflectionUv = (vs_ReflectionClipPos.xy / max(abs(vs_ReflectionClipPos.w), 0.0001)) * 0.5 + 0.5;
    reflectionUv += normal.xz * 0.035;
    float reflectionInside = step(0.0, reflectionUv.x) * step(reflectionUv.x, 1.0) * step(0.0, reflectionUv.y) * step(reflectionUv.y, 1.0);
    vec3 planarReflection = texture(u_PlanarReflectionTex, clamp(reflectionUv, 0.001, 0.999)).rgb;
    float planarEnabled = clamp(u_PlanarReflectionEnabled, 0.0, 1.0) * reflectionInside * 0.65;
    reflection = mix(reflection, planarReflection, planarEnabled);
    float reflectionWeight = mix(0.18, clamp(0.35 + fresnel * 0.65, 0.0, 1.0), mirrorEnabled);
    vec3 color = mix(u_DeepColor, reflection, reflectionWeight);
    vec3 waterTint = mix(u_DeepColor, u_ReflectionTint, 0.30);
    color = mix(color, waterTint, 0.42);
    color += vec3(0.22, 0.25, 0.28) * clamp(rippleHighlight, 0.0, 1.0);
    color = clamp(color, 0.0, 1.0);

    out_Color = vec4(color, u_Alpha);
}
""";
}

