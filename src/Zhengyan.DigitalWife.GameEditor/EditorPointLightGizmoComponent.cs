using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed class EditorPointLightGizmoComponent(GameEditorGame editorGame, OrbitCamera camera) : DrawableGameComponent
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
        RebuildGeometryIfNeeded();
        if (_vertexCount > 0)
        {
            _lineRenderer?.Draw(_vertices, _vertexCount, _camera.View * _camera.Projection);
        }
    }

    public override void Dispose()
    {
        _lineRenderer?.Dispose();
        _lineRenderer = null;
        base.Dispose();
    }

    private void RebuildGeometryIfNeeded()
    {
        if (_lastDebugDrawVersion == _editorGame.DebugDrawVersion)
        {
            return;
        }

        List<float> vertices = [];
        GameEntity? selected = _editorGame.SelectedEntity;
        foreach (GameEntity entity in _editorGame.Project.Scene.Entities)
        {
            if (!GameEditorGame.IsPointLightEntity(entity))
            {
                continue;
            }

            PointLightSettings light = entity.PointLight ??= new PointLightSettings();
            Vector3 position = entity.Transform.Position.ToVector3();
            Vector3 color = light.Enabled
                ? Vector3.Clamp(light.Color.ToVector3(), new Vector3(0.15f), Vector3.One)
                : new Vector3(0.32f);
            AddBulb(vertices, position, color);
            if (ReferenceEquals(entity, selected))
            {
                AddRange(vertices, position, MathF.Max(light.Range, 0.001f), color * 0.65f);
            }
        }

        _vertices = [.. vertices];
        _vertexCount = vertices.Count / FloatStride;
        _lastDebugDrawVersion = _editorGame.DebugDrawVersion;
    }

    private static void AddBulb(List<float> vertices, Vector3 center, Vector3 color)
    {
        const float radius = 0.18f;
        AddCircle(vertices, center, Vector3.UnitX, Vector3.UnitY, radius, color);
        AddCircle(vertices, center, Vector3.UnitX, Vector3.UnitZ, radius, color);
        AddCircle(vertices, center, Vector3.UnitY, Vector3.UnitZ, radius, color);

        for (int i = 0; i < 8; i++)
        {
            float angle = i * MathF.Tau / 8.0f;
            Vector3 direction = Vector3.Normalize(new Vector3(MathF.Cos(angle), 0.35f, MathF.Sin(angle)));
            AddLine(vertices, center + direction * 0.24f, center + direction * 0.36f, color);
        }

        AddLine(vertices, center + new Vector3(-0.09f, -0.20f, 0.0f), center + new Vector3(0.09f, -0.20f, 0.0f), color);
        AddLine(vertices, center + new Vector3(-0.07f, -0.25f, 0.0f), center + new Vector3(0.07f, -0.25f, 0.0f), color);
    }

    private static void AddRange(List<float> vertices, Vector3 center, float radius, Vector3 color)
    {
        AddCircle(vertices, center, Vector3.UnitX, Vector3.UnitY, radius, color);
        AddCircle(vertices, center, Vector3.UnitX, Vector3.UnitZ, radius, color);
        AddCircle(vertices, center, Vector3.UnitY, Vector3.UnitZ, radius, color);
    }

    private static void AddCircle(List<float> vertices, Vector3 center, Vector3 axisA, Vector3 axisB, float radius, Vector3 color)
    {
        for (int i = 0; i < Segments; i++)
        {
            float a0 = i * MathF.Tau / Segments;
            float a1 = (i + 1) * MathF.Tau / Segments;
            AddLine(
                vertices,
                center + (axisA * MathF.Cos(a0) + axisB * MathF.Sin(a0)) * radius,
                center + (axisA * MathF.Cos(a1) + axisB * MathF.Sin(a1)) * radius,
                color);
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
