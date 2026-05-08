using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Audio;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Zhengyan.DigitalWife.Mmd.Game.Speech;
using Silk.NET.OpenGLES;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Samples.MmdDemo;

internal sealed class DemoGame : Zhengyan.DigitalWife.Mmd.Game.Game
{
    private static readonly Vector4 DefaultBackgroundClearColor = new(0.08f, 0.09f, 0.12f, 1.0f);

    private readonly SampleScenePaths _scenePaths;
    private readonly OrbitCamera _camera = new();
    private readonly MmdCharacterGroup _characters;
    private readonly List<PmxModelComponent> _modelViews = [];

    private SceneRenderTarget? _sceneRenderTarget;
    private OrbitCameraController? _cameraController;
    private DebugAxesComponent? _debugAxesComponent;
    private DemoOverlayComponent? _overlayComponent;
    private EventInfo? _fileDropEvent;
    private Delegate? _fileDropHandler;
    private AudioClip? _backgroundMusicClip;
    private AudioSource? _backgroundMusicSource;
    private WaterSurfaceComponent? _waterSurface;
    private ParticleSystemComponent? _cloudParticles;
    private ParticleSystemComponent? _rainParticles;
    private ParticleSystemComponent? _snowParticles;
    private ParticleSystemComponent? _sakuraParticles;
    private ParticleSystemComponent? _waterfallParticles;
    private ParticleSystemComponent? _streamParticles;
    private ParticleSystemComponent? _fireParticles;
    private string? _waterSurfaceUnavailableReason;
    private SpeechDictionarySet? _speechDictionaries;
    private string? _speechDictionaryDirectory;
    private SpeechDictionaryLanguage _speechDictionaryLanguage = SpeechDictionaryLanguage.Japanese;
    private string _statusMessage = "Ready.";
    private float _backgroundMusicVolume = 0.85f;
    private bool _backgroundMusicLooping = true;

    public DemoGame(SampleScenePaths scenePaths)
        : base(new GameOptions
        {
            Title = $"Zhengyan.DigitalWife.Samples.MmdDemo - {Path.GetFileName(scenePaths.ModelPath)}",
            WindowSize = new Silk.NET.Maths.Vector2D<int>(1366, 768),
            VSync = true,
            Samples = 4,
            UseOpenCL = true,
            ClearColor = DefaultBackgroundClearColor
    })
    {
        _scenePaths = scenePaths;
        _characters = new MmdCharacterGroup(this, _camera);
        _speechDictionaryDirectory = scenePaths.SpeechDictionaryDirectory;
    }

    public OrbitCamera Camera => _camera;

    public IReadOnlyList<PmxModelComponent> Models => _modelViews;

    public IReadOnlyList<MmdCharacter> Characters => _characters.Characters;

    public bool HasModels => _characters.HasAny;

    public PmxModelComponent ActiveModel => _characters.ActiveCharacter is not null
        ? _characters.ActiveCharacter.ModelComponent
        : throw new InvalidOperationException("No active model is selected.");

    public int ActiveModelIndex => _characters.ActiveIndex;

    public MmdCharacter? ActiveCharacter => _characters.ActiveCharacter;

    public string StatusMessage => _statusMessage;

    public bool IsFileDropSupported { get; private set; }

    public DebugAxesComponent DebugAxes => _debugAxesComponent ?? throw new InvalidOperationException("Debug axes component has not been created.");

    public SceneRenderTarget SceneRenderTarget => _sceneRenderTarget ?? throw new InvalidOperationException("Scene render target has not been created.");

    public string BackgroundMusicStatus => _backgroundMusicSource is null
        ? "BGM: none"
        : $"BGM: {Path.GetFileName(_backgroundMusicClip?.Name ?? "unknown")} {(IsBackgroundMusicPlaying ? "(playing)" : "(paused)")}, loop={(BackgroundMusicLooping ? "on" : "off")}, volume={BackgroundMusicVolume:F2}";

    public bool HasBackgroundMusic => _backgroundMusicSource is not null;

    public bool IsBackgroundMusicPlaying => _backgroundMusicSource?.State == Silk.NET.OpenAL.SourceState.Playing;

    public float BackgroundMusicVolume
    {
        get => _backgroundMusicVolume;
        set
        {
            _backgroundMusicVolume = Math.Clamp(value, 0.0f, 4.0f);
            if (_backgroundMusicSource is not null)
            {
                _backgroundMusicSource.Volume = _backgroundMusicVolume;
            }
        }
    }

    public bool BackgroundMusicLooping
    {
        get => _backgroundMusicLooping;
        set
        {
            _backgroundMusicLooping = value;
            if (_backgroundMusicSource is not null)
            {
                _backgroundMusicSource.Looping = value;
            }
        }
    }

    public PmxModelComponent? SelectedModel => ActiveCharacter?.ModelComponent;

    public string? SpeechDictionaryDirectory => _speechDictionaryDirectory;

    public SpeechDictionaryLanguage SpeechDictionaryLanguage => _speechDictionaryLanguage;

    public bool HasSpeechDictionaries => _speechDictionaries is not null;

    public WaterSurfaceComponent? WaterSurface => _waterSurface;

    public string? WaterSurfaceUnavailableReason => _waterSurfaceUnavailableReason;

    public ParticleSystemComponent? CloudParticles => _cloudParticles;

    public ParticleSystemComponent? RainParticles => _rainParticles;

    public ParticleSystemComponent? SnowParticles => _snowParticles;

    public ParticleSystemComponent? SakuraParticles => _sakuraParticles;

    public ParticleSystemComponent? WaterfallParticles => _waterfallParticles;

    public ParticleSystemComponent? StreamParticles => _streamParticles;

    public ParticleSystemComponent? FireParticles => _fireParticles;

    public Vector3 LightColor { get; set; } = Vector3.One;

    public Vector3 AmbientLightColor { get; set; } = new(0.65f, 0.65f, 0.65f);

    public float AmbientLightStrength { get; set; } = 0.2f;

    public Vector3 LightDirection { get; set; } = new(-0.5f, -1.0f, -0.5f);

    public Vector4 ShadowColor { get; set; } = new(0.17f, 0.17f, 0.17f, 0.7f);

    public Vector4 BackgroundColor
    {
        get => Options.ClearColor;
        set
        {
            Vector4 clamped = new(
                Math.Clamp(value.X, 0.0f, 1.0f),
                Math.Clamp(value.Y, 0.0f, 1.0f),
                Math.Clamp(value.Z, 0.0f, 1.0f),
                Math.Clamp(value.W, 0.0f, 1.0f));

            Options.ClearColor = clamped;
            if (GraphicsDevice is not null)
            {
                GraphicsDevice.ClearColor = clamped;
            }
        }
    }

    public AnimationTimingMode AnimationTimingMode
    {
        get => Options.AnimationTimingMode;
        set => Options.AnimationTimingMode = value;
    }

    public void ResetBackgroundColor()
    {
        BackgroundColor = DefaultBackgroundClearColor;
    }

    protected override void Initialize()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Logo", "logo.png");
        if (!WindowIconLoader.TrySetWindowIconFromFile(Window, iconPath))
        {
            Console.Error.WriteLine($"Window icon was not set because the configured file was missing or invalid: {iconPath}");
        }

        SubscribeFileDrop();
    }

    protected override void LoadContent()
    {
        _sceneRenderTarget = new SceneRenderTarget(GraphicsDevice.Gl);
        _sceneRenderTarget.EnsureSize(GraphicsDevice.BackBufferSize.X, GraphicsDevice.BackBufferSize.Y);

        ResetCamera();

        _cameraController = AddComponent(new OrbitCameraController(_camera)
        {
            OrbitSensitivity = 0.2f,
            PanSensitivity = 1.0f,
            ZoomSensitivity = 1.0f,
            KeyboardPanSpeed = 4.0f
        });

        TryLoadModels(new[] { _scenePaths.ModelPath }, _scenePaths.MotionPath);
        TryCreateWaterSurface();
        TryCreateParticleSystems();
        _ = AddComponent(new GroundShadowPassComponent(this)
        {
            DrawOrder = 110
        });

        _debugAxesComponent = AddComponent(new DebugAxesComponent(_camera, () => LightDirection)
        {
            DrawOrder = 900
        });

        _overlayComponent = AddComponent(new DemoOverlayComponent(this)
        {
            DrawOrder = int.MaxValue,
            UpdateOrder = int.MaxValue
        });

        _cameraController.CanProcessPointerInput = () => _overlayComponent?.CanInteractWithScenePointer ?? true;
        _cameraController.CanProcessKeyboardInput = () => _overlayComponent?.CanInteractWithSceneKeyboard ?? true;

        UpdateStatus(BuildLoadStatus());
    }

    protected override void UnloadContent()
    {
        UnsubscribeFileDrop();
        _sceneRenderTarget?.Dispose();
        _sceneRenderTarget = null;
    }

    protected override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        if (_sceneRenderTarget is null)
        {
            return;
        }

        _sceneRenderTarget.Bind();
        GraphicsDevice.Gl.Disable(GLEnum.ScissorTest);
        GraphicsDevice.Gl.Disable(GLEnum.StencilTest);
        GraphicsDevice.Gl.ColorMask(true, true, true, true);
        GraphicsDevice.Gl.DepthMask(true);
        GraphicsDevice.Gl.StencilMask(0xFF);
        GraphicsDevice.Gl.ClearColor(Options.ClearColor.X, Options.ClearColor.Y, Options.ClearColor.Z, Options.ClearColor.W);
        GraphicsDevice.Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

        _camera.Width = _sceneRenderTarget.Width;
        _camera.Height = _sceneRenderTarget.Height;
    }

    public void ResetCamera()
    {
        _camera.SetLookAt(new Vector3(0.0f, 2.0f, 8.0f), Vector3.Zero);
        _camera.Fov = 45.0f;
    }

    private void TryCreateWaterSurface()
    {
        try
        {
            _waterSurface = AddComponent(new WaterSurfaceComponent(_camera, 160.0f)
            {
                Position = new Vector3(0.0f, -0.03f, 0.0f),
                Alpha = 0.52f,
                AnimationSpeed = 0.03f,
                NormalTiling = 100.0f,
                DrawOrder = 120
            });
            _waterSurfaceUnavailableReason = null;
        }
        catch (Exception ex)
        {
            _waterSurface = null;
            _waterSurfaceUnavailableReason = ex.Message;
            UpdateStatus($"Water surface disabled: {ex.Message}");
        }
    }

    private void TryCreateParticleSystems()
    {
        try
        {
            _cloudParticles = AddComponent(new ParticleSystemComponent(_camera, ParticleSystemPresets.Cloud())
            {
                Position = new Vector3(0.0f, 11.5f, -10.0f),
                Visible = true,
                DrawOrder = 129
            });

            _rainParticles = AddComponent(new ParticleSystemComponent(_camera, ParticleSystemPresets.Rain())
            {
                Position = new Vector3(0.0f, 8.0f, 1.6f),
                Visible = false,
                DrawOrder = 130
            });

            _snowParticles = AddComponent(new ParticleSystemComponent(_camera, ParticleSystemPresets.Snow())
            {
                Position = new Vector3(0.0f, 7.0f, 1.6f),
                Visible = false,
                DrawOrder = 130
            });

            _sakuraParticles = AddComponent(new ParticleSystemComponent(_camera, ParticleSystemPresets.Sakura())
            {
                Position = new Vector3(0.0f, 7.0f, 1.6f),
                Visible = false,
                DrawOrder = 130
            });

            _waterfallParticles = AddComponent(new ParticleSystemComponent(_camera, ParticleSystemPresets.Waterfall("Waterfall.png"))
            {
                Position = new Vector3(-4.0f, 4.2f, -1.5f),
                Visible = false,
                DrawOrder = 131
            });

            _streamParticles = AddComponent(new ParticleSystemComponent(_camera, ParticleSystemPresets.Stream("Stream.png"))
            {
                Position = new Vector3(-4.0f, 0.3f, -1.6f),
                Visible = false,
                DrawOrder = 131
            });

            _fireParticles = AddComponent(new ParticleSystemComponent(_camera, ParticleSystemPresets.Fire("Fire.png"))
            {
                Position = new Vector3(3.0f, 0.35f, 1.6f),
                Visible = false,
                DrawOrder = 132
            });
        }
        catch (Exception ex)
        {
            UpdateStatus($"Particle systems disabled: {ex.Message}");
        }
    }

    public void PresentSceneToBackBuffer()
    {
        if (_sceneRenderTarget is null)
        {
            return;
        }

        _sceneRenderTarget.ForceOpaqueAlpha();
        _sceneRenderTarget.Unbind(GraphicsDevice.BackBufferSize.X, GraphicsDevice.BackBufferSize.Y);
    }

    public void SetSceneViewportSize(int width, int height)
    {
        _sceneRenderTarget?.EnsureSize(width, height);
    }

    public void TryLoadScene(string modelPath, string? motionPath = null)
    {
        TryLoadModels(new[] { modelPath }, motionPath);
    }

    public void TryLoadModels(IEnumerable<string> modelPaths, string? motionPath = null)
    {
        bool loadedAny = false;
        string? lastError = null;

        foreach (string modelPath in modelPaths)
        {
            try
            {
                AddModel(modelPath, null);
                loadedAny = true;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        if (loadedAny)
        {
            ApplySceneLighting();
            UpdateTitle();
            UpdateStatus(BuildLoadStatus());
        }

        if (!string.IsNullOrWhiteSpace(lastError))
        {
            UpdateStatus($"Load partially failed: {lastError}");
        }

        if (!string.IsNullOrWhiteSpace(motionPath) && HasModels)
        {
            TryApplyMotionToActiveModel(motionPath);
        }
    }

    public PmxModelComponent AddModel(string modelPath, string? motionPath = null)
    {
        MmdCharacter character = _characters.AddCharacter(modelPath, motionPath, configureModel: model =>
        {
            model.LightColor = LightColor;
            model.AmbientLightColor = AmbientLightColor;
            model.AmbientLightStrength = AmbientLightStrength;
            model.LightDirection = LightDirection;
            model.ShadowColor = ShadowColor;
            model.DrawShadowInMainPass = false;
        });

        if (_characters.Count == 1)
        {
            ResetCamera();
        }

        _modelViews.Add(character.ModelComponent);
        return character.ModelComponent;
    }

    public void TryApplyMotion(string motionPath)
    {
        TryApplyMotionToActiveModel(motionPath);
    }

    public void TryApplyMotionToActiveModel(string motionPath)
    {
        if (!HasModels)
        {
            UpdateStatus("Load a PMX model before applying a VMD motion.");
            return;
        }

        TryApplyMotionToModel(ActiveModel, motionPath);
    }

    public void TryClearMotion()
    {
        if (!HasModels)
        {
            UpdateStatus("No PMX model is loaded.");
            return;
        }

        TryClearMotionForModel(ActiveModel);
    }

    public void TryApplyMotionToModel(PmxModelComponent model, string? motionPath)
    {
        try
        {
            model.ApplyMotion(motionPath);
            ApplySceneLightingToModel(model);
            UpdateTitle();
            UpdateStatus(BuildLoadStatus());
        }
        catch (Exception ex)
        {
            UpdateStatus($"Motion load failed: {ex.Message}");
        }
    }

    public void SetSpeechDictionaryDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            _speechDictionaryDirectory = null;
            _speechDictionaries = null;
            return;
        }

        _speechDictionaryDirectory = Path.GetFullPath(directoryPath);
        _speechDictionaries = null;
    }

    public bool TryLoadSpeechDictionaries(string directoryPath, SpeechDictionaryLanguage language = SpeechDictionaryLanguage.Japanese)
    {
        try
        {
            SpeechDictionarySet dictionaries = SpeechDictionarySet.LoadFromDirectory(directoryPath, language);
            _speechDictionaries = dictionaries;
            _speechDictionaryDirectory = Path.GetFullPath(directoryPath);
            _speechDictionaryLanguage = language;
            UpdateStatus($"Loaded {language} speech dictionaries: {_speechDictionaryDirectory}");
            return true;
        }
        catch (Exception ex)
        {
            UpdateStatus($"Failed to load speech dictionaries: {ex.Message}");
            return false;
        }
    }

    public bool TryBindModelToModel(int targetModelIndex, int relationModelIndex, bool bindComponentTransform, bool bindLighting)
    {
        return TrySetModelRelationBinding(targetModelIndex, relationModelIndex, bindComponentTransform, bindLighting);
    }

    public bool TryBindActiveModelToModel(int relationModelIndex, bool bindComponentTransform, bool bindLighting)
    {
        if (!HasModels)
        {
            UpdateStatus("No PMX model is loaded.");
            return false;
        }

        return TrySetModelRelationBinding(ActiveModelIndex, relationModelIndex, bindComponentTransform, bindLighting);
    }

    public bool TryStartSpeechOnActiveModel(
        string text,
        string dictionaryDirectory,
        SpeechDictionaryLanguage language,
        int framePeriodMilliseconds = 240,
        bool isLoop = false)
    {
        if (!HasModels)
        {
            UpdateStatus("No PMX model is loaded.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            UpdateStatus("Speech text is empty.");
            return false;
        }

        if (framePeriodMilliseconds <= 0)
        {
            framePeriodMilliseconds = 240;
        }

        string resolvedDictionaryDirectory = string.IsNullOrWhiteSpace(dictionaryDirectory)
            ? _speechDictionaryDirectory ?? string.Empty
            : dictionaryDirectory;

        if (string.IsNullOrWhiteSpace(resolvedDictionaryDirectory))
        {
            UpdateStatus("Speech dictionary directory is required.");
            return false;
        }

        if (!TryLoadSpeechDictionaries(resolvedDictionaryDirectory, language))
        {
            return false;
        }

        if (_speechDictionaries is null || ActiveCharacter is null)
        {
            return false;
        }

        SpeechTransformUpdater speech = _characters.AttachSpeech(ActiveCharacter, _speechDictionaries);
        speech.Start(text, TimeSpan.FromMilliseconds(framePeriodMilliseconds), isLoop);
        UpdateStatus($"Speech started on '{ActiveCharacter.Name}'.");
        return true;
    }

    public bool TryStopSpeechOnActiveModel(bool resetFace = true)
    {
        if (!HasModels || ActiveCharacter is null)
        {
            UpdateStatus("No PMX model is loaded.");
            return false;
        }

        int stoppedCount = 0;
        foreach (ITransformUpdater updater in ActiveModel.TransformUpdaters.Items)
        {
            if (updater is not SpeechTransformUpdater speechUpdater)
            {
                continue;
            }

            speechUpdater.Stop(resetFace);
            stoppedCount++;
        }

        if (stoppedCount == 0)
        {
            UpdateStatus("Active model has no speech updater.");
            return false;
        }

        ActiveCharacter.SpeechUpdater?.Stop(resetFace);
        UpdateStatus($"Speech stopped on '{ActiveCharacter.Name}'.");
        return true;
    }

    public void TryClearMotionForModel(PmxModelComponent model)
    {
        if (string.IsNullOrWhiteSpace(model.ModelPath))
        {
            UpdateStatus("No PMX model is loaded.");
            return;
        }

        TryApplyMotionToModel(model, null);
    }

    public bool TryRemoveModel(int modelIndex)
    {
        if (modelIndex < 0 || modelIndex >= _characters.Count || modelIndex >= _modelViews.Count)
        {
            UpdateStatus("Model index is out of range.");
            return false;
        }

        bool removed = _characters.RemoveCharacterAt(modelIndex);
        if (!removed)
        {
            UpdateStatus("Failed to remove model.");
            return false;
        }

        _modelViews.RemoveAt(modelIndex);
        UpdateTitle();
        UpdateStatus(BuildLoadStatus());
        return true;
    }

    public int? GetRelationModelIndexForTarget(int targetModelIndex)
    {
        if (targetModelIndex < 0 || targetModelIndex >= _characters.Count)
        {
            return null;
        }

        MmdCharacter target = _characters.Characters[targetModelIndex];
        PmxModelComponent? relationComponent = target.RelationUpdater?.RelationComponent;
        if (relationComponent is null)
        {
            return null;
        }

        for (int i = 0; i < _characters.Count; i++)
        {
            if (ReferenceEquals(_characters.Characters[i].ModelComponent, relationComponent))
            {
                return i;
            }
        }

        return null;
    }

    public bool TrySetModelRelationBinding(
        int targetModelIndex,
        int? relationModelIndex,
        bool bindComponentTransform,
        bool bindLighting)
    {
        if (targetModelIndex < 0 || targetModelIndex >= _characters.Count)
        {
            UpdateStatus("Target model index is out of range.");
            return false;
        }

        MmdCharacter target = _characters.Characters[targetModelIndex];

        if (relationModelIndex is null)
        {
            bool detached = target.DetachRelation();
            if (detached)
            {
                UpdateStatus($"Cleared relation binding for '{target.Name}'.");
            }
            else
            {
                UpdateStatus($"'{target.Name}' has no relation binding.");
            }

            return true;
        }

        int relationIndex = relationModelIndex.Value;
        if (relationIndex < 0 || relationIndex >= _characters.Count)
        {
            UpdateStatus("Relation model index is out of range.");
            return false;
        }

        if (targetModelIndex == relationIndex)
        {
            UpdateStatus("Target model and relation model must be different.");
            return false;
        }

        MmdCharacter relation = _characters.Characters[relationIndex];
        RelationTransformUpdater updater = _characters.BindRelation(target, relation, bindComponentTransform);
        updater.BindLighting = bindLighting;
        UpdateStatus($"Bound '{target.Name}' to '{relation.Name}' by same-name bones.");
        return true;
    }

    public bool TryGetClipboardText(out string text)
    {
        text = string.Empty;
        string? lastError = null;

        try
        {
            string imguiClipboard = ImGui.GetClipboardText();
            if (!string.IsNullOrWhiteSpace(imguiClipboard))
            {
                text = NormalizeClipboardText(imguiClipboard);
                return true;
            }
        }
        catch (Exception ex)
        {
            lastError = $"Failed to read clipboard from ImGui backend: {ex.Message}";
        }

        try
        {
            if (Input.Context.Keyboards.Count > 0)
            {
                string keyboardClipboard = Input.Context.Keyboards[0].ClipboardText ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(keyboardClipboard))
                {
                    text = NormalizeClipboardText(keyboardClipboard);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            lastError = $"Failed to read clipboard from input backend: {ex.Message}";
        }

        try
        {
            object windowObject = Window;
            Type windowType = windowObject.GetType();

            string[] propertyNames = ["ClipboardText", "ClipboardString", "Clipboard"];
            foreach (string propertyName in propertyNames)
            {
                PropertyInfo? property = windowType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property?.CanRead == true && property.PropertyType == typeof(string))
                {
                    string clipboard = (string?)property.GetValue(windowObject) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(clipboard))
                    {
                        text = NormalizeClipboardText(clipboard);
                        return true;
                    }
                }
            }

            MethodInfo? method = windowType.GetMethod("GetClipboardText", BindingFlags.Public | BindingFlags.Instance);
            if (method is not null && method.ReturnType == typeof(string) && method.GetParameters().Length == 0)
            {
                string clipboard = (string?)method.Invoke(windowObject, null) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(clipboard))
                {
                    text = NormalizeClipboardText(clipboard);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            lastError = $"Failed to read clipboard from window backend: {ex.Message}";
        }

        if (!string.IsNullOrEmpty(lastError))
        {
            UpdateStatus(lastError);
        }
        else
        {
            UpdateStatus("Clipboard is empty or unavailable on this runtime.");
        }

        return false;
    }

    private static string NormalizeClipboardText(string text)
    {
        string normalized = text.Trim();
        if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
        {
            normalized = normalized[1..^1];
        }

        return normalized;
    }

    public void SetActiveModel(int index)
    {
        _characters.SetActive(index);
    }

    public void UpdateStatus(string message)
    {
        _statusMessage = message;
    }

    public void ApplySceneLighting()
    {
        foreach (MmdCharacter character in _characters.Characters)
        {
            ApplySceneLightingToModel(character.ModelComponent);
        }
    }

    public void TryLoadBackgroundMusic(string path)
    {
        if (Audio is null)
        {
            UpdateStatus("Audio is unavailable on this machine.");
            return;
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            UpdateStatus($"BGM file not found: {fullPath}");
            return;
        }

        try
        {
            AudioClip clip = Audio.LoadClip(fullPath);
            AudioSource source = Audio.CreateSource(clip);
            source.Looping = _backgroundMusicLooping;
            source.Volume = _backgroundMusicVolume;

            _backgroundMusicSource?.Dispose();
            _backgroundMusicClip?.Dispose();
            _backgroundMusicClip = clip;
            _backgroundMusicSource = source;
            UpdateStatus($"Loaded BGM: {Path.GetFileName(fullPath)}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"BGM load failed: {ex.Message}");
        }
    }

    public void ToggleBackgroundMusic()
    {
        if (_backgroundMusicSource is null)
        {
            UpdateStatus("Load a background music file first.");
            return;
        }

        switch (_backgroundMusicSource.State)
        {
            case Silk.NET.OpenAL.SourceState.Playing:
                _backgroundMusicSource.Pause();
                break;
            case Silk.NET.OpenAL.SourceState.Paused:
                _backgroundMusicSource.Play();
                break;
            default:
                _backgroundMusicSource.Rewind();
                _backgroundMusicSource.Play();
                break;
        }
    }

    public void ResetBackgroundMusic()
    {
        if (_backgroundMusicSource is null)
        {
            UpdateStatus("Load a background music file first.");
            return;
        }

        bool shouldResume = _backgroundMusicSource.State == Silk.NET.OpenAL.SourceState.Playing;
        _backgroundMusicSource.Rewind();

        if (shouldResume)
        {
            _backgroundMusicSource.Play();
        }
    }

    private void HandleFileDrop(IReadOnlyList<string> paths)
    {
        List<string> modelPaths = [];
        string? motionPath = null;

        foreach (string path in paths)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".pmx":
                    modelPaths.Add(Path.GetFullPath(path));
                    break;
                case ".vmd" when motionPath is null:
                    motionPath = Path.GetFullPath(path);
                    break;
                case ".wav":
                case ".ogg":
                    TryLoadBackgroundMusic(path);
                    return;
            }
        }

        if (modelPaths.Count > 0)
        {
            TryLoadModels(modelPaths, motionPath);
            return;
        }

        if (motionPath is not null)
        {
            TryApplyMotionToActiveModel(motionPath);
            return;
        }

        UpdateStatus("Dropped files did not include a .pmx, .vmd, .wav, or .ogg file.");
    }

    private void SubscribeFileDrop()
    {
        IsFileDropSupported = false;
        _fileDropEvent = typeof(IWindow).GetEvent("FileDrop");
        Type? handlerType = _fileDropEvent?.EventHandlerType;
        MethodInfo? invokeMethod = handlerType?.GetMethod("Invoke");
        ParameterInfo[] parameters = invokeMethod?.GetParameters() ?? [];

        MethodInfo? handlerMethod = parameters.Length switch
        {
            1 when parameters[0].ParameterType == typeof(string[]) => GetType().GetMethod(nameof(OnFilesDropped), BindingFlags.Instance | BindingFlags.NonPublic),
            2 when parameters[1].ParameterType == typeof(string[]) => GetType().GetMethod(nameof(OnFilesDroppedWithSender), BindingFlags.Instance | BindingFlags.NonPublic),
            _ => null
        };

        if (_fileDropEvent is null || handlerType is null || handlerMethod is null)
        {
            UpdateStatus("Current Silk.NET runtime does not expose a compatible file-drop event signature.");
            return;
        }

        _fileDropHandler = Delegate.CreateDelegate(handlerType, this, handlerMethod);
        _fileDropEvent.AddEventHandler(Window, _fileDropHandler);
        IsFileDropSupported = true;
    }

    private void UnsubscribeFileDrop()
    {
        if (_fileDropEvent is null || _fileDropHandler is null)
        {
            return;
        }

        _fileDropEvent.RemoveEventHandler(Window, _fileDropHandler);
        _fileDropHandler = null;
        _fileDropEvent = null;
    }

    private void OnFilesDropped(string[] paths)
    {
        HandleFileDrop(paths);
    }

    private void OnFilesDroppedWithSender(IWindow _, string[] paths)
    {
        HandleFileDrop(paths);
    }

    private void UpdateTitle()
    {
        string modelName = HasModels ? Path.GetFileName(ActiveModel.ModelPath ?? "No Model") : "No Model";
        Title = HasModels
            ? $"Zhengyan.DigitalWife.Samples.MmdDemo - {modelName} ({_characters.Count} models)"
            : "Zhengyan.DigitalWife.Samples.MmdDemo - No Model";
    }

    private string BuildLoadStatus()
    {
        if (!HasModels)
        {
            return "No PMX model loaded. Drag .pmx, .vmd, .wav, or .ogg files onto the window.";
        }

        string activeModelName = Path.GetFileName(ActiveModel.ModelPath ?? "No Model");
        string motionSummary = ActiveModel.MotionLayerCount == 0
            ? "No motion"
            : $"{ActiveModel.MotionLayerCount} layer(s), primary: {Path.GetFileName(ActiveModel.MotionPath ?? "(none)")}";
        return $"Loaded {_characters.Count} PMX model(s). Active: {activeModelName}, Motion: {motionSummary}";
    }

    private void ApplySceneLightingToModel(PmxModelComponent model)
    {
        model.LightColor = LightColor;
        model.AmbientLightColor = AmbientLightColor;
        model.AmbientLightStrength = AmbientLightStrength;
        model.LightDirection = LightDirection;
        model.ShadowColor = ShadowColor;
    }

    public void OpenPmxFiles()
    {
        UpdateStatus("Open-file dialog is disabled in cross-platform mode. Drag and drop .pmx files into the window.");
    }

    public void OpenVmdForActiveModel()
    {
        if (!HasModels)
        {
            UpdateStatus("Load a PMX model first.");
            return;
        }

        UpdateStatus("Open-file dialog is disabled in cross-platform mode. Drag and drop a .vmd file into the window.");
    }

    public void OpenVmdForModel(int modelIndex)
    {
        if (modelIndex < 0 || modelIndex >= _characters.Count)
        {
            UpdateStatus("Select a valid PMX model first.");
            return;
        }

        UpdateStatus("Open-file dialog is disabled in cross-platform mode. Drag and drop a .vmd file into the window.");
    }

    public void OpenBackgroundMusic()
    {
        UpdateStatus("Open-file dialog is disabled in cross-platform mode. Drag and drop a .wav/.ogg file into the window.");
    }
}

