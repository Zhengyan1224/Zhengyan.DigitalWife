namespace Zhengyan.DigitalWife.GameEditor;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--dw-opencl-probe", StringComparison.OrdinalIgnoreCase)))
        {
            return Zhengyan.DigitalWife.Mmd.Kernel.ProbeCurrentProcessUnsafe() ? 0 : 2;
        }

        Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend? commandLineBackend = ReadGraphicsBackend(args);
        Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend backend = commandLineBackend
            ?? EditorGraphicsSettingsStore.Load();
        if (!commandLineBackend.HasValue
            && backend == Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend.Vulkan
            && !Zhengyan.DigitalWife.Mmd.Game.Graphics.VulkanRenderer.IsSupported(out string vulkanReason))
        {
            Console.Error.WriteLine(
                $"[GameEditor] Saved Vulkan backend is unavailable: {vulkanReason} Falling back to OpenGL. " +
                "Install/update the Vulkan runtime and graphics driver before selecting Vulkan again.");
            backend = Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend.OpenGL;
            EditorGraphicsSettingsStore.Save(backend);
        }

        string? projectDirectory = ReadOptionValue(args, "--project");
        using GameEditorGame game = new(backend, projectDirectory);
        game.Run();
        return 0;
    }

    private static string? ReadOptionValue(string[] args, string optionName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length ? args[i + 1] : null;
            }

            string prefix = optionName + "=";
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return args[i][prefix.Length..];
            }
        }

        return null;
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
