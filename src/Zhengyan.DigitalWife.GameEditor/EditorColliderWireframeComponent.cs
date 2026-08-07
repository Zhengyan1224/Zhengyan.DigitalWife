using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed class EditorColliderWireframeComponent(GameEditorGame editorGame, OrbitCamera camera) : DrawableGameComponent
{
    private const int FloatStride = 6;
    private const int Segments = 24;

    private readonly GameEditorGame _editorGame = editorGame;
    private readonly OrbitCamera _camera = camera;
    private ILineRenderer? _lineRenderer;
    private float[] _vertices = [];
    private int _vertexCount;
    private int _lastDebugDrawVersion = -1;

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        _lineRenderer = Game.GraphicsDevice.Renderer.Services.CreateLineRenderer();
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;
        if (Game is null)
        {
            return;
        }

        RebuildGeometryIfNeeded();
        if (_vertexCount == 0)
        {
            return;
        }

        _lineRenderer?.Draw(_vertices, _vertexCount, _camera.View * _camera.Projection);
    }

    public override void Dispose()
    {
        _lineRenderer?.Dispose();
        _lineRenderer = null;

        base.Dispose();
    }

    private void RebuildGeometryIfNeeded()
    {
        if (Game is null)
        {
            return;
        }

        bool forceRebuild = _editorGame.HasBoneBoundColliders();
        if (!forceRebuild && _lastDebugDrawVersion == _editorGame.DebugDrawVersion)
        {
            return;
        }

        List<float> vertices = [];
        foreach (GameEntity entity in _editorGame.Project.Scene.Entities)
        {
            foreach (ColliderSettings collider in GameEntityCollision.GetEffectiveColliders(entity))
            {
                if (!collider.Enabled)
                {
                    continue;
                }

                if (!_editorGame.TryCreateColliderGeometry(entity, collider, out ColliderGeometry geometry))
                {
                    continue;
                }

                if (geometry.Shape == "box")
                {
                    AddBox(vertices, geometry.Box, new Vector3(0.26f, 0.72f, 1.0f));
                }
                else
                {
                    AddCapsule(vertices, geometry.Capsule, new Vector3(1.0f, 0.84f, 0.16f));
                }
            }
        }

        _vertexCount = vertices.Count / FloatStride;
        _vertices = [.. vertices];
        _lastDebugDrawVersion = _editorGame.DebugDrawVersion;
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

}
