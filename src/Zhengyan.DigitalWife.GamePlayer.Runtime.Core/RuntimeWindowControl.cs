namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed record RuntimeWindowCommand(string Operation, int Width = 0, int Height = 0, bool Fullscreen = false);

/// <summary>Window requests implemented by the hosting platform.</summary>
public sealed class RuntimeWindowControl
{
    public event Action<RuntimeWindowCommand>? Command;
    public int Width { get; private set; } = 1280;
    public int Height { get; private set; } = 720;
    public int ActualWidth { get; private set; }
    public int ActualHeight { get; private set; }
    public bool Fullscreen { get; private set; }

    public void Configure(int width, int height) { Width = width; Height = height; }
    public void ReportState(int width, int height, bool fullscreen)
    { ActualWidth = width; ActualHeight = height; Fullscreen = fullscreen; }
    public void SetSize(int width, int height)
    {
        Width = Math.Max(320, width); Height = Math.Max(240, height);
        Command?.Invoke(new("size", Width, Height));
    }
    public void SetFullscreen(bool fullscreen) => Command?.Invoke(new("fullscreen", Fullscreen: fullscreen));
}
