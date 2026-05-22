using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Zhengyan.DigitalWife.Mmd.Game.Speech;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeEntity
{
    private readonly GameEntity _definition;
    private readonly PmxModelComponent? _model;
    private readonly ParticleSystemComponent? _particle;
    private readonly WaterSurfaceComponent? _water;
    private readonly Func<string, string> _resolvePath;
    private RuntimeScene? _scene;
    private RuntimeVoice? _voice;
    private RelationTransformUpdater? _relationUpdater;

    internal RuntimeEntity(GameEntity definition, PmxModelComponent model, Func<string, string> resolvePath)
    {
        _definition = definition;
        _model = model;
        _resolvePath = resolvePath;
    }

    internal RuntimeEntity(GameEntity definition, ParticleSystemComponent particle)
    {
        _definition = definition;
        _particle = particle;
        _resolvePath = static path => path;
    }

    internal RuntimeEntity(GameEntity definition, WaterSurfaceComponent water)
    {
        _definition = definition;
        _water = water;
        _resolvePath = static path => path;
    }

    internal RuntimeEntity(GameEntity definition)
    {
        _definition = definition;
        _resolvePath = static path => path;
    }

    public string Id => _definition.Id;

    public string Name => _definition.Name;

    public string Type => _definition.Type;

    public bool IsPmxModel => _model is not null;

    public bool RelationEnabled => _definition.Relation.Enabled;

    public string RelationEntity => _definition.Relation.RelationEntity;

    public bool RelationBindComponentTransform => _definition.Relation.BindComponentTransform;

    public bool RelationBindLighting => _definition.Relation.BindLighting;

    public Vector3 Position
    {
        get => _model?.Position ?? _particle?.Position ?? _water?.Position ?? _definition.Transform.Position.ToVector3();
        set
        {
            if (_model is not null)
            {
                _model.Position = value;
            }

            if (_particle is not null)
            {
                _particle.Position = value;
            }

            if (_water is not null)
            {
                _water.Position = value;
            }
        }
    }

    public Vector3 Scale
    {
        get => _model?.Scale ?? _water?.Scale ?? _definition.Transform.Scale.ToVector3();
        set
        {
            if (_model is not null)
            {
                _model.Scale = value;
            }

            if (_water is not null)
            {
                _water.Scale = value;
            }
        }
    }

    public Quaternion Rotation
    {
        get => _model?.Rotation ?? _water?.Rotation ?? Quaternion.Identity;
        set
        {
            if (_model is not null)
            {
                _model.Rotation = value;
            }

            if (_water is not null)
            {
                _water.Rotation = value;
            }
        }
    }

    public bool IsPlaying
    {
        get => _model?.IsPlaying ?? _particle?.Enabled ?? _water?.Enabled ?? _definition.IsPlaying;
        set
        {
            if (_model is not null)
            {
                _model.IsPlaying = value;
            }

            if (_particle is not null)
            {
                _particle.Enabled = value;
                _particle.Visible = value;
            }

            if (_water is not null)
            {
                _water.Enabled = value;
                _water.Visible = value;
            }
        }
    }

    public float PlaybackSpeed
    {
        get => _model?.PlaybackSpeed ?? _particle?.SimulationSpeed ?? 1.0f;
        set
        {
            float clamped = Math.Clamp(value, 0.0f, 10.0f);
            if (_model is not null)
            {
                _model.PlaybackSpeed = clamped;
            }

            if (_particle is not null)
            {
                _particle.SimulationSpeed = clamped;
            }
        }
    }

    public bool LoopMotion
    {
        get => _model?.LoopMotion ?? _definition.LoopMotion;
        set
        {
            if (_model is not null)
            {
                _model.LoopMotion = value;
            }
        }
    }

    public bool ResetPhysicsOnMotionLoop
    {
        get => _model?.ResetPhysicsOnMotionLoop ?? _definition.ResetPhysicsOnMotionLoop;
        set
        {
            if (_model is not null)
            {
                _model.ResetPhysicsOnMotionLoop = value;
            }
        }
    }

    public bool Visible
    {
        get => _model?.Visible ?? _particle?.Visible ?? _water?.Visible ?? true;
        set
        {
            if (_model is not null)
            {
                _model.Visible = value;
            }

            if (_particle is not null)
            {
                _particle.Visible = value;
            }

            if (_water is not null)
            {
                _water.Visible = value;
            }
        }
    }

    public void SetPosition(float x, float y, float z)
    {
        Position = new Vector3(x, y, z);
    }

    public void Translate(float x, float y, float z)
    {
        Position += new Vector3(x, y, z);
    }

    public void SetScale(float x, float y, float z)
    {
        Scale = new Vector3(x, y, z);
    }

    public void RotateX(float degrees)
    {
        Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitX, ToRadians(degrees)) * Rotation);
    }

    public void RotateY(float degrees)
    {
        Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, ToRadians(degrees)) * Rotation);
    }

    public void RotateZ(float degrees)
    {
        Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, ToRadians(degrees)) * Rotation);
    }

    public void ApplyMotion(string motionPath)
    {
        _model?.ApplyMotion(_resolvePath(motionPath));
    }

    public void AddMotionLayer(string motionPath, float weight = 1.0f)
    {
        _model?.AddMotionLayer(_resolvePath(motionPath), weight);
    }

    public void SetMotionLayerWeight(string motionPath, float weight)
    {
        _model?.SetMotionLayerWeight(_resolvePath(motionPath), weight);
    }

    public void SetMotionLayerResetPhysicsOnLoop(string motionPath, bool resetPhysicsOnLoop)
    {
        _model?.SetMotionLayerResetPhysicsOnLoop(_resolvePath(motionPath), resetPhysicsOnLoop);
    }

    public void RemoveMotionLayer(string motionPath)
    {
        _model?.RemoveMotionLayer(_resolvePath(motionPath));
    }

    public void ClearMotion()
    {
        _model?.ClearMotion();
    }

    public void Speak(string text)
    {
        _voice?.Speak(this, text, (RuntimeVoiceOptions?)null);
    }

    public void Speak(string text, Action onCompleted)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            OnCompleted = onCompleted
        });
    }

    public void Speak(string text, int speakerId)
    {
        _voice?.Speak(this, text, speakerId);
    }

    public void Speak(string text, int speakerId, Action onCompleted)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            SpeakerId = speakerId,
            OnCompleted = onCompleted
        });
    }

    public void Speak(string text, int speakerId, float speed)
    {
        _voice?.Speak(this, text, speakerId, speed);
    }

    public void Speak(string text, int speakerId, float speed, Action onCompleted)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            SpeakerId = speakerId,
            Speed = speed,
            OnCompleted = onCompleted
        });
    }

    public void Speak(string text, int speakerId, float speed, float volume)
    {
        _voice?.Speak(this, text, speakerId, speed, volume);
    }

    public void Speak(string text, int speakerId, float speed, float volume, Action onCompleted)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            SpeakerId = speakerId,
            Speed = speed,
            Volume = volume,
            OnCompleted = onCompleted
        });
    }

    public void Speak(string text, RuntimeVoiceOptions options)
    {
        _voice?.Speak(this, text, options);
    }

    public void SpeakWithCallback(string text, string callbackName)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            OnCompleted = () => DispatchSpeechCallback(callbackName)
        });
    }

    public void SpeakWithCallback(string text, int speakerId, float speed, float volume, string callbackName)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            SpeakerId = speakerId,
            Speed = speed,
            Volume = volume,
            OnCompleted = () => DispatchSpeechCallback(callbackName)
        });
    }

    public void StopSpeaking()
    {
        _voice?.Stop(this);
    }

    public void BindRelation(string targetEntityIdOrName, bool bindComponentTransform = true, bool bindLighting = false)
    {
        if (_model is null || _scene is null || string.IsNullOrWhiteSpace(targetEntityIdOrName))
        {
            return;
        }

        RuntimeEntity? relation = _scene.GetEntity(targetEntityIdOrName);
        if (relation?._model is null || ReferenceEquals(relation, this))
        {
            return;
        }

        ClearRelationBinding();
        _relationUpdater = _model.CreateRelationTransformUpdater(relation._model, bindComponentTransform);
        _relationUpdater.BindLighting = bindLighting;

        _definition.Relation.Enabled = true;
        _definition.Relation.RelationEntity = targetEntityIdOrName;
        _definition.Relation.BindComponentTransform = bindComponentTransform;
        _definition.Relation.BindLighting = bindLighting;
    }

    public void ClearRelationBinding()
    {
        if (_model is not null && _relationUpdater is not null)
        {
            _ = _model.RemoveTransformUpdater(_relationUpdater);
        }

        _relationUpdater = null;
        _definition.Relation.Enabled = false;
        _definition.Relation.RelationEntity = string.Empty;
    }

    internal void AttachVoice(RuntimeVoice voice)
    {
        _voice = voice;
        _voice.AttachEntity(this);
    }

    internal void AttachScene(RuntimeScene scene)
    {
        _scene = scene;
    }

    internal void DispatchSpeechCallback(string callbackName)
    {
        if (_scene is null || string.IsNullOrWhiteSpace(callbackName))
        {
            return;
        }

        _scene.DispatchSpeechEvent(this, callbackName);
    }

    internal SpeechTransformUpdater CreateSpeechUpdater(
        SpeechDictionarySet dictionaries,
        IReadOnlyDictionary<string, string>? vowelMorphMap)
    {
        if (_model is null)
        {
            throw new InvalidOperationException($"Entity '{Name}' is not a PMX model.");
        }

        return _model.CreateSpeechTransformUpdater(dictionaries.Kana, dictionaries.Vowel, vowelMorphMap);
    }

    internal void SyncFromModel()
    {
        if (_model is not null)
        {
            _definition.Transform.Position = Vector3Dto.FromVector3(_model.Position);
            _definition.Transform.Scale = Vector3Dto.FromVector3(_model.Scale);
            _definition.IsPlaying = _model.IsPlaying;
            _definition.PlaybackSpeed = _model.PlaybackSpeed;
            _definition.LoopMotion = _model.LoopMotion;
            _definition.ResetPhysicsOnMotionLoop = _model.ResetPhysicsOnMotionLoop;
        }
        else if (_particle is not null)
        {
            _definition.Transform.Position = Vector3Dto.FromVector3(_particle.Position);
            _definition.IsPlaying = _particle.Enabled;
            _definition.Particle.SimulationSpeed = _particle.SimulationSpeed;
            _definition.Particle.Opacity = _particle.Opacity;
        }
        else if (_water is not null)
        {
            _definition.Transform.Position = Vector3Dto.FromVector3(_water.Position);
            _definition.Transform.Scale = Vector3Dto.FromVector3(_water.Scale);
            _definition.IsPlaying = _water.Enabled;
            _definition.Water.Alpha = _water.Alpha;
            _definition.Water.AnimationSpeed = _water.AnimationSpeed;
            _definition.Water.NormalTiling = _water.NormalTiling;
            _definition.Water.DeepColor = Vector3Dto.FromVector3(_water.DeepColor);
            _definition.Water.ReflectionTint = Vector3Dto.FromVector3(_water.ReflectionTint);
            _definition.Water.SkyReflectionStrength = _water.SkyReflectionStrength;
        }
    }

    private static float ToRadians(float degrees) => degrees * MathF.PI / 180.0f;
}
