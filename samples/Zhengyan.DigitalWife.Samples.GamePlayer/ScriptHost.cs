namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed class ScriptHost
{
    private readonly string _projectDirectory;

    public ScriptHost(string projectDirectory)
    {
        _projectDirectory = projectDirectory;
    }

    public IScriptInstance Load(string language, string scriptPath)
    {
        _ = _projectDirectory;

        return language.Trim().ToLowerInvariant() switch
        {
            "csharp" or "cs" or "csx" => new CSharpScriptInstance(scriptPath),
            "python" or "py" => new PythonScriptInstance(scriptPath),
            _ => throw new NotSupportedException($"Unsupported script language: {language}")
        };
    }
}
