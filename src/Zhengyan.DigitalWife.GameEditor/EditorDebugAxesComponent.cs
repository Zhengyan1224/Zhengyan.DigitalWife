using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed class EditorDebugAxesComponent(OrbitCamera camera) : DrawableGameComponent
{
    private const int FloatStride = 6;
    private const int MaxVertexCount = 96;

    private readonly OrbitCamera _camera = camera;

    private ILineRenderer? _lineRenderer;
    private float[] _vertices = [];
    private int _vertexCount;
    private bool _geometryDirty = true;
    private float _axisLength = 3.0f;
    private float _negativeAxisLength = 1.0f;

    public float AxisLength
    {
        get => _axisLength;
        set
        {
            if (Math.Abs(_axisLength - value) <= 0.0001f)
            {
                return;
            }

            _axisLength = value;
            _geometryDirty = true;
        }
    }

    public float NegativeAxisLength
    {
        get => _negativeAxisLength;
        set
        {
            if (Math.Abs(_negativeAxisLength - value) <= 0.0001f)
            {
                return;
            }

            _negativeAxisLength = value;
            _geometryDirty = true;
        }
    }

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        _lineRenderer = Game.GraphicsDevice.Renderer.Services
            .CreateLineRenderer(MaxVertexCount * FloatStride * sizeof(float));
    }

    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        if (Game is null)
        {
            return;
        }

        if (_geometryDirty)
        {
            Span<float> vertices = stackalloc float[MaxVertexCount * FloatStride];
            int vertexCount = 0;
            AddAxes(vertices, ref vertexCount);
            AddOriginMarker(vertices, ref vertexCount);
            _vertices = vertices[..(vertexCount * FloatStride)].ToArray();
            _vertexCount = vertexCount;
            _geometryDirty = false;
        }
        _lineRenderer?.Draw(_vertices, _vertexCount, _camera.View * _camera.Projection);
    }

    public override void Dispose()
    {
        _lineRenderer?.Dispose();
        _lineRenderer = null;

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

}
