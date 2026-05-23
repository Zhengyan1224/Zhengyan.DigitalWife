using System.Numerics;
using Silk.NET.OpenGLES;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Samples.GameEditor;

internal sealed unsafe class EditorColliderWireframeComponent(GameEditorGame editorGame, OrbitCamera camera) : DrawableGameComponent
{
    private const int FloatStride = 6;
    private const int Segments = 24;

    private readonly GameEditorGame _editorGame = editorGame;
    private readonly OrbitCamera _camera = camera;
    private uint _program;
    private uint _vao;
    private uint _vertexBuffer;
    private int _bufferVertexCapacity;

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
        EnsureBufferCapacity(512);

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

        List<float> vertices = [];
        foreach (GameEntity entity in _editorGame.Project.Scene.Entities)
        {
            Vector3 position = entity.Transform.Position.ToVector3();
            Quaternion rotation = ToQuaternion(entity.Transform.RotationDegrees.ToVector3());
            Vector3 scale = entity.Transform.Scale.ToVector3();
            foreach (ColliderSettings collider in GameEntityCollision.GetEffectiveColliders(entity))
            {
                if (!collider.Enabled)
                {
                    continue;
                }

                if (string.Equals(collider.Shape, "box", StringComparison.OrdinalIgnoreCase))
                {
                    BoxGeometry box = CollisionGeometry.CreateBox(collider, position, rotation, scale);
                    AddBox(vertices, box, new Vector3(0.26f, 0.72f, 1.0f));
                }
                else
                {
                    CapsuleGeometry capsule = CollisionGeometry.CreateCapsule(collider, position, rotation, scale);
                    AddCapsule(vertices, capsule, new Vector3(1.0f, 0.84f, 0.16f));
                }
            }
        }

        int vertexCount = vertices.Count / FloatStride;
        if (vertexCount == 0)
        {
            return;
        }

        EnsureBufferCapacity(vertexCount);
        float[] vertexArray = [.. vertices];

        GL gl = Game.GraphicsDevice.Gl;
        int uniformLocation = gl.GetUniformLocation(_program, "u_WVP");
        gl.Disable(GLEnum.CullFace);
        gl.Disable(GLEnum.DepthTest);
        gl.UseProgram(_program);
        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        gl.SetUniform(uniformLocation, _camera.View * _camera.Projection);

        fixed (float* vertexPtr = vertexArray)
        {
            gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)(vertexArray.Length * sizeof(float)), vertexPtr);
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

    private void EnsureBufferCapacity(int vertexCount)
    {
        if (Game is null || vertexCount <= _bufferVertexCapacity)
        {
            return;
        }

        _bufferVertexCapacity = Math.Max(vertexCount, Math.Max(_bufferVertexCapacity * 2, 512));
        GL gl = Game.GraphicsDevice.Gl;
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        gl.BufferData(GLEnum.ArrayBuffer, (uint)(_bufferVertexCapacity * FloatStride * sizeof(float)), null, GLEnum.DynamicDraw);
    }

    private static void AddCapsule(List<float> vertices, CapsuleGeometry capsule, Vector3 color)
    {
        Vector3 axis = capsule.End - capsule.Start;
        Vector3 axisDirection = axis.LengthSquared() <= 0.000001f ? Vector3.UnitY : Vector3.Normalize(axis);
        Vector3 tangentA = Vector3.Cross(axisDirection, Vector3.UnitY);
        if (tangentA.LengthSquared() <= 0.000001f)
        {
            tangentA = Vector3.Cross(axisDirection, Vector3.UnitX);
        }

        tangentA = Vector3.Normalize(tangentA);
        Vector3 tangentB = Vector3.Normalize(Vector3.Cross(axisDirection, tangentA));
        float radius = capsule.Radius;

        AddCircle(vertices, capsule.Start, tangentA, tangentB, radius, color);
        AddCircle(vertices, capsule.End, tangentA, tangentB, radius, color);

        for (int i = 0; i < 4; i++)
        {
            float angle = i * MathF.PI * 0.5f;
            Vector3 radial = (MathF.Cos(angle) * tangentA) + (MathF.Sin(angle) * tangentB);
            AddLine(vertices, capsule.Start + radial * radius, capsule.End + radial * radius, color);
        }

        AddHemisphereArcs(vertices, capsule.Start, -axisDirection, tangentA, tangentB, radius, color);
        AddHemisphereArcs(vertices, capsule.End, axisDirection, tangentA, tangentB, radius, color);
    }

    private static void AddBox(List<float> vertices, BoxGeometry box, Vector3 color)
    {
        Vector3 x = box.AxisX * box.HalfExtents.X;
        Vector3 y = box.AxisY * box.HalfExtents.Y;
        Vector3 z = box.AxisZ * box.HalfExtents.Z;
        Vector3[] corners =
        [
            box.Center - x - y - z,
            box.Center + x - y - z,
            box.Center + x + y - z,
            box.Center - x + y - z,
            box.Center - x - y + z,
            box.Center + x - y + z,
            box.Center + x + y + z,
            box.Center - x + y + z
        ];

        AddLine(vertices, corners[0], corners[1], color);
        AddLine(vertices, corners[1], corners[2], color);
        AddLine(vertices, corners[2], corners[3], color);
        AddLine(vertices, corners[3], corners[0], color);
        AddLine(vertices, corners[4], corners[5], color);
        AddLine(vertices, corners[5], corners[6], color);
        AddLine(vertices, corners[6], corners[7], color);
        AddLine(vertices, corners[7], corners[4], color);
        AddLine(vertices, corners[0], corners[4], color);
        AddLine(vertices, corners[1], corners[5], color);
        AddLine(vertices, corners[2], corners[6], color);
        AddLine(vertices, corners[3], corners[7], color);
    }

    private static void AddCircle(List<float> vertices, Vector3 center, Vector3 tangentA, Vector3 tangentB, float radius, Vector3 color)
    {
        for (int i = 0; i < Segments; i++)
        {
            float a0 = i * MathF.Tau / Segments;
            float a1 = (i + 1) * MathF.Tau / Segments;
            Vector3 p0 = center + ((MathF.Cos(a0) * tangentA) + (MathF.Sin(a0) * tangentB)) * radius;
            Vector3 p1 = center + ((MathF.Cos(a1) * tangentA) + (MathF.Sin(a1) * tangentB)) * radius;
            AddLine(vertices, p0, p1, color);
        }
    }

    private static void AddHemisphereArcs(List<float> vertices, Vector3 center, Vector3 axisDirection, Vector3 tangentA, Vector3 tangentB, float radius, Vector3 color)
    {
        AddHemisphereArc(vertices, center, axisDirection, tangentA, radius, color);
        AddHemisphereArc(vertices, center, axisDirection, -tangentA, radius, color);
        AddHemisphereArc(vertices, center, axisDirection, tangentB, radius, color);
        AddHemisphereArc(vertices, center, axisDirection, -tangentB, radius, color);
    }

    private static void AddHemisphereArc(List<float> vertices, Vector3 center, Vector3 axisDirection, Vector3 radial, float radius, Vector3 color)
    {
        for (int i = 0; i < Segments / 2; i++)
        {
            float a0 = i * MathF.PI * 0.5f / (Segments / 2);
            float a1 = (i + 1) * MathF.PI * 0.5f / (Segments / 2);
            Vector3 p0 = center + (MathF.Sin(a0) * radial + MathF.Cos(a0) * axisDirection) * radius;
            Vector3 p1 = center + (MathF.Sin(a1) * radial + MathF.Cos(a1) * axisDirection) * radius;
            AddLine(vertices, p0, p1, color);
        }
    }

    private static void AddLine(List<float> vertices, Vector3 start, Vector3 end, Vector3 color)
    {
        AddVertex(vertices, start, color);
        AddVertex(vertices, end, color);
    }

    private static void AddVertex(List<float> vertices, Vector3 position, Vector3 color)
    {
        vertices.Add(position.X);
        vertices.Add(position.Y);
        vertices.Add(position.Z);
        vertices.Add(color.X);
        vertices.Add(color.Y);
        vertices.Add(color.Z);
    }

    private static Quaternion ToQuaternion(Vector3 degrees)
    {
        Vector3 radians = degrees * (MathF.PI / 180.0f);
        return Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
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
