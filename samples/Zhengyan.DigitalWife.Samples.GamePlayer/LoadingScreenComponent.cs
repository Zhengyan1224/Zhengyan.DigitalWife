using System.Numerics;
using Silk.NET.OpenGLES;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed unsafe class LoadingScreenComponent(
    Func<float> getProgress,
    Func<string> getMessage,
    Func<LoadingScreenSettings> getSettings,
    Func<string, string> resolvePath) : DrawableGameComponent
{
    private readonly Func<float> _getProgress = getProgress;
    private readonly Func<string> _getMessage = getMessage;
    private readonly Func<LoadingScreenSettings> _getSettings = getSettings;
    private readonly Func<string, string> _resolvePath = resolvePath;

    private uint _program;
    private uint _vao;
    private uint _vertexBuffer;
    private int _uniformColor = -1;
    private int _uniformTexture = -1;
    private int _uniformUseTexture = -1;
    private Texture2D? _backgroundTexture;
    private string _backgroundTexturePath = string.Empty;

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

        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        gl.BufferData(GLEnum.ArrayBuffer, (uint)(24 * sizeof(float)), null, GLEnum.DynamicDraw);
        gl.VertexAttribPointer(0, 2, GLEnum.Float, false, 4 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindVertexArray(0);

        _uniformColor = gl.GetUniformLocation(_program, "u_Color");
        _uniformTexture = gl.GetUniformLocation(_program, "u_Texture");
        _uniformUseTexture = gl.GetUniformLocation(_program, "u_UseTexture");
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        if (Game is null)
        {
            return;
        }

        GL gl = Game.GraphicsDevice.Gl;
        gl.Disable(GLEnum.DepthTest);
        gl.Disable(GLEnum.CullFace);
        gl.Disable(GLEnum.ScissorTest);
        gl.Disable(GLEnum.StencilTest);
        gl.Disable(GLEnum.PolygonOffsetFill);
        gl.ColorMask(true, true, true, true);
        gl.DepthMask(false);
        gl.UseProgram(_program);
        gl.BindVertexArray(_vao);

        LoadingScreenSettings settings = _getSettings();
        Vector4 backgroundColor = settings.BackgroundColor.ToVector4();

        gl.Disable(GLEnum.Blend);
        DrawRect(gl, new Vector4(-1.0f, -1.0f, 1.0f, 1.0f), backgroundColor, useTexture: false);

        gl.Enable(GLEnum.Blend);
        gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
        gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);

        Texture2D? backgroundTexture = GetBackgroundTexture(settings);
        if (backgroundTexture is not null)
        {
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(GLEnum.Texture2D, backgroundTexture.Id);
            gl.SetUniform(_uniformTexture, 0);
            DrawRect(
                gl,
                new Vector4(-1.0f, -1.0f, 1.0f, 1.0f),
                new Vector4(1.0f, 1.0f, 1.0f, Math.Clamp(settings.BackgroundImageOpacity, 0.0f, 1.0f)),
                useTexture: true);
            gl.BindTexture(GLEnum.Texture2D, 0);
        }

        DrawRect(gl, new Vector4(-0.46f, -0.05f, 0.46f, 0.05f), new Vector4(0.10f, 0.15f, 0.22f, 0.92f), useTexture: false);
        DrawRect(gl, new Vector4(-0.44f, -0.025f, 0.44f, 0.025f), new Vector4(0.02f, 0.04f, 0.07f, 1.0f), useTexture: false);

        float progress = Math.Clamp(_getProgress(), 0.0f, 1.0f);
        float right = -0.44f + (0.88f * progress);
        DrawRect(gl, new Vector4(-0.44f, -0.025f, right, 0.025f), new Vector4(0.30f, 0.62f, 1.0f, 1.0f), useTexture: false);

        _ = _getMessage();

        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.Disable(GLEnum.Blend);
        gl.DepthMask(true);
    }

    public override void Dispose()
    {
        if (Game is not null)
        {
            GL gl = Game.GraphicsDevice.Gl;
            gl.DeleteBuffer(_vertexBuffer);
            gl.DeleteVertexArray(_vao);
            gl.DeleteProgram(_program);
        }

        _backgroundTexture?.Dispose();
        _backgroundTexture = null;
        base.Dispose();
    }

    private Texture2D? GetBackgroundTexture(LoadingScreenSettings settings)
    {
        if (Game is null || string.IsNullOrWhiteSpace(settings.BackgroundImagePath))
        {
            ClearBackgroundTexture();
            return null;
        }

        string fullPath = _resolvePath(settings.BackgroundImagePath);
        if (!File.Exists(fullPath))
        {
            ClearBackgroundTexture();
            return null;
        }

        if (_backgroundTexture is not null && string.Equals(_backgroundTexturePath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return _backgroundTexture;
        }

        ClearBackgroundTexture();
        try
        {
            _backgroundTexture = new Texture2D(Game.GraphicsDevice.Gl, GLEnum.ClampToEdge);
            _backgroundTexture.LoadFromFile(fullPath);
            _backgroundTexturePath = fullPath;
            return _backgroundTexture;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load loading screen background image '{fullPath}': {ex.Message}");
            ClearBackgroundTexture();
            return null;
        }
    }

    private void ClearBackgroundTexture()
    {
        _backgroundTexture?.Dispose();
        _backgroundTexture = null;
        _backgroundTexturePath = string.Empty;
    }

    private void DrawRect(GL gl, Vector4 rect, Vector4 color, bool useTexture)
    {
        Span<float> vertices =
        [
            rect.X, rect.Y, 0.0f, 1.0f,
            rect.Z, rect.Y, 1.0f, 1.0f,
            rect.X, rect.W, 0.0f, 0.0f,
            rect.X, rect.W, 0.0f, 0.0f,
            rect.Z, rect.Y, 1.0f, 1.0f,
            rect.Z, rect.W, 1.0f, 0.0f
        ];

        gl.SetUniform(_uniformColor, color);
        gl.SetUniform(_uniformUseTexture, useTexture ? 1 : 0);
        fixed (float* vertexPtr = vertices)
        {
            gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
            gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(vertices.Length * sizeof(float)), vertexPtr);
            gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        }

        gl.DrawArrays(GLEnum.Triangles, 0, 6);
    }

    private const string VertexShaderSource = """
#version 300 es

layout (location = 0) in vec2 in_Pos;
layout (location = 1) in vec2 in_Uv;

out vec2 v_Uv;

void main()
{
    v_Uv = in_Uv;
    gl_Position = vec4(in_Pos, 0.0, 1.0);
}
""";

    private const string FragmentShaderSource = """
#version 300 es

precision highp float;

in vec2 v_Uv;

uniform vec4 u_Color;
uniform sampler2D u_Texture;
uniform int u_UseTexture;

out vec4 out_Color;

void main()
{
    if (u_UseTexture == 1)
    {
        out_Color = texture(u_Texture, v_Uv) * u_Color;
    }
    else
    {
        out_Color = u_Color;
    }
}
""";
}
