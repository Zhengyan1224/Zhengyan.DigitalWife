using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGLES;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Mmd.Game.Components;

public sealed unsafe class TexturedPlaneComponent : DrawableGameComponent
{
    private OrbitCamera _camera;
    private string _texturePath;
    private Texture2D? _texture;
    private ITexture2D? _backendTexture;
    private IGpuBuffer? _backendVertexBuffer;
    private VeldridTexturedPlanePassRenderer? _vulkanPassRenderer;
    private IRuntimeTextureProvider? _runtimeTextureProvider;
    private uint _program;
    private uint _vao;
    private uint _customVao;
    private uint _vertexBuffer;
    private int _uniformWorld = -1;
    private int _uniformView = -1;
    private int _uniformProjection = -1;
    private int _uniformTexture = -1;
    private int _uniformTint = -1;
    private int _uniformFlipV = -1;
    private int _uniformShadowMap = -1;
    private int _uniformLightViewProjection = -1;
    private int _uniformShadowMapEnabled = -1;
    private int _uniformShadowMapStrength = -1;
    private int _uniformShadowMapBias = -1;
    private int _uniformPlanarReflectionTex = -1;
    private int _uniformPlanarReflectionEnabled = -1;
    private int _uniformReflectionViewProjection = -1;
    private int _uniformMirrorReflectionStrength = -1;
    private uint _planarReflectionTextureId;
    private string? _vulkanCustomVertexShaderPath;
    private string? _vulkanCustomFragmentShaderPath;
    private RuntimeTextureHandle? _planarReflectionTextureHandle;
    private Matrix4x4 _planarReflectionViewProjection = Matrix4x4.Identity;
    private CustomShaderProgram? _customShader;
    private readonly Dictionary<string, CustomShaderUniformValue> _customShaderUniforms = new(StringComparer.Ordinal);

    public TexturedPlaneComponent(OrbitCamera camera, string texturePath)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _texturePath = texturePath;
        DrawOrder = 115;
    }

    public Vector3 Position { get; set; } = Vector3.Zero;

    public OrbitCamera Camera
    {
        get => _camera;
        set => _camera = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    public Vector3 Scale { get; set; } = Vector3.One;

    public float Width { get; set; } = 2.0f;

    public float Height { get; set; } = 2.0f;

    public bool Billboard { get; set; }

    public Vector4 Tint { get; set; } = Vector4.One;

    public float Opacity
    {
        get => Tint.W;
        set => Tint = new Vector4(Tint.X, Tint.Y, Tint.Z, Math.Clamp(value, 0.0f, 1.0f));
    }

    public string TexturePath
    {
        get => _texturePath;
        set
        {
            if (string.Equals(_texturePath, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _texturePath = value;
            if (Game is not null)
            {
                if (Game.GraphicsDevice.Renderer is OpenGlRenderer openGl)
                {
                    ReloadTexture(openGl.Gl);
                }
                else
                {
                    ReloadBackendTexture();
                }
            }
        }
    }

    public IRuntimeTextureProvider? RuntimeTextureProvider
    {
        get => _runtimeTextureProvider;
        set => _runtimeTextureProvider = value;
    }

    public bool ReceiveShadow { get; set; } = true;

    public ShadowMapBinding? ShadowMap { get; set; }

    public bool MirrorReflectionEnabled { get; set; }

    public float MirrorReflectionStrength { get; set; } = 1.0f;

    public void SetPlanarReflection(uint textureId, Matrix4x4 reflectionViewProjection, int width, int height)
    {
        _ = width;
        _ = height;
        _planarReflectionTextureId = textureId;
        _planarReflectionTextureHandle = textureId == 0
            ? null
            : new RuntimeTextureHandle(GraphicsBackend.OpenGL, textureId);
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

    public bool HasCustomShader => _customShader is not null || _vulkanCustomVertexShaderPath is not null;

    public void SetCustomShader(string vertexShaderPath, string fragmentShaderPath)
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        if (Game.GraphicsDevice.Renderer is not OpenGlRenderer openGl)
        {
            PortableShaderContract.ValidatePlane(vertexShaderPath, fragmentShaderPath);
            throw new NotSupportedException("The portable GLSL contract was validated, but custom plane pipelines are not enabled on this backend yet.");
        }

        GL gl = openGl.Gl;
        CustomShaderProgram nextShader = new(gl, vertexShaderPath, fragmentShaderPath);
        _customShader?.Dispose();
        _customShader = nextShader;
        RebuildCustomShaderVao(gl);
    }

    public void SetCustomShader(
        string openGlVertexShaderPath,
        string openGlFragmentShaderPath,
        string vulkanVertexSpirvPath,
        string vulkanFragmentSpirvPath)
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        if (Game.GraphicsDevice.Backend == GraphicsBackend.OpenGL)
        {
            SetCustomShader(openGlVertexShaderPath, openGlFragmentShaderPath);
            return;
        }

        if (_vulkanPassRenderer is null)
        {
            throw new NotSupportedException($"Custom plane shaders are not supported on {Game.GraphicsDevice.Backend}.");
        }

        string vertexPath = Path.GetFullPath(vulkanVertexSpirvPath);
        string fragmentPath = Path.GetFullPath(vulkanFragmentSpirvPath);
        _vulkanPassRenderer.SetCustomShaders(vertexPath, fragmentPath);
        _vulkanCustomVertexShaderPath = vertexPath;
        _vulkanCustomFragmentShaderPath = fragmentPath;
    }

    /// <summary>Validates a custom shader pair against the explicit cross-backend contract.</summary>
    public static void ValidatePortableShaderContract(string vertexShaderPath, string fragmentShaderPath)
        => PortableShaderContract.ValidatePlane(vertexShaderPath, fragmentShaderPath);

    public void ClearCustomShader()
    {
        if (Game is not null && Game.GraphicsDevice.Renderer is OpenGlRenderer openGl)
        {
            DeleteCustomShaderVao(openGl.Gl);
        }

        _customShader?.Dispose();
        _customShader = null;
        if (_vulkanPassRenderer is not null && _vulkanCustomVertexShaderPath is not null)
        {
            _vulkanPassRenderer.ClearCustomShaders();
        }

        _vulkanCustomVertexShaderPath = null;
        _vulkanCustomFragmentShaderPath = null;
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

    public bool TryGetMirrorPlane(out Vector3 normal, out float distance)
    {
        Matrix4x4 world = World;
        Vector3 p0 = Vector3.Transform(new Vector3(-0.5f, -0.5f, 0.0f), world);
        Vector3 p1 = Vector3.Transform(new Vector3(0.5f, -0.5f, 0.0f), world);
        Vector3 p2 = Vector3.Transform(new Vector3(-0.5f, 0.5f, 0.0f), world);
        normal = Vector3.Cross(p1 - p0, p2 - p0);
        if (normal.LengthSquared() <= 0.000001f)
        {
            normal = Vector3.UnitZ;
            distance = 0.0f;
            return false;
        }

        normal = Vector3.Normalize(normal);
        distance = -Vector3.Dot(normal, p0);
        return true;
    }

    public Matrix4x4 World
    {
        get
        {
            Quaternion rotation = Billboard
                ? Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateBillboard(Position, _camera.Position, _camera.Up, _camera.Front))
                : Rotation;
            return Matrix4x4.CreateScale(Width * Scale.X, Height * Scale.Y, Scale.Z)
                * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(Position);
        }
    }

    public IReadOnlyList<Vector3> GetWorldCorners()
    {
        Matrix4x4 world = World;
        return
        [
            Vector3.Transform(new Vector3(-0.5f, -0.5f, 0.0f), world),
            Vector3.Transform(new Vector3(0.5f, -0.5f, 0.0f), world),
            Vector3.Transform(new Vector3(-0.5f, 0.5f, 0.0f), world),
            Vector3.Transform(new Vector3(0.5f, 0.5f, 0.0f), world)
        ];
    }

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        if (Game.GraphicsDevice.Renderer is VulkanRenderer vulkan)
        {
            InitializeVulkan(vulkan);
            return;
        }

        GL gl = Game.GraphicsDevice.Gl;
        _program = gl.CreateShaderProgramFromSource(VertexShaderSource, FragmentShaderSource);
        _vao = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();

        PlaneVertex[] vertices =
        [
            new(new Vector3(-0.5f, -0.5f, 0.0f), new Vector2(0.0f, 1.0f)),
            new(new Vector3( 0.5f, -0.5f, 0.0f), new Vector2(1.0f, 1.0f)),
            new(new Vector3(-0.5f,  0.5f, 0.0f), new Vector2(0.0f, 0.0f)),
            new(new Vector3(-0.5f,  0.5f, 0.0f), new Vector2(0.0f, 0.0f)),
            new(new Vector3( 0.5f, -0.5f, 0.0f), new Vector2(1.0f, 1.0f)),
            new(new Vector3( 0.5f,  0.5f, 0.0f), new Vector2(1.0f, 0.0f))
        ];

        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        fixed (PlaneVertex* vertexPtr = vertices)
        {
            gl.BufferData(GLEnum.ArrayBuffer, (uint)(vertices.Length * sizeof(PlaneVertex)), vertexPtr, GLEnum.StaticDraw);
        }

        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)sizeof(PlaneVertex), (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)sizeof(PlaneVertex), (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindVertexArray(0);

        _uniformWorld = gl.GetUniformLocation(_program, "u_World");
        _uniformView = gl.GetUniformLocation(_program, "u_View");
        _uniformProjection = gl.GetUniformLocation(_program, "u_Projection");
        _uniformTexture = gl.GetUniformLocation(_program, "u_Texture");
        _uniformTint = gl.GetUniformLocation(_program, "u_Tint");
        _uniformFlipV = gl.GetUniformLocation(_program, "u_FlipV");
        _uniformShadowMap = gl.GetUniformLocation(_program, "u_ShadowMap");
        _uniformLightViewProjection = gl.GetUniformLocation(_program, "u_LightViewProjection");
        _uniformShadowMapEnabled = gl.GetUniformLocation(_program, "u_ShadowMapEnabled");
        _uniformShadowMapStrength = gl.GetUniformLocation(_program, "u_ShadowMapStrength");
        _uniformShadowMapBias = gl.GetUniformLocation(_program, "u_ShadowMapBias");
        _uniformPlanarReflectionTex = gl.GetUniformLocation(_program, "u_PlanarReflectionTex");
        _uniformPlanarReflectionEnabled = gl.GetUniformLocation(_program, "u_PlanarReflectionEnabled");
        _uniformReflectionViewProjection = gl.GetUniformLocation(_program, "u_ReflectionViewProjection");
        _uniformMirrorReflectionStrength = gl.GetUniformLocation(_program, "u_MirrorReflectionStrength");
        ReloadTexture(gl);

        if (_customShader is not null)
        {
            RebuildCustomShaderVao(gl);
        }
    }

    private void InitializeVulkan(VulkanRenderer renderer)
    {
        _backendTexture = Game!.GraphicsDevice.CreateTexture2D();
        ReloadBackendTexture();

        PlaneVertex[] vertices = CreateVertices();
        _backendVertexBuffer = Game.GraphicsDevice.CreateBuffer(new GpuBufferDescription(
            checked((uint)(vertices.Length * Marshal.SizeOf<PlaneVertex>())),
            GpuBufferKind.Vertex));
        _backendVertexBuffer.Update<PlaneVertex>(new ReadOnlySpan<PlaneVertex>(vertices));
        _vulkanPassRenderer = new VeldridTexturedPlanePassRenderer(renderer, _backendVertexBuffer, _backendTexture);
    }

    public override void Draw(GameTime gameTime)
    {
        if (Game is null)
        {
            return;
        }

        if (Game.GraphicsDevice.Renderer is VulkanRenderer
            && _vulkanPassRenderer is not null
            && _backendTexture is not null)
        {
            RuntimeTextureHandle? baseHandle = ResolveTextureHandle();
            RuntimeTextureHandle? reflectionHandle = MirrorReflectionEnabled ? _planarReflectionTextureHandle : null;
            _vulkanPassRenderer.Draw(
                _backendTexture,
                baseHandle,
                Tint,
                IsRuntimeTextureReference(_texturePath),
                World,
                _camera.View,
                _camera.Projection,
                ReceiveShadow,
                ShadowMap,
                reflectionHandle,
                _planarReflectionViewProjection,
                MirrorReflectionStrength);
            return;
        }

        if (_texture is null && !IsRuntimeTextureReference(_texturePath))
        {
            return;
        }

        GL gl = Game.GraphicsDevice.Gl;
        gl.Enable(GLEnum.DepthTest);
        gl.Enable(GLEnum.Blend);
        gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
        gl.Disable(GLEnum.CullFace);

        if (_customShader is not null)
        {
            DrawCustomShader(gl, gameTime);
            return;
        }

        gl.UseProgram(_program);
        gl.BindVertexArray(_vao);
        gl.SetUniform(_uniformWorld, World);
        gl.SetUniform(_uniformView, _camera.View);
        gl.SetUniform(_uniformProjection, _camera.Projection);
        gl.SetUniform(_uniformTexture, 0);
        gl.SetUniform(_uniformTint, Tint);
        gl.SetUniform(_uniformFlipV, IsRuntimeTextureReference(_texturePath) ? 1 : 0);
        gl.SetUniform(_uniformShadowMap, 1);
        gl.SetUniform(_uniformPlanarReflectionTex, 2);
        gl.Uniform1(_uniformPlanarReflectionEnabled, MirrorReflectionEnabled && _planarReflectionTextureId != 0 ? 1.0f : 0.0f);
        gl.SetUniform(_uniformReflectionViewProjection, _planarReflectionViewProjection);
        gl.SetUniform(_uniformMirrorReflectionStrength, Math.Clamp(MirrorReflectionStrength, 0.0f, 1.0f));
        ApplyShadowMapUniforms(gl);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(GLEnum.Texture2D, ResolveTextureId());
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(GLEnum.Texture2D, ShadowMap?.TextureId ?? 0);
        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(GLEnum.Texture2D, _planarReflectionTextureId != 0 ? _planarReflectionTextureId : ResolveTextureId());
        gl.DrawArrays(GLEnum.Triangles, 0, 6);

        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.Disable(GLEnum.Blend);
    }

    public override void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _vulkanPassRenderer?.Dispose();
        _vulkanPassRenderer = null;
        _backendVertexBuffer?.Dispose();
        _backendVertexBuffer = null;
        _backendTexture?.Dispose();
        _backendTexture = null;

        if (Game is not null && Game.GraphicsDevice.Renderer is OpenGlRenderer openGl)
        {
            GL gl = openGl.Gl;
            gl.DeleteBuffer(_vertexBuffer);
            gl.DeleteVertexArray(_vao);
            DeleteCustomShaderVao(gl);
            gl.DeleteProgram(_program);
        }

        _customShader?.Dispose();
        _customShader = null;
        base.Dispose();
    }

    private void ReloadTexture(GL gl)
    {
        _texture?.Dispose();
        _texture = new Texture2D(gl, GLEnum.ClampToEdge);
        if (IsRuntimeTextureReference(_texturePath))
        {
            _texture.Fill(255, 255, 255, 255);
        }
        else if (!string.IsNullOrWhiteSpace(_texturePath) && File.Exists(_texturePath))
        {
            _texture.LoadFromFile(_texturePath);
        }
        else
        {
            _texture.Fill(255, 255, 255, 255);
        }
    }

    private void ReloadBackendTexture()
    {
        if (_backendTexture is null)
        {
            return;
        }

        if (IsRuntimeTextureReference(_texturePath))
        {
            _backendTexture.Fill(255, 255, 255, 255);
        }
        else if (!string.IsNullOrWhiteSpace(_texturePath) && File.Exists(_texturePath))
        {
            _backendTexture.LoadFromFile(_texturePath);
        }
        else
        {
            _backendTexture.Fill(255, 255, 255, 255);
        }
    }

    private uint ResolveTextureId()
    {
        if (_runtimeTextureProvider is not null
            && _runtimeTextureProvider.TryGetTexture(_texturePath, out uint runtimeTextureId))
        {
            return runtimeTextureId;
        }

        return _texture?.Id ?? 0;
    }

    private RuntimeTextureHandle? ResolveTextureHandle()
    {
        if (_runtimeTextureProvider is not null
            && _runtimeTextureProvider.TryGetTextureHandle(_texturePath, out RuntimeTextureHandle runtimeTexture))
        {
            return runtimeTexture;
        }

        if (_backendTexture is not null)
        {
            return new RuntimeTextureHandle(_backendTexture.Backend, _backendTexture.LegacyTextureId, _backendTexture.NativeResource);
        }

        return null;
    }

    private void ApplyShadowMapUniforms(GL gl)
    {
        if (!ReceiveShadow || ShadowMap is not { TextureId: not 0 } shadowMap)
        {
            gl.SetUniform(_uniformShadowMapEnabled, 0);
            return;
        }

        gl.SetUniform(_uniformShadowMapEnabled, 1);
        gl.SetUniform(_uniformLightViewProjection, shadowMap.LightViewProjection);
        gl.SetUniform(_uniformShadowMapStrength, Math.Clamp(shadowMap.Strength, 0.0f, 1.0f));
        gl.SetUniform(_uniformShadowMapBias, Math.Max(0.0f, shadowMap.Bias));
    }

    private void DrawCustomShader(GL gl, GameTime gameTime)
    {
        CustomShaderProgram shader = _customShader!;
        Matrix4x4 world = World;
        Matrix4x4 worldView = world * _camera.View;
        Matrix4x4 worldViewProjection = worldView * _camera.Projection;
        uint baseTextureId = ResolveTextureId();

        gl.UseProgram(shader.Id);
        gl.BindVertexArray(_customVao != 0 ? _customVao : _vao);

        shader.SetUniform("u_World", world);
        shader.SetUniform("u_View", _camera.View);
        shader.SetUniform("u_Projection", _camera.Projection);
        shader.SetUniform("u_WV", worldView);
        shader.SetUniform("u_WVP", worldViewProjection);
        shader.SetUniform("u_Time", (float)gameTime.TotalSeconds);
        shader.SetUniform("u_DeltaTime", (float)gameTime.ElapsedSeconds);
        shader.SetUniform("u_FrameCount", (int)Math.Min(int.MaxValue, gameTime.FrameCount));
        shader.SetUniform("u_Texture", 0);
        shader.SetUniform("u_Tint", Tint);
        shader.SetUniform("u_Opacity", Tint.W);
        shader.SetUniform("u_FlipV", IsRuntimeTextureReference(_texturePath) ? 1 : 0);
        shader.SetUniform("u_ShadowMap", 1);
        shader.SetUniform("u_PlanarReflectionTex", 2);
        shader.SetUniform("u_PlanarReflectionEnabled", MirrorReflectionEnabled && _planarReflectionTextureId != 0 ? 1.0f : 0.0f);
        shader.SetUniform("u_ReflectionViewProjection", _planarReflectionViewProjection);
        shader.SetUniform("u_MirrorReflectionStrength", Math.Clamp(MirrorReflectionStrength, 0.0f, 1.0f));
        ApplyCustomShadowMapUniforms(shader);
        shader.ApplyUniforms(_customShaderUniforms);

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(GLEnum.Texture2D, baseTextureId);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(GLEnum.Texture2D, ShadowMap?.TextureId ?? 0);
        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(GLEnum.Texture2D, _planarReflectionTextureId != 0 ? _planarReflectionTextureId : baseTextureId);
        gl.DrawArrays(GLEnum.Triangles, 0, 6);

        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.Disable(GLEnum.Blend);
    }

    private void ApplyCustomShadowMapUniforms(CustomShaderProgram shader)
    {
        if (!ReceiveShadow || ShadowMap is not { TextureId: not 0 } shadowMap)
        {
            shader.SetUniform("u_ShadowMapEnabled", 0);
            return;
        }

        shader.SetUniform("u_ShadowMapEnabled", 1);
        shader.SetUniform("u_LightViewProjection", shadowMap.LightViewProjection);
        shader.SetUniform("u_ShadowMapStrength", Math.Clamp(shadowMap.Strength, 0.0f, 1.0f));
        shader.SetUniform("u_ShadowMapBias", Math.Max(0.0f, shadowMap.Bias));
    }

    private void RebuildCustomShaderVao(GL gl)
    {
        DeleteCustomShaderVao(gl);
        if (_customShader is null || _vertexBuffer == 0)
        {
            return;
        }

        _customVao = gl.GenVertexArray();
        gl.BindVertexArray(_customVao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        if (_customShader.InPos >= 0)
        {
            gl.VertexAttribPointer((uint)_customShader.InPos, 3, GLEnum.Float, false, (uint)sizeof(PlaneVertex), (void*)0);
            gl.EnableVertexAttribArray((uint)_customShader.InPos);
        }

        int uvAttribute = _customShader.InUv >= 0 ? _customShader.InUv : _customShader.InUV;
        if (uvAttribute >= 0)
        {
            gl.VertexAttribPointer((uint)uvAttribute, 2, GLEnum.Float, false, (uint)sizeof(PlaneVertex), (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray((uint)uvAttribute);
        }

        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindVertexArray(0);
    }

    private void DeleteCustomShaderVao(GL gl)
    {
        if (_customVao == 0)
        {
            return;
        }

        gl.DeleteVertexArray(_customVao);
        _customVao = 0;
    }

    private static string NormalizeUniformName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Uniform name cannot be empty.", nameof(name));
        }

        return name.Trim();
    }

    private static bool IsRuntimeTextureReference(string texturePath)
    {
        return texturePath.Trim().StartsWith("rt:", StringComparison.OrdinalIgnoreCase);
    }

    private readonly struct PlaneVertex(Vector3 position, Vector2 uv)
    {
        public readonly Vector3 Position = position;
        public readonly Vector2 Uv = uv;
    }

    private static PlaneVertex[] CreateVertices()
    {
        return
        [
            new(new Vector3(-0.5f, -0.5f, 0.0f), new Vector2(0.0f, 1.0f)),
            new(new Vector3( 0.5f, -0.5f, 0.0f), new Vector2(1.0f, 1.0f)),
            new(new Vector3(-0.5f,  0.5f, 0.0f), new Vector2(0.0f, 0.0f)),
            new(new Vector3(-0.5f,  0.5f, 0.0f), new Vector2(0.0f, 0.0f)),
            new(new Vector3( 0.5f, -0.5f, 0.0f), new Vector2(1.0f, 1.0f)),
            new(new Vector3( 0.5f,  0.5f, 0.0f), new Vector2(1.0f, 0.0f))
        ];
    }

    private const string VertexShaderSource = """
#version 300 es

layout (location = 0) in vec3 in_Pos;
layout (location = 1) in vec2 in_Uv;

uniform mat4 u_World;
uniform mat4 u_View;
uniform mat4 u_Projection;
uniform mat4 u_ReflectionViewProjection;

out vec2 vs_Uv;
out vec3 vs_WorldPos;
out vec4 vs_ReflectionClipPos;

void main()
{
    vec4 worldPos = u_World * vec4(in_Pos, 1.0);
    vs_Uv = in_Uv;
    vs_WorldPos = worldPos.xyz;
    vs_ReflectionClipPos = u_ReflectionViewProjection * worldPos;
    gl_Position = u_Projection * u_View * worldPos;
}
""";

    private const string FragmentShaderSource = """
#version 300 es

precision highp float;
precision highp sampler2DShadow;

in vec2 vs_Uv;
in vec3 vs_WorldPos;
in vec4 vs_ReflectionClipPos;

uniform sampler2D u_Texture;
uniform sampler2DShadow u_ShadowMap;
uniform sampler2D u_PlanarReflectionTex;
uniform vec4 u_Tint;
uniform int u_FlipV;
uniform mat4 u_LightViewProjection;
uniform int u_ShadowMapEnabled;
uniform float u_ShadowMapStrength;
uniform float u_ShadowMapBias;
uniform float u_PlanarReflectionEnabled;
uniform float u_MirrorReflectionStrength;

out vec4 out_Color;

float SampleShadow()
{
    if (u_ShadowMapEnabled == 0)
    {
        return 1.0;
    }

    vec4 clip = u_LightViewProjection * vec4(vs_WorldPos, 1.0);
    vec3 coord = clip.xyz / max(abs(clip.w), 0.0001);
    vec2 uv = coord.xy * 0.5 + 0.5;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || coord.z < -1.0 || coord.z > 1.0)
    {
        return 1.0;
    }

    float depth = (coord.z * 0.5 + 0.5) - u_ShadowMapBias;
    float visibility = texture(u_ShadowMap, vec3(uv, depth));
    return mix(1.0 - clamp(u_ShadowMapStrength, 0.0, 1.0), 1.0, visibility);
}

void main()
{
    vec2 uv = vs_Uv;
    if (u_FlipV != 0)
    {
        uv.y = 1.0 - uv.y;
    }

    vec4 color = texture(u_Texture, uv) * u_Tint;
    color.rgb *= SampleShadow();
    vec2 reflectionUv = (vs_ReflectionClipPos.xy / max(abs(vs_ReflectionClipPos.w), 0.0001)) * 0.5 + 0.5;
    float reflectionInside = step(0.0, reflectionUv.x) * step(reflectionUv.x, 1.0) * step(0.0, reflectionUv.y) * step(reflectionUv.y, 1.0);
    vec3 reflectionColor = texture(u_PlanarReflectionTex, clamp(reflectionUv, 0.001, 0.999)).rgb;
    float reflectionAmount = clamp(u_PlanarReflectionEnabled, 0.0, 1.0) * reflectionInside * clamp(u_MirrorReflectionStrength, 0.0, 1.0);
    color.rgb = mix(color.rgb, reflectionColor, reflectionAmount);
    if (color.a <= 0.001)
    {
        discard;
    }

    out_Color = color;
}
""";
}
