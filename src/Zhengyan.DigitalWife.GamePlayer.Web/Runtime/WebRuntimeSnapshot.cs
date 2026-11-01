namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

/// <summary>UI-facing diagnostics for the browser runtime.</summary>
public sealed record WebRuntimeSnapshot(
    bool IsRunning,
    long FrameCount,
    double ElapsedSeconds,
    double LastDeltaSeconds,
    int CanvasWidth,
    int CanvasHeight,
    double DevicePixelRatio,
    string Status,
    string? LastError,
    bool RendererReady = false,
    string RendererName = "Not initialized",
    bool SceneReady = false,
    string SceneName = "Not loaded",
    int SceneEntityCount = 0,
    int PmxEntityCount = 0,
    bool PmxMeshReady = false,
    string PmxModelName = "Not loaded",
    int PmxVertexCount = 0,
    int PmxIndexCount = 0,
    float LoadingProgress = 0.0f,
    string LoadingStage = "idle",
    string LoadingMessage = "Waiting",
    string InputMode = "Pointer + keyboard",
    int ScriptDiagnosticCount = 0,
    string PhysicsBackend = "disabled")
{
    public static WebRuntimeSnapshot Initial { get; } = new(
        IsRunning: false,
        FrameCount: 0,
        ElapsedSeconds: 0.0,
        LastDeltaSeconds: 0.0,
        CanvasWidth: 0,
        CanvasHeight: 0,
        DevicePixelRatio: 1.0,
        Status: "Waiting for Canvas initialization",
        LastError: null);
}
