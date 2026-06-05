using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeWindowControl
{
    private readonly GamePlayerGame _game;
    private readonly GameWindowSettings _settings;

    internal RuntimeWindowControl(GamePlayerGame game, GameWindowSettings settings)
    {
        _game = game;
        _settings = settings;
    }

    public string Title
    {
        get => _settings.Title;
        set => SetTitle(value);
    }

    public int Width => _settings.Width;

    public int Height => _settings.Height;

    public int ActualWidth => _game.Window.Size.X;

    public int ActualHeight => _game.Window.Size.Y;

    public bool Fullscreen
    {
        get => _settings.Fullscreen;
        set => SetFullscreen(value);
    }

    public bool Resizable
    {
        get => _settings.Resizable;
        set => SetResizable(value);
    }

    public string TimingMode
    {
        get => _settings.TimingMode;
        set => SetTimingMode(value);
    }

    public void SetSize(int width, int height)
    {
        _settings.Width = Math.Max(320, width);
        _settings.Height = Math.Max(240, height);
        _game.SetWindowSize(_settings.Width, _settings.Height);
    }

    public void SetTitle(string title)
    {
        _game.SetConfiguredTitle(title);
    }

    public void SetFullscreen(bool fullscreen)
    {
        _settings.Fullscreen = _settings.DesktopSpriteMode ? false : fullscreen;
        _game.SetFullscreen(_settings.Fullscreen);
    }

    public void SetResizable(bool resizable)
    {
        _settings.Resizable = resizable;
        _game.SetResizable(resizable);
    }

    public void SetTimingMode(string timingMode)
    {
        _settings.TimingMode = NormalizeTimingMode(timingMode);
        _game.AnimationTimingMode = ToAnimationTimingMode(_settings.TimingMode);
    }

    internal static AnimationTimingMode ToAnimationTimingMode(string timingMode)
    {
        return NormalizeTimingMode(timingMode) == "frame_rate_dependent"
            ? AnimationTimingMode.FrameRateDependent
            : AnimationTimingMode.TimeSynchronized;
    }

    internal static string NormalizeTimingMode(string timingMode)
    {
        string normalized = (timingMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "frame_rate_dependent" or "framerate_dependent" or "frame"
            ? "frame_rate_dependent"
            : "time_synchronized";
    }
}
