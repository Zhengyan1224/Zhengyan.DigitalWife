using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Zhengyan.DigitalWife.Mmd.Game.Speech;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

// Owned and updated on the render thread, never on Android's TTS binder thread.
internal sealed class AndroidSpeechLipSync : IDisposable
{
    private readonly PmxModelComponent _model;
    private readonly SpeechTransformUpdater _updater;
    private readonly TimeSpan _period;
    private bool _disposed;

    internal AndroidSpeechLipSync(PmxModelComponent model, SpeechDictionarySet dictionaries,
        GameProjectLipSyncSettings settings, string text, float speed)
    {
        _model = model;
        _period = TimeSpan.FromMilliseconds(Math.Clamp(180.0 / Math.Clamp(speed, 0.1f, 4f),
            Math.Max(1, settings.MinFramePeriodMilliseconds),
            Math.Max(Math.Max(1, settings.MinFramePeriodMilliseconds), settings.MaxFramePeriodMilliseconds)));
        _updater = model.CreateSpeechTransformUpdater(dictionaries.Kana, dictionaries.Vowel,
            settings.VowelMorphMap, settings.UseFallbackVowelOnNoMatch ? settings.NoMatchFallbackVowel : null);
        _updater.Start(text, _period, isLoop: true);
    }

    internal void SetRange(string text)
    {
        if (!_disposed) _updater.Start(text, _period, isLoop: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _updater.Stop(resetFace: true);
        _updater.UpdateTransform(_model, 0);
        // Force one more pose update even for a paused character, so the closed
        // mouth reaches the vertex buffer after removing the active updater.
        _model.RemoveTransformUpdater(_updater);
    }

    internal static SpeechDictionarySet LoadDictionaries(GameProjectLipSyncSettings settings,
        string projectDirectory, string engineDirectory)
    {
        string path = GameProjectPath.NormalizePathText(settings.DictionaryDirectory);
        string directory;
        if (path.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
            directory = GameProjectPath.ToAbsolute(projectDirectory, path[8..]);
        else if (path.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
            directory = Path.Combine(engineDirectory, path[4..]);
        else
        {
            string projectPath = GameProjectPath.ToAbsolute(projectDirectory, path);
            directory = !string.IsNullOrWhiteSpace(path) && Directory.Exists(projectPath)
                ? projectPath : Path.Combine(engineDirectory, "Resources", "SpeechLipSyncDictionaries");
        }
        SpeechDictionaryLanguage[] languages = settings.GetEffectiveDictionaryLanguages()
            .Select(value => Enum.TryParse(value, true, out SpeechDictionaryLanguage language) ? language : SpeechDictionaryLanguage.Chinese)
            .Distinct().ToArray();
        return SpeechDictionarySet.LoadFromDirectory(directory, languages);
    }
}
