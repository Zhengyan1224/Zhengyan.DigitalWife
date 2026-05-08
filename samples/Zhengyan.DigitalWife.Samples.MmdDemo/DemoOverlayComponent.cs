using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Speech;
using Silk.NET.OpenGLES.Extensions.ImGui;

namespace Zhengyan.DigitalWife.Samples.MmdDemo;

internal sealed class DemoOverlayComponent(DemoGame demoGame) : DrawableGameComponent
{
    private static readonly string[] AnimationTimingModeLabels =
    [
        "Frame-rate dependent (slow on FPS drop)",
        "Time synchronized (skip frames)"
    ];

    private static readonly string[] ParticleBlendModeLabels =
    [
        "Alpha",
        "Additive"
    ];

    private static readonly string[] ParticleOrientationModeLabels =
    [
        "Billboard",
        "Velocity aligned"
    ];

    private static readonly string[] ParticleTexturePresetLabels =
    [
        "SoftCircle",
        "Streak",
        "Flame"
    ];

    private static readonly string[] SpeechDictionaryLanguageLabels =
    [
        "Japanese",
        "Chinese"
    ];

    private readonly DemoGame _demoGame = demoGame;
    private ImGuiController? _controller;
    private bool _isViewportHovered;
    private bool _isViewportFocused;
    private bool _canInteractWithScenePointer;
    private bool _canInteractWithSceneKeyboard;
    private bool _bindRelationTransform = true;
    private bool _bindRelationLighting;
    private string _speechText = "ohayou gozaimasu";
    private string _speechDictionaryDirectory = string.Empty;
    private int _speechDictionaryLanguageIndex;
    private int _speechFramePeriodMs = 240;
    private bool _speechLoop;
    private string _pmxPath = string.Empty;
    private string _vmdPath = string.Empty;
    private string _bgmPath = string.Empty;
    private float _newMotionLayerWeight = 1.0f;
    private int _selectedParticleEditorIndex;
    private ParticleSystemComponent? _particleEditorTarget;
    private ParticleSystemSettings? _particleEditorSettings;
    private Vector3 _particleEditorPosition;
    private string _particlePresetPath = string.Empty;

    public bool WantsPointerCapture { get; private set; }

    public bool WantsKeyboardCapture { get; private set; }

    public bool CanInteractWithScenePointer => _canInteractWithScenePointer;

    public bool CanInteractWithSceneKeyboard => _canInteractWithSceneKeyboard;

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        if (TryGetCjkFontPath(out string cjkFontPath))
        {
            try
            {
                _controller = new ImGuiController(
                    Game.GraphicsDevice.Gl,
                    Game.Window,
                    Game.Input.Context,
                    () => ConfigureIoFontAtlas(cjkFontPath));
            }
            catch (Exception ex)
            {
                _controller = new ImGuiController(Game.GraphicsDevice.Gl, Game.Window, Game.Input.Context);
                _demoGame.UpdateStatus($"Failed to initialize CJK UI font: {ex.Message}");
            }
        }
        else
        {
            _controller = new ImGuiController(Game.GraphicsDevice.Gl, Game.Window, Game.Input.Context);
        }

        ImGuiStylePtr style = ImGui.GetStyle();
        style.WindowRounding = 10.0f;
        style.FrameRounding = 6.0f;
        style.GrabRounding = 6.0f;

        _speechDictionaryDirectory = _demoGame.SpeechDictionaryDirectory ?? string.Empty;
        _speechDictionaryLanguageIndex = (int)_demoGame.SpeechDictionaryLanguage;
    }

    public override void Draw(GameTime gameTime)
    {
        if (Game is null || _controller is null)
        {
            return;
        }

        _demoGame.PresentSceneToBackBuffer();
        _controller.Update((float)gameTime.ElapsedSeconds);

        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        ImGui.DockSpaceOverViewport();

        DrawViewport();
        DrawHud(io);
        DrawControlPanel();

        _canInteractWithScenePointer = _isViewportHovered;
        _canInteractWithSceneKeyboard = _isViewportFocused;
        WantsPointerCapture = io.WantCaptureMouse;
        WantsKeyboardCapture = io.WantCaptureKeyboard;

        _controller.Render();
    }

    public override void Dispose()
    {
        _controller?.Dispose();
        _controller = null;
        base.Dispose();
    }

    private void DrawHud(ImGuiIOPtr io)
    {
        ImGui.SetNextWindowPos(new Vector2(14.0f, 14.0f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.78f);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav;

        if (!ImGui.Begin("HUD", flags))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("Zhengyan.DigitalWife.Mmd.Game Demo");
        ImGui.Text($"FPS {io.Framerate:F1}");
        ImGui.TextWrapped(_demoGame.StatusMessage);

        string audioStatus = _demoGame.AudioStatusMessage ?? (_demoGame.IsAudioAvailable ? "Audio enabled." : "Audio disabled.");
        ImGui.TextWrapped(audioStatus);
        ImGui.TextWrapped(_demoGame.BackgroundMusicStatus);

        string modelSummary = _demoGame.HasModels
            ? $"{_demoGame.Models.Count} model(s), active: {Path.GetFileName(_demoGame.ActiveModel.ModelPath ?? "No Model")}"
            : "(none)";
        ImGui.TextWrapped($"PMX: {modelSummary}");
        if (_demoGame.SelectedModel is not null)
        {
            string motionPath = _demoGame.SelectedModel.MotionPath ?? "(none)";
            ImGui.TextWrapped($"VMD: {motionPath} (layers: {_demoGame.SelectedModel.MotionLayerCount})");
        }

        ImGui.Separator();
        ImGui.TextWrapped(_demoGame.IsFileDropSupported
            ? "Drag .pmx/.vmd/.wav/.ogg files onto the window."
            : "File drag-and-drop is not available on the current runtime.");
        ImGui.TextWrapped("Native file dialogs are disabled for cross-platform consistency. Use path inputs in Scene Files.");
        ImGui.TextWrapped("Camera: RMB orbit, MMB pan, Alt+LMB/MMB orbit, Alt+RMB dolly, Wheel zoom.");

        ImGui.End();
    }

    private void DrawViewport()
    {
        ImGui.SetNextWindowSize(new Vector2(960.0f, 720.0f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Viewport"))
        {
            _isViewportHovered = false;
            _isViewportFocused = false;
            ImGui.End();
            return;
        }

        _isViewportHovered = ImGui.IsWindowHovered();
        _isViewportFocused = ImGui.IsWindowFocused();

        Vector2 available = ImGui.GetContentRegionAvail();
        int viewportWidth = Math.Max((int)available.X, 1);
        int viewportHeight = Math.Max((int)available.Y, 1);
        _demoGame.SetSceneViewportSize(viewportWidth, viewportHeight);

        ImGui.Image(
            (nint)_demoGame.SceneRenderTarget.ColorTextureId,
            new Vector2(viewportWidth, viewportHeight),
            new Vector2(0.0f, 1.0f),
            new Vector2(1.0f, 0.0f));

        ImGui.End();
    }

    private void DrawControlPanel()
    {
        ImGui.SetNextWindowSize(new Vector2(460.0f, 760.0f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Scene Controls"))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("Playback");
        ImGui.Separator();

        PmxModelComponent? model = _demoGame.SelectedModel;
        if (model is not null)
        {
            string playLabel = model.IsPlaying ? "Pause" : "Play";
            if (ImGui.Button(playLabel))
            {
                model.IsPlaying = !model.IsPlaying;
            }

            ImGui.SameLine();
            if (ImGui.Button("Reset Animation"))
            {
                model.ResetAnimation();
            }

            ImGui.SameLine();
            if (ImGui.Button("Reset Camera"))
            {
                _demoGame.ResetCamera();
            }

            if (ImGui.Button("Clear Motion"))
            {
                _demoGame.TryClearMotion();
            }

            bool loopMotion = model.LoopMotion;
            if (ImGui.Checkbox("Loop Motion", ref loopMotion))
            {
                model.LoopMotion = loopMotion;
            }

            bool resetPhysicsOnLoop = model.ResetPhysicsOnMotionLoop;
            if (ImGui.Checkbox("Reset Physics On Loop (All Layers)", ref resetPhysicsOnLoop))
            {
                model.ResetPhysicsOnMotionLoop = resetPhysicsOnLoop;
            }
        }
        else
        {
            ImGui.TextUnformatted("No PMX model loaded.");
        }

        int timingModeIndex = (int)_demoGame.AnimationTimingMode;
        if (ImGui.Combo("Timing Mode", ref timingModeIndex, AnimationTimingModeLabels, AnimationTimingModeLabels.Length))
        {
            _demoGame.AnimationTimingMode = (AnimationTimingMode)Math.Clamp(timingModeIndex, 0, AnimationTimingModeLabels.Length - 1);
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Background");
        ImGui.Separator();

        Vector4 backgroundColor = _demoGame.BackgroundColor;
        Vector3 backgroundRgb = new(backgroundColor.X, backgroundColor.Y, backgroundColor.Z);
        if (ImGui.ColorEdit3("Background Color", ref backgroundRgb))
        {
            _demoGame.BackgroundColor = new Vector4(backgroundRgb, backgroundColor.W);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Reset##backgroundColor"))
        {
            _demoGame.ResetBackgroundColor();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Water");
        ImGui.Separator();

        if (_demoGame.WaterSurface is not null)
        {
            bool waterVisible = _demoGame.WaterSurface.Visible;
            if (ImGui.Checkbox("Water Visible", ref waterVisible))
            {
                _demoGame.WaterSurface.Visible = waterVisible;
            }

            float waterAlpha = _demoGame.WaterSurface.Alpha;
            if (ImGui.SliderFloat("Water Alpha", ref waterAlpha, 0.0f, 1.0f, "%.2f"))
            {
                _demoGame.WaterSurface.Alpha = waterAlpha;
            }

            float waveSpeed = _demoGame.WaterSurface.AnimationSpeed;
            if (ImGui.SliderFloat("Wave Speed", ref waveSpeed, 0.0f, 0.3f, "%.3f"))
            {
                _demoGame.WaterSurface.AnimationSpeed = waveSpeed;
            }

            float waveTiling = _demoGame.WaterSurface.NormalTiling;
            if (ImGui.SliderFloat("Wave Tiling", ref waveTiling, 5.0f, 200.0f, "%.1f"))
            {
                _demoGame.WaterSurface.NormalTiling = waveTiling;
            }

            Vector3 deepColor = _demoGame.WaterSurface.DeepColor;
            if (ImGui.ColorEdit3("Water Deep Color", ref deepColor))
            {
                _demoGame.WaterSurface.DeepColor = deepColor;
            }

            Vector3 reflectionColor = _demoGame.WaterSurface.ReflectionTint;
            if (ImGui.ColorEdit3("Water Reflection", ref reflectionColor))
            {
                _demoGame.WaterSurface.ReflectionTint = reflectionColor;
            }

            float skyMix = _demoGame.WaterSurface.SkyReflectionStrength;
            if (ImGui.SliderFloat("Sky Mix", ref skyMix, 0.0f, 1.0f, "%.2f"))
            {
                _demoGame.WaterSurface.SkyReflectionStrength = skyMix;
            }
        }
        else
        {
            ImGui.TextDisabled("Water surface resource is unavailable.");
            if (!string.IsNullOrWhiteSpace(_demoGame.WaterSurfaceUnavailableReason))
            {
                ImGui.TextWrapped(_demoGame.WaterSurfaceUnavailableReason);
            }
        }

        DrawParticleControls();

        ImGui.Spacing();
        ImGui.TextUnformatted("Background Music");
        ImGui.Separator();

        ImGui.InputText("BGM Path", ref _bgmPath, 512);
        ImGui.SameLine();
        if (ImGui.SmallButton("Paste##bgmPath"))
        {
            PasteClipboard(ref _bgmPath);
        }

        if (ImGui.Button("Load BGM Path"))
        {
            if (string.IsNullOrWhiteSpace(_bgmPath))
            {
                _demoGame.UpdateStatus("BGM path is empty.");
            }
            else
            {
                _demoGame.TryLoadBackgroundMusic(_bgmPath);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button(_demoGame.IsBackgroundMusicPlaying ? "Pause BGM" : "Play BGM"))
        {
            _demoGame.ToggleBackgroundMusic();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset BGM"))
        {
            _demoGame.ResetBackgroundMusic();
        }

        if (_demoGame.HasBackgroundMusic)
        {
            bool looping = _demoGame.BackgroundMusicLooping;
            if (ImGui.Checkbox("Loop", ref looping))
            {
                _demoGame.BackgroundMusicLooping = looping;
            }

            float volume = _demoGame.BackgroundMusicVolume;
            if (ImGui.SliderFloat("Volume", ref volume, 0.0f, 4.0f, "%.2f"))
            {
                _demoGame.BackgroundMusicVolume = volume;
            }
        }
        else
        {
            ImGui.TextDisabled("No BGM loaded.");
        }

        ImGui.TextWrapped(_demoGame.BackgroundMusicStatus);

        ImGui.Spacing();
        ImGui.TextUnformatted("Scene Files");
        ImGui.Separator();

        ImGui.InputText("PMX Path", ref _pmxPath, 512);
        ImGui.SameLine();
        if (ImGui.SmallButton("Paste##pmxPath"))
        {
            PasteClipboard(ref _pmxPath);
        }

        if (ImGui.Button("Load PMX Path"))
        {
            if (string.IsNullOrWhiteSpace(_pmxPath))
            {
                _demoGame.UpdateStatus("PMX path is empty.");
            }
            else
            {
                _demoGame.TryLoadModels([_pmxPath]);
            }
        }

        ImGui.InputText("VMD Path", ref _vmdPath, 512);
        ImGui.SameLine();
        if (ImGui.SmallButton("Paste##vmdPath"))
        {
            PasteClipboard(ref _vmdPath);
        }

        if (ImGui.Button("Load VMD Path"))
        {
            if (string.IsNullOrWhiteSpace(_vmdPath))
            {
                _demoGame.UpdateStatus("VMD path is empty.");
            }
            else
            {
                _demoGame.TryApplyMotionToActiveModel(_vmdPath);
            }
        }

        DrawMotionLayerEditor(model);

        ImGui.Spacing();
        ImGui.TextUnformatted("Models");
        ImGui.Separator();
        ImGui.Checkbox("Bind Model Transform", ref _bindRelationTransform);
        ImGui.SameLine();
        ImGui.Checkbox("Bind Lighting", ref _bindRelationLighting);

        if (_demoGame.HasModels)
        {
            bool modelListVisible = ImGui.BeginChild("ModelList", new Vector2(0.0f, 190.0f), ImGuiChildFlags.None);
            if (modelListVisible)
            {
                int modelToRemove = -1;
                int activeIndex = _demoGame.ActiveModelIndex;
                for (int i = 0; i < _demoGame.Models.Count; i++)
                {
                    PmxModelComponent item = _demoGame.Models[i];
                    bool isActive = i == activeIndex;
                    string label = $"{i + 1}. {Path.GetFileName(item.ModelPath ?? "No Model")}";

                    ImGui.PushID(i);
                    ImGui.TextUnformatted(isActive ? $"{label}  [active]" : label);
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Select"))
                    {
                        _demoGame.SetActiveModel(i);
                    }

                    ImGui.SameLine();
                    if (ImGui.SmallButton("Clear Motion"))
                    {
                        _demoGame.TryClearMotionForModel(item);
                    }

                    ImGui.SameLine();
                    if (ImGui.SmallButton("Delete"))
                    {
                        modelToRemove = i;
                    }

                    ImGui.TextDisabled($"Motion: {Path.GetFileName(item.MotionPath ?? "(none)")}");
                    DrawRelationBindingSelector(i);
                    ImGui.Separator();
                    ImGui.PopID();
                }

                if (modelToRemove >= 0)
                {
                    _demoGame.TryRemoveModel(modelToRemove);
                }
            }

            ImGui.EndChild();
        }
        else
        {
            ImGui.TextUnformatted("No models loaded yet.");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Speech Lip Sync");
        ImGui.Separator();

        if (_demoGame.SelectedModel is not null)
        {
            ImGui.InputText("Dic Directory", ref _speechDictionaryDirectory, 512);
            ImGui.SameLine();
            if (ImGui.SmallButton("Paste##dicPath"))
            {
                PasteClipboard(ref _speechDictionaryDirectory);
            }

            ImGui.Combo("Dictionary Language", ref _speechDictionaryLanguageIndex, SpeechDictionaryLanguageLabels, SpeechDictionaryLanguageLabels.Length);

            ImGui.InputText("Speech Text", ref _speechText, 512);
            ImGui.SameLine();
            if (ImGui.SmallButton("Paste##speechText"))
            {
                PasteClipboard(ref _speechText);
            }

            ImGui.SliderInt("Frame Period (ms)", ref _speechFramePeriodMs, 60, 500);
            ImGui.Checkbox("Speech Loop", ref _speechLoop);

            if (ImGui.Button("Play Lip Sync"))
            {
                _demoGame.TryStartSpeechOnActiveModel(
                    _speechText,
                    _speechDictionaryDirectory,
                    (SpeechDictionaryLanguage)Math.Clamp(_speechDictionaryLanguageIndex, 0, SpeechDictionaryLanguageLabels.Length - 1),
                    _speechFramePeriodMs,
                    _speechLoop);
            }

            ImGui.SameLine();
            if (ImGui.Button("Stop Lip Sync"))
            {
                _demoGame.TryStopSpeechOnActiveModel();
            }
        }
        else
        {
            ImGui.TextDisabled("Load a PMX model to use speech lip sync.");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Lighting");
        ImGui.Separator();

        Vector3 lightColor = _demoGame.LightColor;
        if (ImGui.ColorEdit3("Light Color", ref lightColor))
        {
            _demoGame.LightColor = lightColor;
            _demoGame.ApplySceneLighting();
        }

        Vector3 ambientLightColor = _demoGame.AmbientLightColor;
        if (ImGui.ColorEdit3("Ambient Light", ref ambientLightColor))
        {
            _demoGame.AmbientLightColor = ambientLightColor;
            _demoGame.ApplySceneLighting();
        }

        float ambientStrength = _demoGame.AmbientLightStrength;
        if (ImGui.SliderFloat("Ambient Strength", ref ambientStrength, 0.0f, 1.0f, "%.2f"))
        {
            _demoGame.AmbientLightStrength = ambientStrength;
            _demoGame.ApplySceneLighting();
        }

        Vector3 lightDirection = _demoGame.LightDirection;
        if (ImGui.DragFloat3("Light Direction", ref lightDirection, 0.02f))
        {
            _demoGame.LightDirection = lightDirection;
            _demoGame.ApplySceneLighting();
        }

        Vector4 shadowColor = _demoGame.ShadowColor;
        if (ImGui.ColorEdit4("Shadow Color", ref shadowColor))
        {
            _demoGame.ShadowColor = shadowColor;
            _demoGame.ApplySceneLighting();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Transform");
        ImGui.Separator();

        if (model is not null)
        {
            Vector3 position = model.Position;
            if (ImGui.DragFloat3("Position", ref position, 0.01f))
            {
                model.Position = position;
            }

            Vector3 scale = model.Scale;
            if (ImGui.DragFloat3("Scale", ref scale, 0.01f))
            {
                model.Scale = scale;
            }

            float playbackSpeed = model.PlaybackSpeed;
            if (ImGui.SliderFloat("Playback Speed", ref playbackSpeed, 0.0f, 3.0f, "%.2f"))
            {
                model.PlaybackSpeed = playbackSpeed;
            }

            float animationTime = model.AnimationTimeSeconds;
            if (ImGui.DragFloat("Animation Time (s)", ref animationTime, 0.01f, 0.0f, 9999.0f, "%.2f"))
            {
                model.AnimationTimeSeconds = animationTime;
            }

            bool enablePhysical = model.EnablePhysical;
            if (ImGui.Checkbox("Enable Physical", ref enablePhysical))
            {
                model.EnablePhysical = enablePhysical;
            }

            bool enableEdge = model.EnableEdge;
            if (ImGui.Checkbox("Enable Edge", ref enableEdge))
            {
                model.EnableEdge = enableEdge;
            }

            bool enableShadow = model.EnableShadow;
            if (ImGui.Checkbox("Enable Shadow", ref enableShadow))
            {
                model.EnableShadow = enableShadow;
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Scene Stats");
        ImGui.Separator();
        if (model is not null)
        {
            ImGui.Text($"Vertices: {model.VertexCount}");
            ImGui.Text($"Meshes: {model.MeshCount}");
            ImGui.Text($"Materials: {model.MaterialCount}");
            ImGui.Text($"Animation Loaded: {(model.HasAnimation ? "Yes" : "No")}");
            ImGui.Text($"Opaque Draw Calls: {model.LastOpaqueMeshDrawCount}");
            ImGui.Text($"Edge Draw Calls: {model.LastEdgeMeshDrawCount}");
            ImGui.Text($"Shadow Draw Calls: {model.LastShadowMeshDrawCount}");
            ImGui.Text($"Bounds Min: {FormatVector3(model.BoundsMin)}");
            ImGui.Text($"Bounds Max: {FormatVector3(model.BoundsMax)}");
        }
        else
        {
            ImGui.TextUnformatted("No active model.");
        }

        bool showAxes = _demoGame.DebugAxes.VisibleAxes;
        if (ImGui.Checkbox("Show Debug Axes", ref showAxes))
        {
            _demoGame.DebugAxes.VisibleAxes = showAxes;
        }

        bool showLightArrow = _demoGame.DebugAxes.VisibleLightArrow;
        if (ImGui.Checkbox("Show Light Arrow", ref showLightArrow))
        {
            _demoGame.DebugAxes.VisibleLightArrow = showLightArrow;
        }

        ImGui.End();
    }

    private readonly record struct ParticleEntry(string Label, ParticleSystemComponent System);

    private void DrawParticleControls()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Particles");
        ImGui.Separator();

        List<ParticleEntry> systems = GetParticleEntries();
        if (systems.Count == 0)
        {
            ImGui.TextDisabled("Particle systems are unavailable.");
            return;
        }

        foreach (ParticleEntry entry in systems)
        {
            DrawParticleRow(entry.Label, entry.System);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Particle Parameters");

        if (_selectedParticleEditorIndex < 0 || _selectedParticleEditorIndex >= systems.Count)
        {
            _selectedParticleEditorIndex = 0;
        }

        string[] labels = new string[systems.Count];
        for (int i = 0; i < systems.Count; i++)
        {
            labels[i] = systems[i].Label;
        }

        if (ImGui.Combo("Edit Target", ref _selectedParticleEditorIndex, labels, labels.Length))
        {
            SyncParticleEditorFromSystem(systems[_selectedParticleEditorIndex], true);
        }

        ParticleEntry selected = systems[_selectedParticleEditorIndex];
        SyncParticleEditorFromSystem(selected, false);
        DrawParticleParameterEditor(selected);
    }

    private static void DrawParticleRow(string label, ParticleSystemComponent system)
    {
        ImGui.PushID(label);

        bool visible = system.Visible;
        if (ImGui.Checkbox(label, ref visible))
        {
            system.Visible = visible;
        }

        ImGui.SameLine();
        float opacity = system.Opacity;
        if (ImGui.SliderFloat("Opacity", ref opacity, 0.0f, 1.0f, "%.2f"))
        {
            system.Opacity = opacity;
        }

        float simulationSpeed = system.SimulationSpeed;
        if (ImGui.SliderFloat("Speed", ref simulationSpeed, 0.0f, 3.0f, "%.2f"))
        {
            system.SimulationSpeed = simulationSpeed;
        }

        ImGui.TextDisabled($"Count: {system.ParticleCount}");
        ImGui.PopID();
    }

    private List<ParticleEntry> GetParticleEntries()
    {
        List<ParticleEntry> systems = [];
        if (_demoGame.CloudParticles is not null)
        {
            systems.Add(new ParticleEntry("Cloud", _demoGame.CloudParticles));
        }

        if (_demoGame.RainParticles is not null)
        {
            systems.Add(new ParticleEntry("Rain", _demoGame.RainParticles));
        }

        if (_demoGame.SnowParticles is not null)
        {
            systems.Add(new ParticleEntry("Snow", _demoGame.SnowParticles));
        }

        if (_demoGame.SakuraParticles is not null)
        {
            systems.Add(new ParticleEntry("Sakura", _demoGame.SakuraParticles));
        }

        if (_demoGame.WaterfallParticles is not null)
        {
            systems.Add(new ParticleEntry("Waterfall", _demoGame.WaterfallParticles));
        }

        if (_demoGame.StreamParticles is not null)
        {
            systems.Add(new ParticleEntry("Stream", _demoGame.StreamParticles));
        }

        if (_demoGame.FireParticles is not null)
        {
            systems.Add(new ParticleEntry("Fire", _demoGame.FireParticles));
        }

        return systems;
    }

    private void SyncParticleEditorFromSystem(ParticleEntry entry, bool force)
    {
        bool targetChanged = !ReferenceEquals(_particleEditorTarget, entry.System);
        if (!force && !targetChanged && _particleEditorSettings is not null)
        {
            return;
        }

        _particleEditorTarget = entry.System;
        _particleEditorSettings = entry.System.GetSettingsSnapshot();
        NormalizeParticleEditorSettings(_particleEditorSettings);
        _particleEditorPosition = entry.System.Position;

        if (targetChanged || string.IsNullOrWhiteSpace(_particlePresetPath))
        {
            _particlePresetPath = BuildDefaultParticlePresetPath(entry.Label);
        }
    }

    private void DrawParticleParameterEditor(ParticleEntry entry)
    {
        ParticleSystemSettings? settings = _particleEditorSettings;
        if (settings is null)
        {
            ImGui.TextDisabled("No particle system selected.");
            return;
        }

        ImGui.TextDisabled($"Editing: {entry.Label}");

        string name = settings.Name;
        if (ImGui.InputText("System Name", ref name, 128))
        {
            settings.Name = name;
        }

        int particleCount = settings.ParticleCount;
        if (ImGui.SliderInt("Particle Count", ref particleCount, 1, 4000))
        {
            settings.ParticleCount = particleCount;
        }

        Vector3 emitterPosition = _particleEditorPosition;
        if (ImGui.DragFloat3("Emitter Position", ref emitterPosition, 0.02f))
        {
            _particleEditorPosition = emitterPosition;
        }

        Vector3 spawnExtents = settings.SpawnBoxHalfExtents;
        if (ImGui.DragFloat3("Spawn Half Extents", ref spawnExtents, 0.02f))
        {
            settings.SpawnBoxHalfExtents = ClampVector3NonNegative(spawnExtents);
        }

        Vector3 baseVelocity = settings.BaseVelocity;
        if (ImGui.DragFloat3("Base Velocity", ref baseVelocity, 0.02f))
        {
            settings.BaseVelocity = baseVelocity;
        }

        Vector3 velocityJitter = settings.VelocityJitter;
        if (ImGui.DragFloat3("Velocity Jitter", ref velocityJitter, 0.02f))
        {
            settings.VelocityJitter = ClampVector3NonNegative(velocityJitter);
        }

        Vector3 acceleration = settings.Acceleration;
        if (ImGui.DragFloat3("Acceleration", ref acceleration, 0.02f))
        {
            settings.Acceleration = acceleration;
        }

        float minLifetime = settings.MinLifetime;
        if (ImGui.DragFloat("Min Lifetime", ref minLifetime, 0.01f, 0.01f, 60.0f, "%.2f"))
        {
            settings.MinLifetime = minLifetime;
        }

        float maxLifetime = settings.MaxLifetime;
        if (ImGui.DragFloat("Max Lifetime", ref maxLifetime, 0.01f, 0.01f, 60.0f, "%.2f"))
        {
            settings.MaxLifetime = maxLifetime;
        }

        float minSize = settings.MinSize;
        if (ImGui.DragFloat("Min Size", ref minSize, 0.002f, 0.001f, 20.0f, "%.3f"))
        {
            settings.MinSize = minSize;
        }

        float maxSize = settings.MaxSize;
        if (ImGui.DragFloat("Max Size", ref maxSize, 0.002f, 0.001f, 20.0f, "%.3f"))
        {
            settings.MaxSize = maxSize;
        }

        float startSizeScale = settings.StartSizeScale;
        if (ImGui.DragFloat("Start Size Scale", ref startSizeScale, 0.01f, 0.0f, 10.0f, "%.3f"))
        {
            settings.StartSizeScale = startSizeScale;
        }

        float endSizeScale = settings.EndSizeScale;
        if (ImGui.DragFloat("End Size Scale", ref endSizeScale, 0.01f, 0.0f, 10.0f, "%.3f"))
        {
            settings.EndSizeScale = endSizeScale;
        }

        float widthScale = settings.WidthScale;
        if (ImGui.DragFloat("Width Scale", ref widthScale, 0.01f, 0.01f, 10.0f, "%.3f"))
        {
            settings.WidthScale = widthScale;
        }

        float heightScale = settings.HeightScale;
        if (ImGui.DragFloat("Height Scale", ref heightScale, 0.01f, 0.01f, 10.0f, "%.3f"))
        {
            settings.HeightScale = heightScale;
        }

        float minRotation = settings.MinRotationSpeedRadians;
        if (ImGui.DragFloat("Min Rotation Speed", ref minRotation, 0.01f, -20.0f, 20.0f, "%.3f"))
        {
            settings.MinRotationSpeedRadians = minRotation;
        }

        float maxRotation = settings.MaxRotationSpeedRadians;
        if (ImGui.DragFloat("Max Rotation Speed", ref maxRotation, 0.01f, -20.0f, 20.0f, "%.3f"))
        {
            settings.MaxRotationSpeedRadians = maxRotation;
        }

        Vector4 startColor = settings.StartColor;
        if (ImGui.ColorEdit4("Start Color", ref startColor))
        {
            settings.StartColor = ClampColor(startColor);
        }

        Vector4 endColor = settings.EndColor;
        if (ImGui.ColorEdit4("End Color", ref endColor))
        {
            settings.EndColor = ClampColor(endColor);
        }

        bool randomizeInitialAge = settings.RandomizeInitialAge;
        if (ImGui.Checkbox("Randomize Initial Age", ref randomizeInitialAge))
        {
            settings.RandomizeInitialAge = randomizeInitialAge;
        }

        int blendModeIndex = (int)settings.BlendMode;
        if (ImGui.Combo("Blend Mode", ref blendModeIndex, ParticleBlendModeLabels, ParticleBlendModeLabels.Length))
        {
            settings.BlendMode = (ParticleBlendMode)Math.Clamp(blendModeIndex, 0, ParticleBlendModeLabels.Length - 1);
        }

        int orientationIndex = (int)settings.OrientationMode;
        if (ImGui.Combo("Orientation", ref orientationIndex, ParticleOrientationModeLabels, ParticleOrientationModeLabels.Length))
        {
            settings.OrientationMode = (ParticleOrientationMode)Math.Clamp(orientationIndex, 0, ParticleOrientationModeLabels.Length - 1);
        }

        int texturePresetIndex = (int)settings.TexturePreset;
        if (ImGui.Combo("Texture Preset", ref texturePresetIndex, ParticleTexturePresetLabels, ParticleTexturePresetLabels.Length))
        {
            settings.TexturePreset = (ParticleTexturePreset)Math.Clamp(texturePresetIndex, 0, ParticleTexturePresetLabels.Length - 1);
        }

        bool useTextureColor = settings.UseTextureColor;
        if (ImGui.Checkbox("Use Texture Color", ref useTextureColor))
        {
            settings.UseTextureColor = useTextureColor;
        }

        bool preventDarkening = settings.PreventDarkening;
        if (ImGui.Checkbox("Prevent Darkening", ref preventDarkening))
        {
            settings.PreventDarkening = preventDarkening;
        }

        string texturePath = settings.TexturePath ?? string.Empty;
        if (ImGui.InputText("Texture Path", ref texturePath, 512))
        {
            settings.TexturePath = texturePath;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Paste##particleTexturePath"))
        {
            PasteClipboard(ref texturePath);
            settings.TexturePath = texturePath;
        }

        NormalizeParticleEditorSettings(settings);

        if (ImGui.Button("Apply Parameters"))
        {
            TryApplyParticleEditor(entry.System);
        }

        ImGui.SameLine();
        if (ImGui.Button("Reload Runtime Values"))
        {
            SyncParticleEditorFromSystem(entry, true);
            _demoGame.UpdateStatus($"Reloaded particle settings from '{entry.Label}'.");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Preset");
        ImGui.InputText("Preset Path", ref _particlePresetPath, 512);
        ImGui.SameLine();
        if (ImGui.SmallButton("Paste##particlePresetPath"))
        {
            PasteClipboard(ref _particlePresetPath);
        }

        if (ImGui.Button("Save Preset"))
        {
            TrySaveParticlePreset();
        }

        ImGui.SameLine();
        if (ImGui.Button("Load Preset"))
        {
            TryLoadParticlePreset();
        }

        ImGui.SameLine();
        if (ImGui.Button("Load + Apply"))
        {
            if (TryLoadParticlePreset())
            {
                TryApplyParticleEditor(entry.System);
            }
        }
    }

    private bool TryApplyParticleEditor(ParticleSystemComponent system)
    {
        ParticleSystemSettings? settings = _particleEditorSettings;
        if (settings is null)
        {
            _demoGame.UpdateStatus("Particle editor state is unavailable.");
            return false;
        }

        try
        {
            NormalizeParticleEditorSettings(settings);
            ParticleSystemSettings runtimeSettings = settings.Clone();
            runtimeSettings.TexturePath = string.IsNullOrWhiteSpace(runtimeSettings.TexturePath)
                ? null
                : runtimeSettings.TexturePath.Trim();
            runtimeSettings.Validate();

            system.ApplySettings(runtimeSettings, resetParticles: true);
            system.Position = _particleEditorPosition;
            _particleEditorSettings = system.GetSettingsSnapshot();

            _demoGame.UpdateStatus($"Applied particle settings to '{system.Name}'.");
            return true;
        }
        catch (Exception ex)
        {
            _demoGame.UpdateStatus($"Failed to apply particle settings: {ex.Message}");
            return false;
        }
    }

    private bool TrySaveParticlePreset()
    {
        ParticleSystemSettings? settings = _particleEditorSettings;
        if (settings is null)
        {
            _demoGame.UpdateStatus("Particle editor state is unavailable.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_particlePresetPath))
        {
            _demoGame.UpdateStatus("Preset path is empty.");
            return false;
        }

        try
        {
            NormalizeParticleEditorSettings(settings);
            ParticleSystemPresetStore.Save(_particlePresetPath, settings);
            _demoGame.UpdateStatus($"Saved particle preset: {Path.GetFullPath(_particlePresetPath)}");
            return true;
        }
        catch (Exception ex)
        {
            _demoGame.UpdateStatus($"Failed to save particle preset: {ex.Message}");
            return false;
        }
    }

    private bool TryLoadParticlePreset()
    {
        if (string.IsNullOrWhiteSpace(_particlePresetPath))
        {
            _demoGame.UpdateStatus("Preset path is empty.");
            return false;
        }

        try
        {
            ParticleSystemSettings settings = ParticleSystemPresetStore.Load(_particlePresetPath);
            NormalizeParticleEditorSettings(settings);
            _particleEditorSettings = settings;
            _demoGame.UpdateStatus($"Loaded particle preset: {Path.GetFullPath(_particlePresetPath)}");
            return true;
        }
        catch (Exception ex)
        {
            _demoGame.UpdateStatus($"Failed to load particle preset: {ex.Message}");
            return false;
        }
    }

    private static void NormalizeParticleEditorSettings(ParticleSystemSettings settings)
    {
        settings.Name = string.IsNullOrWhiteSpace(settings.Name) ? "Particles" : settings.Name.Trim();
        settings.ParticleCount = Math.Max(settings.ParticleCount, 1);
        settings.SpawnBoxHalfExtents = ClampVector3NonNegative(settings.SpawnBoxHalfExtents);
        settings.VelocityJitter = ClampVector3NonNegative(settings.VelocityJitter);
        settings.MinLifetime = Math.Clamp(settings.MinLifetime, 0.01f, 120.0f);
        settings.MaxLifetime = Math.Clamp(settings.MaxLifetime, settings.MinLifetime, 120.0f);
        settings.MinSize = Math.Clamp(settings.MinSize, 0.001f, 20.0f);
        settings.MaxSize = Math.Clamp(settings.MaxSize, settings.MinSize, 20.0f);
        settings.StartSizeScale = Math.Clamp(settings.StartSizeScale, 0.0f, 20.0f);
        settings.EndSizeScale = Math.Clamp(settings.EndSizeScale, 0.0f, 20.0f);
        settings.WidthScale = Math.Clamp(settings.WidthScale, 0.01f, 20.0f);
        settings.HeightScale = Math.Clamp(settings.HeightScale, 0.01f, 20.0f);
        settings.MaxRotationSpeedRadians = Math.Max(settings.MaxRotationSpeedRadians, settings.MinRotationSpeedRadians);
        settings.StartColor = ClampColor(settings.StartColor);
        settings.EndColor = ClampColor(settings.EndColor);
        settings.TexturePath = string.IsNullOrWhiteSpace(settings.TexturePath) ? null : settings.TexturePath.Trim();
    }

    private static Vector3 ClampVector3NonNegative(Vector3 value)
    {
        return new Vector3(
            Math.Max(0.0f, value.X),
            Math.Max(0.0f, value.Y),
            Math.Max(0.0f, value.Z));
    }

    private static Vector4 ClampColor(Vector4 value)
    {
        return new Vector4(
            Math.Clamp(value.X, 0.0f, 1.0f),
            Math.Clamp(value.Y, 0.0f, 1.0f),
            Math.Clamp(value.Z, 0.0f, 1.0f),
            Math.Clamp(value.W, 0.0f, 1.0f));
    }

    private static string BuildDefaultParticlePresetPath(string systemLabel)
    {
        string safeLabel = ToSafeFileName(string.IsNullOrWhiteSpace(systemLabel) ? "Particles" : systemLabel);
        return Path.Combine(AppContext.BaseDirectory, "Resources", "ParticlePresets", $"{safeLabel}.json");
    }

    private static string ToSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Particles";
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] chars = value.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private void DrawMotionLayerEditor(PmxModelComponent? model)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Motion Layers");
        ImGui.Separator();

        if (model is null)
        {
            ImGui.TextDisabled("Load a PMX model to edit motion layers.");
            return;
        }

        float newLayerWeight = _newMotionLayerWeight;
        if (ImGui.SliderFloat("New Layer Weight", ref newLayerWeight, 0.0f, 1.0f, "%.2f"))
        {
            _newMotionLayerWeight = newLayerWeight;
        }

        if (ImGui.Button("Add VMD As Layer"))
        {
            if (string.IsNullOrWhiteSpace(_vmdPath))
            {
                _demoGame.UpdateStatus("VMD path is empty.");
            }
            else
            {
                try
                {
                    model.AddMotionLayer(_vmdPath, _newMotionLayerWeight);
                    _demoGame.UpdateStatus($"Added motion layer: {Path.GetFileName(_vmdPath)} (weight {_newMotionLayerWeight:F2}).");
                }
                catch (Exception ex)
                {
                    _demoGame.UpdateStatus($"Failed to add motion layer: {ex.Message}");
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Replace With VMD"))
        {
            if (string.IsNullOrWhiteSpace(_vmdPath))
            {
                _demoGame.UpdateStatus("VMD path is empty.");
            }
            else
            {
                _demoGame.TryApplyMotionToModel(model, _vmdPath);
            }
        }

        IReadOnlyList<MotionLayerInfo> layers = model.GetMotionLayers();
        ImGui.Text($"Layer Count: {layers.Count}");

        if (layers.Count == 0)
        {
            ImGui.TextDisabled("No motion layers loaded.");
            return;
        }

        bool layerListVisible = ImGui.BeginChild("MotionLayerList", new Vector2(0.0f, 190.0f), ImGuiChildFlags.None);
        if (layerListVisible)
        {
            string? removePath = null;
            for (int i = 0; i < layers.Count; i++)
            {
                MotionLayerInfo layer = layers[i];
                float durationSeconds = layer.DurationFrames > 0 ? layer.DurationFrames / 30.0f : 0.0f;

                ImGui.PushID($"motionLayer{i}");
                ImGui.TextUnformatted($"{i + 1}. {Path.GetFileName(layer.MotionPath)}");

                ImGui.SameLine();
                if (ImGui.SmallButton("Use Path"))
                {
                    _vmdPath = layer.MotionPath;
                }

                ImGui.SameLine();
                if (ImGui.SmallButton("Remove"))
                {
                    removePath = layer.MotionPath;
                }

                float weight = layer.Weight;
                if (ImGui.SliderFloat("Weight", ref weight, 0.0f, 1.0f, "%.2f"))
                {
                    try
                    {
                        if (!model.TrySetMotionLayerWeight(layer.MotionPath, weight))
                        {
                            _demoGame.UpdateStatus($"Failed to update layer weight: {Path.GetFileName(layer.MotionPath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _demoGame.UpdateStatus($"Failed to update layer weight: {ex.Message}");
                    }
                }

                bool resetPhysicsOnLoop = layer.ResetPhysicsOnLoop;
                if (ImGui.Checkbox("Reset Physics On Loop", ref resetPhysicsOnLoop))
                {
                    try
                    {
                        if (!model.TrySetMotionLayerResetPhysicsOnLoop(layer.MotionPath, resetPhysicsOnLoop))
                        {
                            _demoGame.UpdateStatus($"Failed to update loop physics setting: {Path.GetFileName(layer.MotionPath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _demoGame.UpdateStatus($"Failed to update loop physics setting: {ex.Message}");
                    }
                }

                ImGui.TextDisabled($"Time: {layer.TimeSeconds:F2}s / {durationSeconds:F2}s");
                ImGui.TextDisabled(layer.MotionPath);
                ImGui.Separator();
                ImGui.PopID();
            }

            if (!string.IsNullOrWhiteSpace(removePath))
            {
                try
                {
                    if (model.RemoveMotionLayer(removePath))
                    {
                        _demoGame.UpdateStatus($"Removed motion layer: {Path.GetFileName(removePath)}.");
                    }
                    else
                    {
                        _demoGame.UpdateStatus($"Motion layer not found: {Path.GetFileName(removePath)}.");
                    }
                }
                catch (Exception ex)
                {
                    _demoGame.UpdateStatus($"Failed to remove motion layer: {ex.Message}");
                }
            }
        }

        ImGui.EndChild();
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.X:F2}, {value.Y:F2}, {value.Z:F2})";
    }

    private static string BuildModelPreview(IReadOnlyList<PmxModelComponent> models, int index)
    {
        string name = Path.GetFileNameWithoutExtension(models[index].ModelPath ?? "Model");
        return $"{index + 1}. {name}";
    }

    private static string BuildRelationPreview(IReadOnlyList<PmxModelComponent> models, int? relationIndex)
    {
        return relationIndex is null ? "(none)" : BuildModelPreview(models, relationIndex.Value);
    }

    private void DrawRelationBindingSelector(int targetIndex)
    {
        IReadOnlyList<PmxModelComponent> models = _demoGame.Models;
        int? currentRelation = _demoGame.GetRelationModelIndexForTarget(targetIndex);
        string preview = BuildRelationPreview(models, currentRelation);

        if (!ImGui.BeginCombo($"Relation##relation{targetIndex}", preview))
        {
            return;
        }

        bool noneSelected = currentRelation is null;
        if (ImGui.Selectable("(none)", noneSelected))
        {
            _demoGame.TrySetModelRelationBinding(targetIndex, null, _bindRelationTransform, _bindRelationLighting);
        }

        if (noneSelected)
        {
            ImGui.SetItemDefaultFocus();
        }

        for (int i = 0; i < models.Count; i++)
        {
            if (i == targetIndex)
            {
                continue;
            }

            bool isSelected = currentRelation == i;
            string label = BuildModelPreview(models, i);
            if (ImGui.Selectable(label, isSelected))
            {
                _demoGame.TrySetModelRelationBinding(targetIndex, i, _bindRelationTransform, _bindRelationLighting);
            }

            if (isSelected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private void PasteClipboard(ref string target)
    {
        if (_demoGame.TryGetClipboardText(out string text))
        {
            target = text;
        }
    }

    private static void ConfigureIoFontAtlas(string fontPath)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.Clear();
        io.Fonts.AddFontFromFileTTF(fontPath, 18.0f, default, io.Fonts.GetGlyphRangesChineseFull());
    }

    private static bool TryGetCjkFontPath(out string fontPath)
    {
        IEnumerable<string> candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? GetWindowsFontCandidates()
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? GetMacFontCandidates()
                : GetLinuxFontCandidates();

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                fontPath = candidate;
                return true;
            }
        }

        fontPath = string.Empty;
        return false;
    }

    private static IEnumerable<string> GetWindowsFontCandidates()
    {
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string fontsDir = Path.Combine(windowsDir, "Fonts");

        return
        [
            Path.Combine(fontsDir, "msyh.ttc"),
            Path.Combine(fontsDir, "msyhbd.ttc"),
            Path.Combine(fontsDir, "simsun.ttc"),
            Path.Combine(fontsDir, "arialuni.ttf"),
            Path.Combine(fontsDir, "meiryo.ttc"),
            Path.Combine(fontsDir, "YuGothM.ttc"),
            Path.Combine(fontsDir, "msgothic.ttc")
        ];
    }

    private static IEnumerable<string> GetLinuxFontCandidates()
    {
        return
        [
            "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJKjp-Regular.otf",
            "/usr/share/fonts/truetype/noto/NotoSansJP-Regular.otf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
        ];
    }

    private static IEnumerable<string> GetMacFontCandidates()
    {
        return
        [
            "/System/Library/Fonts/PingFang.ttc",
            "/System/Library/Fonts/Hiragino Sans GB.ttc",
            "/System/Library/Fonts/Supplemental/Arial Unicode.ttf"
        ];
    }
}

