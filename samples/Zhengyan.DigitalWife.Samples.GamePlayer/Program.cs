namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal static class Program
{
    public static int Main(string[] args)
    {
        string projectDirectory = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? Path.GetFullPath(args[0].Trim().Trim('"'))
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "GameEditorProjects", "DemoGame"));

        if (!Directory.Exists(projectDirectory))
        {
            Console.Error.WriteLine($"Project directory not found: {projectDirectory}");
            Console.Error.WriteLine("Usage: dotnet run --project samples/Zhengyan.DigitalWife.Samples.GamePlayer -- <project-directory>");
            return 1;
        }

        using GamePlayerGame game = new(projectDirectory);
        game.Run();
        return 0;
    }
}
