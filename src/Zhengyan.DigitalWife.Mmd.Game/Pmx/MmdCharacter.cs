using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Zhengyan.DigitalWife.Mmd.Game.Speech;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

public sealed class MmdCharacter
{
    public MmdCharacter(string name, PmxModelComponent modelComponent)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Character name is required.", nameof(name));
        }

        Name = name;
        ModelComponent = modelComponent ?? throw new ArgumentNullException(nameof(modelComponent));
    }

    public string Name { get; set; }

    public PmxModelComponent ModelComponent { get; }

    public RelationTransformUpdater? RelationUpdater { get; private set; }

    public SpeechTransformUpdater? SpeechUpdater { get; private set; }

    public string? ModelPath => ModelComponent.ModelPath;

    public string? MotionPath => ModelComponent.MotionPath;

    public int MotionLayerCount => ModelComponent.MotionLayerCount;

    public bool IsLoaded => ModelComponent.IsLoaded;

    public bool IsPlaying
    {
        get => ModelComponent.IsPlaying;
        set => ModelComponent.IsPlaying = value;
    }

    public float PlaybackSpeed
    {
        get => ModelComponent.PlaybackSpeed;
        set => ModelComponent.PlaybackSpeed = value;
    }

    public bool LoopMotion
    {
        get => ModelComponent.LoopMotion;
        set => ModelComponent.LoopMotion = value;
    }

    public float GroundShadowPlaneHeight
    {
        get => ModelComponent.GroundShadowPlaneHeight;
        set => ModelComponent.GroundShadowPlaneHeight = value;
    }

    public bool ResetPhysicsOnMotionLoop
    {
        get => ModelComponent.ResetPhysicsOnMotionLoop;
        set => ModelComponent.ResetPhysicsOnMotionLoop = value;
    }

    public void LoadMotion(string motionPath)
    {
        if (string.IsNullOrWhiteSpace(ModelPath))
        {
            throw new InvalidOperationException("Character model is not loaded.");
        }

        ModelComponent.ApplyMotion(motionPath);
    }

    public IReadOnlyList<MotionLayerInfo> GetMotionLayers()
    {
        return ModelComponent.GetMotionLayers();
    }

    public void SetMotionLayers(IEnumerable<MotionLayerDefinition> motionLayers)
    {
        ModelComponent.SetMotionLayers(motionLayers);
    }

    public void AddMotionLayer(string motionPath, float weight = 1.0f)
    {
        ModelComponent.AddMotionLayer(motionPath, weight);
    }

    public void AddMotionLayer(string motionPath, float weight, bool? resetPhysicsOnLoop)
    {
        ModelComponent.AddMotionLayer(motionPath, weight, resetPhysicsOnLoop);
    }

    public bool RemoveMotionLayer(string motionPath)
    {
        return ModelComponent.RemoveMotionLayer(motionPath);
    }

    public bool TrySetMotionLayerWeight(string motionPath, float weight)
    {
        return ModelComponent.TrySetMotionLayerWeight(motionPath, weight);
    }

    public void SetMotionLayerWeight(string motionPath, float weight)
    {
        ModelComponent.SetMotionLayerWeight(motionPath, weight);
    }

    public bool TrySetMotionLayerResetPhysicsOnLoop(string motionPath, bool resetPhysicsOnLoop)
    {
        return ModelComponent.TrySetMotionLayerResetPhysicsOnLoop(motionPath, resetPhysicsOnLoop);
    }

    public void SetMotionLayerResetPhysicsOnLoop(string motionPath, bool resetPhysicsOnLoop)
    {
        ModelComponent.SetMotionLayerResetPhysicsOnLoop(motionPath, resetPhysicsOnLoop);
    }

    public void ClearMotion()
    {
        ModelComponent.ClearMotion();
    }

    public void ResetAnimation()
    {
        ModelComponent.ResetAnimation();
    }

    public RelationTransformUpdater BindRelationTo(MmdCharacter relationCharacter, bool bindComponentTransform = true)
    {
        ArgumentNullException.ThrowIfNull(relationCharacter);

        RelationUpdater = ModelComponent.CreateRelationTransformUpdater(relationCharacter.ModelComponent, bindComponentTransform);
        return RelationUpdater;
    }

    public SpeechTransformUpdater AttachSpeech(
        SpeechDictionarySet dictionaries,
        IReadOnlyDictionary<string, string>? vowelMorphMap = null,
        string? noMatchFallbackVowel = null)
    {
        ArgumentNullException.ThrowIfNull(dictionaries);

        if (SpeechUpdater is not null)
        {
            SpeechUpdater.Stop(resetFace: true);
            _ = ModelComponent.RemoveTransformUpdater(SpeechUpdater);
            SpeechUpdater = null;
        }

        SpeechUpdater = ModelComponent.CreateSpeechTransformUpdater(dictionaries.Kana, dictionaries.Vowel, vowelMorphMap, noMatchFallbackVowel);
        return SpeechUpdater;
    }

    public bool DetachSpeech()
    {
        if (SpeechUpdater is null)
        {
            return false;
        }

        bool removed = ModelComponent.RemoveTransformUpdater(SpeechUpdater);
        SpeechUpdater = null;
        return removed;
    }

    public bool DetachRelation()
    {
        if (RelationUpdater is null)
        {
            return false;
        }

        bool removed = ModelComponent.RemoveTransformUpdater(RelationUpdater);
        RelationUpdater = null;
        return removed;
    }
}

