using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed class EditorSpotLightGizmoComponent(GameEditorGame editorGame, OrbitCamera camera) : DrawableGameComponent
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
        _lineRenderer = Game?.GraphicsDevice.Renderer.Services.CreateLineRenderer()
            ?? throw new InvalidOperationException("Game is not attached.");
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
            if (!GameEditorGame.IsSpotLightEntity(entity))
            {
                continue;
            }

            SpotLightSettings light = entity.SpotLight ??= new SpotLightSettings();
            Vector3 position = entity.Transform.Position.ToVector3();
            Vector3 direction = SpotLightTransform.GetDirectionFromEulerDegrees(entity.Transform.RotationDegrees.ToVector3());
            Vector3 color = light.Enabled
                ? Vector3.Clamp(light.Color.ToVector3(), new Vector3(0.15f), Vector3.One)
                : new Vector3(0.32f);
            bool isSelected = ReferenceEquals(entity, selected);
            float displayLength = isSelected ? MathF.Max(light.Range, 0.1f) : MathF.Min(MathF.Max(light.Range, 0.1f), 1.5f);
            AddLampHead(vertices, position, direction, color);
            AddCone(vertices, position, direction, displayLength, light.OuterConeAngleDegrees, color * 0.8f);
            if (isSelected)
            {
                AddCone(vertices, position, direction, displayLength, light.InnerConeAngleDegrees, color * 0.55f);
            }
        }

        _vertices = [.. vertices];
        _vertexCount = vertices.Count / FloatStride;
        _lastDebugDrawVersion = _editorGame.DebugDrawVersion;
    }

    private static void AddLampHead(List<float> vertices, Vector3 center, Vector3 direction, Vector3 color)
    {
        CreateBasis(direction, out Vector3 right, out Vector3 up);
        Vector3 back = center - direction * 0.16f;
        AddCircle(vertices, back, right, up, 0.16f, color);
        AddCircle(vertices, center, right, up, 0.11f, color);
        AddLine(vertices, back + right * 0.16f, center + right * 0.11f, color);
        AddLine(vertices, back - right * 0.16f, center - right * 0.11f, color);
        AddLine(vertices, back + up * 0.16f, center + up * 0.11f, color);
        AddLine(vertices, back - up * 0.16f, center - up * 0.11f, color);
        AddLine(vertices, center, center + direction * 0.35f, color);
    }

    private static void AddCone(List<float> vertices, Vector3 origin, Vector3 direction, float length, float angleDegrees, Vector3 color)
    {
        CreateBasis(direction, out Vector3 right, out Vector3 up);
        float angle = Math.Clamp(angleDegrees, 0.1f, 89.0f) * MathF.PI / 180.0f;
        Vector3 center = origin + direction * length;
        float radius = MathF.Tan(angle) * length;
        AddCircle(vertices, center, right, up, radius, color);
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.PI * 0.5f;
            AddLine(vertices, origin, center + (right * MathF.Cos(a) + up * MathF.Sin(a)) * radius, color);
        }
    }

    private static void CreateBasis(Vector3 direction, out Vector3 right, out Vector3 up)
    {
        Vector3 reference = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
        right = Vector3.Normalize(Vector3.Cross(reference, direction));
        up = Vector3.Normalize(Vector3.Cross(direction, right));
    }

    private static void AddCircle(List<float> vertices, Vector3 center, Vector3 axisA, Vector3 axisB, float radius, Vector3 color)
    {
        for (int i = 0; i < Segments; i++)
        {
            float a0 = i * MathF.Tau / Segments;
            float a1 = (i + 1) * MathF.Tau / Segments;
            AddLine(vertices,
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
