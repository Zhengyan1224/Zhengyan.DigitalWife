using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.OpenGLES;
using Silk.NET.OpenGLES.Extensions.ImGui;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Samples.GameEditor;

internal sealed class GameEditorOverlayComponent(GameEditorGame editorGame) : DrawableGameComponent
{
    private readonly GameEditorGame _editorGame = editorGame;
    private ImGuiController? _controller;
    private bool _isViewportHovered;
    private bool _isViewportFocused;
    private string _projectDirectory = editorGame.ProjectDirectory;
    private string _newProjectName = editorGame.Project.Name;
    private string _pmxPath = string.Empty;
    private string _audioPath = string.Empty;
    private string _motionPath = string.Empty;
    private string _spritePath = string.Empty;
    private int _selectedMotionAssetIndex;
    private string _particlePreset = "sakura";
    private bool _copyAssets = true;
    private int _preferredLanguageIndex;
    private readonly Dictionary<string, Texture2D> _spriteTextures = new(StringComparer.OrdinalIgnoreCase);

    public bool CanInteractWithScenePointer => _isViewportHovered;

    public bool CanInteractWithSceneKeyboard => _isViewportFocused;

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
            catch
            {
                _controller = new ImGuiController(Game.GraphicsDevice.Gl, Game.Window, Game.Input.Context);
            }
        }
        else
        {
            _controller = new ImGuiController(Game.GraphicsDevice.Gl, Game.Window, Game.Input.Context);
        }

        ImGuiStylePtr style = ImGui.GetStyle();
        style.WindowRounding = 8.0f;
        style.FrameRounding = 5.0f;
        style.GrabRounding = 5.0f;
    }

    public override void Draw(GameTime gameTime)
    {
        if (Game is null || _controller is null)
        {
            return;
        }

        _editorGame.PresentSceneToBackBuffer();
        _controller.Update((float)gameTime.ElapsedSeconds);

        DrawViewport();
        DrawEditorPanel();

        _controller.Render();
    }

    public override void Dispose()
    {
        foreach (Texture2D texture in _spriteTextures.Values)
        {
            texture.Dispose();
        }

        _spriteTextures.Clear();
        _controller?.Dispose();
        _controller = null;
        base.Dispose();
    }

    private void DrawViewport()
    {
        ImGui.SetNextWindowSize(new Vector2(920.0f, 660.0f), ImGuiCond.FirstUseEver);
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
        int width = Math.Max((int)available.X, 1);
        int height = Math.Max((int)available.Y, 1);
        _editorGame.SetSceneViewportSize(width, height);

        Vector2 imageMin = ImGui.GetCursorScreenPos();
        ImGui.Image(
            (nint)_editorGame.SceneRenderTarget.ColorTextureId,
            new Vector2(width, height),
            new Vector2(0.0f, 1.0f),
            new Vector2(1.0f, 0.0f));
        DrawSpritePreview(imageMin, new Vector2(width, height));
        DrawGuiPreview(imageMin, new Vector2(width, height));

        ImGui.End();
    }

    private void DrawEditorPanel()
    {
        ImGui.SetNextWindowSize(new Vector2(520.0f, 820.0f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new Vector2(16.0f, 68.0f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Editor Panel"))
        {
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("EditorPanelTabs"))
        {
            if (ImGui.BeginTabItem("Project"))
            {
                DrawProjectPanel();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Assets"))
            {
                DrawAssetsPanel();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Hierarchy"))
            {
                DrawHierarchyPanel();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Inspector"))
            {
                DrawInspectorPanel();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Status"))
            {
                DrawStatusBar();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    private void DrawSpritePreview(Vector2 viewportMin, Vector2 viewportSize)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(viewportMin, viewportMin + viewportSize, true);

        foreach (SpriteSettings sprite in _editorGame.Project.Scene.Sprites
            .Where(sprite => sprite.Visible && !string.IsNullOrWhiteSpace(sprite.Path))
            .OrderBy(sprite => sprite.DrawOrder))
        {
            Texture2D? texture = GetSpriteTexture(sprite.Path);
            if (texture is null)
            {
                continue;
            }

            Vector2 min = viewportMin + new Vector2(sprite.X, sprite.Y);
            Vector2 max = min + new Vector2(Math.Max(sprite.Width, 1.0f), Math.Max(sprite.Height, 1.0f));
            uint tint = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, Math.Clamp(sprite.Opacity, 0.0f, 1.0f)));
            AddSpriteImage(drawList, texture.Id, min, max, sprite.RotationDegrees, tint);
        }

        drawList.PopClipRect();
    }

    private static void AddSpriteImage(ImDrawListPtr drawList, uint textureId, Vector2 min, Vector2 max, float rotationDegrees, uint tint)
    {
        if (MathF.Abs(rotationDegrees) <= 0.001f)
        {
            drawList.AddImage((nint)textureId, min, max, Vector2.Zero, Vector2.One, tint);
            return;
        }

        Vector2 center = (min + max) * 0.5f;
        Vector2 half = (max - min) * 0.5f;
        float radians = rotationDegrees * MathF.PI / 180.0f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        Vector2 Rotate(Vector2 local)
        {
            return center + new Vector2((local.X * cos) - (local.Y * sin), (local.X * sin) + (local.Y * cos));
        }

        Vector2 p1 = Rotate(new Vector2(-half.X, -half.Y));
        Vector2 p2 = Rotate(new Vector2(half.X, -half.Y));
        Vector2 p3 = Rotate(new Vector2(half.X, half.Y));
        Vector2 p4 = Rotate(new Vector2(-half.X, half.Y));
        drawList.AddImageQuad((nint)textureId, p1, p2, p3, p4, Vector2.Zero, new Vector2(1.0f, 0.0f), Vector2.One, new Vector2(0.0f, 1.0f), tint);
    }

    private Texture2D? GetSpriteTexture(string path)
    {
        if (Game is null)
        {
            return null;
        }

        string fullPath = GameProjectPath.ToAbsolute(_editorGame.ProjectDirectory, path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        if (_spriteTextures.TryGetValue(fullPath, out Texture2D? texture))
        {
            return texture;
        }

        texture = new Texture2D(Game.GraphicsDevice.Gl, GLEnum.ClampToEdge);
        texture.LoadFromFile(fullPath);
        _spriteTextures[fullPath] = texture;
        return texture;
    }

    private void DrawGuiPreview(Vector2 viewportMin, Vector2 viewportSize)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 viewportMax = viewportMin + viewportSize;
        drawList.PushClipRect(viewportMin, viewportMax, true);

        foreach (GuiControlSettings control in _editorGame.Project.Scene.GuiControls)
        {
            if (!control.Visible)
            {
                continue;
            }

            Vector2 position = viewportMin + new Vector2(control.X, control.Y);
            Vector2 size = new(Math.Max(control.Width, 1.0f), Math.Max(control.Height, 1.0f));
            Vector2 max = position + size;
            string text = string.IsNullOrWhiteSpace(control.Text) ? control.Name : control.Text;

            GuiControlStyleSettings style = control.Style;
            Vector4 backgroundColor = style.BackgroundColor.ToVector4();
            Vector4 textColor = style.TextColor.ToVector4();
            Vector4 borderColor = style.BorderColor.ToVector4();
            float rounding = Math.Max(style.Rounding, 0.0f);
            float borderThickness = Math.Max(style.BorderThickness, 0.0f);
            string type = control.Type.ToLowerInvariant();

            if (type == "label")
            {
                Vector2 padding = new(8.0f, 5.0f);
                drawList.AddRectFilled(position, max, ImGui.GetColorU32(backgroundColor), rounding);
                drawList.AddRect(position, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, borderThickness);
                DrawTextBlock(drawList, position + padding, max - padding, text, textColor, style, control.WordWrap);
            }
            else if (type == "checkbox")
            {
                drawList.AddRectFilled(position, max, ImGui.GetColorU32(backgroundColor), rounding);
                drawList.AddRect(position, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, borderThickness);
                float boxSize = MathF.Min(18.0f, Math.Max(12.0f, size.Y - 12.0f));
                Vector2 boxMin = position + new Vector2(8.0f, (size.Y - boxSize) * 0.5f);
                Vector2 boxMax = boxMin + new Vector2(boxSize, boxSize);
                drawList.AddRect(boxMin, boxMax, ImGui.GetColorU32(borderColor), 3.0f, ImDrawFlags.None, 1.2f);
                if (control.Checked)
                {
                    drawList.AddLine(boxMin + new Vector2(3.0f, boxSize * 0.52f), boxMin + new Vector2(boxSize * 0.42f, boxSize - 4.0f), ImGui.GetColorU32(textColor), 2.0f);
                    drawList.AddLine(boxMin + new Vector2(boxSize * 0.42f, boxSize - 4.0f), boxMin + new Vector2(boxSize - 3.0f, 4.0f), ImGui.GetColorU32(textColor), 2.0f);
                }

                Vector2 textSize = ImGui.CalcTextSize(text);
                Vector2 textMin = position + new Vector2(boxSize + 16.0f, 0.0f);
                Vector2 textMax = max - new Vector2(8.0f, 0.0f);
                drawList.AddText(GetAlignedTextPosition(textMin, textMax, textSize, style), ImGui.GetColorU32(textColor), text);
            }
            else if (type == "dropdown")
            {
                drawList.AddRectFilled(position, max, ImGui.GetColorU32(backgroundColor), rounding);
                drawList.AddRect(position, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, borderThickness);
                string selectedText = control.Items.Count == 0
                    ? text
                    : control.Items[Math.Clamp(control.SelectedIndex, 0, control.Items.Count - 1)];
                Vector2 arrowCenter = max - new Vector2(16.0f, size.Y * 0.5f);
                drawList.AddTriangleFilled(
                    arrowCenter + new Vector2(-5.0f, -2.0f),
                    arrowCenter + new Vector2(5.0f, -2.0f),
                    arrowCenter + new Vector2(0.0f, 4.0f),
                    ImGui.GetColorU32(textColor));
                Vector2 textSize = ImGui.CalcTextSize(selectedText);
                drawList.AddText(GetAlignedTextPosition(position + new Vector2(8.0f, 0.0f), max - new Vector2(28.0f, 0.0f), textSize, style), ImGui.GetColorU32(textColor), selectedText);
            }
            else
            {
                drawList.AddRectFilled(position, max, ImGui.GetColorU32(backgroundColor), rounding);
                drawList.AddRect(position, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, borderThickness);

                Vector2 textSize = ImGui.CalcTextSize(text);
                Vector2 textPosition = GetAlignedTextPosition(position + new Vector2(6.0f, 4.0f), max - new Vector2(6.0f, 4.0f), textSize, style);
                drawList.AddText(textPosition, ImGui.GetColorU32(textColor), text);
            }
        }

        drawList.PopClipRect();
    }

    private static Vector2 GetAlignedTextPosition(Vector2 min, Vector2 max, Vector2 textSize, GuiControlStyleSettings style)
    {
        Vector2 available = Vector2.Max(max - min, Vector2.One);
        float x = ResolveHorizontalOffset(style.HorizontalAlignment, available.X, textSize.X);
        float y = ResolveVerticalOffset(style.VerticalAlignment, available.Y, textSize.Y);
        return min + new Vector2(x, y);
    }

    private static void DrawTextBlock(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        string text,
        Vector4 color,
        GuiControlStyleSettings style,
        bool wordWrap)
    {
        Vector2 available = Vector2.Max(max - min, Vector2.One);
        string[] lines = BuildTextLines(text, available.X, wordWrap);
        float lineHeight = Math.Max(ImGui.CalcTextSize("Ag").Y, 1.0f);
        float blockHeight = lineHeight * lines.Length;
        float startY = min.Y + ResolveVerticalOffset(style.VerticalAlignment, available.Y, blockHeight);
        uint textColor = ImGui.GetColorU32(color);

        drawList.PushClipRect(min, max, true);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            Vector2 lineSize = ImGui.CalcTextSize(line);
            float x = min.X + ResolveHorizontalOffset(style.HorizontalAlignment, available.X, lineSize.X);
            float y = startY + (lineHeight * i);
            drawList.AddText(new Vector2(x, y), textColor, line);
        }

        drawList.PopClipRect();
    }

    private static string[] BuildTextLines(string text, float maxWidth, bool wordWrap)
    {
        if (!wordWrap)
        {
            return [text];
        }

        List<string> lines = [];
        foreach (string paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string current = string.Empty;
            foreach (char ch in paragraph)
            {
                string candidate = current + ch;
                if (current.Length > 0 && ImGui.CalcTextSize(candidate).X > maxWidth)
                {
                    lines.Add(current);
                    current = ch.ToString();
                    continue;
                }

                current = candidate;
            }

            lines.Add(current);
        }

        return lines.Count == 0 ? [string.Empty] : [.. lines];
    }

    private static float ResolveHorizontalOffset(string alignment, float available, float content)
    {
        return alignment.ToLowerInvariant() switch
        {
            "right" => Math.Max(0.0f, available - content),
            "center" => Math.Max(0.0f, (available - content) * 0.5f),
            _ => 0.0f
        };
    }

    private static float ResolveVerticalOffset(string alignment, float available, float content)
    {
        return alignment.ToLowerInvariant() switch
        {
            "bottom" => Math.Max(0.0f, available - content),
            "middle" or "center" => Math.Max(0.0f, (available - content) * 0.5f),
            _ => 0.0f
        };
    }

    private void DrawProjectPanel()
    {
        DrawPathInput("Project directory", ref _projectDirectory, 1024, "projectDirectory");
        if (ImGui.Button("Use Directory"))
        {
            _editorGame.SetProjectDirectory(_projectDirectory);
        }

        ImGui.SameLine();
        if (ImGui.Button("Load"))
        {
            try
            {
                _editorGame.SetProjectDirectory(_projectDirectory);
                _editorGame.LoadProject();
                _newProjectName = _editorGame.Project.Name;
            }
            catch (Exception ex)
            {
                _editorGame.UpdateStatus($"Load failed: {ex.Message}");
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Save"))
        {
            try
            {
                _editorGame.SetProjectDirectory(_projectDirectory);
                _editorGame.SaveProject();
            }
            catch (Exception ex)
            {
                _editorGame.UpdateStatus($"Save failed: {ex.Message}");
            }
        }

        ImGui.Separator();
        ImGui.InputText("Project name", ref _newProjectName, 256);
        if (ImGui.Button("New Project"))
        {
            _editorGame.SetProjectDirectory(_projectDirectory);
            _editorGame.NewProject(_newProjectName);
        }

        ImGui.Separator();
        GameProject project = _editorGame.Project;
        string projectName = project.Name;
        string projectVersion = project.Version;
        if (ImGui.InputText("Name", ref projectName, 256))
        {
            project.Name = projectName;
        }

        if (ImGui.InputText("Version", ref projectVersion, 128))
        {
            project.Version = projectVersion;
        }

        DrawWindowSettings(project.Window);

        string[] languages = ["csharp", "python"];
        _preferredLanguageIndex = string.Equals(project.ScriptRuntime.PreferredLanguage, "python", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (ImGui.Combo("Script runtime", ref _preferredLanguageIndex, languages, languages.Length))
        {
            project.ScriptRuntime.PreferredLanguage = languages[_preferredLanguageIndex];
        }

        DrawVoiceSettings(project.Voice);

        ImGui.TextWrapped("The editor saves scene, resources, and script templates into the selected project directory.");
    }

    private void DrawWindowSettings(GameWindowSettings window)
    {
        if (!ImGui.CollapsingHeader("Window / Runtime", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        string title = window.Title;
        if (ImGui.InputText("Window title", ref title, 256))
        {
            window.Title = title;
        }

        string iconPath = window.IconPath;
        if (DrawPathInput("Window icon", ref iconPath, 1024, "windowIconPath"))
        {
            window.IconPath = iconPath;
        }

        int width = window.Width;
        int height = window.Height;
        bool fullscreen = window.Fullscreen;
        bool resizable = window.Resizable;
        string timingMode = window.TimingMode;
        bool changed = false;
        changed |= ImGui.DragInt("Width", ref width, 1.0f, 320, 7680);
        changed |= ImGui.DragInt("Height", ref height, 1.0f, 240, 4320);
        changed |= ImGui.Checkbox("Fullscreen", ref fullscreen);
        changed |= ImGui.Checkbox("Resizable", ref resizable);
        changed |= DrawStringCombo("Timing Mode", ref timingMode, ["time_synchronized", "frame_rate_dependent"]);

        if (changed)
        {
            window.Width = Math.Max(320, width);
            window.Height = Math.Max(240, height);
            window.Fullscreen = fullscreen;
            window.Resizable = resizable;
            window.TimingMode = NormalizeChoice(timingMode, "time_synchronized", ["time_synchronized", "frame_rate_dependent"]);
        }

        if (ImGui.Button("Apply To Editor Window"))
        {
            _editorGame.ApplyWindowSettings();
        }

        ImGui.TextWrapped("GamePlayer applies these settings on project load. The button above only previews them in the editor.");
    }

    private void DrawVoiceSettings(GameProjectVoiceSettings voice)
    {
        if (!ImGui.CollapsingHeader("Voice / TTS", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        bool enabled = voice.Enabled;
        if (ImGui.Checkbox("Enable runtime TTS", ref enabled))
        {
            voice.Enabled = enabled;
        }

        string provider = voice.TtsProvider;
        if (ImGui.InputText("TTS provider", ref provider, 128))
        {
            voice.TtsProvider = provider;
        }

        string[] modelKinds = ["vits", "matcha"];
        int modelKindIndex = string.Equals(voice.ModelKind, "matcha", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (ImGui.Combo("TTS model kind", ref modelKindIndex, modelKinds, modelKinds.Length))
        {
            voice.ModelKind = modelKinds[modelKindIndex];
        }

        string modelPath = voice.ModelPath;
        if (DrawPathInput("TTS model path", ref modelPath, 1024, "ttsModelPath"))
        {
            voice.ModelPath = modelPath;
        }

        string tokensPath = voice.TokensPath;
        if (DrawPathInput("TTS tokens path", ref tokensPath, 1024, "ttsTokensPath"))
        {
            voice.TokensPath = tokensPath;
        }

        string lexiconPath = voice.LexiconPath ?? string.Empty;
        if (DrawPathInput("TTS lexicon path", ref lexiconPath, 1024, "ttsLexiconPath"))
        {
            voice.LexiconPath = string.IsNullOrWhiteSpace(lexiconPath) ? null : lexiconPath;
        }

        string dataDirectory = voice.DataDirectory ?? string.Empty;
        if (DrawPathInput("TTS data directory", ref dataDirectory, 1024, "ttsDataDirectory"))
        {
            voice.DataDirectory = string.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory;
        }

        string dictDirectory = voice.DictDirectory ?? string.Empty;
        if (DrawPathInput("TTS dict directory", ref dictDirectory, 1024, "ttsDictDirectory"))
        {
            voice.DictDirectory = string.IsNullOrWhiteSpace(dictDirectory) ? null : dictDirectory;
        }

        string vocoderPath = voice.VocoderPath ?? string.Empty;
        if (DrawPathInput("Matcha vocoder path", ref vocoderPath, 1024, "ttsVocoderPath"))
        {
            voice.VocoderPath = string.IsNullOrWhiteSpace(vocoderPath) ? null : vocoderPath;
        }

        string inferenceProvider = voice.InferenceProvider;
        if (ImGui.InputText("Inference provider", ref inferenceProvider, 128))
        {
            voice.InferenceProvider = inferenceProvider;
        }

        int threads = voice.Threads;
        if (ImGui.DragInt("TTS threads", ref threads, 1.0f, 1, 128))
        {
            voice.Threads = Math.Max(1, threads);
        }

        int speakerId = voice.DefaultSpeakerId;
        if (ImGui.DragInt("Default speaker ID", ref speakerId, 1.0f, 0, 9999))
        {
            voice.DefaultSpeakerId = Math.Max(0, speakerId);
        }

        float speed = voice.DefaultSpeed;
        if (ImGui.DragFloat("Default speech speed", ref speed, 0.01f, 0.1f, 5.0f, "%.2f"))
        {
            voice.DefaultSpeed = Math.Clamp(speed, 0.1f, 5.0f);
        }

        float volume = voice.DefaultVolume;
        if (ImGui.DragFloat("Default speech volume", ref volume, 0.01f, 0.0f, 4.0f, "%.2f"))
        {
            voice.DefaultVolume = Math.Clamp(volume, 0.0f, 4.0f);
        }

        bool lipSyncEnabled = voice.LipSync.Enabled;
        if (ImGui.Checkbox("Enable lip sync", ref lipSyncEnabled))
        {
            voice.LipSync.Enabled = lipSyncEnabled;
        }

        string dictionaryDirectory = voice.LipSync.DictionaryDirectory;
        if (DrawPathInput("Lip-sync dictionary", ref dictionaryDirectory, 1024, "lipSyncDictionary"))
        {
            voice.LipSync.DictionaryDirectory = dictionaryDirectory;
        }

        string[] dictionaryLanguages = ["Chinese", "Japanese"];
        int dictionaryLanguageIndex = string.Equals(voice.LipSync.DictionaryLanguage, "Japanese", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (ImGui.Combo("Lip-sync language", ref dictionaryLanguageIndex, dictionaryLanguages, dictionaryLanguages.Length))
        {
            voice.LipSync.DictionaryLanguage = dictionaryLanguages[dictionaryLanguageIndex];
        }

        float minFrame = voice.LipSync.MinFramePeriodMilliseconds;
        float maxFrame = voice.LipSync.MaxFramePeriodMilliseconds;
        if (ImGui.DragFloat("Min lip frame ms", ref minFrame, 1.0f, 10.0f, 1000.0f, "%.0f"))
        {
            voice.LipSync.MinFramePeriodMilliseconds = Math.Max(10.0f, minFrame);
        }

        if (ImGui.DragFloat("Max lip frame ms", ref maxFrame, 1.0f, 10.0f, 1000.0f, "%.0f"))
        {
            voice.LipSync.MaxFramePeriodMilliseconds = Math.Max(voice.LipSync.MinFramePeriodMilliseconds, maxFrame);
        }

        ImGui.TextWrapped("Scripts call Entity.Speak(...) / entity.speak(...). The runtime uses this project-level TTS configuration.");
    }

    private void DrawHierarchyPanel()
    {
        ImGui.TextUnformatted(_editorGame.Project.Scene.Name);
        ImGui.Separator();

        for (int i = 0; i < _editorGame.Project.Scene.Entities.Count; i++)
        {
            GameEntity entity = _editorGame.Project.Scene.Entities[i];
            bool selected = _editorGame.SelectedEntityIndex == i;
            string prefix = entity.Type.ToLowerInvariant() switch
            {
                "particle_system" => "[FX]",
                "water_surface" => "[Water]",
                "textured_plane" => "[Plane]",
                "empty" or "empty_object" or "game_object" => "[Empty]",
                _ => "[PMX]"
            };
            if (ImGui.Selectable($"{prefix} {entity.Name}##entity{i}", selected))
            {
                _editorGame.SelectedEntityIndex = i;
            }
        }

        ImGui.Separator();
        if (ImGui.Button("Remove Selected"))
        {
            _editorGame.RemoveSelectedEntity();
        }
    }

    private void DrawInspectorPanel()
    {
        DrawSceneInspector();
        DrawGuiInspector();
        ImGui.Separator();

        GameEntity? entity = _editorGame.SelectedEntity;
        if (entity is null)
        {
            ImGui.TextWrapped("Select an entity to edit its transform, rendering flags, and scripts.");
            return;
        }

        ImGui.TextUnformatted("Entity");
        string entityName = entity.Name;
        string entityType = entity.Type;
        string assetPath = entity.AssetPath;
        bool textChanged = false;
        textChanged |= ImGui.InputText("Name", ref entityName, 256);
        textChanged |= ImGui.InputText("Type", ref entityType, 128);
        textChanged |= ImGui.InputText("Asset", ref assetPath, 1024);
        ImGui.SameLine();
        if (ImGui.SmallButton("Paste##entityAssetPath"))
        {
            PasteClipboard(ref assetPath);
            textChanged = true;
        }

        if (textChanged)
        {
            entity.Name = entityName;
            entity.Type = entityType;
            entity.AssetPath = assetPath;
        }

        Vector3 position = entity.Transform.Position.ToVector3();
        Vector3 rotation = entity.Transform.RotationDegrees.ToVector3();
        Vector3 scale = entity.Transform.Scale.ToVector3();
        bool isPlaying = entity.IsPlaying;
        bool enableEdge = entity.EnableEdge;
        bool enableShadow = entity.EnableShadow;
        bool drawShadowInMainPass = entity.DrawShadowInMainPass;
        float playbackSpeed = entity.PlaybackSpeed;
        bool loopMotion = entity.LoopMotion;
        bool resetPhysicsOnMotionLoop = entity.ResetPhysicsOnMotionLoop;
        bool changed = false;
        changed |= ImGui.DragFloat3("Position", ref position, 0.02f);
        changed |= ImGui.DragFloat3("Rotation", ref rotation, 0.5f);
        changed |= ImGui.DragFloat3("Scale", ref scale, 0.01f, 0.001f, 100.0f);

        changed |= ImGui.Checkbox("Play animation", ref isPlaying);
        if (string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase))
        {
            changed |= ImGui.DragFloat("Playback speed", ref playbackSpeed, 0.01f, 0.0f, 5.0f, "%.2f");
            changed |= ImGui.Checkbox("Loop motion", ref loopMotion);
            changed |= ImGui.Checkbox("Reset physics on loop", ref resetPhysicsOnMotionLoop);
        }

        changed |= ImGui.Checkbox("Edge", ref enableEdge);
        changed |= ImGui.Checkbox("Shadow", ref enableShadow);
        changed |= ImGui.Checkbox("Draw shadow in main pass", ref drawShadowInMainPass);

        if (changed)
        {
            entity.Transform.Position = Vector3Dto.FromVector3(position);
            entity.Transform.RotationDegrees = Vector3Dto.FromVector3(rotation);
            entity.Transform.Scale = Vector3Dto.FromVector3(scale);
            entity.IsPlaying = isPlaying;
            entity.PlaybackSpeed = playbackSpeed;
            entity.LoopMotion = loopMotion;
            entity.ResetPhysicsOnMotionLoop = resetPhysicsOnMotionLoop;
            entity.EnableEdge = enableEdge;
            entity.EnableShadow = enableShadow;
            entity.DrawShadowInMainPass = drawShadowInMainPass;
            _editorGame.ApplySelectedEntityToRuntime();
        }

        DrawColliderInspector(entity);

        if (string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase))
        {
            DrawRelationInspector(entity);
            DrawMotionLayerInspector(entity);
        }
        else if (string.Equals(entity.Type, "particle_system", StringComparison.OrdinalIgnoreCase))
        {
            DrawParticleInspector(entity);
        }
        else if (string.Equals(entity.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
        {
            DrawWaterInspector(entity);
        }
        else if (string.Equals(entity.Type, "textured_plane", StringComparison.OrdinalIgnoreCase))
        {
            DrawPlaneInspector(entity);
        }

        if (ImGui.Button("Reload Runtime Object"))
        {
            _editorGame.ApplySelectedEntityToRuntime();
        }

        ImGui.Separator();
        ImGui.TextWrapped("Scripts are attached to the selected entity. GamePlayer calls enabled scripts on Start and Update.");
        for (int i = 0; i < entity.Scripts.Count; i++)
        {
            ScriptBinding script = entity.Scripts[i];
            ImGui.PushID(i);
            bool enabled = script.Enabled;
            string language = script.Language;
            string path = script.Path;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                script.Enabled = enabled;
            }

            if (ImGui.InputText("Language", ref language, 64))
            {
                script.Language = language;
            }

            bool pathEdited = ImGui.InputText("Path", ref path, 512);
            bool pathCommitted = pathEdited && ImGui.IsItemDeactivatedAfterEdit();
            if (pathEdited)
            {
                script.Path = path;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Paste##scriptPath"))
            {
                PasteClipboard(ref path);
                script.Path = path;
                pathCommitted = true;
            }

            if (pathCommitted)
            {
                _editorGame.NormalizeAndValidateScriptBinding(script, $"entity '{entity.Name}' script");
            }

            ImGui.PopID();
        }

        if (ImGui.Button("Add C# Script"))
        {
            _editorGame.AddScriptToSelected("csharp");
        }

        ImGui.SameLine();
        if (ImGui.Button("Add Python Script"))
        {
            _editorGame.AddScriptToSelected("python");
        }
    }

    private void DrawSceneInspector()
    {
        GameProjectScene scene = _editorGame.Project.Scene;
        ImGui.TextUnformatted("Scene");
        string sceneName = scene.Name;
        if (ImGui.InputText("Scene name", ref sceneName, 256))
        {
            scene.Name = sceneName;
        }

        Vector3 cameraPosition = scene.Camera.Position.ToVector3();
        Vector3 cameraTarget = scene.Camera.Target.ToVector3();
        string projectionMode = NormalizeProjectionMode(scene.Camera.ProjectionMode);
        string[] projectionModes = ["perspective", "orthographic"];
        int projectionModeIndex = projectionMode == "orthographic" ? 1 : 0;
        float fov = scene.Camera.Fov;
        float orthographicSize = scene.Camera.OrthographicSize;
        float nearClipPlane = scene.Camera.NearClipPlane;
        float farClipPlane = scene.Camera.FarClipPlane;
        bool cameraChanged = false;
        cameraChanged |= ImGui.DragFloat3("Camera position", ref cameraPosition, 0.05f);
        cameraChanged |= ImGui.DragFloat3("Camera target", ref cameraTarget, 0.05f);
        if (ImGui.Combo("Projection", ref projectionModeIndex, projectionModes, projectionModes.Length))
        {
            projectionMode = projectionModes[projectionModeIndex];
            cameraChanged = true;
        }

        cameraChanged |= ImGui.SliderFloat("FOV", ref fov, 10.0f, 90.0f);
        cameraChanged |= ImGui.DragFloat("Orthographic size", ref orthographicSize, 0.05f, 0.01f, 10000.0f);
        cameraChanged |= ImGui.DragFloat("Near clip", ref nearClipPlane, 0.01f, 0.001f, 10000.0f);
        cameraChanged |= ImGui.DragFloat("Far clip", ref farClipPlane, 1.0f, 0.01f, 1000000.0f);
        if (cameraChanged)
        {
            scene.Camera.Position = Vector3Dto.FromVector3(cameraPosition);
            scene.Camera.Target = Vector3Dto.FromVector3(cameraTarget);
            scene.Camera.ProjectionMode = projectionMode;
            scene.Camera.Fov = fov;
            scene.Camera.OrthographicSize = Math.Max(0.01f, orthographicSize);
            scene.Camera.NearClipPlane = Math.Max(0.001f, nearClipPlane);
            scene.Camera.FarClipPlane = Math.Max(scene.Camera.NearClipPlane + 0.001f, farClipPlane);
            _editorGame.ApplyCameraSettings();
        }

        Vector3 lightDirection = scene.Lighting.LightDirection.ToVector3();
        Vector3 ambientColor = scene.Lighting.AmbientColor.ToVector3();
        float ambientStrength = scene.Lighting.AmbientStrength;
        Vector4 clearColor = scene.Lighting.ClearColor.ToVector4();
        bool lightingChanged = false;
        lightingChanged |= ImGui.DragFloat3("Light direction", ref lightDirection, 0.02f);
        lightingChanged |= ImGui.ColorEdit3("Ambient color", ref ambientColor);
        lightingChanged |= ImGui.SliderFloat("Ambient strength", ref ambientStrength, 0.0f, 2.0f);
        lightingChanged |= ImGui.ColorEdit4("Clear color", ref clearColor);
        if (lightingChanged)
        {
            scene.Lighting.LightDirection = Vector3Dto.FromVector3(lightDirection);
            scene.Lighting.AmbientColor = Vector3Dto.FromVector3(ambientColor);
            scene.Lighting.AmbientStrength = ambientStrength;
            scene.Lighting.ClearColor = Vector4Dto.FromVector4(clearColor);
            _editorGame.ApplySceneSettings();
        }

        DrawSkyboxInspector(scene);
        DrawCamerasInspector(scene);
        DrawRenderTexturesInspector(scene);
        DrawLoadingScreenInspector(scene);
        DrawSceneLoadingScriptsInspector(scene);
    }

    private static void EnsureCameraList(GameProjectScene scene)
    {
        if (scene.Cameras.Count == 0)
        {
            scene.Cameras.Add(new SceneCameraSettings
            {
                Name = string.IsNullOrWhiteSpace(scene.MainCamera) ? "Main Camera" : scene.MainCamera,
                IsMain = true,
                Camera = scene.Camera
            });
        }

        SceneCameraSettings? main = scene.Cameras.FirstOrDefault(camera => camera.IsMain)
            ?? scene.Cameras.FirstOrDefault(camera => string.Equals(camera.Name, scene.MainCamera, StringComparison.OrdinalIgnoreCase))
            ?? scene.Cameras[0];
        foreach (SceneCameraSettings camera in scene.Cameras)
        {
            camera.IsMain = ReferenceEquals(camera, main);
        }

        scene.MainCamera = main.Name;
        scene.Camera = main.Camera;
    }

    private void DrawCamerasInspector(GameProjectScene scene)
    {
        if (!ImGui.CollapsingHeader("Cameras"))
        {
            return;
        }

        EnsureCameraList(scene);
        int mainIndex = Math.Max(0, scene.Cameras.FindIndex(camera => camera.IsMain));
        string[] names = scene.Cameras.Select(camera => camera.Name).ToArray();
        if (ImGui.Combo("Main camera", ref mainIndex, names, names.Length))
        {
            for (int i = 0; i < scene.Cameras.Count; i++)
            {
                scene.Cameras[i].IsMain = i == mainIndex;
            }

            scene.MainCamera = scene.Cameras[mainIndex].Name;
            scene.Camera = scene.Cameras[mainIndex].Camera;
            _editorGame.ApplyCameraSettings();
        }

        if (ImGui.Button("Add Camera"))
        {
            scene.Cameras.Add(new SceneCameraSettings
            {
                Name = $"Camera {scene.Cameras.Count + 1}",
                Camera = new CameraSettings
                {
                    Position = scene.Camera.Position,
                    Target = scene.Camera.Target,
                    ProjectionMode = scene.Camera.ProjectionMode,
                    Fov = scene.Camera.Fov,
                    OrthographicSize = scene.Camera.OrthographicSize,
                    NearClipPlane = scene.Camera.NearClipPlane,
                    FarClipPlane = scene.Camera.FarClipPlane
                }
            });
            _editorGame.ApplyCameraSettings();
        }

        int removeIndex = -1;
        for (int i = 0; i < scene.Cameras.Count; i++)
        {
            SceneCameraSettings camera = scene.Cameras[i];
            ImGui.PushID($"camera{i}");
            if (ImGui.TreeNodeEx(camera.Name, ImGuiTreeNodeFlags.DefaultOpen))
            {
                string name = camera.Name;
                bool enabled = camera.Enabled;
                Vector3 position = camera.Camera.Position.ToVector3();
                Vector3 target = camera.Camera.Target.ToVector3();
                string[] projectionModes = ["perspective", "orthographic"];
                int projectionIndex = NormalizeProjectionMode(camera.Camera.ProjectionMode) == "orthographic" ? 1 : 0;
                float fov = camera.Camera.Fov;
                float orthoSize = camera.Camera.OrthographicSize;
                float nearClip = camera.Camera.NearClipPlane;
                float farClip = camera.Camera.FarClipPlane;
                bool changed = false;

                changed |= ImGui.InputText("Name", ref name, 256);
                changed |= ImGui.Checkbox("Enabled", ref enabled);
                changed |= ImGui.DragFloat3("Position", ref position, 0.05f);
                changed |= ImGui.DragFloat3("Target", ref target, 0.05f);
                if (ImGui.Combo("Projection", ref projectionIndex, projectionModes, projectionModes.Length))
                {
                    changed = true;
                }

                changed |= ImGui.SliderFloat("FOV", ref fov, 10.0f, 90.0f);
                changed |= ImGui.DragFloat("Orthographic size", ref orthoSize, 0.05f, 0.01f, 10000.0f);
                changed |= ImGui.DragFloat("Near clip", ref nearClip, 0.01f, 0.001f, 10000.0f);
                changed |= ImGui.DragFloat("Far clip", ref farClip, 1.0f, 0.01f, 1000000.0f);
                if (changed)
                {
                    camera.Name = string.IsNullOrWhiteSpace(name) ? camera.Name : name.Trim();
                    camera.Enabled = enabled;
                    camera.Camera.Position = Vector3Dto.FromVector3(position);
                    camera.Camera.Target = Vector3Dto.FromVector3(target);
                    camera.Camera.ProjectionMode = projectionModes[projectionIndex];
                    camera.Camera.Fov = fov;
                    camera.Camera.OrthographicSize = Math.Max(0.01f, orthoSize);
                    camera.Camera.NearClipPlane = Math.Max(0.001f, nearClip);
                    camera.Camera.FarClipPlane = Math.Max(camera.Camera.NearClipPlane + 0.001f, farClip);
                    if (camera.IsMain)
                    {
                        scene.MainCamera = camera.Name;
                        scene.Camera = camera.Camera;
                    }

                    _editorGame.ApplyCameraSettings();
                }

                if (!camera.IsMain && ImGui.SmallButton("Remove Camera"))
                {
                    removeIndex = i;
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            scene.Cameras.RemoveAt(removeIndex);
            _editorGame.ApplyCameraSettings();
        }
    }

    private void DrawRenderTexturesInspector(GameProjectScene scene)
    {
        if (!ImGui.CollapsingHeader("Render Textures"))
        {
            return;
        }

        EnsureCameraList(scene);
        if (ImGui.Button("Add Render Texture"))
        {
            scene.RenderTextures.Add(new RenderTextureSettings
            {
                Name = $"RenderTexture{scene.RenderTextures.Count + 1}",
                Camera = scene.MainCamera
            });
        }

        string[] cameraNames = scene.Cameras.Select(camera => camera.Name).ToArray();
        int removeIndex = -1;
        for (int i = 0; i < scene.RenderTextures.Count; i++)
        {
            RenderTextureSettings renderTexture = scene.RenderTextures[i];
            ImGui.PushID($"renderTexture{i}");
            ImGui.Separator();
            string name = renderTexture.Name;
            bool enabled = renderTexture.Enabled;
            int cameraIndex = Math.Max(0, Array.FindIndex(cameraNames, name => string.Equals(name, renderTexture.Camera, StringComparison.OrdinalIgnoreCase)));
            int width = renderTexture.Width;
            int height = renderTexture.Height;
            Vector4 clearColor = renderTexture.ClearColor.ToVector4();
            bool changed = false;

            changed |= ImGui.InputText("Name", ref name, 256);
            changed |= ImGui.Checkbox("Enabled", ref enabled);
            if (cameraNames.Length > 0 && ImGui.Combo("Camera", ref cameraIndex, cameraNames, cameraNames.Length))
            {
                changed = true;
            }

            changed |= ImGui.DragInt("Width", ref width, 1.0f, 1, 8192);
            changed |= ImGui.DragInt("Height", ref height, 1.0f, 1, 8192);
            changed |= ImGui.ColorEdit4("Clear color", ref clearColor);
            if (changed)
            {
                renderTexture.Name = string.IsNullOrWhiteSpace(name) ? renderTexture.Name : name.Trim();
                renderTexture.Enabled = enabled;
                renderTexture.Camera = cameraNames.Length == 0 ? renderTexture.Camera : cameraNames[cameraIndex];
                renderTexture.Width = Math.Max(1, width);
                renderTexture.Height = Math.Max(1, height);
                renderTexture.ClearColor = Vector4Dto.FromVector4(clearColor);
            }

            ImGui.TextUnformatted($"Reference: rt:{renderTexture.Name}");
            if (ImGui.SmallButton("Remove Render Texture"))
            {
                removeIndex = i;
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            scene.RenderTextures.RemoveAt(removeIndex);
        }
    }

    private void DrawSkyboxInspector(GameProjectScene scene)
    {
        if (!ImGui.CollapsingHeader("Skybox", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        SkyboxSettings skybox = scene.Skybox;
        bool enabled = skybox.Enabled;
        string texturePath = skybox.TexturePath;
        float exposure = skybox.Exposure;
        Vector3 tint = skybox.Tint.ToVector3();
        bool changed = false;

        changed |= ImGui.Checkbox("Enable skybox", ref enabled);
        if (DrawPathInput("Skybox texture", ref texturePath, 1024, "skyboxTexturePath"))
        {
            changed = true;
        }

        changed |= ImGui.DragFloat("Skybox exposure", ref exposure, 0.01f, 0.0f, 10.0f);
        changed |= ImGui.ColorEdit3("Skybox tint", ref tint);

        if (changed)
        {
            skybox.Enabled = enabled;
            skybox.TexturePath = texturePath;
            skybox.Exposure = Math.Max(0.0f, exposure);
            skybox.Tint = Vector3Dto.FromVector3(tint);
            _editorGame.ApplySceneSettings();
        }
    }

    private static string NormalizeProjectionMode(string projectionMode)
    {
        string normalized = (projectionMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "orthographic" or "ortho"
            ? "orthographic"
            : "perspective";
    }

    private void DrawLoadingScreenInspector(GameProjectScene scene)
    {
        if (!ImGui.CollapsingHeader("Loading Screen"))
        {
            return;
        }

        LoadingScreenSettings loadingScreen = scene.LoadingScreen;
        Vector4 backgroundColor = loadingScreen.BackgroundColor.ToVector4();
        string backgroundImagePath = loadingScreen.BackgroundImagePath;
        float backgroundImageOpacity = loadingScreen.BackgroundImageOpacity;
        bool changed = false;

        changed |= ImGui.ColorEdit4("Background color", ref backgroundColor);
        if (ImGui.InputText("Background image", ref backgroundImagePath, 1024))
        {
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Paste##loadingBackgroundImage"))
        {
            PasteClipboard(ref backgroundImagePath);
            changed = true;
        }

        changed |= ImGui.SliderFloat("Image opacity", ref backgroundImageOpacity, 0.0f, 1.0f);

        if (changed)
        {
            loadingScreen.BackgroundColor = Vector4Dto.FromVector4(backgroundColor);
            loadingScreen.BackgroundImagePath = backgroundImagePath;
            loadingScreen.BackgroundImageOpacity = Math.Clamp(backgroundImageOpacity, 0.0f, 1.0f);
        }
    }

    private void DrawSceneLoadingScriptsInspector(GameProjectScene scene)
    {
        if (!ImGui.CollapsingHeader("Scene Loading Scripts"))
        {
            return;
        }

        ImGui.TextWrapped("These scripts run during scene transitions: loading_started, loading_progress, and loading_completed.");

        int removeIndex = -1;
        for (int i = 0; i < scene.LoadingScripts.Count; i++)
        {
            ScriptBinding script = scene.LoadingScripts[i];
            ImGui.PushID($"loadingScript{i}");
            ImGui.Separator();

            bool enabled = script.Enabled;
            string language = script.Language;
            string path = script.Path;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                script.Enabled = enabled;
            }

            if (ImGui.InputText("Language", ref language, 64))
            {
                script.Language = language;
            }

            bool pathEdited = ImGui.InputText("Path", ref path, 512);
            bool pathCommitted = pathEdited && ImGui.IsItemDeactivatedAfterEdit();
            if (pathEdited)
            {
                script.Path = path;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Paste##loadingScriptPath"))
            {
                PasteClipboard(ref path);
                script.Path = path;
                pathCommitted = true;
            }

            if (pathCommitted)
            {
                _editorGame.NormalizeAndValidateScriptBinding(script, "scene loading script");
            }

            if (ImGui.SmallButton("Remove Loading Script"))
            {
                removeIndex = i;
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            scene.LoadingScripts.RemoveAt(removeIndex);
        }

        if (ImGui.Button("Add C# Loading Script"))
        {
            _editorGame.AddSceneLoadingScript("csharp");
        }

        ImGui.SameLine();
        if (ImGui.Button("Add Python Loading Script"))
        {
            _editorGame.AddSceneLoadingScript("python");
        }
    }

    private void DrawGuiInspector()
    {
        GameProjectScene scene = _editorGame.Project.Scene;
        if (!ImGui.CollapsingHeader("GUI Controls"))
        {
            return;
        }

        if (ImGui.Button("Add Button"))
        {
            scene.GuiControls.Add(new GuiControlSettings
            {
                Name = $"Button {scene.GuiControls.Count + 1}",
                Type = "button",
                Text = "Button",
                EventName = "clicked"
            });
        }

        ImGui.SameLine();
        if (ImGui.Button("Add Label"))
        {
            scene.GuiControls.Add(new GuiControlSettings
            {
                Name = $"Label {scene.GuiControls.Count + 1}",
                Type = "label",
                Text = "Label"
            });
        }

        ImGui.SameLine();
        if (ImGui.Button("Add Checkbox"))
        {
            scene.GuiControls.Add(new GuiControlSettings
            {
                Name = $"Checkbox {scene.GuiControls.Count + 1}",
                Type = "checkbox",
                Text = "Checkbox",
                Width = 180.0f,
                EventName = "changed"
            });
        }

        ImGui.SameLine();
        if (ImGui.Button("Add Dropdown"))
        {
            scene.GuiControls.Add(new GuiControlSettings
            {
                Name = $"Dropdown {scene.GuiControls.Count + 1}",
                Type = "dropdown",
                Text = "Dropdown",
                Width = 180.0f,
                Items = ["Option A", "Option B", "Option C"],
                EventName = "changed"
            });
        }

        int removeIndex = -1;
        for (int i = 0; i < scene.GuiControls.Count; i++)
        {
            GuiControlSettings control = scene.GuiControls[i];
            ImGui.PushID($"gui{i}");
            ImGui.Separator();
            bool visible = control.Visible;
            string name = control.Name;
            string type = control.Type;
            string text = control.Text;
            string targetEntity = control.TargetEntity;
            string eventName = control.EventName;
            Vector2 position = new(control.X, control.Y);
            Vector2 size = new(control.Width, control.Height);
            bool wordWrap = control.WordWrap;
            bool checkedValue = control.Checked;
            int selectedIndex = control.SelectedIndex;
            bool changed = false;

            changed |= ImGui.Checkbox("Visible", ref visible);
            changed |= ImGui.InputText("Name", ref name, 128);
            string[] types = ["button", "label", "checkbox", "dropdown"];
            int typeIndex = Array.FindIndex(types, item => string.Equals(item, type, StringComparison.OrdinalIgnoreCase));
            typeIndex = Math.Max(0, typeIndex);
            if (ImGui.Combo("Type", ref typeIndex, types, types.Length))
            {
                type = types[typeIndex];
                changed = true;
            }

            changed |= ImGui.InputText("Text", ref text, 512);
            changed |= ImGui.Checkbox("Word wrap", ref wordWrap);
            changed |= ImGui.DragFloat2("Position", ref position, 1.0f);
            changed |= ImGui.DragFloat2("Size", ref size, 1.0f, 1.0f, 4096.0f);
            if (string.Equals(type, "checkbox", StringComparison.OrdinalIgnoreCase))
            {
                changed |= ImGui.Checkbox("Checked", ref checkedValue);
            }

            if (string.Equals(type, "dropdown", StringComparison.OrdinalIgnoreCase))
            {
                changed |= DrawDropdownItemsInspector(control, ref selectedIndex);
            }

            changed |= DrawEntityTargetCombo("Target entity", ref targetEntity);
            changed |= ImGui.InputText("Event name", ref eventName, 128);
            changed |= DrawGuiStyleInspector(control.Style);

            if (changed)
            {
                control.Visible = visible;
                control.Name = name;
                control.Type = type;
                control.Text = text;
                control.X = Math.Max(0.0f, position.X);
                control.Y = Math.Max(0.0f, position.Y);
                control.Width = Math.Max(1.0f, size.X);
                control.Height = Math.Max(1.0f, size.Y);
                control.TargetEntity = targetEntity;
                control.EventName = eventName;
                control.WordWrap = wordWrap;
                control.Checked = checkedValue;
                control.SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(control.Items.Count - 1, 0));
            }

            if (ImGui.SmallButton("Remove GUI Control"))
            {
                removeIndex = i;
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            scene.GuiControls.RemoveAt(removeIndex);
        }
    }

    private static bool DrawDropdownItemsInspector(GuiControlSettings control, ref int selectedIndex)
    {
        bool changed = false;
        if (!ImGui.TreeNode("Dropdown Items"))
        {
            selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(control.Items.Count - 1, 0));
            return false;
        }

        if (control.Items.Count == 0)
        {
            control.Items.Add("Option");
            changed = true;
        }

        int removeIndex = -1;
        for (int i = 0; i < control.Items.Count; i++)
        {
            ImGui.PushID($"dropdownItem{i}");
            string item = control.Items[i];
            if (ImGui.InputText("Item", ref item, 256))
            {
                control.Items[i] = item;
                changed = true;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                removeIndex = i;
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            control.Items.RemoveAt(removeIndex);
            changed = true;
        }

        if (ImGui.Button("Add Item"))
        {
            control.Items.Add($"Option {control.Items.Count + 1}");
            changed = true;
        }

        if (control.Items.Count != 0)
        {
            selectedIndex = Math.Clamp(selectedIndex, 0, control.Items.Count - 1);
            string[] items = control.Items.ToArray();
            changed |= ImGui.Combo("Selected", ref selectedIndex, items, items.Length);
        }

        ImGui.TreePop();
        return changed;
    }

    private static bool DrawGuiStyleInspector(GuiControlStyleSettings style)
    {
        if (!ImGui.TreeNode("Style"))
        {
            return false;
        }

        Vector4 backgroundColor = style.BackgroundColor.ToVector4();
        Vector4 hoverColor = style.HoverColor.ToVector4();
        Vector4 activeColor = style.ActiveColor.ToVector4();
        Vector4 textColor = style.TextColor.ToVector4();
        Vector4 borderColor = style.BorderColor.ToVector4();
        float borderThickness = style.BorderThickness;
        float rounding = style.Rounding;
        string horizontalAlignment = style.HorizontalAlignment;
        string verticalAlignment = style.VerticalAlignment;
        bool changed = false;

        changed |= ImGui.ColorEdit4("Background", ref backgroundColor);
        changed |= ImGui.ColorEdit4("Hover", ref hoverColor);
        changed |= ImGui.ColorEdit4("Active", ref activeColor);
        changed |= ImGui.ColorEdit4("Text", ref textColor);
        changed |= ImGui.ColorEdit4("Border", ref borderColor);
        changed |= ImGui.DragFloat("Border thickness", ref borderThickness, 0.05f, 0.0f, 16.0f, "%.2f");
        changed |= ImGui.DragFloat("Rounding", ref rounding, 0.1f, 0.0f, 64.0f, "%.1f");
        changed |= DrawStringCombo("Horizontal align", ref horizontalAlignment, ["left", "center", "right"]);
        changed |= DrawStringCombo("Vertical align", ref verticalAlignment, ["top", "middle", "bottom"]);

        if (changed)
        {
            style.BackgroundColor = Vector4Dto.FromVector4(backgroundColor);
            style.HoverColor = Vector4Dto.FromVector4(hoverColor);
            style.ActiveColor = Vector4Dto.FromVector4(activeColor);
            style.TextColor = Vector4Dto.FromVector4(textColor);
            style.BorderColor = Vector4Dto.FromVector4(borderColor);
            style.BorderThickness = Math.Max(0.0f, borderThickness);
            style.Rounding = Math.Max(0.0f, rounding);
            style.HorizontalAlignment = NormalizeChoice(horizontalAlignment, "center", ["left", "center", "right"]);
            style.VerticalAlignment = NormalizeChoice(verticalAlignment, "middle", ["top", "middle", "bottom"]);
        }

        ImGui.TreePop();
        return changed;
    }

    private static bool DrawStringCombo(string label, ref string value, string[] choices)
    {
        int index = -1;
        for (int i = 0; i < choices.Length; i++)
        {
            if (string.Equals(choices[i], value, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        index = Math.Max(0, index);
        if (!ImGui.Combo(label, ref index, choices, choices.Length))
        {
            return false;
        }

        value = choices[index];
        return true;
    }

    private static string NormalizeChoice(string value, string fallback, string[] choices)
    {
        return choices.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private bool DrawEntityTargetCombo(string label, ref string targetEntity)
    {
        GameProjectScene scene = _editorGame.Project.Scene;
        string normalizedTarget = targetEntity.Trim();
        GameEntity? selectedEntity = scene.Entities.FirstOrDefault(entity =>
            string.Equals(entity.Id, normalizedTarget, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entity.Name, normalizedTarget, StringComparison.OrdinalIgnoreCase));
        string preview = selectedEntity is not null
            ? selectedEntity.Name
            : string.IsNullOrWhiteSpace(normalizedTarget)
                ? "(none)"
                : $"Missing: {normalizedTarget}";

        bool changed = false;
        if (ImGui.BeginCombo(label, preview))
        {
            bool noneSelected = string.IsNullOrWhiteSpace(normalizedTarget);
            if (ImGui.Selectable("(none)", noneSelected))
            {
                targetEntity = string.Empty;
                changed = true;
            }

            foreach (GameEntity entity in scene.Entities)
            {
                bool selected = selectedEntity is not null && string.Equals(selectedEntity.Id, entity.Id, StringComparison.OrdinalIgnoreCase);
                string itemLabel = $"{entity.Name}##targetEntity{entity.Id}";
                if (ImGui.Selectable(itemLabel, selected))
                {
                    targetEntity = entity.Id;
                    changed = true;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            if (selectedEntity is null && !string.IsNullOrWhiteSpace(normalizedTarget))
            {
                ImGui.Separator();
                ImGui.TextDisabled($"Current value is not found: {normalizedTarget}");
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private void DrawAssetsPanel()
    {
        ImGui.Checkbox("Copy imported files into project", ref _copyAssets);

        ImGui.SeparatorText("PMX");
        DrawPathInput("PMX path", ref _pmxPath, 1024, "pmxPath");
        if (ImGui.Button("Add PMX Entity") && !string.IsNullOrWhiteSpace(_pmxPath))
        {
            TryRun(() => _editorGame.AddPmxEntityFromPath(_pmxPath, _copyAssets));
        }

        ImGui.SeparatorText("Empty Object");
        if (ImGui.Button("Add Empty Object"))
        {
            TryRun(_editorGame.AddEmptyEntity);
        }

        ImGui.SeparatorText("Audio");
        DrawPathInput("Audio path", ref _audioPath, 1024, "audioPath");
        if (ImGui.Button("Add WAV/OGG") && !string.IsNullOrWhiteSpace(_audioPath))
        {
            TryRun(() => _editorGame.AddAudioFromPath(_audioPath, _copyAssets));
        }

        ImGui.SeparatorText("Motion");
        DrawPathInput("VMD path", ref _motionPath, 1024, "motionPath");
        if (ImGui.Button("Add VMD Motion") && !string.IsNullOrWhiteSpace(_motionPath))
        {
            TryRun(() => _editorGame.AddMotionFromPath(_motionPath, _copyAssets));
        }

        ImGui.SeparatorText("2D Sprites");
        DrawPathInput("Sprite path", ref _spritePath, 1024, "spritePath");
        if (ImGui.Button("Add Sprite") && !string.IsNullOrWhiteSpace(_spritePath))
        {
            TryRun(() => _editorGame.AddSpriteFromPath(_spritePath, _copyAssets));
        }

        ImGui.SameLine();
        if (ImGui.Button("Add Textured Plane") && !string.IsNullOrWhiteSpace(_spritePath))
        {
            TryRun(() => _editorGame.AddTexturedPlaneFromPath(_spritePath, _copyAssets));
        }

        ImGui.SeparatorText("Particles");
        string[] presets = ["sakura", "rain", "snow", "cloud", "waterfall", "stream", "fire"];
        int presetIndex = Math.Max(0, Array.FindIndex(presets, item => string.Equals(item, _particlePreset, StringComparison.OrdinalIgnoreCase)));
        if (ImGui.Combo("Particle preset", ref presetIndex, presets, presets.Length))
        {
            _particlePreset = presets[presetIndex];
        }

        if (ImGui.Button("Add Particle Entity"))
        {
            TryRun(() => _editorGame.AddParticleEntity(_particlePreset));
        }

        ImGui.SeparatorText("Water");
        if (ImGui.Button("Add Water Surface"))
        {
            TryRun(_editorGame.AddWaterSurfaceEntity);
        }

        for (int i = 0; i < _editorGame.Project.Scene.Audio.Count; i++)
        {
            AudioAsset audio = _editorGame.Project.Scene.Audio[i];
            ImGui.PushID(i);
            ImGui.Separator();
            string audioName = audio.Name;
            bool loop = audio.Loop;
            float volume = audio.Volume;
            bool playOnStart = audio.PlayOnStart;
            if (ImGui.InputText("Name", ref audioName, 256))
            {
                audio.Name = audioName;
            }

            ImGui.TextWrapped(audio.Path);
            if (ImGui.Checkbox("Loop", ref loop))
            {
                audio.Loop = loop;
            }

            if (ImGui.SliderFloat("Volume", ref volume, 0.0f, 2.0f))
            {
                audio.Volume = volume;
            }

            if (ImGui.Checkbox("Play on start", ref playOnStart))
            {
                audio.PlayOnStart = playOnStart;
            }

            if (ImGui.Button("Play/Pause"))
            {
                _editorGame.PlayOrPauseAudio(audio);
            }

            ImGui.PopID();
        }

        DrawSpriteAssetList();

        for (int i = 0; i < _editorGame.Project.Scene.Motions.Count; i++)
        {
            MotionAsset motion = _editorGame.Project.Scene.Motions[i];
            ImGui.PushID($"motion{i}");
            ImGui.Separator();
            string motionName = motion.Name;
            string motionAssetPath = motion.Path;
            if (ImGui.InputText("Motion name", ref motionName, 256))
            {
                motion.Name = motionName;
            }

            if (ImGui.InputText("Motion path", ref motionAssetPath, 1024))
            {
                motion.Path = motionAssetPath;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Paste##motionAssetPath"))
            {
                PasteClipboard(ref motionAssetPath);
                motion.Path = motionAssetPath;
            }

            ImGui.PopID();
        }
    }

    private void DrawSpriteAssetList()
    {
        int removeIndex = -1;
        for (int i = 0; i < _editorGame.Project.Scene.Sprites.Count; i++)
        {
            SpriteSettings sprite = _editorGame.Project.Scene.Sprites[i];
            ImGui.PushID($"sprite{i}");
            ImGui.Separator();
            ImGui.TextUnformatted("2D Sprite");

            string name = sprite.Name;
            string path = sprite.Path;
            Vector2 position = new(sprite.X, sprite.Y);
            Vector2 size = new(sprite.Width, sprite.Height);
            float rotation = sprite.RotationDegrees;
            float opacity = sprite.Opacity;
            int drawOrder = sprite.DrawOrder;
            bool visible = sprite.Visible;
            bool changed = false;

            changed |= ImGui.InputText("Name", ref name, 256);
            if (DrawPathInput("Path", ref path, 1024, "spriteAssetPath"))
            {
                changed = true;
            }
            if (DrawRenderTextureCombo("Render texture", ref path))
            {
                changed = true;
            }

            changed |= ImGui.DragFloat2("Position", ref position, 1.0f);
            changed |= ImGui.DragFloat2("Size", ref size, 1.0f, 1.0f, 4096.0f);
            changed |= ImGui.DragFloat("Rotation", ref rotation, 1.0f, -360.0f, 360.0f);
            changed |= ImGui.SliderFloat("Opacity", ref opacity, 0.0f, 1.0f);
            changed |= ImGui.DragInt("Draw order", ref drawOrder, 1.0f);
            changed |= ImGui.Checkbox("Visible", ref visible);

            if (changed)
            {
                sprite.Name = name;
                sprite.Path = path;
                sprite.X = position.X;
                sprite.Y = position.Y;
                sprite.Width = Math.Max(1.0f, size.X);
                sprite.Height = Math.Max(1.0f, size.Y);
                sprite.RotationDegrees = rotation;
                sprite.Opacity = Math.Clamp(opacity, 0.0f, 1.0f);
                sprite.DrawOrder = drawOrder;
                sprite.Visible = visible;
            }

            if (ImGui.SmallButton("Remove Sprite"))
            {
                removeIndex = i;
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            _editorGame.Project.Scene.Sprites.RemoveAt(removeIndex);
        }
    }

    private void DrawStatusBar()
    {
        ImGui.TextUnformatted("Zhengyan Game Editor");
        ImGui.Separator();
        ImGui.TextWrapped(_editorGame.StatusMessage);
        ImGui.TextWrapped(_editorGame.AudioSummary);
        ImGui.TextWrapped("Camera: RMB orbit, MMB pan, wheel zoom.");
    }

    private void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _editorGame.UpdateStatus(ex.Message);
        }
    }

    private void DrawParticleInspector(GameEntity entity)
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Particle System");

        ParticleEntitySettings particle = entity.Particle;
        string[] presets = ["custom", "sakura", "rain", "snow", "cloud", "waterfall", "stream", "fire"];
        int presetIndex = Math.Max(0, Array.FindIndex(presets, item => string.Equals(item, particle.Preset, StringComparison.OrdinalIgnoreCase)));
        if (ImGui.Combo("Preset", ref presetIndex, presets, presets.Length) && presets[presetIndex] != "custom")
        {
            entity.Particle = ParticleEntitySettingsMapper.FromPreset(presets[presetIndex]);
            _editorGame.ApplySelectedParticleToRuntime();
            return;
        }

        bool changed = false;
        int particleCount = particle.ParticleCount;
        Vector3 spawnBox = particle.SpawnBoxHalfExtents.ToVector3();
        Vector3 baseVelocity = particle.BaseVelocity.ToVector3();
        Vector3 velocityJitter = particle.VelocityJitter.ToVector3();
        Vector3 acceleration = particle.Acceleration.ToVector3();
        float minLifetime = particle.MinLifetime;
        float maxLifetime = particle.MaxLifetime;
        float minSize = particle.MinSize;
        float maxSize = particle.MaxSize;
        float simulationSpeed = particle.SimulationSpeed;
        float opacity = particle.Opacity;
        Vector4 startColor = particle.StartColor.ToVector4();
        Vector4 endColor = particle.EndColor.ToVector4();
        string texturePath = particle.TexturePath ?? string.Empty;

        changed |= ImGui.DragInt("Particle count", ref particleCount, 8.0f, 1, 10000);
        changed |= ImGui.DragFloat3("Spawn half extents", ref spawnBox, 0.05f);
        changed |= ImGui.DragFloat3("Base velocity", ref baseVelocity, 0.05f);
        changed |= ImGui.DragFloat3("Velocity jitter", ref velocityJitter, 0.05f);
        changed |= ImGui.DragFloat3("Acceleration", ref acceleration, 0.05f);
        changed |= ImGui.DragFloat("Min lifetime", ref minLifetime, 0.05f, 0.05f, 120.0f);
        changed |= ImGui.DragFloat("Max lifetime", ref maxLifetime, 0.05f, 0.05f, 120.0f);
        changed |= ImGui.DragFloat("Min size", ref minSize, 0.01f, 0.001f, 100.0f);
        changed |= ImGui.DragFloat("Max size", ref maxSize, 0.01f, 0.001f, 100.0f);
        changed |= ImGui.SliderFloat("Simulation speed", ref simulationSpeed, 0.0f, 5.0f);
        changed |= ImGui.SliderFloat("Opacity", ref opacity, 0.0f, 1.0f);
        changed |= ImGui.ColorEdit4("Start color", ref startColor);
        changed |= ImGui.ColorEdit4("End color", ref endColor);
        changed |= ImGui.InputText("Texture path", ref texturePath, 1024);
        ImGui.SameLine();
        if (ImGui.SmallButton("Paste##particleTexturePath"))
        {
            PasteClipboard(ref texturePath);
            changed = true;
        }
        if (DrawRenderTextureCombo("Render texture", ref texturePath))
        {
            changed = true;
        }

        if (changed)
        {
            particle.Preset = "custom";
            particle.ParticleCount = Math.Max(1, particleCount);
            particle.SpawnBoxHalfExtents = Vector3Dto.FromVector3(spawnBox);
            particle.BaseVelocity = Vector3Dto.FromVector3(baseVelocity);
            particle.VelocityJitter = Vector3Dto.FromVector3(velocityJitter);
            particle.Acceleration = Vector3Dto.FromVector3(acceleration);
            particle.MinLifetime = Math.Max(0.05f, minLifetime);
            particle.MaxLifetime = Math.Max(particle.MinLifetime, maxLifetime);
            particle.MinSize = Math.Max(0.001f, minSize);
            particle.MaxSize = Math.Max(particle.MinSize, maxSize);
            particle.SimulationSpeed = simulationSpeed;
            particle.Opacity = opacity;
            particle.StartColor = Vector4Dto.FromVector4(startColor);
            particle.EndColor = Vector4Dto.FromVector4(endColor);
            particle.TexturePath = string.IsNullOrWhiteSpace(texturePath) ? null : texturePath.Trim();
            _editorGame.ApplySelectedParticleToRuntime();
        }
    }

    private void DrawMotionLayerInspector(GameEntity entity)
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Motion Layers");

        List<MotionAsset> motions = _editorGame.Project.Scene.Motions;
        if (motions.Count == 0)
        {
            ImGui.TextDisabled("Add VMD motions from the Assets panel first, or type a path manually in a layer.");
        }
        else
        {
            string[] labels = motions.Select(motion => string.IsNullOrWhiteSpace(motion.Name) ? motion.Path : motion.Name).ToArray();
            _selectedMotionAssetIndex = Math.Clamp(_selectedMotionAssetIndex, 0, motions.Count - 1);
            ImGui.Combo("Motion asset", ref _selectedMotionAssetIndex, labels, labels.Length);
            if (ImGui.Button("Add Selected Motion Layer"))
            {
                MotionAsset motion = motions[_selectedMotionAssetIndex];
                entity.MotionLayers.Add(new MotionLayerSettings
                {
                    Path = motion.Path,
                    Weight = 1.0f,
                    ResetPhysicsOnLoop = entity.ResetPhysicsOnMotionLoop
                });
                _editorGame.ApplySelectedEntityToRuntime();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear Motion Layers"))
        {
            entity.MotionLayers.Clear();
            _editorGame.ApplySelectedEntityToRuntime();
        }

        int removeIndex = -1;
        for (int i = 0; i < entity.MotionLayers.Count; i++)
        {
            MotionLayerSettings layer = entity.MotionLayers[i];
            ImGui.PushID($"motionLayer{i}");
            ImGui.Separator();

            string path = layer.Path;
            float weight = layer.Weight;
            bool resetPhysics = layer.ResetPhysicsOnLoop;
            bool changed = false;
            changed |= ImGui.InputText("Layer path", ref path, 1024);
            ImGui.SameLine();
            if (ImGui.SmallButton("Paste##motionLayerPath"))
            {
                PasteClipboard(ref path);
                changed = true;
            }

            changed |= ImGui.SliderFloat("Weight", ref weight, 0.0f, 1.0f);
            changed |= ImGui.Checkbox("Reset physics on loop", ref resetPhysics);
            if (changed)
            {
                layer.Path = path;
                layer.Weight = Math.Clamp(weight, 0.0f, 1.0f);
                layer.ResetPhysicsOnLoop = resetPhysics;
                _editorGame.ApplySelectedEntityToRuntime();
            }

            if (ImGui.SmallButton("Remove Layer"))
            {
                removeIndex = i;
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            entity.MotionLayers.RemoveAt(removeIndex);
            _editorGame.ApplySelectedEntityToRuntime();
        }
    }

    private void DrawColliderInspector(GameEntity entity)
    {
        ImGui.Separator();
        if (!ImGui.CollapsingHeader("Colliders", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        GameEntityCollision.MigrateLegacyCollision(entity);

        if (ImGui.Button("Add Capsule Collider"))
        {
            entity.Colliders.Add(new ColliderSettings
            {
                Name = $"Capsule Collider {entity.Colliders.Count + 1}",
                Shape = "capsule",
                Position = new Vector3Dto(0.0f, 1.0f, 0.0f),
                Radius = 0.5f,
                Height = 2.0f,
                Axis = "y"
            });
            _editorGame.ApplySelectedEntityToRuntime();
        }

        ImGui.SameLine();
        if (ImGui.Button("Add Box Collider"))
        {
            entity.Colliders.Add(new ColliderSettings
            {
                Name = $"Box Collider {entity.Colliders.Count + 1}",
                Shape = "box",
                Position = new Vector3Dto(0.0f, 0.5f, 0.0f),
                Size = Vector3Dto.One
            });
            _editorGame.ApplySelectedEntityToRuntime();
        }

        int removeIndex = -1;
        for (int i = 0; i < entity.Colliders.Count; i++)
        {
            ColliderSettings collider = entity.Colliders[i];
            ImGui.PushID($"collider{i}");
            ImGui.Separator();

            string header = string.IsNullOrWhiteSpace(collider.Name) ? $"Collider {i + 1}" : collider.Name;
            if (ImGui.TreeNodeEx(header, ImGuiTreeNodeFlags.DefaultOpen))
            {
                bool enabled = collider.Enabled;
                string name = collider.Name;
                string shape = NormalizeChoice(collider.Shape, "capsule", ["capsule", "box"]);
                Vector3 position = collider.Position.ToVector3();
                Vector3 rotation = collider.RotationDegrees.ToVector3();
                bool changed = false;

                changed |= ImGui.Checkbox("Enabled", ref enabled);
                changed |= ImGui.InputText("Name", ref name, 256);
                changed |= DrawStringCombo("Shape", ref shape, ["capsule", "box"]);
                changed |= ImGui.DragFloat3("Local position", ref position, 0.02f);
                changed |= ImGui.DragFloat3("Local rotation", ref rotation, 0.5f);

                if (shape == "box")
                {
                    Vector3 size = collider.Size.ToVector3();
                    changed |= ImGui.DragFloat3("Size", ref size, 0.02f, 0.001f, 10000.0f);
                    if (changed)
                    {
                        collider.Size = new Vector3Dto(
                            Math.Max(0.001f, size.X),
                            Math.Max(0.001f, size.Y),
                            Math.Max(0.001f, size.Z));
                    }
                }
                else
                {
                    float radius = collider.Radius;
                    float height = collider.Height;
                    string axis = NormalizeChoice(collider.Axis, "y", ["x", "y", "z"]);
                    changed |= ImGui.DragFloat("Radius", ref radius, 0.01f, 0.001f, 1000.0f);
                    changed |= ImGui.DragFloat("Height", ref height, 0.01f, 0.0f, 10000.0f);
                    changed |= DrawStringCombo("Axis", ref axis, ["x", "y", "z"]);
                    if (changed)
                    {
                        collider.Radius = Math.Max(0.001f, radius);
                        collider.Height = Math.Max(0.0f, height);
                        collider.Axis = NormalizeChoice(axis, "y", ["x", "y", "z"]);
                    }
                }

                if (changed)
                {
                    collider.Enabled = enabled;
                    collider.Name = string.IsNullOrWhiteSpace(name) ? $"Collider {i + 1}" : name;
                    collider.Shape = NormalizeChoice(shape, "capsule", ["capsule", "box"]);
                    collider.Position = Vector3Dto.FromVector3(position);
                    collider.RotationDegrees = Vector3Dto.FromVector3(rotation);
                    _editorGame.ApplySelectedEntityToRuntime();
                }

                if (ImGui.SmallButton("Remove Collider"))
                {
                    removeIndex = i;
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            entity.Colliders.RemoveAt(removeIndex);
            _editorGame.ApplySelectedEntityToRuntime();
        }

        ImGui.TextWrapped("Colliders are local to the entity. Moving or rotating the entity moves all attached colliders while preserving their local offsets.");
    }

    private void DrawRelationInspector(GameEntity entity)
    {
        ImGui.Separator();
        ImGui.TextUnformatted("PMX Relation Binding");

        bool enabled = entity.Relation.Enabled;
        string relationEntity = entity.Relation.RelationEntity;
        bool bindTransform = entity.Relation.BindComponentTransform;
        bool bindLighting = entity.Relation.BindLighting;

        bool changed = false;
        changed |= ImGui.Checkbox("Enable relation", ref enabled);

        List<GameEntity> pmxEntities = _editorGame.Project.Scene.Entities
            .Where(item => !ReferenceEquals(item, entity) && string.Equals(item.Type, "pmx_model", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pmxEntities.Count > 0)
        {
            string[] labels = pmxEntities.Select(item => item.Name).ToArray();
            int relationIndex = Math.Max(0, pmxEntities.FindIndex(item =>
                string.Equals(item.Id, relationEntity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Name, relationEntity, StringComparison.OrdinalIgnoreCase)));
            if (ImGui.Combo("Relation PMX", ref relationIndex, labels, labels.Length))
            {
                relationEntity = pmxEntities[relationIndex].Name;
                changed = true;
            }
        }

        changed |= ImGui.InputText("Relation entity", ref relationEntity, 256);
        changed |= ImGui.Checkbox("Bind component transform", ref bindTransform);
        changed |= ImGui.Checkbox("Bind lighting", ref bindLighting);

        if (changed)
        {
            entity.Relation.Enabled = enabled;
            entity.Relation.RelationEntity = relationEntity;
            entity.Relation.BindComponentTransform = bindTransform;
            entity.Relation.BindLighting = bindLighting;
            _editorGame.ApplySelectedEntityToRuntime();
        }
    }

    private void DrawWaterInspector(GameEntity entity)
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Water Surface");

        WaterSurfaceSettings water = entity.Water;
        bool changed = false;
        float size = water.Size;
        float alpha = water.Alpha;
        float animationSpeed = water.AnimationSpeed;
        float normalTiling = water.NormalTiling;
        Vector3 deepColor = water.DeepColor.ToVector3();
        Vector3 reflectionTint = water.ReflectionTint.ToVector3();
        float skyReflectionStrength = water.SkyReflectionStrength;

        changed |= ImGui.DragFloat("Water size", ref size, 0.5f, 0.1f, 10000.0f);
        changed |= ImGui.SliderFloat("Water alpha", ref alpha, 0.0f, 1.0f);
        changed |= ImGui.DragFloat("Wave speed", ref animationSpeed, 0.001f, 0.0f, 10.0f, "%.3f");
        changed |= ImGui.DragFloat("Normal tiling", ref normalTiling, 0.5f, 0.001f, 10000.0f);
        changed |= ImGui.ColorEdit3("Deep color", ref deepColor);
        changed |= ImGui.ColorEdit3("Reflection tint", ref reflectionTint);
        changed |= ImGui.SliderFloat("Sky reflection", ref skyReflectionStrength, 0.0f, 1.0f);
        bool enableInteraction = water.EnableInteraction;
        float interactionRadius = water.InteractionRadius;
        float interactionStrength = water.InteractionStrength;
        changed |= ImGui.Checkbox("Enable water interaction", ref enableInteraction);
        changed |= ImGui.DragFloat("Interaction radius", ref interactionRadius, 0.01f, 0.001f, 100.0f);
        changed |= ImGui.SliderFloat("Interaction strength", ref interactionStrength, 0.0f, 4.0f);

        if (changed)
        {
            water.Size = Math.Max(size, 0.1f);
            water.Alpha = Math.Clamp(alpha, 0.0f, 1.0f);
            water.AnimationSpeed = Math.Max(animationSpeed, 0.0f);
            water.NormalTiling = Math.Max(normalTiling, 0.001f);
            water.DeepColor = Vector3Dto.FromVector3(deepColor);
            water.ReflectionTint = Vector3Dto.FromVector3(reflectionTint);
            water.SkyReflectionStrength = Math.Clamp(skyReflectionStrength, 0.0f, 1.0f);
            water.EnableInteraction = enableInteraction;
            water.InteractionRadius = Math.Max(0.001f, interactionRadius);
            water.InteractionStrength = Math.Clamp(interactionStrength, 0.0f, 4.0f);
            _editorGame.ApplySelectedWaterToRuntime();
        }
    }

    private void DrawPlaneInspector(GameEntity entity)
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Textured Plane");

        TexturedPlaneSettings plane = entity.Plane;
        string texturePath = plane.TexturePath;
        float width = plane.Width;
        float height = plane.Height;
        bool billboard = plane.Billboard;
        float opacity = plane.Opacity;
        Vector4 tint = plane.Tint.ToVector4();
        bool changed = false;

        if (DrawPathInput("Texture path", ref texturePath, 1024, "planeTexturePath"))
        {
            changed = true;
        }
        if (DrawRenderTextureCombo("Render texture", ref texturePath))
        {
            changed = true;
        }

        changed |= ImGui.DragFloat("Width", ref width, 0.01f, 0.001f, 10000.0f);
        changed |= ImGui.DragFloat("Height", ref height, 0.01f, 0.001f, 10000.0f);
        changed |= ImGui.Checkbox("Billboard", ref billboard);
        changed |= ImGui.SliderFloat("Opacity", ref opacity, 0.0f, 1.0f);
        changed |= ImGui.ColorEdit4("Tint", ref tint);

        if (changed)
        {
            plane.TexturePath = texturePath;
            entity.AssetPath = texturePath;
            plane.Width = Math.Max(0.001f, width);
            plane.Height = Math.Max(0.001f, height);
            plane.Billboard = billboard;
            plane.Opacity = Math.Clamp(opacity, 0.0f, 1.0f);
            plane.Tint = Vector4Dto.FromVector4(tint);
            _editorGame.ApplySelectedPlaneToRuntime();
        }
    }

    private bool DrawPathInput(string label, ref string value, uint maxLength, string id)
    {
        bool changed = ImGui.InputText(label, ref value, maxLength);
        ImGui.SameLine();
        if (ImGui.SmallButton($"Paste##{id}"))
        {
            PasteClipboard(ref value);
            changed = true;
        }

        return changed;
    }

    private bool DrawRenderTextureCombo(string label, ref string value)
    {
        List<string> options = ["(file texture)"];
        options.AddRange(_editorGame.Project.Scene.RenderTextures.Select(renderTexture => renderTexture.Name));
        int selectedIndex = 0;
        if (value.Trim().StartsWith("rt:", StringComparison.OrdinalIgnoreCase))
        {
            string name = value.Trim()["rt:".Length..].Trim();
            int match = options.FindIndex(option => string.Equals(option, name, StringComparison.OrdinalIgnoreCase));
            selectedIndex = Math.Max(0, match);
        }

        if (!ImGui.Combo(label, ref selectedIndex, options.ToArray(), options.Count))
        {
            return false;
        }

        if (selectedIndex <= 0)
        {
            if (value.Trim().StartsWith("rt:", StringComparison.OrdinalIgnoreCase))
            {
                value = string.Empty;
                return true;
            }

            return false;
        }

        value = $"rt:{options[selectedIndex]}";
        return true;
    }

    private void PasteClipboard(ref string target)
    {
        if (_editorGame.TryGetClipboardText(out string text))
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
            Path.Combine(fontsDir, "meiryo.ttc")
        ];
    }

    private static IEnumerable<string> GetLinuxFontCandidates()
    {
        return
        [
            "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
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
