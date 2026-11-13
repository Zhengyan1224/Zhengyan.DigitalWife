namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed record RuntimeWindowCommand(string Operation, int Width = 0, int Height = 0, bool Fullscreen = false)
{
    public string Text { get; init; } = string.Empty;
    public bool Flag { get; init; }
}

/// <summary>Window requests implemented by the hosting platform.</summary>
public sealed partial class RuntimeWindowControl
{
    public event Action<RuntimeWindowCommand>? Command;
    public int Width { get; private set; } = 1280;
    public int Height { get; private set; } = 720;
    public int ActualWidth { get; private set; }
    public int ActualHeight { get; private set; }
    private bool _fullscreen, _visible = true, _resizable = true;
    private string _title = string.Empty, _timingMode = Zhengyan.DigitalWife.GameProjects.GameProjectTiming.TimeSynchronized;
    public bool Fullscreen { get => _fullscreen; set => SetFullscreen(value); }
    public bool Visible { get => _visible; set => SetVisible(value); }
    public bool Resizable { get => _resizable; set => SetResizable(value); }
    public string Title { get => _title; set => SetTitle(value); }
    public string TimingMode { get => _timingMode; set => SetTimingMode(value); }

    public void Configure(int width, int height) { Width = width; Height = height; }
    public void Configure(Zhengyan.DigitalWife.GameProjects.GameWindowSettings settings)
    {
        Configure(settings.Width, settings.Height);
        _title = settings.Title; _resizable = settings.Resizable;
        _timingMode = Zhengyan.DigitalWife.GameProjects.GameProjectTiming.NormalizeMode(settings.TimingMode);
    }
    public void Configure(RuntimeWindowControl previous)
    {
        Configure(previous.Width, previous.Height);
        _title = previous._title; _resizable = previous._resizable;
        _timingMode = previous._timingMode; _visible = previous._visible;
    }
    public void ReportState(int width, int height, bool fullscreen)
    { ActualWidth = width; ActualHeight = height; _fullscreen = fullscreen; }
    public void SetSize(int width, int height)
    {
        Width = Math.Max(320, width); Height = Math.Max(240, height);
        Command?.Invoke(new("size", Width, Height));
    }
    public void SetFullscreen(bool fullscreen) => Command?.Invoke(new("fullscreen", Fullscreen: fullscreen));
    public void Exit() => Command?.Invoke(new("exit"));
    public void Quit() => Exit();
    public void SetTitle(string title) { _title = title ?? string.Empty; Command?.Invoke(new("title") { Text = _title }); }
    public void SetVisible(bool visible) { _visible = visible; Command?.Invoke(new("visible") { Flag = visible }); }
    public void ToggleVisible() => SetVisible(!Visible);
    public void SetResizable(bool resizable) { _resizable = resizable; Command?.Invoke(new("resizable") { Flag = resizable }); }
    public void SetTimingMode(string timingMode)
    {
        _timingMode = Zhengyan.DigitalWife.GameProjects.GameProjectTiming.NormalizeMode(timingMode);
        Command?.Invoke(new("timing") { Text = _timingMode });
    }
}
