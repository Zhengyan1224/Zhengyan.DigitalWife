using System.Text;

namespace Zhengyan.DigitalWife.Mmd.Game.Speech;

public sealed class VowelDictionary
{
    private readonly Dictionary<string, string> _vowelDictionary = new(StringComparer.Ordinal);

    public int Count => _vowelDictionary.Count;

    public string this[string key]
    {
        get => _vowelDictionary[key];
        set => _vowelDictionary[key] = value;
    }

    public bool ContainsKey(string key)
    {
        return _vowelDictionary.ContainsKey(key);
    }

    public bool ContainsValue(string value)
    {
        return _vowelDictionary.ContainsValue(value);
    }

    public void LoadFromDicFile(string filePath)
    {
        _vowelDictionary.Clear();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Vowel dictionary file not found: {filePath}", filePath);
        }

        foreach (string line in File.ReadLines(filePath, Encoding.UTF8))
        {
            string[] splitResult = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (splitResult.Length < 2)
            {
                continue;
            }

            _vowelDictionary[splitResult[0]] = splitResult[1];
        }
    }

    public void LoadFormDicFile(string filePath)
    {
        LoadFromDicFile(filePath);
    }

    public string GetVowel(string key)
    {
        return _vowelDictionary.TryGetValue(key, out string? vowel) ? vowel : key;
    }

    public string GetVowel(char key)
    {
        return GetVowel(new string(key, 1));
    }

    public static VowelDictionary Load(string filePath)
    {
        VowelDictionary dictionary = new();
        dictionary.LoadFromDicFile(filePath);
        return dictionary;
    }
}

