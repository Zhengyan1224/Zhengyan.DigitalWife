namespace Zhengyan.DigitalWife.GameEditor;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--dw-opencl-probe", StringComparison.OrdinalIgnoreCase)))
        {
            return Zhengyan.DigitalWife.Mmd.Kernel.ProbeCurrentProcessUnsafe() ? 0 : 2;
        }

        Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend backend = ReadGraphicsBackend(args)
            ?? EditorGraphicsSettingsStore.Load();
        using GameEditorGame game = new(backend);
        game.Run();
        return 0;
    }

    private static Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend? ReadGraphicsBackend(string[] args)
    {
        const string optionName = "--graphics-backend";
        for (int i = 0; i < args.Length; i++)
        {
            string value;
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for {optionName}.");
                }

                value = args[i + 1];
            }
            else if (args[i].StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = args[i][(optionName.Length + 1)..];
            }
            else
            {
                continue;
            }

            return Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackendNames.Parse(value);
        }

        return null;
    }
}
