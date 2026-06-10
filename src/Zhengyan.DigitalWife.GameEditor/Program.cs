namespace Zhengyan.DigitalWife.GameEditor;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--dw-opencl-probe", StringComparison.OrdinalIgnoreCase)))
        {
            return Zhengyan.DigitalWife.Mmd.Kernel.ProbeCurrentProcessUnsafe() ? 0 : 2;
        }

        using GameEditorGame game = new();
        game.Run();
        return 0;
    }
}
