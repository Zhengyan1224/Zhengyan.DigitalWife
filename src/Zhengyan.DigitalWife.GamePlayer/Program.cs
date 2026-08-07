namespace Zhengyan.DigitalWife.GamePlayer;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--dw-opencl-probe", StringComparison.OrdinalIgnoreCase)))
        {
            return Zhengyan.DigitalWife.Mmd.Kernel.ProbeCurrentProcessUnsafe() ? 0 : 2;
        }

        string? microphoneProbeOptionsPath = ReadOptionValue(args, "--dw-portaudio-microphone-probe");
        if (microphoneProbeOptionsPath is not null)
        {
            return PortAudioMicrophoneProbeProcess.RunProbeChild(microphoneProbeOptionsPath);
        }

        string? packagePassword = ReadOptionValue(args, "--package-password");
        string inputPath = ReadProjectInputPath(args);

        string projectInput = !string.IsNullOrWhiteSpace(inputPath)
            ? Path.GetFullPath(inputPath.Trim().Trim('"'))
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "GameEditorProjects", "DemoGame"));

        Zhengyan.DigitalWife.GameProjects.GameProjectPackageSession projectSession;
        try
        {
            projectSession = Zhengyan.DigitalWife.GameProjects.GameProjectPackage.OpenOrExtract(
                projectInput,
                new Zhengyan.DigitalWife.GameProjects.GameProjectPackageOpenOptions
                {
                    Password = packagePassword
                });
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException or InvalidDataException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            Console.Error.WriteLine($"Failed to load game project: {ex.Message}");
            Console.Error.WriteLine("Usage: dotnet run --project src/Zhengyan.DigitalWife.GamePlayer -- <project-directory-or-package> [--package-password <password>]");
            return 1;
        }

        Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend? graphicsBackend = ReadGraphicsBackend(args);
        using GamePlayerGame game = new(projectSession, graphicsBackend);
        game.Run();
        return 0;
    }

    private static string? ReadOptionValue(string[] args, string optionName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length ? args[i + 1] : string.Empty;
            }

            string prefix = optionName + "=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }

    private static Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackend? ReadGraphicsBackend(string[] args)
    {
        string? value = ReadOptionValue(args, "--graphics-backend");
        return value is null
            ? null
            : Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsBackendNames.Parse(value);
    }

    private static string ReadProjectInputPath(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (!arg.Contains('=') && IsOptionWithValue(arg) && i + 1 < args.Length)
                {
                    i++;
                }

                continue;
            }

            return arg;
        }

        return string.Empty;
    }

    private static bool IsOptionWithValue(string optionName)
    {
        return string.Equals(optionName, "--package-password", StringComparison.OrdinalIgnoreCase)
            || string.Equals(optionName, "--graphics-backend", StringComparison.OrdinalIgnoreCase);
    }
}
