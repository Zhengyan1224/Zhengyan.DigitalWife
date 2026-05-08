using Zhengyan.DigitalWife.Llm.OpenAI;
using Zhengyan.DigitalWife.Samples.AssistantConsole;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;
using Zhengyan.DigitalWife.Speech.WhisperNet;

namespace Zhengyan.DigitalWife.Samples.AssistantConsole;

internal sealed class SamplePathResolver
{
    private readonly string _baseDirectory;
    private readonly string? _repositoryRoot;

    public SamplePathResolver(string baseDirectory)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _repositoryRoot = FindRepositoryRoot(_baseDirectory);
    }

    public DemoOptions Resolve(DemoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new DemoOptions
        {
            RecognitionProvider = options.RecognitionProvider,
            Audio = options.Audio,
            Llm = ResolveLlm(options.Llm),
            Tts = ResolveTts(options.Tts),
            SherpaRecognizer = options.SherpaRecognizer is null ? null : ResolveSherpaRecognizer(options.SherpaRecognizer),
            WhisperRecognizer = options.WhisperRecognizer is null ? null : ResolveWhisperRecognizer(options.WhisperRecognizer),
            WakeWord = options.WakeWord is null ? null : ResolveWakeWord(options.WakeWord),
            Capture = options.Capture,
            SystemPrompt = options.SystemPrompt,
            LlmModel = options.LlmModel,
            CapturedAudioDirectory = ResolveOptionalDirectory(options.CapturedAudioDirectory)
        };
    }

    private OpenAiCompatibleLlmOptions ResolveLlm(OpenAiCompatibleLlmOptions options)
    {
        return new OpenAiCompatibleLlmOptions
        {
            BaseUrl = options.BaseUrl,
            ApiKey = options.ApiKey,
            ChatCompletionsPath = options.ChatCompletionsPath,
            Timeout = options.Timeout
        };
    }

    private SherpaOnnxRecognizerOptions ResolveSherpaRecognizer(SherpaOnnxRecognizerOptions options)
    {
        return new SherpaOnnxRecognizerOptions
        {
            ModelKind = options.ModelKind,
            TokensPath = ResolveRequiredFile(options.TokensPath),
            EncoderPath = ResolveOptionalFile(options.EncoderPath),
            DecoderPath = ResolveOptionalFile(options.DecoderPath),
            JoinerPath = ResolveOptionalFile(options.JoinerPath),
            ModelPath = ResolveOptionalFile(options.ModelPath),
            Language = options.Language,
            Provider = options.Provider,
            SampleRate = options.SampleRate,
            FeatureDim = options.FeatureDim,
            Threads = options.Threads,
            DecodingMethod = options.DecodingMethod,
            HotwordsScore = options.HotwordsScore,
            HotwordsFile = ResolveOptionalFile(options.HotwordsFile)
        };
    }

    private WhisperNetRecognizerOptions ResolveWhisperRecognizer(WhisperNetRecognizerOptions options)
    {
        return new WhisperNetRecognizerOptions
        {
            ModelPath = ResolveRequiredFile(options.ModelPath),
            Language = options.Language,
            TranslateToEnglish = options.TranslateToEnglish,
            UseGpu = options.UseGpu,
            Threads = options.Threads,
            SampleRate = options.SampleRate
        };
    }

    private SherpaOnnxTtsOptions ResolveTts(SherpaOnnxTtsOptions options)
    {
        return new SherpaOnnxTtsOptions
        {
            ModelPath = ResolveRequiredFile(options.ModelPath),
            TokensPath = ResolveRequiredFile(options.TokensPath),
            ModelKind = options.ModelKind,
            LexiconPath = ResolveOptionalFile(options.LexiconPath),
            DataDirectory = ResolveOptionalDirectory(options.DataDirectory),
            DictDirectory = ResolveOptionalDirectory(options.DictDirectory),
            Provider = options.Provider,
            Threads = options.Threads,
            NoiseScale = options.NoiseScale,
            NoiseScaleW = options.NoiseScaleW,
            LengthScale = options.LengthScale
        };
    }

    private SherpaOnnxWakeWordOptions ResolveWakeWord(SherpaOnnxWakeWordOptions options)
    {
        return new SherpaOnnxWakeWordOptions
        {
            TokensPath = ResolveRequiredFile(options.TokensPath),
            EncoderPath = ResolveRequiredFile(options.EncoderPath),
            DecoderPath = ResolveRequiredFile(options.DecoderPath),
            JoinerPath = ResolveRequiredFile(options.JoinerPath),
            KeywordsFile = ResolveRequiredFile(options.KeywordsFile),
            SampleRate = options.SampleRate,
            FeatureDim = options.FeatureDim,
            Threads = options.Threads,
            Provider = options.Provider,
            KeywordsThreshold = options.KeywordsThreshold,
            KeywordsScore = options.KeywordsScore,
            NumTrailingBlanks = options.NumTrailingBlanks,
            CaptureOptions = options.CaptureOptions
        };
    }

    private string ResolveRequiredFile(string path)
    {
        foreach (string candidate in EnumerateCandidates(path))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Required file was not found: {path}", path);
    }

    private string? ResolveOptionalFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string candidate in EnumerateCandidates(path))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? ResolveOptionalDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        if (IsRepositoryScopedPath(path) && !string.IsNullOrWhiteSpace(_repositoryRoot))
        {
            return Path.GetFullPath(Path.Combine(_repositoryRoot, path));
        }

        return Path.GetFullPath(Path.Combine(_baseDirectory, path));
    }

    private IEnumerable<string> EnumerateCandidates(string path)
    {
        if (Path.IsPathRooted(path))
        {
            yield return Path.GetFullPath(path);
            yield break;
        }

        if (IsRepositoryScopedPath(path) && !string.IsNullOrWhiteSpace(_repositoryRoot))
        {
            yield return Path.GetFullPath(Path.Combine(_repositoryRoot, path));
        }

        yield return Path.GetFullPath(Path.Combine(_baseDirectory, path));

        if (!IsRepositoryScopedPath(path) && !string.IsNullOrWhiteSpace(_repositoryRoot))
        {
            yield return Path.GetFullPath(Path.Combine(_repositoryRoot, path));
        }
    }

    private static bool IsRepositoryScopedPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "models"))
                && Directory.Exists(Path.Combine(directory.FullName, "samples"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
