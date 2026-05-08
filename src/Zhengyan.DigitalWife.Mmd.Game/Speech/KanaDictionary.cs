using System.Text;

namespace Zhengyan.DigitalWife.Mmd.Game.Speech;

public sealed class KanaDictionary
{
    private readonly Dictionary<string, string[]> _kanaDictionary = new(StringComparer.Ordinal);
    private int _maxKeyLength = 1;

    public string? UnknownCjkFallbackKana { get; set; }

    public int Count => _kanaDictionary.Count;

    public string[] this[string key]
    {
        get => _kanaDictionary[key];
        set => _kanaDictionary[key] = value;
    }

    public bool ContainsKey(string key)
    {
        return _kanaDictionary.ContainsKey(key);
    }

    public bool ContainsValue(string[] value)
    {
        return _kanaDictionary.ContainsValue(value);
    }

    public void LoadFromDicFile(string filePath)
    {
        _kanaDictionary.Clear();
        _maxKeyLength = 1;

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
                _kanaDictionary[simpleKey] = simpleKana;
                _maxKeyLength = Math.Max(_maxKeyLength, simpleKey.Length);
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

            _kanaDictionary[key] = kana;
            _maxKeyLength = Math.Max(_maxKeyLength, key.Length);
        }
    }

    public void LoadFormDicFile(string filePath)
    {
        LoadFromDicFile(filePath);
    }

    public string GetKana(string key)
    {
        if (!_kanaDictionary.TryGetValue(key, out string[]? value) || value.Length == 0)
        {
            return key;
        }

        return SelectPreferredKana(value);
    }

    public string ConvertText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new(text.Length * 2);

        int index = 0;
        while (index < text.Length)
        {
            bool matched = false;
            int maxLength = Math.Min(_maxKeyLength, text.Length - index);

            for (int length = maxLength; length > 0; length--)
            {
                string key = text.Substring(index, length);
                if (!_kanaDictionary.TryGetValue(key, out string[]? value) || value.Length == 0)
                {
                    continue;
                }

                builder.Append(SelectPreferredKana(value));
                index += length;
                matched = true;
                break;
            }

            if (!matched)
            {
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
        }

        return builder.ToString();
    }

    public string GetKana(char key)
    {
        return GetKana(new string(key, 1));
    }

    public static KanaDictionary Load(string filePath)
    {
        KanaDictionary dictionary = new();
        dictionary.LoadFromDicFile(filePath);
        return dictionary;
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

        parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
