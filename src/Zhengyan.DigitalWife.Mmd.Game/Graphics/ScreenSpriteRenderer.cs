using System.Numerics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public readonly record struct ScreenSpriteDrawCommand(
    uint TextureId,
    Vector2 Min,
    Vector2 Max,
    float RotationDegrees,
    float Opacity,
    bool FlipV);

public sealed unsafe class ScreenSpriteRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _program;
    private readonly uint _vao;
    private readonly uint _vertexBuffer;
    private readonly int _uniformTexture;
    private readonly int _uniformTargetSize;
    private readonly int _uniformOpacity;
    private readonly float[] _vertices = new float[6 * 4];
    private bool _disposed;

    public ScreenSpriteRenderer(GL gl)
    {
        _gl = gl;
        _program = gl.CreateShaderProgramFromSource(VertexShaderSource, FragmentShaderSource);
        _vao = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();

        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        gl.BufferData(GLEnum.ArrayBuffer, (uint)(6 * 4 * sizeof(float)), null, GLEnum.DynamicDraw);
        gl.VertexAttribPointer(0, 2, GLEnum.Float, false, (uint)(4 * sizeof(float)), (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)(4 * sizeof(float)), (void*)(2 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindVertexArray(0);

        _uniformTexture = gl.GetUniformLocation(_program, "u_Texture");
        _uniformTargetSize = gl.GetUniformLocation(_program, "u_TargetSize");
        _uniformOpacity = gl.GetUniformLocation(_program, "u_Opacity");
    }

    public void Draw(IReadOnlyList<ScreenSpriteDrawCommand> commands, int targetWidth, int targetHeight)
    {
        if (_disposed || commands.Count == 0)
        {
            return;
        }

        bool depthTestEnabled = _gl.IsEnabled(GLEnum.DepthTest);
        bool stencilTestEnabled = _gl.IsEnabled(GLEnum.StencilTest);
        bool blendEnabled = _gl.IsEnabled(GLEnum.Blend);
        bool cullFaceEnabled = _gl.IsEnabled(GLEnum.CullFace);

        _gl.Disable(GLEnum.DepthTest);
        _gl.Disable(GLEnum.StencilTest);
        _gl.Disable(GLEnum.CullFace);
        _gl.Enable(GLEnum.Blend);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
        _gl.DepthMask(false);
        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);
        _gl.SetUniform(_uniformTexture, 0);
        _gl.SetUniform(_uniformTargetSize, new Vector2(Math.Max(targetWidth, 1), Math.Max(targetHeight, 1)));
        _gl.ActiveTexture(TextureUnit.Texture0);

        foreach (ScreenSpriteDrawCommand command in commands)
        {
            if (command.TextureId == 0)
            {
                continue;
            }

            FillVertices(command, _vertices);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
            fixed (float* vertexPtr = _vertices)
            {
                _gl.BufferData(GLEnum.ArrayBuffer, (uint)(_vertices.Length * sizeof(float)), vertexPtr, GLEnum.DynamicDraw);
            }

            _gl.SetUniform(_uniformOpacity, Math.Clamp(command.Opacity, 0.0f, 1.0f));
            _gl.BindTexture(GLEnum.Texture2D, command.TextureId);
            _gl.DrawArrays(GLEnum.Triangles, 0, 6);
        }

        _gl.BindTexture(GLEnum.Texture2D, 0);
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.DepthMask(true);
        RestoreCapability(GLEnum.DepthTest, depthTestEnabled);
        RestoreCapability(GLEnum.StencilTest, stencilTestEnabled);
        RestoreCapability(GLEnum.Blend, blendEnabled);
        RestoreCapability(GLEnum.CullFace, cullFaceEnabled);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gl.DeleteBuffer(_vertexBuffer);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
        GC.SuppressFinalize(this);
    }

    private void RestoreCapability(GLEnum capability, bool enabled)
    {
        if (enabled)
        {
            _gl.Enable(capability);
        }
        else
        {
            _gl.Disable(capability);
        }
    }

    private static void FillVertices(ScreenSpriteDrawCommand command, Span<float> vertices)
    {
        Vector2 center = (command.Min + command.Max) * 0.5f;
        Vector2 half = (command.Max - command.Min) * 0.5f;
        float radians = command.RotationDegrees * MathF.PI / 180.0f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        Vector2 Rotate(Vector2 local)
        {
            return center + new Vector2(
                (local.X * cos) - (local.Y * sin),
                (local.X * sin) + (local.Y * cos));
        }

        Vector2 p1 = Rotate(new Vector2(-half.X, -half.Y));
        Vector2 p2 = Rotate(new Vector2(half.X, -half.Y));
        Vector2 p3 = Rotate(new Vector2(half.X, half.Y));
        Vector2 p4 = Rotate(new Vector2(-half.X, half.Y));
        float topV = command.FlipV ? 1.0f : 0.0f;
        float bottomV = command.FlipV ? 0.0f : 1.0f;

        WriteVertex(vertices, 0, p1, 0.0f, topV);
        WriteVertex(vertices, 4, p2, 1.0f, topV);
        WriteVertex(vertices, 8, p3, 1.0f, bottomV);
        WriteVertex(vertices, 12, p1, 0.0f, topV);
        WriteVertex(vertices, 16, p3, 1.0f, bottomV);
        WriteVertex(vertices, 20, p4, 0.0f, bottomV);
    }

    private static void WriteVertex(Span<float> vertices, int offset, Vector2 position, float u, float v)
    {
        vertices[offset] = position.X;
        vertices[offset + 1] = position.Y;
        vertices[offset + 2] = u;
        vertices[offset + 3] = v;
    }

    private const string VertexShaderSource = """
        #version 300 es
        layout (location = 0) in vec2 a_Position;
        layout (location = 1) in vec2 a_TexCoord;

        uniform vec2 u_TargetSize;

        out vec2 v_TexCoord;

        void main()
        {
            vec2 normalized = a_Position / max(u_TargetSize, vec2(1.0));
            gl_Position = vec4((normalized.x * 2.0) - 1.0, 1.0 - (normalized.y * 2.0), 0.0, 1.0);
            v_TexCoord = a_TexCoord;
        }
        """;

    private const string FragmentShaderSource = """
        #version 300 es
        precision mediump float;

        uniform sampler2D u_Texture;
        uniform float u_Opacity;

        in vec2 v_TexCoord;
        out vec4 out_Color;

        void main()
        {
            vec4 color = texture(u_Texture, v_TexCoord);
            out_Color = vec4(color.rgb, color.a * u_Opacity);
        }
        """;
}
