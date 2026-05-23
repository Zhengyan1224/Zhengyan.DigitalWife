using System.Numerics;
using Silk.NET.OpenGLES;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Mmd.Game.Components;

public sealed unsafe class TexturedPlaneComponent : DrawableGameComponent
{
    private OrbitCamera _camera;
    private string _texturePath;
    private Texture2D? _texture;
    private IRuntimeTextureProvider? _runtimeTextureProvider;
    private uint _program;
    private uint _vao;
    private uint _vertexBuffer;
    private int _uniformWorld = -1;
    private int _uniformView = -1;
    private int _uniformProjection = -1;
    private int _uniformTexture = -1;
    private int _uniformTint = -1;

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
                ReloadTexture(Game.GraphicsDevice.Gl);
            }
        }
    }

    public IRuntimeTextureProvider? RuntimeTextureProvider
    {
        get => _runtimeTextureProvider;
        set => _runtimeTextureProvider = value;
    }

    private Matrix4x4 World
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
        ReloadTexture(gl);
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;
        if (Game is null || (_texture is null && !IsRuntimeTextureReference(_texturePath)))
        {
            return;
        }

        GL gl = Game.GraphicsDevice.Gl;
        gl.Enable(GLEnum.DepthTest);
        gl.Enable(GLEnum.Blend);
        gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
        gl.Disable(GLEnum.CullFace);

        gl.UseProgram(_program);
        gl.BindVertexArray(_vao);
        gl.SetUniform(_uniformWorld, World);
        gl.SetUniform(_uniformView, _camera.View);
        gl.SetUniform(_uniformProjection, _camera.Projection);
        gl.SetUniform(_uniformTexture, 0);
        gl.SetUniform(_uniformTint, Tint);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(GLEnum.Texture2D, ResolveTextureId());
        gl.DrawArrays(GLEnum.Triangles, 0, 6);

        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.Disable(GLEnum.Blend);
    }

    public override void Dispose()
    {
        _texture?.Dispose();
        _texture = null;

        if (Game is not null)
        {
            GL gl = Game.GraphicsDevice.Gl;
            gl.DeleteBuffer(_vertexBuffer);
            gl.DeleteVertexArray(_vao);
            gl.DeleteProgram(_program);
        }

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

    private uint ResolveTextureId()
    {
        if (_runtimeTextureProvider is not null
            && _runtimeTextureProvider.TryGetTexture(_texturePath, out uint runtimeTextureId))
        {
            return runtimeTextureId;
        }

        return _texture?.Id ?? 0;
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

    private const string VertexShaderSource = """
#version 300 es

layout (location = 0) in vec3 in_Pos;
layout (location = 1) in vec2 in_Uv;

uniform mat4 u_World;
uniform mat4 u_View;
uniform mat4 u_Projection;

out vec2 vs_Uv;

void main()
{
    vs_Uv = in_Uv;
    gl_Position = u_Projection * u_View * u_World * vec4(in_Pos, 1.0);
}
""";

    private const string FragmentShaderSource = """
#version 300 es

precision highp float;

in vec2 vs_Uv;

uniform sampler2D u_Texture;
uniform vec4 u_Tint;

out vec4 out_Color;

void main()
{
    vec4 color = texture(u_Texture, vs_Uv) * u_Tint;
    if (color.a <= 0.001)
    {
        discard;
    }

    out_Color = color;
}
""";
}
