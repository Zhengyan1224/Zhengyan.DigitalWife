using Android.Views;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal interface IAndroidRenderHost : IDisposable
{
    GameProject? Project { get; }

    void SetProject(GameProject? project, string? projectDirectory);

    void CreateSurface(Surface surface);

    void Resize(int width, int height);

    void Render(long frameTimeNanos, AndroidInputSnapshot input);

    void Pause();

    void DestroySurface();

    void RequestSceneChange(string scenePath);

    bool RequestRenderTextureRefresh(string idOrName);

    void DispatchContextMenuItem(ContextMenuSettings menu, ContextMenuItemSettings item, float x, float y);
}
