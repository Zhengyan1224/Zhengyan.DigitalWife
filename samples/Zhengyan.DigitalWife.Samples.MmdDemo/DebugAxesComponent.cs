using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Samples.MmdDemo;

internal sealed unsafe class DebugAxesComponent(OrbitCamera camera, Func<Vector3> getLightDirection) : DrawableGameComponent
{
    private const int FloatStride = 6;
    private const int MaxVertexCount = 512;

    private readonly OrbitCamera _camera = camera;
    private readonly Func<Vector3> _getLightDirection = getLightDirection;

    private uint _program;
    private uint _vao;
    private uint _vertexBuffer;

    public bool VisibleAxes { get; set; } = true;

    public bool VisibleLightArrow { get; set; } = true;

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

        GL gl = Game.GraphicsDevice.Gl;
        int uniformLocation = gl.GetUniformLocation(_program, "u_WVP");

        gl.Disable(GLEnum.CullFace);
        gl.Disable(GLEnum.DepthTest);
        gl.UseProgram(_program);
        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);

        if (VisibleAxes)
        {
            Span<float> axisVertices = stackalloc float[18 * FloatStride];
            int axisVertexCount = 0;
            AddLine(axisVertices, ref axisVertexCount, Vector3.Zero, new Vector3(3.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.25f, 0.25f));
            AddLine(axisVertices, ref axisVertexCount, Vector3.Zero, new Vector3(0.0f, 3.0f, 0.0f), new Vector3(0.2f, 1.0f, 0.35f));
            AddLine(axisVertices, ref axisVertexCount, Vector3.Zero, new Vector3(0.0f, 0.0f, 3.0f), new Vector3(0.2f, 0.55f, 1.0f));

            Matrix4x4 worldWvp = _camera.View * _camera.Projection;
            gl.SetUniform(uniformLocation, worldWvp);
            fixed (float* vertexPtr = axisVertices)
            {
                gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(axisVertexCount * FloatStride * sizeof(float)), vertexPtr);
            }

            gl.DrawArrays(GLEnum.Lines, 0, (uint)axisVertexCount);
        }

        if (VisibleLightArrow)
        {
            Span<float> overlayVertices = stackalloc float[MaxVertexCount * FloatStride];
            int overlayVertexCount = 0;
            AddLightGizmo(overlayVertices, ref overlayVertexCount);

            gl.SetUniform(uniformLocation, Matrix4x4.Identity);
            fixed (float* vertexPtr = overlayVertices)
            {
                gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(overlayVertexCount * FloatStride * sizeof(float)), vertexPtr);
            }

            gl.DrawArrays(GLEnum.Lines, 0, (uint)overlayVertexCount);
        }

        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
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

    private void AddLightGizmo(Span<float> vertices, ref int vertexCount)
    {
        Vector3 lightDirection = _getLightDirection();
        if (lightDirection.LengthSquared() < 0.0001f)
        {
            lightDirection = new Vector3(0.0f, -1.0f, 0.0f);
        }

        lightDirection = Vector3.Normalize(lightDirection);
        Vector3 viewDirection = Vector3.Normalize(Vector3.TransformNormal(lightDirection, _camera.View));
        Vector2 projectedDirection = new(viewDirection.X, -viewDirection.Y);
        if (projectedDirection.LengthSquared() < 0.0001f)
        {
            projectedDirection = new Vector2(0.0f, -1.0f);
        }

        projectedDirection = Vector2.Normalize(projectedDirection);

        Vector2 center = new(0.78f, 0.72f);
        float outerRadius = 0.104f;
        float innerRadius = 0.078f;
        Vector3 ringColor = new(0.92f, 0.94f, 0.98f);
        Vector3 centerColor = new(0.82f, 0.84f, 0.88f);
        Vector3 arrowColor = new(1.0f, 0.86f, 0.22f);

        AddCircle(vertices, ref vertexCount, center, outerRadius, 40, ringColor);
        AddCircle(vertices, ref vertexCount, center, innerRadius, 32, new Vector3(0.55f, 0.58f, 0.64f));
        AddCrosshair(vertices, ref vertexCount, center, outerRadius + 0.008f, 0.014f, centerColor);
        AddDiamond(vertices, ref vertexCount, center, 0.010f, centerColor);

        AddArrow2D(vertices, ref vertexCount, center, center + (new Vector2(0.72f, 0.34f) * 0.070f), 0.019f, 0.015f, new Vector3(1.0f, 0.25f, 0.25f));
        AddArrow2D(vertices, ref vertexCount, center, center + (new Vector2(0.0f, -1.0f) * 0.076f), 0.019f, 0.015f, new Vector3(0.2f, 1.0f, 0.35f));
        AddArrow2D(vertices, ref vertexCount, center, center + (new Vector2(-0.62f, 0.62f) * 0.072f), 0.019f, 0.015f, new Vector3(0.2f, 0.55f, 1.0f));

        Vector2 start = center + (projectedDirection * 0.016f);
        Vector2 end = center + (projectedDirection * 0.088f);
        AddArrow2D(vertices, ref vertexCount, start, end, 0.028f, 0.020f, arrowColor);

        Vector2 arrowNormal = new(-projectedDirection.Y, projectedDirection.X);
        Vector2 rayBase = end - (projectedDirection * 0.018f);
        AddLine(vertices, ref vertexCount, ToClip(rayBase + (arrowNormal * 0.015f)), ToClip(rayBase + (arrowNormal * 0.029f)), arrowColor);
        AddLine(vertices, ref vertexCount, ToClip(rayBase - (arrowNormal * 0.015f)), ToClip(rayBase - (arrowNormal * 0.029f)), arrowColor);
        AddLine(vertices, ref vertexCount, ToClip(rayBase - (projectedDirection * 0.012f)), ToClip(rayBase - (projectedDirection * 0.028f)), arrowColor);
    }

    private static Vector3 ToClip(Vector2 normalizedViewportPosition)
    {
        return new Vector3(normalizedViewportPosition.X, normalizedViewportPosition.Y, 0.0f);
    }

    private static void AddCircle(Span<float> vertices, ref int vertexCount, Vector2 center, float radius, int segments, Vector3 color)
    {
        float step = MathF.Tau / segments;
        for (int i = 0; i < segments; i++)
        {
            float angle0 = i * step;
            float angle1 = (i + 1) * step;
            Vector2 p0 = center + new Vector2(MathF.Cos(angle0), MathF.Sin(angle0)) * radius;
            Vector2 p1 = center + new Vector2(MathF.Cos(angle1), MathF.Sin(angle1)) * radius;
            AddLine(vertices, ref vertexCount, ToClip(p0), ToClip(p1), color);
        }
    }

    private static void AddArrow2D(Span<float> vertices, ref int vertexCount, Vector2 start, Vector2 end, float headLength, float headWidth, Vector3 color)
    {
        Vector2 direction = end - start;
        if (direction.LengthSquared() < 0.000001f)
        {
            return;
        }

        direction = Vector2.Normalize(direction);
        Vector2 normal = new(-direction.Y, direction.X);
        Vector2 headBase = end - (direction * headLength);
        Vector2 left = headBase + (normal * headWidth);
        Vector2 right = headBase - (normal * headWidth);

        AddLine(vertices, ref vertexCount, ToClip(start), ToClip(end), color);
        AddLine(vertices, ref vertexCount, ToClip(end), ToClip(left), color);
        AddLine(vertices, ref vertexCount, ToClip(end), ToClip(right), color);
        AddLine(vertices, ref vertexCount, ToClip(headBase + (normal * (headWidth * 0.55f))), ToClip(headBase - (normal * (headWidth * 0.55f))), color);
    }

    private static void AddCrosshair(Span<float> vertices, ref int vertexCount, Vector2 center, float radius, float tickLength, Vector3 color)
    {
        AddLine(vertices, ref vertexCount, ToClip(new Vector2(center.X - radius, center.Y)), ToClip(new Vector2(center.X - radius - tickLength, center.Y)), color);
        AddLine(vertices, ref vertexCount, ToClip(new Vector2(center.X + radius, center.Y)), ToClip(new Vector2(center.X + radius + tickLength, center.Y)), color);
        AddLine(vertices, ref vertexCount, ToClip(new Vector2(center.X, center.Y - radius)), ToClip(new Vector2(center.X, center.Y - radius - tickLength)), color);
        AddLine(vertices, ref vertexCount, ToClip(new Vector2(center.X, center.Y + radius)), ToClip(new Vector2(center.X, center.Y + radius + tickLength)), color);
    }

    private static void AddDiamond(Span<float> vertices, ref int vertexCount, Vector2 center, float radius, Vector3 color)
    {
        Vector2 top = center + new Vector2(0.0f, -radius);
        Vector2 right = center + new Vector2(radius, 0.0f);
        Vector2 bottom = center + new Vector2(0.0f, radius);
        Vector2 left = center + new Vector2(-radius, 0.0f);

        AddLine(vertices, ref vertexCount, ToClip(top), ToClip(right), color);
        AddLine(vertices, ref vertexCount, ToClip(right), ToClip(bottom), color);
        AddLine(vertices, ref vertexCount, ToClip(bottom), ToClip(left), color);
        AddLine(vertices, ref vertexCount, ToClip(left), ToClip(top), color);
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

