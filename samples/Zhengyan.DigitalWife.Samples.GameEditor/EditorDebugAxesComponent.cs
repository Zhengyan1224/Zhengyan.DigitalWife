using System.Numerics;
using Silk.NET.OpenGLES;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Samples.GameEditor;

internal sealed unsafe class EditorDebugAxesComponent(OrbitCamera camera) : DrawableGameComponent
{
    private const int FloatStride = 6;
    private const int MaxVertexCount = 96;

    private readonly OrbitCamera _camera = camera;

    private uint _program;
    private uint _vao;
    private uint _vertexBuffer;

    public float AxisLength { get; set; } = 3.0f;

    public float NegativeAxisLength { get; set; } = 1.0f;

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
        gl.BufferData(GLEnum.ArrayBuffer, (uint)(MaxVertexCount * FloatStride * sizeof(float)), null, GLEnum.DynamicDraw);

        uint positionLocation = (uint)gl.GetAttribLocation(_program, "in_Pos");
        uint colorLocation = (uint)gl.GetAttribLocation(_program, "in_Color");
        gl.VertexAttribPointer(positionLocation, 3, GLEnum.Float, false, FloatStride * (uint)sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(positionLocation);
        gl.VertexAttribPointer(colorLocation, 3, GLEnum.Float, false, FloatStride * (uint)sizeof(float), (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(colorLocation);

        gl.BindVertexArray(0);
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        if (Game is null)
        {
            return;
        }

        Span<float> vertices = stackalloc float[MaxVertexCount * FloatStride];
        int vertexCount = 0;
        AddAxes(vertices, ref vertexCount);
        AddOriginMarker(vertices, ref vertexCount);

        GL gl = Game.GraphicsDevice.Gl;
        int uniformLocation = gl.GetUniformLocation(_program, "u_WVP");

        gl.Disable(GLEnum.CullFace);
        gl.Disable(GLEnum.DepthTest);
        gl.UseProgram(_program);
        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        gl.SetUniform(uniformLocation, _camera.View * _camera.Projection);

        fixed (float* vertexPtr = vertices)
        {
            gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(vertexCount * FloatStride * sizeof(float)), vertexPtr);
        }

        gl.DrawArrays(GLEnum.Lines, 0, (uint)vertexCount);

        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.Enable(GLEnum.DepthTest);
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

        base.Dispose();
    }

    private void AddAxes(Span<float> vertices, ref int vertexCount)
    {
        float axisLength = MathF.Max(AxisLength, 0.1f);
        float negativeLength = MathF.Max(NegativeAxisLength, 0.0f);

        Vector3 xColor = new(1.0f, 0.18f, 0.18f);
        Vector3 yColor = new(0.18f, 1.0f, 0.35f);
        Vector3 zColor = new(0.22f, 0.52f, 1.0f);
        Vector3 xNegativeColor = new(0.42f, 0.07f, 0.07f);
        Vector3 yNegativeColor = new(0.06f, 0.36f, 0.12f);
        Vector3 zNegativeColor = new(0.08f, 0.18f, 0.42f);

        AddLine(vertices, ref vertexCount, Vector3.Zero, new Vector3(axisLength, 0.0f, 0.0f), xColor);
        AddLine(vertices, ref vertexCount, Vector3.Zero, new Vector3(0.0f, axisLength, 0.0f), yColor);
        AddLine(vertices, ref vertexCount, Vector3.Zero, new Vector3(0.0f, 0.0f, axisLength), zColor);

        if (negativeLength > 0.0f)
        {
            AddLine(vertices, ref vertexCount, Vector3.Zero, new Vector3(-negativeLength, 0.0f, 0.0f), xNegativeColor);
            AddLine(vertices, ref vertexCount, Vector3.Zero, new Vector3(0.0f, -negativeLength, 0.0f), yNegativeColor);
            AddLine(vertices, ref vertexCount, Vector3.Zero, new Vector3(0.0f, 0.0f, -negativeLength), zNegativeColor);
        }

        AddArrowHead(vertices, ref vertexCount, new Vector3(axisLength, 0.0f, 0.0f), Vector3.UnitX, xColor);
        AddArrowHead(vertices, ref vertexCount, new Vector3(0.0f, axisLength, 0.0f), Vector3.UnitY, yColor);
        AddArrowHead(vertices, ref vertexCount, new Vector3(0.0f, 0.0f, axisLength), Vector3.UnitZ, zColor);
    }

    private static void AddOriginMarker(Span<float> vertices, ref int vertexCount)
    {
        const float radius = 0.08f;
        Vector3 color = new(1.0f, 1.0f, 1.0f);
        AddLine(vertices, ref vertexCount, new Vector3(-radius, 0.0f, 0.0f), new Vector3(radius, 0.0f, 0.0f), color);
        AddLine(vertices, ref vertexCount, new Vector3(0.0f, -radius, 0.0f), new Vector3(0.0f, radius, 0.0f), color);
        AddLine(vertices, ref vertexCount, new Vector3(0.0f, 0.0f, -radius), new Vector3(0.0f, 0.0f, radius), color);
    }

    private static void AddArrowHead(Span<float> vertices, ref int vertexCount, Vector3 end, Vector3 axis, Vector3 color)
    {
        const float headLength = 0.22f;
        const float headWidth = 0.10f;

        Vector3 tangentA;
        Vector3 tangentB;
        if (axis == Vector3.UnitX)
        {
            tangentA = Vector3.UnitY;
            tangentB = Vector3.UnitZ;
        }
        else if (axis == Vector3.UnitY)
        {
            tangentA = Vector3.UnitX;
            tangentB = Vector3.UnitZ;
        }
        else
        {
            tangentA = Vector3.UnitX;
            tangentB = Vector3.UnitY;
        }

        Vector3 basePoint = end - axis * headLength;
        AddLine(vertices, ref vertexCount, end, basePoint + tangentA * headWidth, color);
        AddLine(vertices, ref vertexCount, end, basePoint - tangentA * headWidth, color);
        AddLine(vertices, ref vertexCount, end, basePoint + tangentB * headWidth, color);
        AddLine(vertices, ref vertexCount, end, basePoint - tangentB * headWidth, color);
    }

    private static void AddLine(Span<float> vertices, ref int vertexCount, Vector3 start, Vector3 end, Vector3 color)
    {
        WriteVertex(vertices, vertexCount++, start, color);
        WriteVertex(vertices, vertexCount++, end, color);
    }

    private static void WriteVertex(Span<float> vertices, int vertexIndex, Vector3 position, Vector3 color)
    {
        int offset = vertexIndex * FloatStride;
        vertices[offset + 0] = position.X;
        vertices[offset + 1] = position.Y;
        vertices[offset + 2] = position.Z;
        vertices[offset + 3] = color.X;
        vertices[offset + 4] = color.Y;
        vertices[offset + 5] = color.Z;
    }

    private const string VertexShaderSource = """
#version 300 es

in vec3 in_Pos;
in vec3 in_Color;

out vec3 vs_Color;

uniform mat4 u_WVP;

void main()
{
    vs_Color = in_Color;
    gl_Position = u_WVP * vec4(in_Pos, 1.0);
}
""";

    private const string FragmentShaderSource = """
#version 300 es

precision highp float;

in vec3 vs_Color;
out vec4 out_Color;

void main()
{
    out_Color = vec4(vs_Color, 1.0);
}
""";
}
