using System.Text;

namespace Zhengyan.DigitalWife.Mmd.Game.Speech;

public sealed class KanaDictionary
{
    private readonly Dictionary<string, string[]> _kanaDictionary = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SpeechDictionaryLanguage, Dictionary<string, string[]>> _languageDictionaries = [];
    private readonly Dictionary<SpeechDictionaryLanguage, int> _languageMaxKeyLengths = [];
    private readonly List<SpeechDictionaryLanguage> _languageOrder = [];
    private int _maxKeyLength = 1;

    public string? UnknownCjkFallbackKana { get; set; }

    public IReadOnlyList<SpeechDictionaryLanguage> Languages => _languageOrder;

    public int Count => _kanaDictionary.Count;

    public string[] this[string key]
    {
        get => _kanaDictionary[NormalizeKey(key)];
        set => AddEntry(key, value, language: null);
    }

    public bool ContainsKey(string key)
    {
        return _kanaDictionary.ContainsKey(NormalizeKey(key));
    }

    public bool ContainsValue(string[] value)
    {
        return _kanaDictionary.ContainsValue(value);
    }

    public void LoadFromDicFile(string filePath)
    {
        Clear();
        LoadEntries(filePath, language: null);
    }

    public void AddFromDicFile(string filePath, SpeechDictionaryLanguage language)
    {
        LoadEntries(filePath, language);
    }

    public void LoadFormDicFile(string filePath)
    {
        LoadFromDicFile(filePath);
    }

    public string GetKana(string key)
    {
        if (!_kanaDictionary.TryGetValue(NormalizeKey(key), out string[]? value) || value.Length == 0)
        {
            return key;
        }

        return SelectPreferredKana(value);
    }

    public string GetKana(char key)
    {
        return GetKana(new string(key, 1));
    }

    public string ConvertText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string normalizedText = text.Normalize(NormalizationForm.FormKC);
        if (_languageOrder.Count == 0)
        {
            return ConvertUsingDictionary(
                normalizedText,
                _kanaDictionary,
                _maxKeyLength,
                useCjkFallback: true,
                unknownCjkFallbackKana: UnknownCjkFallbackKana);
        }

        return ConvertCompositeText(normalizedText);
    }

    public static KanaDictionary Load(string filePath)
    {
        KanaDictionary dictionary = new();
        dictionary.LoadFromDicFile(filePath);
        return dictionary;
    }

    private void Clear()
    {
        _kanaDictionary.Clear();
        _languageDictionaries.Clear();
        _languageMaxKeyLengths.Clear();
        _languageOrder.Clear();
        _maxKeyLength = 1;
    }

    private void LoadEntries(string filePath, SpeechDictionaryLanguage? language)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Kana dictionary file not found: {filePath}", filePath);
        }

        foreach (string line in File.ReadLines(filePath, Encoding.UTF8))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (TryParseSimpleLine(trimmed, out string simpleKey, out string[] simpleKana))
            {
                AddEntry(simpleKey, simpleKana, language);
                continue;
            }

            string[] splitResult = trimmed.Split(' ', StringSplitOptions.None);
            if (splitResult.Length < 8)
            {
                continue;
            }

            string key = splitResult[1].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            string[] kana = splitResult[7]
                .Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (kana.Length == 0)
            {
                continue;
            }

            AddEntry(key, kana, language);
        }
    }

    private void AddEntry(string key, string[] value, SpeechDictionaryLanguage? language)
    {
        string normalizedKey = NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey) || value.Length == 0)
        {
            return;
        }

        _kanaDictionary[normalizedKey] = value;
        _maxKeyLength = Math.Max(_maxKeyLength, normalizedKey.Length);

        if (!language.HasValue)
        {
            return;
        }

        SpeechDictionaryLanguage resolvedLanguage = language.Value;
        if (!_languageDictionaries.TryGetValue(resolvedLanguage, out Dictionary<string, string[]>? dictionary))
        {
            dictionary = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            _languageDictionaries[resolvedLanguage] = dictionary;
            _languageOrder.Add(resolvedLanguage);
        }

        dictionary[normalizedKey] = value;
        int existingMaxLength = _languageMaxKeyLengths.TryGetValue(resolvedLanguage, out int maxLength)
            ? maxLength
            : 1;
        _languageMaxKeyLengths[resolvedLanguage] = Math.Max(existingMaxLength, normalizedKey.Length);
    }

    private string ConvertCompositeText(string text)
    {
        StringBuilder builder = new(text.Length * 2);
        int index = 0;
        while (index < text.Length)
        {
            if (TryConvertNumberToken(text, ref index, builder))
            {
                continue;
            }

            if (TryConvertDictionaryMatch(text, ref index, builder))
            {
                continue;
            }

            if (TryConvertLatinToken(text, ref index, builder))
            {
                continue;
            }

            char current = text[index];
            if (!string.IsNullOrWhiteSpace(UnknownCjkFallbackKana) && IsCjkUnifiedIdeograph(current))
            {
                builder.Append(UnknownCjkFallbackKana);
            }
            else
            {
                builder.Append(current);
            }

            index++;
        }

        return builder.ToString();
    }

    private bool TryConvertDictionaryMatch(string text, ref int index, StringBuilder builder)
    {
        List<SpeechDictionaryLanguage> preferredLanguages = GetPreferredLanguages(text, index);
        if (preferredLanguages.Count == 0)
        {
            return false;
        }

        int remainingLength = text.Length - index;
        int maxLength = 1;
        foreach (SpeechDictionaryLanguage language in preferredLanguages)
        {
            if (_languageMaxKeyLengths.TryGetValue(language, out int languageMaxLength))
            {
                maxLength = Math.Max(maxLength, languageMaxLength);
            }
        }

        for (int length = Math.Min(maxLength, remainingLength); length > 0; length--)
        {
            string key = text.Substring(index, length);
            foreach (SpeechDictionaryLanguage language in preferredLanguages)
            {
                if (!_languageDictionaries.TryGetValue(language, out Dictionary<string, string[]>? dictionary)
                    || !dictionary.TryGetValue(key, out string[]? value)
                    || value.Length == 0)
                {
                    continue;
                }

                builder.Append(SelectPreferredKana(value));
                index += length;
                return true;
            }
        }

        return false;
    }

    private bool TryConvertLatinToken(string text, ref int index, StringBuilder builder)
    {
        if (!IsLanguageEnabled(SpeechDictionaryLanguage.English) || !IsLatinLetter(text[index]))
        {
            return false;
        }

        int end = index + 1;
        while (end < text.Length && IsLatinTokenChar(text[end]))
        {
            end++;
        }

        builder.Append(SpellLatinToken(text.AsSpan(index, end - index)));
        index = end;
        return true;
    }

    private bool TryConvertNumberToken(string text, ref int index, StringBuilder builder)
    {
        if (!TryGetDigitValue(text[index], out _))
        {
            return false;
        }

        int end = index + 1;
        while (end < text.Length && TryGetDigitValue(text[end], out _))
        {
            end++;
        }

        builder.Append(ConvertNumberToken(text.AsSpan(index, end - index), DetectNumberLanguage(text, index, end)));
        index = end;
        return true;
    }

    private SpeechDictionaryLanguage DetectNumberLanguage(string text, int start, int end)
    {
        if (IsLanguageEnabled(SpeechDictionaryLanguage.English) && HasNearbyCharacter(text, start, end, IsLatinLetter))
        {
            return SpeechDictionaryLanguage.English;
        }

        if (IsLanguageEnabled(SpeechDictionaryLanguage.Japanese)
            && (HasJapaneseKanaContext(text, start) || HasJapaneseKanaContext(text, end - 1)))
        {
            return SpeechDictionaryLanguage.Japanese;
        }

        if (IsLanguageEnabled(SpeechDictionaryLanguage.Chinese) && HasNearbyCharacter(text, start, end, IsCjkUnifiedIdeograph))
        {
            return SpeechDictionaryLanguage.Chinese;
        }

        if (IsLanguageEnabled(SpeechDictionaryLanguage.Chinese))
        {
            return SpeechDictionaryLanguage.Chinese;
        }

        if (IsLanguageEnabled(SpeechDictionaryLanguage.English))
        {
            return SpeechDictionaryLanguage.English;
        }

        if (IsLanguageEnabled(SpeechDictionaryLanguage.Japanese))
        {
            return SpeechDictionaryLanguage.Japanese;
        }

        return SpeechDictionaryLanguage.Japanese;
    }

    private List<SpeechDictionaryLanguage> GetPreferredLanguages(string text, int index)
    {
        List<SpeechDictionaryLanguage> languages = [];
        char current = text[index];

        if (IsLatinLetter(current))
        {
            AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.English);
            AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.Chinese);
            AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.Japanese);
            return languages;
        }

        if (IsHiraganaOrKatakana(current))
        {
            AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.Japanese);
            AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.Chinese);
            AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.English);
            return languages;
        }

        if (IsCjkUnifiedIdeograph(current))
        {
            if (HasJapaneseKanaContext(text, index))
            {
                AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.Japanese);
                AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.Chinese);
            }
            else
            {
                AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.Chinese);
                AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.Japanese);
            }

            AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.English);
            return languages;
        }

        AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.Chinese);
        AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.Japanese);
        AddLanguageIfEnabled(languages, SpeechDictionaryLanguage.English);
        return languages;
    }

    private void AddLanguageIfEnabled(List<SpeechDictionaryLanguage> languages, SpeechDictionaryLanguage language)
    {
        if (!IsLanguageEnabled(language) || languages.Contains(language))
        {
            return;
        }

        languages.Add(language);
    }

    private bool IsLanguageEnabled(SpeechDictionaryLanguage language)
    {
        return _languageDictionaries.ContainsKey(language);
    }

    private static string ConvertUsingDictionary(
        string text,
        Dictionary<string, string[]> dictionary,
        int maxKeyLength,
        bool useCjkFallback,
        string? unknownCjkFallbackKana = null)
    {
        StringBuilder builder = new(text.Length * 2);
        int index = 0;
        while (index < text.Length)
        {
            bool matched = false;
            int currentMaxLength = Math.Min(maxKeyLength, text.Length - index);
            for (int length = currentMaxLength; length > 0; length--)
            {
                string key = text.Substring(index, length);
                if (!dictionary.TryGetValue(key, out string[]? value) || value.Length == 0)
                {
                    continue;
                }

                builder.Append(SelectPreferredKana(value));
                index += length;
                matched = true;
                break;
            }

            if (matched)
            {
                continue;
            }

            char current = text[index];
            if (useCjkFallback && !string.IsNullOrWhiteSpace(unknownCjkFallbackKana) && IsCjkUnifiedIdeograph(current))
            {
                builder.Append(unknownCjkFallbackKana);
            }
            else
            {
                builder.Append(current);
            }

            index++;
        }

        return builder.ToString();
    }

    private static string SpellLatinToken(ReadOnlySpan<char> token)
    {
        StringBuilder builder = new(token.Length * 3);
        foreach (char current in token)
        {
            if (current is '\'' or '-')
            {
                continue;
            }

            if (TryGetLatinLetterKana(current, out string? kana))
            {
                builder.Append(kana);
            }
        }

        return builder.ToString();
    }

    private static string ConvertNumberToken(ReadOnlySpan<char> token, SpeechDictionaryLanguage language)
    {
        StringBuilder builder = new(token.Length * 3);
        foreach (char current in token)
        {
            if (!TryGetDigitValue(current, out int digit))
            {
                continue;
            }

            builder.Append(GetDigitKana(language, digit));
        }

        return builder.ToString();
    }

    private static bool TryGetLatinLetterKana(char value, out string? kana)
    {
        switch (char.ToUpperInvariant(value))
        {
            case 'A': kana = "エー"; return true;
            case 'B': kana = "ビー"; return true;
            case 'C': kana = "シー"; return true;
            case 'D': kana = "ディー"; return true;
            case 'E': kana = "イー"; return true;
            case 'F': kana = "エフ"; return true;
            case 'G': kana = "ジー"; return true;
            case 'H': kana = "エイチ"; return true;
            case 'I': kana = "アイ"; return true;
            case 'J': kana = "ジェー"; return true;
            case 'K': kana = "ケー"; return true;
            case 'L': kana = "エル"; return true;
            case 'M': kana = "エム"; return true;
            case 'N': kana = "エヌ"; return true;
            case 'O': kana = "オー"; return true;
            case 'P': kana = "ピー"; return true;
            case 'Q': kana = "キュー"; return true;
            case 'R': kana = "アール"; return true;
            case 'S': kana = "エス"; return true;
            case 'T': kana = "ティー"; return true;
            case 'U': kana = "ユー"; return true;
            case 'V': kana = "ヴィー"; return true;
            case 'W': kana = "ダブリュー"; return true;
            case 'X': kana = "エックス"; return true;
            case 'Y': kana = "ワイ"; return true;
            case 'Z': kana = "ゼット"; return true;
            default:
                kana = null;
                return false;
        }
    }

    private static string GetDigitKana(SpeechDictionaryLanguage language, int digit)
    {
        return language switch
        {
            SpeechDictionaryLanguage.Chinese => digit switch
            {
                0 => "リン",
                1 => "イー",
                2 => "アー",
                3 => "サン",
                4 => "スー",
                5 => "ウー",
                6 => "リウ",
                7 => "チー",
                8 => "バー",
                9 => "ジウ",
                _ => string.Empty
            },
            SpeechDictionaryLanguage.English => digit switch
            {
                0 => "ゼロ",
                1 => "ワン",
                2 => "トゥー",
                3 => "スリー",
                4 => "フォー",
                5 => "ファイブ",
                6 => "シックス",
                7 => "セブン",
                8 => "エイト",
                9 => "ナイン",
                _ => string.Empty
            },
            _ => digit switch
            {
                0 => "ゼロ",
                1 => "イチ",
                2 => "ニ",
                3 => "サン",
                4 => "ヨン",
                5 => "ゴ",
                6 => "ロク",
                7 => "ナナ",
                8 => "ハチ",
                9 => "キュウ",
                _ => string.Empty
            }
        };
    }

    private static bool TryGetDigitValue(char value, out int digit)
    {
        if (value is >= '0' and <= '9')
        {
            digit = value - '0';
            return true;
        }

        if (value is >= '０' and <= '９')
        {
            digit = value - '０';
            return true;
        }

        digit = default;
        return false;
    }

    private static bool HasNearbyCharacter(string text, int start, int end, Func<char, bool> predicate)
    {
        const int radius = 8;
        int searchStart = Math.Max(0, start - radius);
        int searchEnd = Math.Min(text.Length - 1, end - 1 + radius);
        for (int index = searchStart; index <= searchEnd; index++)
        {
            if (index >= start && index < end)
            {
                continue;
            }

            if (predicate(text[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasJapaneseKanaContext(string text, int index)
    {
        const int radius = 8;
        int start = Math.Max(0, index - radius);
        int end = Math.Min(text.Length - 1, index + radius);
        for (int current = start; current <= end; current++)
        {
            if (IsHiraganaOrKatakana(text[current]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLatinTokenChar(char value)
    {
        return IsLatinLetter(value) || value is '\'' or '-';
    }

    private static bool IsLatinLetter(char value)
    {
        return value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }

    private static bool IsHiraganaOrKatakana(char value)
    {
        return value is >= '\u3040' and <= '\u30FF' or >= '\uFF66' and <= '\uFF9D';
    }

    private static string NormalizeKey(string key)
    {
        return key.Normalize(NormalizationForm.FormKC);
    }

    private static string SelectPreferredKana(string[] value)
    {
        int mod = value.Length % 2;
        int index = (value.Length / 2) + mod - 1;
        return value[index];
    }

    private static bool TryParseSimpleLine(string line, out string key, out string[] kana)
    {
        string[] parts = line.Split('\t', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
        {
            key = parts[0];
            kana = parts[1].Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return kana.Length > 0;
        }

        parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
        {
            key = parts[0];
            kana = parts[1].Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return kana.Length > 0;
        }

        key = string.Empty;
        kana = [];
        return false;
    }

    private static bool IsCjkUnifiedIdeograph(char value)
    {
        return value is >= '\u4E00' and <= '\u9FFF';
    }
}
