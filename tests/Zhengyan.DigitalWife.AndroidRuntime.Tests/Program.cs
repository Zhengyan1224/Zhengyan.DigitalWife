using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GameProjects;

if (args is ["--compile-project", string projectDirectory])
{
    GameProject project = GameProjectStore.Load(projectDirectory);
    HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
    foreach (string scenePath in project.Scenes.Append(project.EditorScene).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
    {
        GameProjectScene scene = GameProjectStore.LoadScene(projectDirectory, scenePath);
        foreach (ScriptBinding binding in scene.LoadingScripts.Concat(scene.Entities.SelectMany(e => e.Scripts)))
        {
            if (binding.Enabled && binding.Language is "csharp" or "cs" or "csx")
                paths.Add(GameProjectPath.ToAbsolute(projectDirectory, binding.Path));
        }
    }
    int failures = 0;
    foreach (string path in paths.Order())
    {
        try
        {
            byte[] assembly = AndroidScriptCompiler.Compile(path);
            Console.WriteLine($"PASS {Path.GetRelativePath(projectDirectory, path)} ({assembly.Length} bytes)");
        }
        catch (Exception ex)
        {
            failures++;
            Console.Error.WriteLine($"FAIL {Path.GetRelativePath(projectDirectory, path)}: {ex}");
        }
    }
    return failures == 0 ? 0 : 1;
}

return AndroidRuntimeRegressionTests.Run();
