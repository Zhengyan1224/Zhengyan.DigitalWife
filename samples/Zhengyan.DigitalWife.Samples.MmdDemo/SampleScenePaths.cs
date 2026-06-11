namespace Zhengyan.DigitalWife.Samples.MmdDemo;

internal sealed class SampleScenePaths
{
    private SampleScenePaths(string modelPath, string? motionPath, string? speechDictionaryDirectory)
    {
        ModelPath = modelPath;
        MotionPath = motionPath;
        SpeechDictionaryDirectory = speechDictionaryDirectory;
    }

    public string ModelPath { get; }

    public string? MotionPath { get; }

    public string? SpeechDictionaryDirectory { get; }

    public static SampleScenePaths Resolve(string[] args)
    {
        if (args.Length > 0)
        {
            string modelPath = Path.GetFullPath(args[0]);
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"PMX file not found: {modelPath}");
            }

            string? motionPath = null;
            if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
            {
                motionPath = Path.GetFullPath(args[1]);
                if (!File.Exists(motionPath))
                {
                    throw new FileNotFoundException($"VMD file not found: {motionPath}");
                }
            }

            string? dictionaryDir = null;
            if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
            {
                dictionaryDir = Path.GetFullPath(args[2]);
                if (!Directory.Exists(dictionaryDir))
                {
                    throw new DirectoryNotFoundException($"Speech dictionary directory not found: {dictionaryDir}");
                }
            }

            return new SampleScenePaths(modelPath, motionPath, dictionaryDir);
        }

        string defaultModel = ResolveRequiredBundledFile("GameData", "Character", "Body", "Body.pmx");
        string? defaultMotion = ResolveOptionalBundledFile("GameData", "Motion", "Basic", "basic_walk.vmd");
        string? defaultDictionaryDirectory = ResolveDefaultSpeechDictionaryDirectory();
        return new SampleScenePaths(defaultModel, defaultMotion, defaultDictionaryDirectory);
    }

    private static string ResolveRequiredBundledFile(params string[] segments)
    {
        string path = BuildBundledPath(segments);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Bundled sample file not found: {path}", path);
        }

        return path;
    }

    private static string? ResolveOptionalBundledFile(params string[] segments)
    {
        string path = BuildBundledPath(segments);
        return File.Exists(path) ? path : null;
    }

    private static string? ResolveDefaultSpeechDictionaryDirectory()
    {
        string bundledOutputDirectory = BuildBundledPath("Resources", "SpeechLipSyncDictionaries");
        return Directory.Exists(bundledOutputDirectory) ? bundledOutputDirectory : null;
    }

    private static string BuildBundledPath(params string[] segments)
    {
        string path = AppContext.BaseDirectory;
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }
}

