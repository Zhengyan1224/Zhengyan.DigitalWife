using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidVulkanRuntimeDebugDrawComponent(AndroidVulkanGame owner) : DrawableGameComponent
{
    private ILineRenderer? _renderer;
    protected override void Initialize()
    {
        if (Game is null) throw new InvalidOperationException("Game is not attached.");
        _renderer = Game.GraphicsDevice.Renderer.Services.CreateLineRenderer();
    }
    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;
        IReadOnlyList<RuntimeDebugLine> lines = owner.Scene.Debug.Snapshot();
        if (_renderer is null || lines.Count == 0) return;
        float[] vertices = new float[lines.Count * 12]; int index = 0;
        foreach (RuntimeDebugLine line in lines)
        {
            Write(vertices, index++, line.Start, line.Color); Write(vertices, index++, line.End, line.Color);
        }
        _renderer.Draw(vertices, index, owner.Camera.View * owner.Camera.Projection);
    }
    public override void Dispose() { _renderer?.Dispose(); _renderer = null; base.Dispose(); }
    private static void Write(float[] data, int vertex, Vector3 position, Vector4 color)
    { int i = vertex * 6; data[i] = position.X; data[i + 1] = position.Y; data[i + 2] = position.Z; data[i + 3] = color.X; data[i + 4] = color.Y; data[i + 5] = color.Z; }
}
