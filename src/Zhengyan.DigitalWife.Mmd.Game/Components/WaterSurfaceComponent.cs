using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Components;

public sealed unsafe class WaterSurfaceComponent : DrawableGameComponent
{
    private static readonly string[] DefaultNormalMapFileNames =
    [
        "Ocean0_N.dds",
        "Ocean1_N.dds",
        "Ocean2_N.dds",
        "Ocean3_N.dds"
    ];
    private const string DefaultSkyTextureFileName = "Sky.dds";

    private readonly OrbitCamera _camera;
    private readonly string[] _normalMapPaths;
    private readonly string _skyTexturePath;
    private readonly float _surfaceSize;
    private Texture2D[] _normalMaps = [];
    private Texture2D? _skyTexture;

    private uint _program;
    private uint _vao;
    private uint _vertexBuffer;
    private float _elapsedSeconds;
    private float _alpha = 0.55f;
    private float _animationSpeed = 0.03f;
    private float _skyReflectionStrength = 0.85f;

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
    private int _uniformSkyReflectionStrength = -1;

    public WaterSurfaceComponent(
        OrbitCamera camera,
        float surfaceSize = 1000.0f,
        IReadOnlyList<string>? normalMapPaths = null,
        string? skyTexturePath = null)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        if (surfaceSize <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceSize), "Surface size must be greater than zero.");
        }

        _surfaceSize = surfaceSize;
        _normalMapPaths = ResolveNormalMapPaths(normalMapPaths);
        _skyTexturePath = ResolveSkyTexturePath(skyTexturePath);
        DrawOrder = 100;
    }

    public Vector3 Position { get; set; } = Vector3.Zero;

    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    public Vector3 Scale { get; set; } = Vector3.One;

    public Matrix4x4 World => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    public float SurfaceSize => _surfaceSize;

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

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        GL gl = Game.GraphicsDevice.Gl;
        _program = gl.CreateShaderProgramFromSource(VertexShaderSource, FragmentShaderSource);
        _vao = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();

        WaterVertex[] vertices = CreateVertices(_surfaceSize);

        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        fixed (WaterVertex* vertexPtr = vertices)
        {
            gl.BufferData(GLEnum.ArrayBuffer, (uint)(vertices.Length * sizeof(WaterVertex)), vertexPtr, GLEnum.StaticDraw);
        }

        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)sizeof(WaterVertex), (void*)0);
        gl.EnableVertexAttribArray(0);

        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)sizeof(WaterVertex), (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);

        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindVertexArray(0);

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
        _uniformSkyReflectionStrength = gl.GetUniformLocation(_program, "u_SkyReflectionStrength");

        _normalMaps = LoadNormalMaps(gl, _normalMapPaths);
        _skyTexture = new Texture2D(gl, GLEnum.Repeat);
        _skyTexture.LoadFromFile(_skyTexturePath);
    }

    public override void Update(GameTime gameTime)
    {
        _elapsedSeconds += (float)gameTime.ElapsedSeconds;
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        if (Game is null || _normalMaps.Length == 0 || _skyTexture is null)
        {
            return;
        }

        GL gl = Game.GraphicsDevice.Gl;
        int frameIndex = ((int)_elapsedSeconds) % _normalMaps.Length;
        int nextFrameIndex = (frameIndex + 1) % _normalMaps.Length;
        float frameLerpTime = _elapsedSeconds - MathF.Floor(_elapsedSeconds);
        float textureLerp = (((frameLerpTime * 2.0f) - 1.0f) * 0.5f) + 0.5f;

        gl.Enable(GLEnum.DepthTest);
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
        gl.SetUniform(_uniformSkyReflectionStrength, _skyReflectionStrength);

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(GLEnum.Texture2D, _normalMaps[nextFrameIndex].Id);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(GLEnum.Texture2D, _normalMaps[frameIndex].Id);
        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(GLEnum.Texture2D, _skyTexture.Id);

        gl.DrawArrays(GLEnum.Triangles, 0, 6);

        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(GLEnum.Texture2D, 0);

        gl.BindVertexArray(0);
        gl.UseProgram(0);

        gl.DepthMask(true);
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

        if (Game is not null)
        {
            GL gl = Game.GraphicsDevice.Gl;
            gl.DeleteBuffer(_vertexBuffer);
            gl.DeleteVertexArray(_vao);
            gl.DeleteProgram(_program);
        }

        base.Dispose();
    }

    private static WaterVertex[] CreateVertices(float size)
    {
        return
        [
            new WaterVertex(new Vector3(-size, 0.0f, -size), new Vector2(0.0f, 0.0f)),
            new WaterVertex(new Vector3( size, 0.0f, -size), new Vector2(1.0f, 0.0f)),
            new WaterVertex(new Vector3(-size, 0.0f,  size), new Vector2(0.0f, 1.0f)),
            new WaterVertex(new Vector3(-size, 0.0f,  size), new Vector2(0.0f, 1.0f)),
            new WaterVertex(new Vector3( size, 0.0f, -size), new Vector2(1.0f, 0.0f)),
            new WaterVertex(new Vector3( size, 0.0f,  size), new Vector2(1.0f, 1.0f))
        ];
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

    private static string? TryResolveBundledResourcePath(params string[] segments)
    {
        return BundledAssetPathResolver.TryResolveFile(segments);
    }

    private readonly struct WaterVertex(Vector3 position, Vector2 uv)
    {
        public readonly Vector3 Position = position;
        public readonly Vector2 Uv = uv;
    }

    private const string VertexShaderSource = """
#version 300 es

layout (location = 0) in vec3 in_Pos;
layout (location = 1) in vec2 in_Uv;

uniform mat4 u_World;
uniform mat4 u_View;
uniform mat4 u_Projection;
uniform float u_NormalTiling;

out vec2 vs_Uv;
out vec3 vs_WorldPos;

void main()
{
    vec4 worldPos = u_World * vec4(in_Pos, 1.0);
    vs_WorldPos = worldPos.xyz;
    vs_Uv = in_Uv * u_NormalTiling;
    gl_Position = u_Projection * u_View * worldPos;
}
""";

    private const string FragmentShaderSource = """
#version 300 es

precision highp float;

in vec2 vs_Uv;
in vec3 vs_WorldPos;

uniform sampler2D u_NormalTex;
uniform sampler2D u_NormalTex2;
uniform sampler2D u_SkyTex;
uniform vec3 u_EyePos;
uniform float u_Time;
uniform float u_TextureLerp;
uniform float u_Alpha;
uniform vec3 u_DeepColor;
uniform vec3 u_ReflectionTint;
uniform float u_SkyReflectionStrength;

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

    vec3 normal = normalize((((0.5 * normalTexture) + (0.5 * normalTextureDetail)) * 2.0) - 1.0);
    normal = normal.xzy;

    vec3 incident = normalize(vs_WorldPos - u_EyePos);
    vec3 reflected = normalize(reflect(incident, normal));
    float horizon = clamp(reflected.y * 0.5 + 0.5, 0.0, 1.0);

    float fresnel = pow(1.0 - max(dot(normalize(u_EyePos - vs_WorldPos), normal), 0.0), 5.0);
    vec3 gradientReflection = mix(u_DeepColor * 0.72, u_ReflectionTint, horizon);
    vec3 skyColor = texture(u_SkyTex, DirectionToEquirectUv(reflected)).rgb;
    vec3 reflection = mix(gradientReflection, skyColor, clamp(u_SkyReflectionStrength, 0.0, 1.0));
    vec3 color = mix(u_DeepColor, reflection, clamp(0.35 + fresnel * 0.65, 0.0, 1.0));

    out_Color = vec4(color, u_Alpha);
}
""";
}

