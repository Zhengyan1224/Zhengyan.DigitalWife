using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;

namespace Zhengyan.DigitalWife.GamePlayer;

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

    public bool Visible
    {
        get => _game.IsWindowVisible;
        set => _game.SetWindowVisible(value);
    }

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

    public void SetVisible(bool visible)
    {
        _game.SetWindowVisible(visible);
    }

    public void ToggleVisible()
    {
        _game.ToggleWindowVisible();
    }

    public void Exit()
    {
        _game.RequestExit();
    }

    public void Quit()
    {
        Exit();
    }

    internal static AnimationTimingMode ToAnimationTimingMode(string timingMode)
    {
        return NormalizeTimingMode(timingMode) == GameProjectTiming.FrameRateDependent
            ? AnimationTimingMode.FrameRateDependent
            : AnimationTimingMode.TimeSynchronized;
    }

    internal static string NormalizeTimingMode(string timingMode)
    {
        return GameProjectTiming.NormalizeMode(timingMode);
    }
}
