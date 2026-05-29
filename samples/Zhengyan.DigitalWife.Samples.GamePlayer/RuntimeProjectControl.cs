namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeProjectControl
{
    private readonly GamePlayerGame _game;

    internal RuntimeProjectControl(GamePlayerGame game)
    {
        _game = game;
    }

    public bool UseOpenCL
    {
        get => _game.Project.Runtime.UseOpenCL;
        set => SetUseOpenCL(value);
    }

    public bool IsUsingOpenCL => _game.IsUsingOpenClRuntime;

    public string ComputeBackend => _game.CurrentComputeBackend;

    public void SetUseOpenCL(bool useOpenCl)
    {
        _game.Project.Runtime.UseOpenCL = useOpenCl;
        _game.ApplyRuntimeSettings();
    }
}
