using System.Numerics;
using Silk.NET.OpenGLES;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed unsafe class RuntimeDebugDrawComponent(OrbitCamera camera) : DrawableGameComponent
{
    private const int FloatStride = 7;

    private readonly OrbitCamera _camera = camera;
    private readonly List<DebugLine> _lines = [];
    private uint _program;
    private uint _vao;
    private uint _vertexBuffer;
    private VeldridLineRenderer? _vulkanLineRenderer;
    private int _bufferVertexCapacity;

    public void DrawRay(Vector3 origin, Vector3 direction, float length, Vector4 color, float durationSeconds)
    {
        Vector3 normalized = direction.LengthSquared() <= 0.000001f ? -Vector3.UnitZ : Vector3.Normalize(direction);
        Vector3 end = origin + normalized * Math.Max(0.0f, length);
        DrawLine(origin, end, color, durationSeconds);
    }

    public void DrawLine(Vector3 start, Vector3 end, Vector4 color, float durationSeconds)
    {
        _lines.Add(new DebugLine(start, end, color, Math.Max(0.0f, durationSeconds)));
    }

    public override void Update(GameTime gameTime)
    {
        float dt = Math.Max(0.0f, (float)gameTime.ElapsedSeconds);
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            DebugLine line = _lines[i];
            line.RemainingSeconds -= dt;
            if (line.RemainingSeconds <= 0.0f)
            {
                _lines.RemoveAt(i);
            }
            else
            {
                _lines[i] = line;
            }
        }
    }

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        if (Game.GraphicsDevice.Renderer is VulkanRenderer vulkan)
        {
            _vulkanLineRenderer = new VeldridLineRenderer(vulkan);
            return;
        }

        GL gl = Game.GraphicsDevice.Gl;
        _program = gl.CreateShaderProgramFromSource(VertexShaderSource, FragmentShaderSource);
        _vao = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();

        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        EnsureBufferCapacity(128);

        uint positionLocation = (uint)gl.GetAttribLocation(_program, "in_Pos");
        uint colorLocation = (uint)gl.GetAttribLocation(_program, "in_Color");
        gl.VertexAttribPointer(positionLocation, 3, GLEnum.Float, false, FloatStride * (uint)sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(positionLocation);
        gl.VertexAttribPointer(colorLocation, 4, GLEnum.Float, false, FloatStride * (uint)sizeof(float), (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(colorLocation);

        gl.BindVertexArray(0);
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;
        if (Game is null || _lines.Count == 0)
        {
            return;
        }

        int vertexCount = _lines.Count * 2;
        EnsureBufferCapacity(vertexCount);
        float[] vertices = new float[vertexCount * FloatStride];
        int vertexIndex = 0;
        foreach (DebugLine line in _lines)
        {
            WriteVertex(vertices, vertexIndex++, line.Start, line.Color);
            WriteVertex(vertices, vertexIndex++, line.End, line.Color);
        }

        if (_vulkanLineRenderer is not null)
        {
            float[] rgbVertices = new float[vertexCount * 6];
            for (int source = 0, target = 0; source < vertices.Length; source += 7, target += 6)
            {
                Array.Copy(vertices, source, rgbVertices, target, 6);
            }
            _vulkanLineRenderer.Draw(rgbVertices, vertexCount, _camera.View * _camera.Projection);
            return;
        }

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
            gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(vertices.Length * sizeof(float)), vertexPtr);
        }

        gl.DrawArrays(GLEnum.Lines, 0, (uint)vertexCount);

        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
        gl.Enable(GLEnum.DepthTest);
    }

    public override void Dispose()
    {
        _vulkanLineRenderer?.Dispose();
        _vulkanLineRenderer = null;
        if (Game is not null && Game.GraphicsDevice.Renderer is OpenGlRenderer)
        {
            GL gl = Game.GraphicsDevice.Gl;
            gl.DeleteBuffer(_vertexBuffer);
            gl.DeleteVertexArray(_vao);
            gl.DeleteProgram(_program);
        }

        base.Dispose();
    }

    private void EnsureBufferCapacity(int vertexCount)
    {
        if (Game is null || _vulkanLineRenderer is not null || vertexCount <= _bufferVertexCapacity)
        {
            return;
        }

        _bufferVertexCapacity = Math.Max(vertexCount, _bufferVertexCapacity * 2);
        if (_bufferVertexCapacity <= 0)
        {
            _bufferVertexCapacity = 128;
        }

        GL gl = Game.GraphicsDevice.Gl;
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        gl.BufferData(GLEnum.ArrayBuffer, (uint)(_bufferVertexCapacity * FloatStride * sizeof(float)), null, GLEnum.DynamicDraw);
    }

    private static void WriteVertex(float[] vertices, int vertexIndex, Vector3 position, Vector4 color)
    {
        int offset = vertexIndex * FloatStride;
        vertices[offset + 0] = position.X;
        vertices[offset + 1] = position.Y;
        vertices[offset + 2] = position.Z;
        vertices[offset + 3] = color.X;
        vertices[offset + 4] = color.Y;
        vertices[offset + 5] = color.Z;
        vertices[offset + 6] = color.W;
    }

    private struct DebugLine(Vector3 start, Vector3 end, Vector4 color, float remainingSeconds)
    {
        public Vector3 Start = start;
        public Vector3 End = end;
        public Vector4 Color = color;
        public float RemainingSeconds = remainingSeconds;
    }

    private const string VertexShaderSource = """
#version 300 es

in vec3 in_Pos;
in vec4 in_Color;

out vec4 vs_Color;

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

in vec4 vs_Color;
out vec4 out_Color;

void main()
{
    out_Color = vs_Color;
}
""";
}
