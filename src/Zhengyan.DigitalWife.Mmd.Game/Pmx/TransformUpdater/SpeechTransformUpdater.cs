using System.Text;
using Zhengyan.DigitalWife.Mmd;
using Zhengyan.DigitalWife.Mmd.Game.Speech;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;

public sealed class SpeechTransformUpdater : ITransformUpdater
{
    private readonly KanaDictionary _kanaDictionary;
    private readonly VowelDictionary _vowelDictionary;
    private readonly Dictionary<string, string> _vowelMorphMap;
    private readonly Dictionary<string, float> _vowelFaceTable;
    private readonly Dictionary<string, MMDMorph?> _cachedMorphs = new(StringComparer.Ordinal);
    private readonly List<string> _vowelSequence = [];

    private MMDModel? _cachedModel;
    private bool _isLoop;
    private bool _needsApplyFace;
    private double _timePointMilliseconds;
    private double _framePeriodMilliseconds = 240.0;

    public SpeechTransformUpdater(
        KanaDictionary kanaDictionary,
        VowelDictionary vowelDictionary,
        IReadOnlyDictionary<string, string>? vowelMorphMap = null)
    {
        _kanaDictionary = kanaDictionary ?? throw new ArgumentNullException(nameof(kanaDictionary));
        _vowelDictionary = vowelDictionary ?? throw new ArgumentNullException(nameof(vowelDictionary));
        _vowelMorphMap = CreateVowelMorphMap(vowelMorphMap);
        _vowelFaceTable = CreateFaceTable(_vowelMorphMap.Keys);
    }

    public TransformUpdaterStage Stage => TransformUpdaterStage.PreAnimation;

    public bool Enabled { get; set; } = true;

    public bool IsPlaying { get; private set; }

    public bool IsLoop => _isLoop;

    public TimeSpan FramePeriod
    {
        get => TimeSpan.FromMilliseconds(_framePeriodMilliseconds);
        set
        {
            double milliseconds = value.TotalMilliseconds;
            if (milliseconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Frame period must be greater than zero.");
            }

            _framePeriodMilliseconds = milliseconds;
        }
    }

    public IReadOnlyDictionary<string, string> VowelMorphMap => _vowelMorphMap;

    public bool UpdateTransform(PmxModelComponent component, float elapsedSeconds)
    {
        if (_needsApplyFace)
        {
            ApplyFace(component);
            _needsApplyFace = false;
        }

        if (!IsPlaying)
        {
            return false;
        }

        if (_vowelSequence.Count == 0)
        {
            Stop(resetFace: true);
            ApplyFace(component);
            _needsApplyFace = false;
            return true;
        }

        _timePointMilliseconds += Math.Max(0.0f, elapsedSeconds) * 1000.0f;
        double maxTime = _vowelSequence.Count * _framePeriodMilliseconds;

        if (_timePointMilliseconds >= maxTime)
        {
            if (!_isLoop)
            {
                Stop(resetFace: true);
                ApplyFace(component);
                _needsApplyFace = false;
                return true;
            }

            _timePointMilliseconds %= maxTime;
        }

        int leftIndex = (int)(_timePointMilliseconds / _framePeriodMilliseconds);
        if (leftIndex >= _vowelSequence.Count)
        {
            return true;
        }

        double mod = _timePointMilliseconds % _framePeriodMilliseconds;

        ResetVowelFaceTable();
        if (mod <= float.Epsilon)
        {
            SetVowelValue(_vowelSequence[leftIndex], 1.0f);
        }
        else
        {
            float interpolation = (float)(mod / _framePeriodMilliseconds);
            float left = 1.0f - interpolation;
            float right = interpolation;

            SetVowelValue(_vowelSequence[leftIndex], left);
            if (leftIndex + 1 < _vowelSequence.Count)
            {
                SetVowelValue(_vowelSequence[leftIndex + 1], right);
            }
        }

        ApplyFace(component);
        return false;
    }

    public void Start(string text, TimeSpan? framePeriod = null, bool isLoop = false)
    {
        if (framePeriod.HasValue)
        {
            FramePeriod = framePeriod.Value;
        }

        _isLoop = isLoop;
        _timePointMilliseconds = 0.0;
        _vowelSequence.Clear();
        _vowelSequence.AddRange(BuildVowelSequence(text));
        ResetVowelFaceTable();
        IsPlaying = _vowelSequence.Count > 0;
        _needsApplyFace = true;
    }

    public void Start(bool reset = false)
    {
        if (reset)
        {
            _timePointMilliseconds = 0.0;
        }

        IsPlaying = _vowelSequence.Count > 0;
    }

    public void Stop(bool resetFace = true)
    {
        IsPlaying = false;

        if (resetFace)
        {
            ResetVowelFaceTable();
            _needsApplyFace = true;
        }
    }

    public void SetVowelMorph(string vowel, string morphName)
    {
        if (string.IsNullOrWhiteSpace(vowel))
        {
            throw new ArgumentException("Vowel is required.", nameof(vowel));
        }

        if (string.IsNullOrWhiteSpace(morphName))
        {
            throw new ArgumentException("Morph name is required.", nameof(morphName));
        }

        _vowelMorphMap[vowel] = morphName;
        if (!_vowelFaceTable.ContainsKey(vowel))
        {
            _vowelFaceTable.Add(vowel, 0.0f);
        }

        _cachedModel = null;
        _cachedMorphs.Clear();
    }

    private static Dictionary<string, string> CreateVowelMorphMap(IReadOnlyDictionary<string, string>? input)
    {
        if (input is null || input.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["あ"] = "あ",
                ["い"] = "い",
                ["う"] = "う",
                ["え"] = "え",
                ["お"] = "お"
            };
        }

        Dictionary<string, string> mapping = new(StringComparer.Ordinal);
        foreach ((string vowel, string morphName) in input)
        {
            if (string.IsNullOrWhiteSpace(vowel) || string.IsNullOrWhiteSpace(morphName))
            {
                continue;
            }

            mapping[vowel] = morphName;
        }

        if (mapping.Count == 0)
        {
            throw new ArgumentException("Vowel morph map is empty.", nameof(input));
        }

        return mapping;
    }

    private static Dictionary<string, float> CreateFaceTable(IEnumerable<string> vowels)
    {
        Dictionary<string, float> table = new(StringComparer.Ordinal);
        foreach (string vowel in vowels)
        {
            if (!table.ContainsKey(vowel))
            {
                table.Add(vowel, 0.0f);
            }
        }

        return table;
    }

    private void SetVowelValue(string vowel, float value)
    {
        if (_vowelFaceTable.ContainsKey(vowel))
        {
            _vowelFaceTable[vowel] = value;
        }
    }

    private void ResetVowelFaceTable()
    {
        string[] keys = [.. _vowelFaceTable.Keys];
        foreach (string key in keys)
        {
            _vowelFaceTable[key] = 0.0f;
        }
    }

    private List<string> BuildVowelSequence(string sourceText)
    {
        List<string> sequence = [];
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return sequence;
        }

        string kanaText = _kanaDictionary.ConvertText(sourceText);
        foreach (char kana in kanaText)
        {
            string vowel = _vowelDictionary.GetVowel(kana);
            if (_vowelFaceTable.ContainsKey(vowel))
            {
                sequence.Add(vowel);
            }
        }

        return sequence;
    }

    private void ApplyFace(PmxModelComponent component)
    {
        MMDModel? model = component.Model;
        if (model is null)
        {
            return;
        }

        RebuildMorphCacheIfNeeded(model);

        Dictionary<string, float> morphWeights = new(StringComparer.Ordinal);
        foreach ((string vowel, float weight) in _vowelFaceTable)
        {
            if (!_vowelMorphMap.TryGetValue(vowel, out string? morphName))
            {
                continue;
            }

            if (!morphWeights.TryGetValue(morphName, out float currentWeight) || weight > currentWeight)
            {
                morphWeights[morphName] = weight;
            }
        }

        foreach ((string morphName, float weight) in morphWeights)
        {
            if (_cachedMorphs.TryGetValue(morphName, out MMDMorph? morph) && morph is not null)
            {
                morph.Weight = weight;
            }
        }
    }

    private void RebuildMorphCacheIfNeeded(MMDModel model)
    {
        if (ReferenceEquals(_cachedModel, model))
        {
            return;
        }

        _cachedModel = model;
        _cachedMorphs.Clear();
        foreach (string morphName in _vowelMorphMap.Values.Distinct(StringComparer.Ordinal))
        {
            _cachedMorphs[morphName] = model.FindMorph(item => item.Name == morphName);
        }
    }
}

