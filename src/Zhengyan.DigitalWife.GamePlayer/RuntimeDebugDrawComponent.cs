using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class RuntimeDebugDrawComponent(OrbitCamera camera) : DrawableGameComponent
{
    private const int FloatStride = 6;

    private readonly OrbitCamera _camera = camera;
    private readonly List<DebugLine> _lines = [];
    private ILineRenderer? _lineRenderer;

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

        _lineRenderer = Game.GraphicsDevice.Renderer.Services.CreateLineRenderer();
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;
        if (Game is null || _lines.Count == 0)
        {
            return;
        }

        int vertexCount = _lines.Count * 2;
        float[] vertices = new float[vertexCount * FloatStride];
        int vertexIndex = 0;
        foreach (DebugLine line in _lines)
        {
            WriteVertex(vertices, vertexIndex++, line.Start, line.Color);
            WriteVertex(vertices, vertexIndex++, line.End, line.Color);
        }

        _lineRenderer?.Draw(vertices, vertexCount, _camera.View * _camera.Projection);
    }

    public override void Dispose()
    {
        _lineRenderer?.Dispose();
        _lineRenderer = null;

        base.Dispose();
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
    }

    private struct DebugLine(Vector3 start, Vector3 end, Vector4 color, float remainingSeconds)
    {
        public Vector3 Start = start;
        public Vector3 End = end;
        public Vector4 Color = color;
        public float RemainingSeconds = remainingSeconds;
    }

}
