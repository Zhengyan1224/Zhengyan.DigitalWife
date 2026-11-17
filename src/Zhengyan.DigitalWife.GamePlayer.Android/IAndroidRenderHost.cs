using Android.Views;
using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal interface IAndroidRenderHost : IDisposable
{
    GameProject? Project { get; }

    bool IsReady { get; }
    bool HasSceneFrame { get; }
    string? LoadingError { get; }

    float LoadingProgress { get; }

    string LoadingMessage { get; }

    void SetProject(GameProject? project, string? projectDirectory);

    void CreateSurface(Surface surface);

    void Resize(int width, int height);

    void Render(long frameTimeNanos, AndroidInputSnapshot input);
    Action<Zhengyan.DigitalWife.Mmd.Game.Graphics.IRenderer>? BeforePresent { get; set; }
    bool SuppressCameraInput { get; set; }
    ContextMenuSettings? ResolveContextMenu(float x, float y);
    void DispatchContextMenuOpened(ContextMenuSettings menu, float x, float y);

    bool TryGetBubblePosition(RuntimeDialogueBubble bubble, int canvasWidth, int canvasHeight, out Vector2 position);

    void Pause();

    void DestroySurface();

    void RequestSceneChange(string scenePath);

    bool RequestRenderTextureRefresh(string idOrName);

    void DispatchContextMenuItem(ContextMenuSettings menu, ContextMenuItemSettings item, float x, float y);
    void DispatchGuiControlChanged(GuiControlSettings control);
}
