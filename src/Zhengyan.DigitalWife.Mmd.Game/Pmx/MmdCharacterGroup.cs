using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Zhengyan.DigitalWife.Mmd.Game.Speech;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

public sealed class MmdCharacterGroup
{
    private readonly Game _game;
    private readonly OrbitCamera _camera;
    private readonly List<MmdCharacter> _characters = [];

    public MmdCharacterGroup(Game game, OrbitCamera camera)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    }

    public IReadOnlyList<MmdCharacter> Characters => _characters;

    public int Count => _characters.Count;

    public bool HasAny => _characters.Count > 0;

    public MmdCharacter? ActiveCharacter { get; private set; }

    public int ActiveIndex => ActiveCharacter is null ? -1 : _characters.IndexOf(ActiveCharacter);

    public MmdCharacter AddCharacter(
        string modelPath,
        string? motionPath = null,
        string? name = null,
        Action<PmxModelComponent>? configureModel = null)
    {
        PmxModelComponent modelComponent = _game.AddComponent(new PmxModelComponent(modelPath, motionPath)
        {
            Camera = _camera,
            Scale = new Vector3(0.2f, 0.2f, 0.2f),
            Position = GetDefaultCharacterPosition(_characters.Count),
            IsPlaying = motionPath is not null,
            EnablePhysical = true,
            EnableEdge = true,
            EnableShadow = true
        });

        configureModel?.Invoke(modelComponent);

        MmdCharacter character = new(
            name ?? Path.GetFileNameWithoutExtension(modelPath),
            modelComponent);

        _characters.Add(character);
        ActiveCharacter = character;
        return character;
    }

    public bool SetActive(int index)
    {
        if (index < 0 || index >= _characters.Count)
        {
            return false;
        }

        ActiveCharacter = _characters[index];
        return true;
    }

    public bool RemoveCharacterAt(int index)
    {
        if (index < 0 || index >= _characters.Count)
        {
            return false;
        }

        MmdCharacter character = _characters[index];

        foreach (MmdCharacter other in _characters)
        {
            if (ReferenceEquals(other, character))
            {
                continue;
            }

            if (other.RelationUpdater is not null &&
                ReferenceEquals(other.RelationUpdater.RelationComponent, character.ModelComponent))
            {
                other.DetachRelation();
            }
        }

        _characters.RemoveAt(index);
        _ = _game.RemoveComponent(character.ModelComponent);

        if (_characters.Count == 0)
        {
            ActiveCharacter = null;
        }
        else if (ReferenceEquals(ActiveCharacter, character))
        {
            int activeIndex = Math.Clamp(index, 0, _characters.Count - 1);
            ActiveCharacter = _characters[activeIndex];
        }

        return true;
    }

    public bool RemoveCharacter(MmdCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        int index = _characters.IndexOf(character);
        return index >= 0 && RemoveCharacterAt(index);
    }

    public MmdCharacter? FindByName(string name, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        foreach (MmdCharacter character in _characters)
        {
            if (string.Equals(character.Name, name, comparison))
            {
                return character;
            }
        }

        return null;
    }

    public RelationTransformUpdater BindRelation(MmdCharacter target, MmdCharacter relation, bool bindComponentTransform = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(relation);
        return target.BindRelationTo(relation, bindComponentTransform);
    }

    public SpeechTransformUpdater AttachSpeech(
        MmdCharacter character,
        SpeechDictionarySet dictionaries,
        IReadOnlyDictionary<string, string>? vowelMorphMap = null,
        string? noMatchFallbackVowel = null)
    {
        ArgumentNullException.ThrowIfNull(character);
        return character.AttachSpeech(dictionaries, vowelMorphMap, noMatchFallbackVowel);
    }

    private static Vector3 GetDefaultCharacterPosition(int index)
    {
        float spacing = 2.5f;
        int column = index % 3;
        int row = index / 3;
        return new Vector3((column - 1) * spacing, row * 0.25f, 1.6f);
    }
}

