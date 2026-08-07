using System.Numerics;
using Silk.NET.OpenGLES;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Mmd.Game.Components;

public sealed unsafe class SkyboxComponent : DrawableGameComponent
{
    private OrbitCamera _camera;
    private string _texturePath;
    private Texture2D? _texture;
    private ITexture2D? _backendTexture;
    private ISkyboxPassRenderer? _backendRenderer;
    private uint _program;
    private uint _vao;
    private uint _vertexBuffer;
    private int _uniformInverseViewProjection = -1;
    private int _uniformTexture = -1;
    private int _uniformTint = -1;
    private int _uniformExposure = -1;

    public SkyboxComponent(OrbitCamera camera, string texturePath)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _texturePath = texturePath;
        DrawOrder = -10000;
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
                if (Game.GraphicsDevice.Renderer is OpenGlRenderer openGl) ReloadTexture(openGl.Gl);
                else ReloadBackendTexture();
            }
        }
    }

    public OrbitCamera Camera
    {
        get => _camera;
        set => _camera = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Vector3 Tint { get; set; } = Vector3.One;

    public float Exposure { get; set; } = 1.0f;

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        _backendRenderer = Game.GraphicsDevice.Renderer.Services.CreateSkyboxPassRenderer();
        if (_backendRenderer is not null)
        {
            _backendTexture = Game.GraphicsDevice.CreateTexture2D();
            ReloadBackendTexture();
            return;
        }

        GL gl = Game.GraphicsDevice.Gl;
        _program = gl.CreateShaderProgramFromSource(VertexShaderSource, FragmentShaderSource);
        _vao = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();

        float[] vertices =
        [
            -1.0f, -1.0f,
             1.0f, -1.0f,
            -1.0f,  1.0f,
            -1.0f,  1.0f,
             1.0f, -1.0f,
             1.0f,  1.0f
        ];

        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        fixed (float* vertexPtr = vertices)
        {
            gl.BufferData(GLEnum.ArrayBuffer, (uint)(vertices.Length * sizeof(float)), vertexPtr, GLEnum.StaticDraw);
        }

        gl.VertexAttribPointer(0, 2, GLEnum.Float, false, 2 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindVertexArray(0);

        _uniformInverseViewProjection = gl.GetUniformLocation(_program, "u_InverseViewProjection");
        _uniformTexture = gl.GetUniformLocation(_program, "u_Texture");
        _uniformTint = gl.GetUniformLocation(_program, "u_Tint");
        _uniformExposure = gl.GetUniformLocation(_program, "u_Exposure");
        ReloadTexture(gl);
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;
        if (Game is null)
        {
            return;
        }

        Matrix4x4 viewNoTranslation = _camera.View;
        viewNoTranslation.M41 = 0.0f;
        viewNoTranslation.M42 = 0.0f;
        viewNoTranslation.M43 = 0.0f;
        Matrix4x4 viewProjection = viewNoTranslation * _camera.Projection;
        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 inverseViewProjection))
        {
            return;
        }

        if (_backendRenderer is not null && _backendTexture is not null)
        {
            _backendRenderer.Draw(_backendTexture, inverseViewProjection, Tint, Exposure);
            return;
        }

        if (_texture is null) return;

        GL gl = Game.GraphicsDevice.Gl;
        gl.Disable(GLEnum.DepthTest);
        gl.DepthMask(false);
        gl.Disable(GLEnum.CullFace);
        gl.Disable(GLEnum.Blend);

        gl.UseProgram(_program);
        gl.BindVertexArray(_vao);
        gl.SetUniform(_uniformInverseViewProjection, inverseViewProjection);
        gl.SetUniform(_uniformTexture, 0);
        gl.SetUniform(_uniformTint, Tint);
        gl.SetUniform(_uniformExposure, MathF.Max(0.0f, Exposure));
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(GLEnum.Texture2D, _texture.Id);
        gl.DrawArrays(GLEnum.Triangles, 0, 6);

        gl.BindTexture(GLEnum.Texture2D, 0);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.DepthMask(true);
        gl.Enable(GLEnum.DepthTest);
    }

    public override void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _backendRenderer?.Dispose();
        _backendRenderer = null;
        _backendTexture?.Dispose();
        _backendTexture = null;

        if (Game is not null && Game.GraphicsDevice.Renderer is OpenGlRenderer openGl)
        {
            GL gl = openGl.Gl;
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
        if (!string.IsNullOrWhiteSpace(_texturePath) && File.Exists(_texturePath))
        {
            _texture.LoadFromFile(_texturePath);
        }
        else
        {
            _texture.Fill(20, 28, 40, 255);
        }
    }

    private void ReloadBackendTexture()
    {
        if (_backendTexture is null) return;
        if (!string.IsNullOrWhiteSpace(_texturePath) && File.Exists(_texturePath)) _backendTexture.LoadFromFile(_texturePath);
        else _backendTexture.Fill(20, 28, 40, 255);
    }

    private const string VertexShaderSource = """
#version 300 es

layout (location = 0) in vec2 in_Pos;

out vec2 vs_Pos;

void main()
{
    vs_Pos = in_Pos;
    gl_Position = vec4(in_Pos, 1.0, 1.0);
}
""";

    private const string FragmentShaderSource = """
#version 300 es

precision highp float;

in vec2 vs_Pos;

uniform mat4 u_InverseViewProjection;
uniform sampler2D u_Texture;
uniform vec3 u_Tint;
uniform float u_Exposure;

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
    vec4 farPoint = u_InverseViewProjection * vec4(vs_Pos, 1.0, 1.0);
    vec3 direction = normalize(farPoint.xyz / farPoint.w);
    vec3 color = texture(u_Texture, DirectionToEquirectUv(direction)).rgb * u_Tint * u_Exposure;
    out_Color = vec4(color, 1.0);
}
""";
}
