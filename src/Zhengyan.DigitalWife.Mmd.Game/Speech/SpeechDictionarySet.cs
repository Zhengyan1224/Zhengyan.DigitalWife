namespace Zhengyan.DigitalWife.Mmd.Game.Speech;

public enum SpeechDictionaryLanguage
{
    Japanese = 0,
    Chinese = 1
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
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Dictionary directory is required.", nameof(directoryPath));
        }

        string fullDirectory = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"Dictionary directory not found: {fullDirectory}");
        }

        string resolvedKanaFileName = kanaFileName ?? language switch
        {
            SpeechDictionaryLanguage.Japanese => "kanadic.txt",
            SpeechDictionaryLanguage.Chinese => "zh_kanadic.txt",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };

        string kanaPath = Path.Combine(fullDirectory, resolvedKanaFileName);
        string vowelPath = Path.Combine(fullDirectory, vowelFileName);
        KanaDictionary kana = KanaDictionary.Load(kanaPath);
        if (language == SpeechDictionaryLanguage.Chinese)
        {
            kana.UnknownCjkFallbackKana = "あ";
        }

        return new SpeechDictionarySet(
            kana,
            VowelDictionary.Load(vowelPath));
    }
}
