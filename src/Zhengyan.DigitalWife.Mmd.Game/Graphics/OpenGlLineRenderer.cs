using System.Numerics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal sealed unsafe class OpenGlLineRenderer : ILineRenderer
{
    private const int FloatStride = 6;
    private readonly GL _gl;
    private readonly uint _program;
    private readonly uint _vao;
    private readonly uint _vertexBuffer;
    private int _capacityBytes;
    private bool _disposed;

    public OpenGlLineRenderer(GL gl, int initialCapacityBytes)
    {
        _gl = gl;
        _capacityBytes = Math.Max(initialCapacityBytes, FloatStride * sizeof(float) * 2);
        _program = gl.CreateShaderProgramFromSource(VertexShaderSource, FragmentShaderSource);
        _vao = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        gl.BufferData(GLEnum.ArrayBuffer, (uint)_capacityBytes, null, GLEnum.DynamicDraw);
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, FloatStride * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 3, GLEnum.Float, false, FloatStride * sizeof(float), (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.BindVertexArray(0);
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
    }

    public void Draw(ReadOnlySpan<float> vertices, int vertexCount, Matrix4x4 worldViewProjection, bool depth = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (vertexCount <= 0) return;
        int bytes = checked(vertices.Length * sizeof(float));
        if (bytes > _capacityBytes)
        {
            _capacityBytes = Math.Max(bytes, _capacityBytes * 2);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
            _gl.BufferData(GLEnum.ArrayBuffer, (uint)_capacityBytes, null, GLEnum.DynamicDraw);
        }

        _gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        fixed (float* data = vertices)
        {
            _gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)bytes, data);
        }

        if (depth) _gl.Enable(GLEnum.DepthTest);
        else _gl.Disable(GLEnum.DepthTest);
        _gl.Disable(GLEnum.CullFace);
        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);
        _gl.SetUniform(_gl.GetUniformLocation(_program, "u_WVP"), worldViewProjection);
        _gl.DrawArrays(GLEnum.Lines, 0, (uint)vertexCount);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.Enable(GLEnum.DepthTest);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteBuffer(_vertexBuffer);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }

    private const string VertexShaderSource = """
        #version 300 es
        layout(location=0) in vec3 in_Pos;
        layout(location=1) in vec3 in_Color;
        out vec3 vs_Color;
        uniform mat4 u_WVP;
        void main(){ vs_Color=in_Color; gl_Position=u_WVP*vec4(in_Pos,1.0); }
        """;

    private const string FragmentShaderSource = """
        #version 300 es
        precision highp float;
        in vec3 vs_Color;
        out vec4 out_Color;
        void main(){ out_Color=vec4(vs_Color,1.0); }
        """;
}
