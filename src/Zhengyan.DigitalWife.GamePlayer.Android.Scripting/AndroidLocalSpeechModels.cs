using Microsoft.Extensions.Logging;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Speech;
using Zhengyan.DigitalWife.Speech.SherpaOnnx;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidLocalSpeechModels(string projectDirectory, string engineDirectory, ILoggerFactory loggerFactory)
{
    internal ISpeechRecognizer CreateRecognizer(GameProjectAsrSettings settings)
    {
        if (!settings.Enabled) throw new InvalidOperationException("Project ASR is disabled. Enable Asr.Enabled.");
        if (!string.Equals(settings.Provider, "sherpa", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Android local ASR supports Sherpa only; provider '{settings.Provider}' is not supported. Select sherpa in GameEditor.");
        GameProjectSherpaAsrSettings model = settings.Sherpa;
        if (!Enum.TryParse(model.ModelKind, true, out SherpaOnnxRecognizerModelKind kind)
            || !Enum.IsDefined(kind) || kind == SherpaOnnxRecognizerModelKind.OfflineWhisper)
            throw new NotSupportedException($"Android local ASR model kind '{model.ModelKind}' is not supported; Whisper is not enabled.");
        Cpu(model.Provider, "Asr.Sherpa.Provider");
        string? encoder = FilePath(model.EncoderPath, "Asr.Sherpa.EncoderPath", kind is SherpaOnnxRecognizerModelKind.OnlineTransducer or SherpaOnnxRecognizerModelKind.OfflineTransducer);
        string? decoder = FilePath(model.DecoderPath, "Asr.Sherpa.DecoderPath", kind is SherpaOnnxRecognizerModelKind.OnlineTransducer or SherpaOnnxRecognizerModelKind.OfflineTransducer);
        string? joiner = FilePath(model.JoinerPath, "Asr.Sherpa.JoinerPath", kind is SherpaOnnxRecognizerModelKind.OnlineTransducer or SherpaOnnxRecognizerModelKind.OfflineTransducer);
        bool singleModel = kind is SherpaOnnxRecognizerModelKind.OfflineParaformer or SherpaOnnxRecognizerModelKind.OfflineZipformerCtc
            or SherpaOnnxRecognizerModelKind.OfflineWenetCtc or SherpaOnnxRecognizerModelKind.OnlineZipformer2Ctc;
        // Validate every native input before entering Sherpa, where bad paths
        // can abort the process rather than produce a managed exception.
        if (kind == SherpaOnnxRecognizerModelKind.OnlineParaformer)
        {
            encoder = FilePath(model.EncoderPath, "Asr.Sherpa.EncoderPath", true);
            decoder = FilePath(model.DecoderPath, "Asr.Sherpa.DecoderPath", true);
        }
        return new SherpaOnnxSpeechRecognizer(new()
        {
            ModelKind = kind, TokensPath = FilePath(model.TokensPath, "Asr.Sherpa.TokensPath", true)!,
            EncoderPath = encoder, DecoderPath = decoder, JoinerPath = joiner,
            ModelPath = FilePath(model.ModelPath, "Asr.Sherpa.ModelPath", singleModel),
            Language = model.Language, Provider = "cpu", SampleRate = Math.Max(8000, model.SampleRate),
            FeatureDim = Math.Max(1, model.FeatureDim), Threads = Math.Clamp(model.Threads, 1, 8), DecodingMethod = model.DecodingMethod
        }, loggerFactory.CreateLogger<SherpaOnnxSpeechRecognizer>());
    }

    internal ITextToSpeechSynthesizer CreateSynthesizer(GameProjectVoiceSettings settings)
    {
        if (!settings.Enabled) throw new InvalidOperationException("Project voice is disabled. Enable Voice.Enabled.");
        if (!string.Equals(settings.TtsProvider, "sherpa-onnx", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Android local TTS supports sherpa-onnx only; provider '{settings.TtsProvider}' is not supported.");
        if (!Enum.TryParse(settings.ModelKind, true, out SpeechSynthesisModelKind kind)
            || kind is not (SpeechSynthesisModelKind.Vits or SpeechSynthesisModelKind.Matcha))
            throw new NotSupportedException($"Android local TTS model kind '{settings.ModelKind}' is not supported. Use Vits or Matcha.");
        Cpu(settings.InferenceProvider, "Voice.InferenceProvider");
        string model = FilePath(settings.ModelPath, "Voice.ModelPath", true)!;
        string? data = DirectoryPath(settings.DataDirectory, "Voice.DataDirectory");
        string? dict = DirectoryPath(settings.DictDirectory, "Voice.DictDirectory");
        if (kind == SpeechSynthesisModelKind.Matcha)
        {
            data = new[] { data, dict, Path.GetDirectoryName(model) }
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Select(candidate => File.Exists(Path.Combine(candidate!, "phontab")) ? candidate
                    : Path.Combine(candidate!, "espeak-ng-data"))
                .FirstOrDefault(candidate => File.Exists(Path.Combine(candidate!, "phontab")))
                ?? throw new DirectoryNotFoundException("Voice.DataDirectory: Matcha requires a local espeak-ng-data directory containing phontab.");
        }
        string? vocoder = settings.VocoderPath;
        if (kind == SpeechSynthesisModelKind.Matcha && string.IsNullOrWhiteSpace(vocoder))
            vocoder = Path.Combine(Path.GetDirectoryName(model)!, "vocos-16khz-univ.onnx");
        return new SherpaOnnxTextToSpeechSynthesizer(new()
        {
            ModelKind = kind, ModelPath = model, TokensPath = FilePath(settings.TokensPath, "Voice.TokensPath", true)!,
            LexiconPath = FilePath(settings.LexiconPath, "Voice.LexiconPath"), DataDirectory = data, DictDirectory = dict,
            VocoderPath = FilePath(vocoder, "Voice.VocoderPath", kind == SpeechSynthesisModelKind.Matcha),
            RuleFars = RulePaths(settings.RuleFars, "Voice.RuleFars"), RuleFsts = RulePaths(settings.RuleFsts, "Voice.RuleFsts"),
            Provider = "cpu", Threads = Math.Clamp(settings.Threads, 1, 8)
        }, loggerFactory.CreateLogger<SherpaOnnxTextToSpeechSynthesizer>());
    }

    internal string? ResolvePath(string? value)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? sourceUri) && sourceUri.Scheme is "http" or "https")
            throw new NotSupportedException("Local speech model paths must reference packaged files, not network URLs.");
        string path = GameProjectPath.NormalizePathText(value);
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
            throw new NotSupportedException("Local speech model paths must reference packaged files, not network URLs.");
        if (path.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(Path.Combine(engineDirectory, path[4..].TrimStart('/', '\\')));
        if (path.StartsWith("project:", StringComparison.OrdinalIgnoreCase)) path = path[8..];
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        // Prefer packaged project resources; never interpret project paths
        // relative to the Mono application's managed assembly directory.
        string projectPath = Path.GetFullPath(Path.Combine(projectDirectory, path));
        if (File.Exists(projectPath) || Directory.Exists(projectPath)) return projectPath;
        string enginePath = Path.GetFullPath(Path.Combine(engineDirectory, path));
        return File.Exists(enginePath) || Directory.Exists(enginePath) ? enginePath : projectPath;
    }

    private string? FilePath(string? path, string setting, bool required = false)
    {
        string? resolved = ResolvePath(path);
        if (resolved is null && !required) return null;
        if (resolved is null || !File.Exists(resolved)) throw new FileNotFoundException($"{setting}: local speech model file is missing: '{resolved ?? "<empty>"}'. Include the model in the game project and re-export.", resolved);
        return resolved;
    }
    private string? DirectoryPath(string? path, string setting)
    {
        string? resolved = ResolvePath(path);
        if (resolved is not null && !Directory.Exists(resolved)) throw new DirectoryNotFoundException($"{setting}: local speech directory is missing: '{resolved}'.");
        return resolved;
    }
    private string? RulePaths(string? paths, string setting) => string.IsNullOrWhiteSpace(paths) ? null
        : string.Join(",", paths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(path => FilePath(path, setting, true)));
    private static void Cpu(string provider, string setting)
    {
        if (!string.Equals(provider, "cpu", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"{setting}: Android local speech currently supports CPU inference only; select cpu.");
    }
}
