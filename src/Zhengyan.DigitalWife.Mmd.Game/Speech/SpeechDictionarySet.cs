namespace Zhengyan.DigitalWife.Mmd.Game.Speech;

public enum SpeechDictionaryLanguage
{
    Japanese = 0,
    Chinese = 1,
    English = 2
}

public sealed class SpeechDictionarySet
{
    public SpeechDictionarySet(KanaDictionary kana, VowelDictionary vowel)
    {
        Kana = kana ?? throw new ArgumentNullException(nameof(kana));
        Vowel = vowel ?? throw new ArgumentNullException(nameof(vowel));
    }

    public KanaDictionary Kana { get; }

    public VowelDictionary Vowel { get; }

    public static SpeechDictionarySet LoadFromDirectory(
        string directoryPath,
        SpeechDictionaryLanguage language = SpeechDictionaryLanguage.Japanese,
        string? kanaFileName = null,
        string vowelFileName = "voweldic.txt")
    {
        if (!string.IsNullOrWhiteSpace(kanaFileName))
        {
            string fullDirectory = ValidateAndResolveDirectory(directoryPath);
            string kanaPath = Path.Combine(fullDirectory, kanaFileName);
            string vowelPath = Path.Combine(fullDirectory, vowelFileName);
            KanaDictionary kana = KanaDictionary.Load(kanaPath);
            if (language == SpeechDictionaryLanguage.Chinese)
            {
                kana.UnknownCjkFallbackKana = "あ";
            }

            return new SpeechDictionarySet(kana, VowelDictionary.Load(vowelPath));
        }

        return LoadFromDirectory(directoryPath, [language], vowelFileName);
    }

    public static SpeechDictionarySet LoadFromDirectory(
        string directoryPath,
        IEnumerable<SpeechDictionaryLanguage> languages,
        string vowelFileName = "voweldic.txt")
    {
        ArgumentNullException.ThrowIfNull(languages);

        string fullDirectory = ValidateAndResolveDirectory(directoryPath);
        List<SpeechDictionaryLanguage> resolvedLanguages = NormalizeLanguages(languages);
        string vowelPath = Path.Combine(fullDirectory, vowelFileName);

        KanaDictionary kana = new();
        bool includeChineseFallback = false;
        foreach (SpeechDictionaryLanguage language in resolvedLanguages)
        {
            kana.AddFromDicFile(
                Path.Combine(fullDirectory, ResolveKanaFileName(language)),
                language);

            if (language == SpeechDictionaryLanguage.Chinese)
            {
                includeChineseFallback = true;
            }
        }

        if (includeChineseFallback)
        {
            kana.UnknownCjkFallbackKana = "あ";
        }

        return new SpeechDictionarySet(kana, VowelDictionary.Load(vowelPath));
    }

    private static string ValidateAndResolveDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Dictionary directory is required.", nameof(directoryPath));
        }

        string fullDirectory = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"Dictionary directory not found: {fullDirectory}");
        }

        return fullDirectory;
    }

    private static List<SpeechDictionaryLanguage> NormalizeLanguages(IEnumerable<SpeechDictionaryLanguage> languages)
    {
        List<SpeechDictionaryLanguage> normalized = [];
        foreach (SpeechDictionaryLanguage language in languages)
        {
            if (!normalized.Contains(language))
            {
                normalized.Add(language);
            }
        }

        if (normalized.Count == 0)
        {
            normalized.Add(SpeechDictionaryLanguage.Japanese);
        }

        return normalized;
    }

    private static string ResolveKanaFileName(SpeechDictionaryLanguage language)
    {
        return language switch
        {
            SpeechDictionaryLanguage.Japanese => "kanadic.txt",
            SpeechDictionaryLanguage.Chinese => "zh_kanadic.txt",
            SpeechDictionaryLanguage.English => "en_kanadic.txt",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
    }
}
