using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed partial class RuntimeSceneNavigation
{
    public IReadOnlyList<RuntimeMeshTriangle> Triangles => _nodes.Select(node => node.Triangle).ToArray();

    public void DrawDebug(RuntimeDebug debug, Vector4? color = null, float durationSeconds = .1f)
    {
        foreach (RuntimeMeshTriangle triangle in Triangles)
        {
            debug.DrawLine(triangle.A, triangle.B, color, durationSeconds);
            debug.DrawLine(triangle.B, triangle.C, color, durationSeconds);
            debug.DrawLine(triangle.C, triangle.A, color, durationSeconds);
        }
    }
}
