using System.Numerics;
using System.Text.RegularExpressions;
using ImGuiNET;
using Silk.NET.OpenGLES;
using Zhengyan.DigitalWife.Audio;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed class GameEditorOverlayComponent(GameEditorGame editorGame) : DrawableGameComponent
{
    private readonly GameEditorGame _editorGame = editorGame;
    private IImGuiBackendController? _controller;
    private bool _isViewportHovered;
    private bool _isViewportFocused;
    private string _projectDirectory = editorGame.ProjectDirectory;
    private string _newProjectName = editorGame.Project.Name;
    private string _pmxPath = string.Empty;
    private string _audioPath = string.Empty;
    private string _motionPath = string.Empty;
    private string _spritePath = string.Empty;
    private string _newSceneName = "New Scene";
    private string _packageOutputPath = string.Empty;
    private string _packagePassword = string.Empty;
    private bool _packageEncrypt;
    private bool _packageSplit;
    private int _packageSplitPartSizeMb = 512;
    private bool _packageIncludeSaves;
    private int _selectedMotionAssetIndex;
    private string _particlePreset = "sakura";
    private bool _copyAssets = true;
    private int _preferredLanguageIndex;
    private readonly Dictionary<string, ITexture2D> _spriteTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ScreenSpriteDrawCommand> _backgroundSpriteCommands = [];
    private IScreenSpriteRenderer? _backgroundSpriteRenderer;
    private static readonly string[] CameraControlModes =
    [
        "editor",
        "fixed",
        "first_person",
        "fps_control",
        "third_person",
        "shoulder",
        "lock_on",
        "free_fly",
        "top_down",
        "isometric",
        "side_scroller",
        "cinematic_follow",
        "orbital_follow",
        "custom"
    ];

    public bool CanInteractWithScenePointer => _isViewportHovered;

    public bool CanInteractWithSceneKeyboard => _isViewportFocused;

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        _backgroundSpriteRenderer = Game.GraphicsDevice.CreateScreenSpriteRenderer();

        Action? configureFonts = ImGuiFontResolver.TryGetCjkFontPath(out string cjkFontPath)
            ? () => ConfigureIoFontAtlas(cjkFontPath)
            : null;
        _controller = ImGuiBackendController.Create(Game, configureFonts);

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
        foreach (ITexture2D texture in _spriteTextures.Values)
        {
            texture.Dispose();
        }

        _spriteTextures.Clear();
        _backgroundSpriteRenderer?.Dispose();
        _backgroundSpriteRenderer = null;
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
            _controller!.GetTextureBinding(_editorGame.SceneRenderTarget.ColorTextureHandle),
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

            if (ImGui.BeginTabItem("Scenes"))
            {
                DrawScenesPanel();
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
        PruneSpriteTextureCache(_editorGame.Project.Scene.Sprites);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(viewportMin, viewportMin + viewportSize, true);

        foreach (SpriteSettings sprite in _editorGame.Project.Scene.Sprites
            .Where(sprite => sprite.Visible && sprite.DrawOrder >= 0 && !string.IsNullOrWhiteSpace(sprite.Path))
            .OrderBy(sprite => sprite.DrawOrder))
        {
            RuntimeTextureHandle texture = GetSpriteTextureHandle(sprite.Path);
            if (!texture.IsValid)
            {
                continue;
            }

            LayoutRect rect = ResolveSpriteRect(sprite, viewportSize);
            Vector2 min = viewportMin + new Vector2(rect.X, rect.Y);
            Vector2 max = min + new Vector2(Math.Max(rect.Width, 1.0f), Math.Max(rect.Height, 1.0f));
            uint tint = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, Math.Clamp(sprite.Opacity, 0.0f, 1.0f)));
            AddSpriteImage(drawList, _controller!.GetTextureBinding(texture), min, max, sprite.RotationDegrees, tint, IsRuntimeTextureReference(sprite.Path));
        }

        drawList.PopClipRect();
    }

    public void DrawBackgroundSprites(
        int layoutWidth,
        int layoutHeight,
        int viewportX,
        int viewportY,
        int viewportWidth,
        int viewportHeight)
    {
        if (_backgroundSpriteRenderer is null)
        {
            return;
        }

        IReadOnlyList<SpriteSettings> sprites = _editorGame.Project.Scene.Sprites;
        PruneSpriteTextureCache(sprites);
        _backgroundSpriteCommands.Clear();

        foreach (SpriteSettings sprite in sprites
            .Where(sprite => sprite.Visible && sprite.DrawOrder < 0 && !string.IsNullOrWhiteSpace(sprite.Path))
            .OrderBy(sprite => sprite.DrawOrder))
        {
            RuntimeTextureHandle texture = GetSpriteTextureHandle(sprite.Path);
            if (!texture.IsValid)
            {
                continue;
            }

            LayoutRect rect = ResolveSpriteRect(sprite, new Vector2(layoutWidth, layoutHeight));
            Vector2 min = new(rect.X - viewportX, rect.Y - viewportY);
            Vector2 max = min + new Vector2(Math.Max(rect.Width, 1.0f), Math.Max(rect.Height, 1.0f));
            _backgroundSpriteCommands.Add(new ScreenSpriteDrawCommand(
                texture,
                min,
                max,
                sprite.RotationDegrees,
                sprite.Opacity,
                IsRuntimeTextureReference(sprite.Path)));
        }

        _backgroundSpriteRenderer.Draw(_backgroundSpriteCommands, viewportWidth, viewportHeight);
    }

    private static void AddSpriteImage(ImDrawListPtr drawList, nint textureId, Vector2 min, Vector2 max, float rotationDegrees, uint tint, bool flipV)
    {
        Vector2 uv0 = flipV ? new Vector2(0.0f, 1.0f) : Vector2.Zero;
        Vector2 uv1 = flipV ? new Vector2(1.0f, 0.0f) : Vector2.One;
        Vector2 uvTopRight = flipV ? Vector2.One : new Vector2(1.0f, 0.0f);
        Vector2 uvBottomLeft = flipV ? Vector2.Zero : new Vector2(0.0f, 1.0f);

        if (MathF.Abs(rotationDegrees) <= 0.001f)
        {
            drawList.AddImage((nint)textureId, min, max, uv0, uv1, tint);
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
        drawList.AddImageQuad((nint)textureId, p1, p2, p3, p4, uv0, uvTopRight, uv1, uvBottomLeft, tint);
    }

    private ITexture2D? GetSpriteTexture(string path)
    {
        if (Game is null)
        {
            return null;
        }

        if (!TryResolveSpriteTexturePath(path, out string fullPath))
        {
            return null;
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        if (_spriteTextures.TryGetValue(fullPath, out ITexture2D? texture))
        {
            return texture;
        }

        texture = Game.GraphicsDevice.CreateTexture2D();
        texture.LoadFromFile(fullPath);
        _spriteTextures[fullPath] = texture;
        return texture;
    }

    private uint GetSpriteTextureId(string path)
    {
        return GetSpriteTextureHandle(path).LegacyTextureId;
    }

    private RuntimeTextureHandle GetSpriteTextureHandle(string path)
    {
        if (_editorGame.RenderTextureManager is not null
            && _editorGame.RenderTextureManager.TryGetTextureHandle(path, out RuntimeTextureHandle handle))
        {
            return handle;
        }

        ITexture2D? texture = GetSpriteTexture(path);
        return texture is null
            ? default
            : new RuntimeTextureHandle(texture.Backend, texture.LegacyTextureId, texture.NativeResource);
    }

    private void PruneSpriteTextureCache(IEnumerable<SpriteSettings> sprites)
    {
        HashSet<string> activePaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (SpriteSettings sprite in sprites)
        {
            if (!sprite.Visible || string.IsNullOrWhiteSpace(sprite.Path) || IsRuntimeTextureReference(sprite.Path))
            {
                continue;
            }

            if (TryResolveSpriteTexturePath(sprite.Path, out string fullPath) && File.Exists(fullPath))
            {
                activePaths.Add(fullPath);
            }
        }

        foreach (string stalePath in _spriteTextures.Keys.Where(path => !activePaths.Contains(path)).ToArray())
        {
            _spriteTextures[stalePath].Dispose();
            _spriteTextures.Remove(stalePath);
        }
    }

    private bool TryResolveSpriteTexturePath(string path, out string fullPath)
    {
        try
        {
            fullPath = GameProjectPath.ToAbsolute(_editorGame.ProjectDirectory, path);
            return !string.IsNullOrWhiteSpace(fullPath);
        }
        catch
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private static bool IsRuntimeTextureReference(string path)
    {
        return path.Trim().StartsWith("rt:", StringComparison.OrdinalIgnoreCase);
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

            LayoutRect rect = ResolveGuiRect(control, viewportSize);
            Vector2 position = viewportMin + new Vector2(rect.X, rect.Y);
            Vector2 size = new(Math.Max(rect.Width, 1.0f), Math.Max(rect.Height, 1.0f));
            Vector2 max = position + size;

            GuiControlStyleSettings style = control.Style;
            Vector4 backgroundColor = style.BackgroundColor.ToVector4();
            Vector4 textColor = style.TextColor.ToVector4();
            Vector4 borderColor = style.BorderColor.ToVector4();
            float rounding = Math.Max(style.Rounding, 0.0f);
            float borderThickness = Math.Max(style.BorderThickness, 0.0f);
            string type = control.Type.ToLowerInvariant();
            string text = type == "textbox"
                ? control.Text
                : string.IsNullOrWhiteSpace(control.Text) ? control.Name : control.Text;

            if (type == "label")
            {
                Vector2 padding = new(8.0f, 5.0f);
                drawList.AddRectFilled(position, max, ImGui.GetColorU32(backgroundColor), rounding);
                drawList.AddRect(position, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, borderThickness);
                DrawTextBlock(drawList, position + padding, max - padding, text, textColor, style, control.WordWrap, control.LayoutMode, viewportSize);
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

                float fontSize = ResolveGuiFontSize(style, control.LayoutMode, viewportSize);
                Vector2 textSize = CalcScaledTextSize(text, fontSize);
                Vector2 textMin = position + new Vector2(boxSize + 16.0f, 0.0f);
                Vector2 textMax = max - new Vector2(8.0f, 0.0f);
                AddScaledText(drawList, GetAlignedTextPosition(textMin, textMax, textSize, style), ImGui.GetColorU32(textColor), text, fontSize);
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
                float fontSize = ResolveGuiFontSize(style, control.LayoutMode, viewportSize);
                Vector2 textSize = CalcScaledTextSize(selectedText, fontSize);
                AddScaledText(drawList, GetAlignedTextPosition(position + new Vector2(8.0f, 0.0f), max - new Vector2(28.0f, 0.0f), textSize, style), ImGui.GetColorU32(textColor), selectedText, fontSize);
            }
            else if (type == "textbox")
            {
                Vector2 padding = new(8.0f, 5.0f);
                drawList.AddRectFilled(position, max, ImGui.GetColorU32(backgroundColor), rounding);
                drawList.AddRect(position, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, borderThickness);
                DrawTextBlock(drawList, position + padding, max - padding, text, textColor, style, control.Multiline || control.WordWrap, control.LayoutMode, viewportSize);
            }
            else if (type == "progress_bar")
            {
                float progress = Math.Clamp(control.Progress, 0.0f, 1.0f);
                drawList.AddRectFilled(position, max, ImGui.GetColorU32(backgroundColor), rounding);
                Vector2 fillMax = new(position.X + (size.X * progress), max.Y);
                drawList.AddRectFilled(position, fillMax, ImGui.GetColorU32(style.ActiveColor.ToVector4()), rounding);
                drawList.AddRect(position, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, borderThickness);

                string progressText = string.IsNullOrWhiteSpace(text) ? $"{progress:P0}" : text;
                float fontSize = ResolveGuiFontSize(style, control.LayoutMode, viewportSize);
                Vector2 textSize = CalcScaledTextSize(progressText, fontSize);
                Vector2 textPosition = GetAlignedTextPosition(position + new Vector2(6.0f, 4.0f), max - new Vector2(6.0f, 4.0f), textSize, style);
                AddScaledText(drawList, textPosition, ImGui.GetColorU32(textColor), progressText, fontSize);
            }
            else
            {
                drawList.AddRectFilled(position, max, ImGui.GetColorU32(backgroundColor), rounding);
                drawList.AddRect(position, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, borderThickness);

                float fontSize = ResolveGuiFontSize(style, control.LayoutMode, viewportSize);
                Vector2 textSize = CalcScaledTextSize(text, fontSize);
                Vector2 textPosition = GetAlignedTextPosition(position + new Vector2(6.0f, 4.0f), max - new Vector2(6.0f, 4.0f), textSize, style);
                AddScaledText(drawList, textPosition, ImGui.GetColorU32(textColor), text, fontSize);
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

    private LayoutRect ResolveGuiRect(GuiControlSettings control, Vector2 actualSize)
    {
        return LayoutResolver.Resolve(
            control.LayoutMode,
            control.X,
            control.Y,
            control.Width,
            control.Height,
            actualSize.X,
            actualSize.Y,
            _editorGame.Project.Window.Width,
            _editorGame.Project.Window.Height);
    }

    private LayoutRect ResolveSpriteRect(SpriteSettings sprite, Vector2 actualSize)
    {
        return SpriteLayoutResolver.Resolve(
            sprite,
            actualSize.X,
            actualSize.Y,
            _editorGame.Project.Window.Width,
            _editorGame.Project.Window.Height);
    }

    private void DrawTextBlock(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        string text,
        Vector4 color,
        GuiControlStyleSettings style,
        bool wordWrap,
        string layoutMode,
        Vector2 actualSize)
    {
        Vector2 available = Vector2.Max(max - min, Vector2.One);
        float fontSize = ResolveGuiFontSize(style, layoutMode, actualSize);
        string[] lines = BuildTextLines(text, available.X, wordWrap, fontSize);
        float lineHeight = Math.Max(CalcScaledTextSize("Ag", fontSize).Y, 1.0f);
        float blockHeight = lineHeight * lines.Length;
        float startY = min.Y + ResolveVerticalOffset(style.VerticalAlignment, available.Y, blockHeight);
        uint textColor = ImGui.GetColorU32(color);

        drawList.PushClipRect(min, max, true);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            Vector2 lineSize = CalcScaledTextSize(line, fontSize);
            float x = min.X + ResolveHorizontalOffset(style.HorizontalAlignment, available.X, lineSize.X);
            float y = startY + (lineHeight * i);
            AddScaledText(drawList, new Vector2(x, y), textColor, line, fontSize);
        }

        drawList.PopClipRect();
    }

    private static string[] BuildTextLines(string text, float maxWidth, bool wordWrap, float fontSize)
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
                if (current.Length > 0 && CalcScaledTextSize(candidate, fontSize).X > maxWidth)
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

    private float ResolveGuiFontSize(GuiControlStyleSettings style, string layoutMode, Vector2 actualSize)
    {
        return LayoutResolver.ResolveFontSize(
            layoutMode,
            style.FontSize,
            actualSize.X,
            actualSize.Y,
            _editorGame.Project.Window.Width,
            _editorGame.Project.Window.Height);
    }

    private static Vector2 CalcScaledTextSize(string text, float fontSize)
    {
        float scale = fontSize / Math.Max(ImGui.GetFontSize(), 1.0f);
        return ImGui.CalcTextSize(text) * scale;
    }

    private static void AddScaledText(ImDrawListPtr drawList, Vector2 position, uint color, string text, float fontSize)
    {
        drawList.AddText(ImGui.GetFont(), fontSize, position, color, text);
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
        ImGui.PushID("projectPanel");

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
        _ = DrawTextInputWithPaste("Project name", ref _newProjectName, 256, "newProjectName");
        if (ImGui.Button("New Project"))
        {
            _editorGame.SetProjectDirectory(_projectDirectory);
            _editorGame.NewProject(_newProjectName);
        }

        ImGui.Separator();
        GameProject project = _editorGame.Project;
        string projectName = project.Name;
        string projectVersion = project.Version;
        if (DrawTextInputWithPaste("Name", ref projectName, 256, "projectName"))
        {
            project.Name = projectName;
        }

        if (DrawTextInputWithPaste("Version", ref projectVersion, 128, "projectVersion"))
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

        DrawLlmSettings(project.Llm);
        DrawMicrophoneSettings(project.Microphone);
        DrawVoiceSettings(project.Voice);
        DrawAsrSettings(project.Asr);
        DrawRealtimeVoiceSettings(project.RealtimeVoice);
        DrawPackageExportSettings();

        ImGui.TextWrapped("The editor saves scene, resources, and script templates into the selected project directory.");
        ImGui.PopID();
    }

    private static void DrawMicrophoneSettings(GameProjectMicrophoneSettings microphone)
    {
        if (!ImGui.CollapsingHeader("Microphone", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ImGui.PushID("microphoneSettings");

        bool autoDetectOnPlayerLoad = microphone.AutoDetectOnPlayerLoad;
        if (ImGui.Checkbox("Auto-detect microphone when GamePlayer loads", ref autoDetectOnPlayerLoad))
        {
            microphone.AutoDetectOnPlayerLoad = autoDetectOnPlayerLoad;
        }

        ImGui.TextWrapped("When enabled, published GamePlayer builds detect a microphone on the player's machine during loading and use that runtime device for ASR and Realtime Voice. Saved input device indexes remain available as advanced fallbacks.");
        ImGui.PopID();
    }

    private void DrawPackageExportSettings()
    {
        if (!ImGui.CollapsingHeader("Package / Publish", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ImGui.PushID("packageExport");
        if (string.IsNullOrWhiteSpace(_packageOutputPath))
        {
            _packageOutputPath = BuildDefaultPackageOutputPath();
        }

        DrawPathInput("Output package", ref _packageOutputPath, 1024, "packageOutput");
        ImGui.Checkbox("Encrypt package", ref _packageEncrypt);
        if (_packageEncrypt)
        {
            _ = DrawTextInputWithPaste("Password", ref _packagePassword, 256, "packagePassword");
        }

        ImGui.Checkbox("Split package", ref _packageSplit);
        if (_packageSplit)
        {
            ImGui.DragInt("Part size MB", ref _packageSplitPartSizeMb, 1.0f, 1, 102400);
            _packageSplitPartSizeMb = Math.Clamp(_packageSplitPartSizeMb, 1, 102400);
        }

        ImGui.Checkbox("Include saves", ref _packageIncludeSaves);
        if (ImGui.Button("Export Package"))
        {
            try
            {
                long splitBytes = _packageSplit
                    ? Math.Max(1L, _packageSplitPartSizeMb) * 1024L * 1024L
                    : 0L;
                string? password = _packageEncrypt ? _packagePassword : null;
                _ = _editorGame.ExportProjectPackage(
                    _packageOutputPath,
                    password,
                    splitBytes,
                    _packageIncludeSaves);
            }
            catch (Exception ex)
            {
                _editorGame.UpdateStatus($"Export package failed: {ex.Message}");
            }
        }

        ImGui.TextWrapped("GamePlayer can load either the development project directory or the exported .dwgame package. Split packages are written as .dwgame.001, .dwgame.002, ... and GamePlayer can start from the .dwgame path or the first .001 part. Encryption prevents casual editing, but the password must still be provided at runtime.");
        ImGui.PopID();
    }

    private string BuildDefaultPackageOutputPath()
    {
        string projectName = ToSafeFileStem(_editorGame.Project.Name);
        string parent = Directory.GetParent(_editorGame.ProjectDirectory)?.FullName ?? _editorGame.ProjectDirectory;
        return Path.Combine(parent, $"{projectName}{GameProjectPackage.PackageExtension}");
    }

    private static string ToSafeFileStem(string value)
    {
        string stem = string.IsNullOrWhiteSpace(value) ? "Game" : value.Trim();
        foreach (char ch in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(ch, '_');
        }

        stem = stem.Replace(' ', '_');
        return string.IsNullOrWhiteSpace(stem) ? "Game" : stem;
    }

    private void DrawScenesPanel()
    {
        ImGui.PushID("scenesPanel");

        GameProject project = _editorGame.Project;
        GameProjectStore.NormalizeScenes(project);

        string activeScenePath = _editorGame.ActiveScenePath;
        string activePreview = BuildSceneLabel(project.Scene, activeScenePath);
        if (ImGui.BeginCombo("Editor scene", activePreview))
        {
            foreach (string scenePath in project.Scenes)
            {
                string normalizedScenePath = GameProjectStore.NormalizeScenePath(scenePath);
                bool selected = string.Equals(normalizedScenePath, activeScenePath, StringComparison.OrdinalIgnoreCase);
                string label = selected && ReferenceEquals(project.Scene, _editorGame.Project.Scene)
                    ? BuildSceneLabel(project.Scene, normalizedScenePath)
                    : normalizedScenePath;

                if (ImGui.Selectable($"{label}##scene_{normalizedScenePath}", selected))
                {
                    try
                    {
                        _editorGame.SwitchScene(normalizedScenePath);
                    }
                    catch (Exception ex)
                    {
                        _editorGame.UpdateStatus($"Switch scene failed: {ex.Message}");
                    }
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextWrapped($"Current path: {activeScenePath}");

        string startupScene = GameProjectStore.NormalizeScenePath(project.DefaultScene);
        int startupSceneIndex = Math.Max(0, project.Scenes.FindIndex(path =>
            string.Equals(GameProjectStore.NormalizeScenePath(path), startupScene, StringComparison.OrdinalIgnoreCase)));
        string[] scenePaths = project.Scenes
            .Select(GameProjectStore.NormalizeScenePath)
            .ToArray();
        if (scenePaths.Length > 0 && ImGui.Combo("GamePlayer startup scene", ref startupSceneIndex, scenePaths, scenePaths.Length))
        {
            project.DefaultScene = scenePaths[startupSceneIndex];
        }

        ImGui.Separator();

        _ = DrawTextInputWithPaste("New scene name", ref _newSceneName, 256, "newSceneName");
        if (ImGui.Button("New Scene"))
        {
            try
            {
                _editorGame.CreateScene(_newSceneName);
                _newSceneName = "New Scene";
            }
            catch (Exception ex)
            {
                _editorGame.UpdateStatus($"Create scene failed: {ex.Message}");
            }
        }

        ImGui.SameLine();
        bool canDelete = project.Scenes.Count > 1;
        if (!canDelete)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Delete Active Scene"))
        {
            try
            {
                _editorGame.DeleteScene(activeScenePath);
            }
            catch (Exception ex)
            {
                _editorGame.UpdateStatus($"Delete scene failed: {ex.Message}");
            }
        }

        if (!canDelete)
        {
            ImGui.EndDisabled();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Scenes");
        for (int i = 0; i < project.Scenes.Count; i++)
        {
            string scenePath = GameProjectStore.NormalizeScenePath(project.Scenes[i]);
            bool selected = string.Equals(scenePath, activeScenePath, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{scenePath}##sceneList{i}", selected))
            {
                try
                {
                    _editorGame.SwitchScene(scenePath);
                }
                catch (Exception ex)
                {
                    _editorGame.UpdateStatus($"Switch scene failed: {ex.Message}");
                }
            }
        }

        ImGui.TextWrapped("Editor scene switching saves the current scene file first, then reloads the selected scene into the viewport. GamePlayer starts from the startup scene.");
        ImGui.PopID();
    }

    private static string BuildSceneLabel(GameProjectScene scene, string scenePath)
    {
        return string.IsNullOrWhiteSpace(scene.Name)
            ? scenePath
            : $"{scene.Name} ({scenePath})";
    }

    private void DrawLlmSettings(GameProjectLlmSettings llm)
    {
        if (!ImGui.CollapsingHeader("LLM / OpenAI-compatible"))
        {
            return;
        }

        ImGui.PushID("llmSettings");

        bool enabled = llm.Enabled;
        if (ImGui.Checkbox("Enable runtime LLM", ref enabled))
        {
            llm.Enabled = enabled;
        }

        bool enableSkills = llm.EnableSkills;
        if (ImGui.Checkbox("Enable skills tools", ref enableSkills))
        {
            llm.EnableSkills = enableSkills;
        }

        bool enableMemory = llm.EnableMemory;
        if (ImGui.Checkbox("Enable memory tools", ref enableMemory))
        {
            llm.EnableMemory = enableMemory;
        }

        if (llm.EnableSkills)
        {
            ImGui.TextWrapped("Skills are loaded from the project skills/ directory. Built-in LLM tools can read skills, read/write project files, search text, and run shell commands inside the project directory.");
        }

        if (llm.EnableMemory)
        {
            ImGui.TextWrapped("Memory tools read and write long-term memory under the local save memory/ directory. Use this when the in-game agent should remember user preferences, identity, relationships, or ongoing tasks across turns.");
        }

        string provider = llm.Provider;
        if (DrawTextInputWithPaste("LLM provider", ref provider, 128, "llmProvider"))
        {
            llm.Provider = provider;
        }

        string baseUrl = llm.BaseUrl;
        if (DrawTextInputWithPaste("Base URL", ref baseUrl, 1024, "llmBaseUrl"))
        {
            llm.BaseUrl = baseUrl;
        }

        string apiKeyEnvironmentVariable = llm.ApiKeyEnvironmentVariable;
        if (DrawTextInputWithPaste("API key env var", ref apiKeyEnvironmentVariable, 128, "llmApiKeyEnvVar"))
        {
            llm.ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable;
        }

        string apiKey = llm.ApiKey;
        if (DrawTextInputWithPaste("API key override", ref apiKey, 1024, "llmApiKey", ImGuiInputTextFlags.Password))
        {
            llm.ApiKey = apiKey;
        }

        string model = llm.Model;
        if (DrawTextInputWithPaste("Model", ref model, 256, "llmModel"))
        {
            llm.Model = model;
        }

        string chatCompletionsPath = llm.ChatCompletionsPath;
        if (DrawTextInputWithPaste("Chat completions path", ref chatCompletionsPath, 256, "llmChatCompletionsPath"))
        {
            llm.ChatCompletionsPath = string.IsNullOrWhiteSpace(chatCompletionsPath)
                ? "/v1/chat/completions"
                : chatCompletionsPath;
        }

        int timeoutSeconds = llm.TimeoutSeconds;
        if (ImGui.DragInt("Timeout seconds", ref timeoutSeconds, 1.0f, 1, 3600))
        {
            llm.TimeoutSeconds = Math.Clamp(timeoutSeconds, 1, 3600);
        }

        bool useDefaultTemperature = llm.DefaultTemperature.HasValue;
        if (ImGui.Checkbox("Use default temperature", ref useDefaultTemperature))
        {
            llm.DefaultTemperature = useDefaultTemperature ? 0.7f : null;
        }

        if (llm.DefaultTemperature.HasValue)
        {
            float temperature = llm.DefaultTemperature.Value;
            if (ImGui.DragFloat("Default temperature", ref temperature, 0.01f, 0.0f, 2.0f, "%.2f"))
            {
                llm.DefaultTemperature = Math.Clamp(temperature, 0.0f, 2.0f);
            }
        }

        ImGui.TextWrapped("Scripts call Scene.Llm / scene.llm. Prefer API key environment variables for project files that may be shared.");
        ImGui.PopID();
    }

    private void DrawWindowSettings(GameWindowSettings window)
    {
        if (!ImGui.CollapsingHeader("Window / Runtime", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ImGui.PushID("windowSettings");

        string title = window.Title;
        if (DrawTextInputWithPaste("Window title", ref title, 256, "windowTitle"))
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
        bool desktopSpriteMode = window.DesktopSpriteMode;
        bool desktopSpriteClickThrough = window.DesktopSpriteClickThrough;
        string desktopSpriteDragButton = NormalizeDesktopSpriteDragButton(window.DesktopSpriteDragButton);
        bool desktopSpriteTrayEnabled = window.DesktopSpriteTrayEnabled;
        string desktopSpriteTrayIconPath = window.DesktopSpriteTrayIconPath;
        string desktopSpriteTrayWindowsIconPath = window.DesktopSpriteTrayWindowsIconPath;
        string desktopSpriteTrayLinuxIconPath = window.DesktopSpriteTrayLinuxIconPath;
        string desktopSpriteTrayMacOSIconPath = window.DesktopSpriteTrayMacOSIconPath;
        bool fullscreen = window.Fullscreen;
        bool resizable = window.Resizable;
        string timingMode = window.TimingMode;
        string graphicsBackend = GraphicsBackendNames.Parse(
            _editorGame.Project.Runtime.GraphicsBackend).ToSettingValue();
        bool useOpenCl = _editorGame.Project.Runtime.UseOpenCL;
        bool useVulkanCompute = _editorGame.Project.Runtime.UseVulkanCompute;
        bool changed = false;
        changed |= ImGui.Checkbox("Desktop sprite mode", ref desktopSpriteMode);
        if (!desktopSpriteMode)
        {
            ImGui.BeginDisabled();
        }

        changed |= ImGui.Checkbox("Desktop sprite click-through", ref desktopSpriteClickThrough);
        changed |= DrawStringCombo("Desktop sprite drag button", ref desktopSpriteDragButton, ["none", "left", "right", "middle"]);
        changed |= ImGui.Checkbox("Desktop sprite system tray", ref desktopSpriteTrayEnabled);
        if (DrawPathInput("Desktop sprite tray icon (fallback)", ref desktopSpriteTrayIconPath, 1024, "desktopSpriteTrayIconPath"))
        {
            changed = true;
        }

        if (DrawPathInput("Windows tray icon (.ico)", ref desktopSpriteTrayWindowsIconPath, 1024, "desktopSpriteTrayWindowsIconPath"))
        {
            changed = true;
        }

        if (DrawPathInput("Linux tray icon (.png)", ref desktopSpriteTrayLinuxIconPath, 1024, "desktopSpriteTrayLinuxIconPath"))
        {
            changed = true;
        }

        if (DrawPathInput("macOS tray icon (.png)", ref desktopSpriteTrayMacOSIconPath, 1024, "desktopSpriteTrayMacOSIconPath"))
        {
            changed = true;
        }
        DrawDesktopSpriteTrayMenuItems(window);

        if (!desktopSpriteMode)
        {
            ImGui.EndDisabled();
        }

        changed |= ImGui.DragInt("Width", ref width, 1.0f, 320, 7680);
        changed |= ImGui.DragInt("Height", ref height, 1.0f, 240, 4320);
        changed |= ImGui.Checkbox("Fullscreen", ref fullscreen);
        changed |= ImGui.Checkbox("Resizable", ref resizable);
        changed |= DrawStringCombo("Timing Mode", ref timingMode, ["time_synchronized", "frame_rate_dependent"]);
        if (DrawStringCombo("Graphics backend", ref graphicsBackend, ["Auto", "OpenGL", "Vulkan"]))
        {
            _editorGame.SetGraphicsBackend(GraphicsBackendNames.Parse(graphicsBackend));
        }

        ImGui.TextDisabled($"Active: {_editorGame.ActiveGraphicsBackend.ToSettingValue()}");
        GraphicsBackend configuredBackend = GraphicsBackendNames.Parse(graphicsBackend);
        GraphicsBackend computeBackend = configuredBackend == GraphicsBackend.Auto
            ? _editorGame.ActiveGraphicsBackend
            : configuredBackend;
        if (computeBackend == GraphicsBackend.Vulkan)
        {
            changed |= ImGui.Checkbox("Use Vulkan Compute for PMX skinning", ref useVulkanCompute);
        }
        else
        {
            changed |= ImGui.Checkbox("Use OpenCL for PMX skinning", ref useOpenCl);
        }

        if (changed)
        {
            window.DesktopSpriteMode = desktopSpriteMode;
            window.DesktopSpriteClickThrough = desktopSpriteClickThrough;
            window.DesktopSpriteDragButton = NormalizeDesktopSpriteDragButton(desktopSpriteDragButton);
            window.DesktopSpriteTrayEnabled = desktopSpriteTrayEnabled;
            window.DesktopSpriteTrayIconPath = desktopSpriteTrayIconPath;
            window.DesktopSpriteTrayWindowsIconPath = desktopSpriteTrayWindowsIconPath;
            window.DesktopSpriteTrayLinuxIconPath = desktopSpriteTrayLinuxIconPath;
            window.DesktopSpriteTrayMacOSIconPath = desktopSpriteTrayMacOSIconPath;
            window.Width = Math.Max(320, width);
            window.Height = Math.Max(240, height);
            window.Fullscreen = desktopSpriteMode ? false : fullscreen;
            window.Resizable = resizable;
            window.TimingMode = NormalizeChoice(timingMode, "time_synchronized", ["time_synchronized", "frame_rate_dependent"]);
            _editorGame.Project.Runtime.UseOpenCL = useOpenCl;
            _editorGame.Project.Runtime.UseVulkanCompute = useVulkanCompute;
            _editorGame.ApplyRuntimeSettings();
        }

        if (ImGui.Button("Apply To Editor Window"))
        {
            _editorGame.ApplyWindowSettings();
        }

        ImGui.TextWrapped("GamePlayer applies these settings on project load. Desktop sprite mode uses a transparent, borderless, topmost window and forces windowed mode. Click-through excludes transparent pixels from mouse input so clicks pass to the desktop or apps underneath. Drag button controls which mouse button moves the desktop sprite window. OpenGL can use OpenCL for PMX skinning; Vulkan can use Vulkan Compute. Either path falls back to CPU if initialization fails. The button above only previews regular window settings in the editor.");
        ImGui.PopID();
    }

    private void DrawVoiceSettings(GameProjectVoiceSettings voice)
    {
        if (!ImGui.CollapsingHeader("Voice / TTS", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ImGui.PushID("voiceSettings");

        bool enabled = voice.Enabled;
        if (ImGui.Checkbox("Enable runtime TTS", ref enabled))
        {
            voice.Enabled = enabled;
        }

        string[] playbackBackends = ["OpenAL", "PortAudio"];
        int playbackBackendIndex = voice.PlaybackBackend == AudioPlaybackBackend.PortAudio ? 1 : 0;
        if (ImGui.Combo("Speech playback backend", ref playbackBackendIndex, playbackBackends, playbackBackends.Length))
        {
            voice.PlaybackBackend = playbackBackendIndex == 1
                ? AudioPlaybackBackend.PortAudio
                : AudioPlaybackBackend.OpenAL;
        }

        int outputDeviceIndexValue = voice.OutputDeviceIndex ?? -1;
        if (ImGui.DragInt("Speech output device index", ref outputDeviceIndexValue, 1.0f, -1, 9999))
        {
            voice.OutputDeviceIndex = outputDeviceIndexValue < 0 ? null : outputDeviceIndexValue;
        }

        ImGui.TextDisabled("Set to -1 to use the default output device. This is only used when speech playback backend is PortAudio.");

        string provider = voice.TtsProvider;
        if (DrawTextInputWithPaste("TTS provider", ref provider, 128, "ttsProvider"))
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
        if (DrawTextInputWithPaste("Inference provider", ref inferenceProvider, 128, "ttsInferenceProvider"))
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

        ImGui.Text("Lip-sync languages");
        bool chineseLipSync = HasLipSyncLanguage(voice.LipSync, "Chinese");
        if (ImGui.Checkbox("Chinese##LipSyncLanguage", ref chineseLipSync))
        {
            SetLipSyncLanguageSelection(voice.LipSync, "Chinese", chineseLipSync);
        }

        bool japaneseLipSync = HasLipSyncLanguage(voice.LipSync, "Japanese");
        if (ImGui.Checkbox("Japanese##LipSyncLanguage", ref japaneseLipSync))
        {
            SetLipSyncLanguageSelection(voice.LipSync, "Japanese", japaneseLipSync);
        }

        bool englishLipSync = HasLipSyncLanguage(voice.LipSync, "English");
        if (ImGui.Checkbox("English##LipSyncLanguage", ref englishLipSync))
        {
            SetLipSyncLanguageSelection(voice.LipSync, "English", englishLipSync);
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

        bool useFallbackVowelOnNoMatch = voice.LipSync.UseFallbackVowelOnNoMatch;
        if (ImGui.Checkbox("Fallback vowel when no match", ref useFallbackVowelOnNoMatch))
        {
            voice.LipSync.UseFallbackVowelOnNoMatch = useFallbackVowelOnNoMatch;
        }

        if (voice.LipSync.UseFallbackVowelOnNoMatch)
        {
            string[] fallbackVowels = ["\u3042", "\u3044", "\u3046", "\u3048", "\u304A"];
            string currentFallbackVowel = voice.LipSync.GetEffectiveNoMatchFallbackVowel();
            int fallbackVowelIndex = Array.IndexOf(fallbackVowels, currentFallbackVowel);
            if (fallbackVowelIndex < 0)
            {
                fallbackVowelIndex = 0;
            }

            if (ImGui.Combo("Fallback vowel", ref fallbackVowelIndex, fallbackVowels, fallbackVowels.Length))
            {
                voice.LipSync.NoMatchFallbackVowel = fallbackVowels[fallbackVowelIndex];
            }
        }

        ImGui.TextWrapped("Scripts call Entity.Speak(...) / entity.speak(...). The runtime uses this project-level TTS configuration.");
        ImGui.PopID();
    }

    private static bool HasLipSyncLanguage(GameProjectLipSyncSettings lipSync, string language)
    {
        foreach (string selectedLanguage in lipSync.GetEffectiveDictionaryLanguages())
        {
            if (string.Equals(selectedLanguage, language, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void SetLipSyncLanguageSelection(GameProjectLipSyncSettings lipSync, string language, bool enabled)
    {
        List<string> languages = [.. lipSync.GetEffectiveDictionaryLanguages()];
        int existingIndex = -1;
        for (int index = 0; index < languages.Count; index++)
        {
            if (string.Equals(languages[index], language, StringComparison.OrdinalIgnoreCase))
            {
                existingIndex = index;
                break;
            }
        }

        if (enabled)
        {
            if (existingIndex < 0)
            {
                languages.Add(language);
            }
        }
        else if (existingIndex >= 0)
        {
            languages.RemoveAt(existingIndex);
        }

        lipSync.SetEffectiveDictionaryLanguages(languages);
    }

    private void DrawDesktopSpriteTrayMenuItems(GameWindowSettings window)
    {
        window.DesktopSpriteTrayMenuItems ??= [];

        if (!ImGui.TreeNodeEx("Desktop sprite tray menu", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (window.DesktopSpriteTrayMenuItems.Count == 0)
        {
            if (ImGui.Button("Add default tray items"))
            {
                window.DesktopSpriteTrayMenuItems.Add(new DesktopSpriteTrayMenuItemSettings
                {
                    Id = "toggle_visibility",
                    Text = "Show / Hide",
                    BuiltInAction = "toggle_visibility",
                    EventName = "tray_toggle_visibility"
                });
                window.DesktopSpriteTrayMenuItems.Add(new DesktopSpriteTrayMenuItemSettings
                {
                    Id = "exit",
                    Text = "Exit",
                    BuiltInAction = "exit",
                    EventName = "tray_exit"
                });
            }

            ImGui.TreePop();
            return;
        }

        int removeIndex = -1;
        string[] actions = ["none", "toggle_visibility", "exit"];
        for (int i = 0; i < window.DesktopSpriteTrayMenuItems.Count; i++)
        {
            DesktopSpriteTrayMenuItemSettings item = window.DesktopSpriteTrayMenuItems[i];
            ImGui.PushID($"trayItem{i}");
            if (ImGui.TreeNodeEx(string.IsNullOrWhiteSpace(item.Text) ? item.Id : item.Text, ImGuiTreeNodeFlags.DefaultOpen))
            {
                string id = item.Id;
                if (DrawTextInputWithPaste("Id", ref id, 128, "trayItemId"))
                {
                    item.Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
                }

                string text = item.Text;
                if (DrawTextInputWithPaste("Text", ref text, 128, "trayItemText"))
                {
                    item.Text = string.IsNullOrWhiteSpace(text) ? "Menu Item" : text;
                }

                bool enabled = item.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                {
                    item.Enabled = enabled;
                }

                string action = NormalizeTrayBuiltInAction(item.BuiltInAction);
                if (DrawStringCombo("Built-in action", ref action, actions))
                {
                    item.BuiltInAction = NormalizeTrayBuiltInAction(action);
                }

                string eventName = item.EventName;
                if (DrawTextInputWithPaste("Script event", ref eventName, 128, "trayItemEvent"))
                {
                    item.EventName = NormalizeScriptEventName(eventName);
                }

                if (ImGui.Button("Remove"))
                {
                    removeIndex = i;
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            window.DesktopSpriteTrayMenuItems.RemoveAt(removeIndex);
        }

        if (ImGui.Button("Add tray item"))
        {
            window.DesktopSpriteTrayMenuItems.Add(new DesktopSpriteTrayMenuItemSettings
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = "Menu Item",
                BuiltInAction = "none",
                EventName = "tray_menu_item"
            });
        }

        ImGui.TextWrapped("Script event calls tray_menu_event / TrayMenuEvent with this event name. Built-in actions run before the script event.");
        ImGui.TreePop();
    }

    private void DrawAsrSettings(GameProjectAsrSettings asr)
    {
        if (!ImGui.CollapsingHeader("ASR", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ImGui.PushID("asrSettings");

        bool enabled = asr.Enabled;
        if (ImGui.Checkbox("Enable ASR", ref enabled))
        {
            asr.Enabled = enabled;
        }

        string[] providers = ["sherpa", "whisper"];
        int providerIndex = string.Equals(asr.Provider, "whisper", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (ImGui.Combo("ASR provider", ref providerIndex, providers, providers.Length))
        {
            asr.Provider = providers[providerIndex];
        }

        int inputDeviceIndex = asr.InputDeviceIndex ?? -1;
        if (ImGui.DragInt("Input device index", ref inputDeviceIndex, 1.0f, -1, 9999))
        {
            asr.InputDeviceIndex = inputDeviceIndex < 0 ? null : inputDeviceIndex;
        }

        float partialResultIntervalSeconds = asr.PartialResultIntervalSeconds;
        if (ImGui.DragFloat("Partial result interval seconds", ref partialResultIntervalSeconds, 0.05f, 0.05f, 10.0f, "%.2f"))
        {
            asr.PartialResultIntervalSeconds = Math.Clamp(partialResultIntervalSeconds, 0.05f, 10.0f);
        }

        bool preloadOnSceneLoad = asr.PreloadOnSceneLoad;
        if (ImGui.Checkbox("Preload ASR on scene load", ref preloadOnSceneLoad))
        {
            asr.PreloadOnSceneLoad = preloadOnSceneLoad;
        }

        if (ImGui.TreeNode("Capture"))
        {
            ImGui.PushID("capture");
            DrawAudioCaptureSettings(asr.Capture);
            ImGui.PopID();
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Sherpa"))
        {
            string[] modelKinds =
            [
                "OnlineTransducer",
                "OnlineParaformer",
                "OnlineZipformer2Ctc",
                "OfflineWhisper",
                "OfflineParaformer",
                "OfflineTransducer",
                "OfflineZipformerCtc",
                "OfflineWenetCtc"
            ];
            int modelKindIndex = Array.FindIndex(modelKinds, item => string.Equals(item, asr.Sherpa.ModelKind, StringComparison.OrdinalIgnoreCase));
            if (modelKindIndex < 0)
            {
                modelKindIndex = 0;
            }

            if (ImGui.Combo("Sherpa model kind", ref modelKindIndex, modelKinds, modelKinds.Length))
            {
                asr.Sherpa.ModelKind = modelKinds[modelKindIndex];
            }

            string sherpaTokensPath = asr.Sherpa.TokensPath;
            if (DrawPathInput("Sherpa tokens path", ref sherpaTokensPath, 1024, "asrSherpaTokensPath"))
            {
                asr.Sherpa.TokensPath = sherpaTokensPath;
            }

            string sherpaEncoderPath = asr.Sherpa.EncoderPath ?? string.Empty;
            if (DrawPathInput("Sherpa encoder path", ref sherpaEncoderPath, 1024, "asrSherpaEncoderPath"))
            {
                asr.Sherpa.EncoderPath = string.IsNullOrWhiteSpace(sherpaEncoderPath) ? null : sherpaEncoderPath;
            }

            string sherpaDecoderPath = asr.Sherpa.DecoderPath ?? string.Empty;
            if (DrawPathInput("Sherpa decoder path", ref sherpaDecoderPath, 1024, "asrSherpaDecoderPath"))
            {
                asr.Sherpa.DecoderPath = string.IsNullOrWhiteSpace(sherpaDecoderPath) ? null : sherpaDecoderPath;
            }

            string sherpaJoinerPath = asr.Sherpa.JoinerPath ?? string.Empty;
            if (DrawPathInput("Sherpa joiner path", ref sherpaJoinerPath, 1024, "asrSherpaJoinerPath"))
            {
                asr.Sherpa.JoinerPath = string.IsNullOrWhiteSpace(sherpaJoinerPath) ? null : sherpaJoinerPath;
            }

            string sherpaModelPath = asr.Sherpa.ModelPath ?? string.Empty;
            if (DrawPathInput("Sherpa model path", ref sherpaModelPath, 1024, "asrSherpaModelPath"))
            {
                asr.Sherpa.ModelPath = string.IsNullOrWhiteSpace(sherpaModelPath) ? null : sherpaModelPath;
            }

            string sherpaLanguage = asr.Sherpa.Language;
            if (DrawTextInputWithPaste("Sherpa language", ref sherpaLanguage, 64, "sherpaLanguage"))
            {
                asr.Sherpa.Language = sherpaLanguage;
            }

            string sherpaProvider = asr.Sherpa.Provider;
            if (DrawTextInputWithPaste("Sherpa provider", ref sherpaProvider, 64, "sherpaProvider"))
            {
                asr.Sherpa.Provider = sherpaProvider;
            }

            int sherpaSampleRate = asr.Sherpa.SampleRate;
            if (ImGui.DragInt("Sherpa sample rate", ref sherpaSampleRate, 100.0f, 8000, 192000))
            {
                asr.Sherpa.SampleRate = Math.Clamp(sherpaSampleRate, 8000, 192000);
            }

            int sherpaFeatureDim = asr.Sherpa.FeatureDim;
            if (ImGui.DragInt("Sherpa feature dim", ref sherpaFeatureDim, 1.0f, 1, 1024))
            {
                asr.Sherpa.FeatureDim = Math.Clamp(sherpaFeatureDim, 1, 1024);
            }

            int sherpaThreads = asr.Sherpa.Threads;
            if (ImGui.DragInt("Sherpa threads", ref sherpaThreads, 1.0f, 1, 128))
            {
                asr.Sherpa.Threads = Math.Clamp(sherpaThreads, 1, 128);
            }

            string sherpaDecodingMethod = asr.Sherpa.DecodingMethod;
            if (DrawTextInputWithPaste("Sherpa decoding method", ref sherpaDecodingMethod, 64, "sherpaDecodingMethod"))
            {
                asr.Sherpa.DecodingMethod = sherpaDecodingMethod;
            }

            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Whisper"))
        {
            string whisperModelPath = asr.Whisper.ModelPath;
            if (DrawPathInput("Whisper model path", ref whisperModelPath, 1024, "asrWhisperModelPath"))
            {
                asr.Whisper.ModelPath = whisperModelPath;
            }

            string whisperLanguage = asr.Whisper.Language;
            if (DrawTextInputWithPaste("Whisper language", ref whisperLanguage, 64, "whisperLanguage"))
            {
                asr.Whisper.Language = whisperLanguage;
            }

            bool whisperTranslateToEnglish = asr.Whisper.TranslateToEnglish;
            if (ImGui.Checkbox("Whisper translate to English", ref whisperTranslateToEnglish))
            {
                asr.Whisper.TranslateToEnglish = whisperTranslateToEnglish;
            }

            bool whisperUseGpu = asr.Whisper.UseGpu;
            if (ImGui.Checkbox("Whisper use GPU", ref whisperUseGpu))
            {
                asr.Whisper.UseGpu = whisperUseGpu;
            }

            int whisperThreads = asr.Whisper.Threads;
            if (ImGui.DragInt("Whisper threads", ref whisperThreads, 1.0f, 1, 128))
            {
                asr.Whisper.Threads = Math.Clamp(whisperThreads, 1, 128);
            }

            int whisperSampleRate = asr.Whisper.SampleRate;
            if (ImGui.DragInt("Whisper sample rate", ref whisperSampleRate, 100.0f, 8000, 192000))
            {
                asr.Whisper.SampleRate = Math.Clamp(whisperSampleRate, 8000, 192000);
            }

            ImGui.TreePop();
        }

        ImGui.TextWrapped("Scripts call Scene.Asr / scene.asr. Buttons can use GUI events like 'pressed' and 'released' for push-to-talk recording flows. Enable preload to warm up ASR during scene loading and avoid the first-record delay.");
        ImGui.PopID();
    }

    private void DrawRealtimeVoiceSettings(GameProjectRealtimeVoiceSettings realtimeVoice)
    {
        if (!ImGui.CollapsingHeader("Realtime Voice", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ImGui.PushID("realtimeVoiceSettings");

        bool enabled = realtimeVoice.Enabled;
        if (ImGui.Checkbox("Enable Realtime Voice", ref enabled))
        {
            realtimeVoice.Enabled = enabled;
        }

        string baseUrl = realtimeVoice.BaseUrl;
        if (DrawTextInputWithPaste("Base URL", ref baseUrl, 512, "realtimeBaseUrl"))
        {
            realtimeVoice.BaseUrl = baseUrl;
        }

        string realtimePath = realtimeVoice.RealtimePath;
        if (DrawTextInputWithPaste("Realtime path", ref realtimePath, 256, "realtimePath"))
        {
            realtimeVoice.RealtimePath = realtimePath;
        }

        string audioSpeechPath = realtimeVoice.AudioSpeechPath;
        if (DrawTextInputWithPaste("Audio speech path", ref audioSpeechPath, 256, "realtimeAudioSpeechPath"))
        {
            realtimeVoice.AudioSpeechPath = audioSpeechPath;
        }

        string apiKeyEnvironmentVariable = realtimeVoice.ApiKeyEnvironmentVariable;
        if (DrawTextInputWithPaste("API key env var", ref apiKeyEnvironmentVariable, 128, "realtimeApiKeyEnvVar"))
        {
            realtimeVoice.ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable;
        }

        string apiKey = realtimeVoice.ApiKey;
        if (DrawTextInputWithPaste("API key override", ref apiKey, 256, "realtimeApiKey"))
        {
            realtimeVoice.ApiKey = apiKey;
        }

        string model = realtimeVoice.Model;
        if (DrawTextInputWithPaste("Model", ref model, 128, "realtimeModel"))
        {
            realtimeVoice.Model = model;
        }

        string voice = realtimeVoice.Voice;
        if (DrawTextInputWithPaste("Voice", ref voice, 128, "realtimeVoice"))
        {
            realtimeVoice.Voice = voice;
        }

        string instructions = realtimeVoice.Instructions;
        if (DrawTextInputMultilineWithPaste(
            "Instructions",
            ref instructions,
            4096,
            new Vector2(-1.0f, ImGui.GetTextLineHeight() * 5.0f),
            "realtimeInstructions"))
        {
            realtimeVoice.Instructions = instructions;
        }

        int connectTimeoutSeconds = realtimeVoice.ConnectTimeoutSeconds;
        if (ImGui.DragInt("Connect timeout seconds", ref connectTimeoutSeconds, 1.0f, 1, 3600))
        {
            realtimeVoice.ConnectTimeoutSeconds = Math.Clamp(connectTimeoutSeconds, 1, 3600);
        }

        int outboundAudioChunkSamples = realtimeVoice.OutboundAudioChunkSamples;
        if (ImGui.DragInt("Outbound audio chunk samples", ref outboundAudioChunkSamples, 128.0f, 512, 65536))
        {
            realtimeVoice.OutboundAudioChunkSamples = Math.Clamp(outboundAudioChunkSamples, 512, 65536);
        }

        int inputAudioSampleRate = realtimeVoice.InputAudioSampleRate;
        if (ImGui.DragInt("Input audio sample rate", ref inputAudioSampleRate, 100.0f, 8000, 192000))
        {
            realtimeVoice.InputAudioSampleRate = Math.Clamp(inputAudioSampleRate, 8000, 192000);
        }

        int outputAudioSampleRate = realtimeVoice.OutputAudioSampleRate;
        if (ImGui.DragInt("Output audio sample rate", ref outputAudioSampleRate, 100.0f, 8000, 192000))
        {
            realtimeVoice.OutputAudioSampleRate = Math.Clamp(outputAudioSampleRate, 8000, 192000);
        }

        string inputTranscriptionModel = realtimeVoice.InputTranscriptionModel;
        if (DrawTextInputWithPaste("Input transcription model", ref inputTranscriptionModel, 128, "realtimeInputTranscriptionModel"))
        {
            realtimeVoice.InputTranscriptionModel = inputTranscriptionModel;
        }

        string inputTranscriptionLanguage = realtimeVoice.InputTranscriptionLanguage;
        if (DrawTextInputWithPaste("Input transcription language", ref inputTranscriptionLanguage, 64, "realtimeInputTranscriptionLanguage"))
        {
            realtimeVoice.InputTranscriptionLanguage = inputTranscriptionLanguage;
        }

        string inputTranscriptionPrompt = realtimeVoice.InputTranscriptionPrompt;
        if (DrawTextInputWithPaste("Input transcription prompt", ref inputTranscriptionPrompt, 512, "realtimeInputTranscriptionPrompt"))
        {
            realtimeVoice.InputTranscriptionPrompt = inputTranscriptionPrompt;
        }

        bool useMaxOutputTokens = realtimeVoice.MaxOutputTokens.HasValue;
        if (ImGui.Checkbox("Use max output tokens", ref useMaxOutputTokens))
        {
            realtimeVoice.MaxOutputTokens = useMaxOutputTokens ? Math.Max(1, realtimeVoice.MaxOutputTokens ?? 1024) : null;
        }

        if (realtimeVoice.MaxOutputTokens.HasValue)
        {
            int maxOutputTokens = realtimeVoice.MaxOutputTokens.Value;
            if (ImGui.DragInt("Max output tokens", ref maxOutputTokens, 1.0f, 1, 32768))
            {
                realtimeVoice.MaxOutputTokens = Math.Clamp(maxOutputTokens, 1, 32768);
            }
        }

        bool useTemperature = realtimeVoice.Temperature.HasValue;
        if (ImGui.Checkbox("Use temperature", ref useTemperature))
        {
            realtimeVoice.Temperature = useTemperature ? realtimeVoice.Temperature ?? 0.7f : null;
        }

        if (realtimeVoice.Temperature.HasValue)
        {
            float temperature = realtimeVoice.Temperature.Value;
            if (ImGui.DragFloat("Temperature", ref temperature, 0.01f, 0.0f, 2.0f, "%.2f"))
            {
                realtimeVoice.Temperature = Math.Clamp(temperature, 0.0f, 2.0f);
            }
        }

        int inputDeviceIndex = realtimeVoice.InputDeviceIndex ?? -1;
        if (ImGui.DragInt("Input device index", ref inputDeviceIndex, 1.0f, -1, 9999))
        {
            realtimeVoice.InputDeviceIndex = inputDeviceIndex < 0 ? null : inputDeviceIndex;
        }

        float outputVolume = realtimeVoice.OutputVolume;
        if (ImGui.DragFloat("Output volume", ref outputVolume, 0.01f, 0.0f, 4.0f, "%.2f"))
        {
            realtimeVoice.OutputVolume = Math.Clamp(outputVolume, 0.0f, 4.0f);
        }

        float promptSpeed = realtimeVoice.PromptSpeed;
        if (ImGui.DragFloat("Prompt speech speed", ref promptSpeed, 0.01f, 0.1f, 5.0f, "%.2f"))
        {
            realtimeVoice.PromptSpeed = Math.Clamp(promptSpeed, 0.1f, 5.0f);
        }

        if (ImGui.TreeNode("User Capture"))
        {
            DrawVoiceActivityCaptureSettings(realtimeVoice.UserCapture);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Wake Word"))
        {
            bool wakeWordEnabled = realtimeVoice.WakeWord.Enabled;
            if (ImGui.Checkbox("Enable wake word monitoring", ref wakeWordEnabled))
            {
                realtimeVoice.WakeWord.Enabled = wakeWordEnabled;
            }

            string keywords = string.Join(", ", realtimeVoice.WakeWord.Keywords);
            if (DrawTextInputWithPaste("Wake word keywords", ref keywords, 1024, "wakeWordKeywords"))
            {
                realtimeVoice.WakeWord.Keywords = keywords
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            float chunkDuration = realtimeVoice.WakeWord.ChunkDurationSeconds;
            if (ImGui.DragFloat("Wake word chunk seconds", ref chunkDuration, 0.05f, 0.1f, 60.0f, "%.2f"))
            {
                realtimeVoice.WakeWord.ChunkDurationSeconds = Math.Clamp(chunkDuration, 0.1f, 60.0f);
            }

            float extensionDuration = realtimeVoice.WakeWord.ExtensionDurationSeconds;
            if (ImGui.DragFloat("Wake word extension seconds", ref extensionDuration, 0.05f, 0.0f, 30.0f, "%.2f"))
            {
                realtimeVoice.WakeWord.ExtensionDurationSeconds = Math.Clamp(extensionDuration, 0.0f, 30.0f);
            }

            float trailingSilencePadding = realtimeVoice.WakeWord.TrailingSilencePaddingSeconds;
            if (ImGui.DragFloat("Wake word tail silence seconds", ref trailingSilencePadding, 0.05f, 0.0f, 10.0f, "%.2f"))
            {
                realtimeVoice.WakeWord.TrailingSilencePaddingSeconds = Math.Clamp(trailingSilencePadding, 0.0f, 10.0f);
            }

            if (ImGui.TreeNode("Wake Word Capture"))
            {
                ImGui.PushID("wakeWordCapture");
                DrawAudioCaptureSettings(realtimeVoice.WakeWord.Capture);
                ImGui.PopID();
                ImGui.TreePop();
            }

            ImGui.TreePop();
        }

        ImGui.TextWrapped("Scripts call Scene.RealtimeVoice / scene.realtime_voice for remote speech transcription, wake-word monitoring, and streamed voice replies.");
        ImGui.PopID();
    }

    private static void DrawAudioCaptureSettings(GameProjectAudioCaptureSettings capture)
    {
        int sampleRate = capture.SampleRate;
        if (ImGui.DragInt("Sample rate", ref sampleRate, 100.0f, 8000, 192000))
        {
            capture.SampleRate = Math.Clamp(sampleRate, 8000, 192000);
        }

        int channels = capture.Channels;
        if (ImGui.DragInt("Channels", ref channels, 1.0f, 1, 8))
        {
            capture.Channels = Math.Clamp(channels, 1, 8);
        }

        int framesPerBuffer = capture.FramesPerBuffer;
        if (ImGui.DragInt("Frames per buffer", ref framesPerBuffer, 1.0f, 0, 8192))
        {
            capture.FramesPerBuffer = Math.Clamp(framesPerBuffer, 0, 8192);
        }
    }

    private static void DrawVoiceActivityCaptureSettings(GameProjectVoiceActivityCaptureSettings capture)
    {
        DrawAudioCaptureSettings(capture);

        float preRoll = capture.PreRollSeconds;
        if (ImGui.DragFloat("Pre-roll seconds", ref preRoll, 0.01f, 0.0f, 10.0f, "%.2f"))
        {
            capture.PreRollSeconds = Math.Clamp(preRoll, 0.0f, 10.0f);
        }

        float minDuration = capture.MinDurationSeconds;
        if (ImGui.DragFloat("Min duration seconds", ref minDuration, 0.05f, 0.0f, 120.0f, "%.2f"))
        {
            capture.MinDurationSeconds = Math.Clamp(minDuration, 0.0f, 120.0f);
        }

        float maxDuration = capture.MaxDurationSeconds;
        if (ImGui.DragFloat("Max duration seconds", ref maxDuration, 0.05f, 0.1f, 300.0f, "%.2f"))
        {
            capture.MaxDurationSeconds = Math.Clamp(maxDuration, 0.1f, 300.0f);
        }

        float silenceTimeout = capture.SilenceTimeoutSeconds;
        if (ImGui.DragFloat("Silence timeout seconds", ref silenceTimeout, 0.01f, 0.0f, 30.0f, "%.2f"))
        {
            capture.SilenceTimeoutSeconds = Math.Clamp(silenceTimeout, 0.0f, 30.0f);
        }

        float silenceThreshold = capture.SilenceThreshold;
        if (ImGui.DragFloat("Silence threshold", ref silenceThreshold, 0.001f, 0.0f, 1.0f, "%.3f"))
        {
            capture.SilenceThreshold = Math.Clamp(silenceThreshold, 0.0f, 1.0f);
        }
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

        ImGui.PushID("inspectorPanel");

        ImGui.TextUnformatted("Entity");
        string entityName = entity.Name;
        string entityType = entity.Type;
        string assetPath = entity.AssetPath;
        bool textChanged = false;
        textChanged |= DrawTextInputWithPaste("Name", ref entityName, 256, "entityName");
        textChanged |= DrawTextInputWithPaste("Type", ref entityType, 128, "entityType");
        if (DrawPathInput("Asset", ref assetPath, 1024, "entityAssetPath"))
        {
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
        bool enablePhysics = entity.EnablePhysics;
        Vector3 physicsGravityDirection = entity.PhysicsGravityDirection.ToVector3();
        physicsGravityDirection = physicsGravityDirection.LengthSquared() > 1e-12f
            ? Vector3.Normalize(physicsGravityDirection)
            : -Vector3.UnitY;
        float physicsGravityMagnitude = MathF.Max(0.0f, entity.PhysicsGravityMagnitude);
        bool physicsGravityChanged = false;
        bool changed = false;
        changed |= ImGui.DragFloat3("Position", ref position, 0.02f);
        changed |= ImGui.DragFloat3("Rotation", ref rotation, 0.5f);
        changed |= ImGui.DragFloat3("Scale", ref scale, 0.01f, 0.001f, 100.0f);

        changed |= ImGui.Checkbox("Play animation", ref isPlaying);
        if (string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase))
        {
            changed |= ImGui.DragFloat("Playback speed", ref playbackSpeed, 0.01f, 0.0f, 5.0f, "%.2f");
            changed |= ImGui.Checkbox("Loop motion", ref loopMotion);
            changed |= ImGui.Checkbox("Physics", ref enablePhysics);
            physicsGravityChanged = ImGui.DragFloat3(
                "Gravity direction",
                ref physicsGravityDirection,
                0.01f,
                -1.0f,
                1.0f,
                "%.3f");
            physicsGravityChanged |= ImGui.DragFloat(
                "Gravity magnitude",
                ref physicsGravityMagnitude,
                0.1f,
                0.0f,
                10000.0f,
                "%.2f");
            if (physicsGravityChanged)
            {
                physicsGravityMagnitude = Math.Clamp(physicsGravityMagnitude, 0.0f, 10000.0f);
                physicsGravityDirection = physicsGravityDirection.LengthSquared() > 1e-12f
                    ? Vector3.Normalize(physicsGravityDirection)
                    : -Vector3.UnitY;
                changed = true;
            }

            changed |= ImGui.Checkbox("Reset physics on loop", ref resetPhysicsOnMotionLoop);
        }

        changed |= ImGui.Checkbox("Edge", ref enableEdge);
        changed |= ImGui.Checkbox("Shadow", ref enableShadow);
        changed |= ImGui.Checkbox("Draw shadow in main pass (legacy)", ref drawShadowInMainPass);

        if (changed)
        {
            entity.Transform.Position = Vector3Dto.FromVector3(position);
            entity.Transform.RotationDegrees = Vector3Dto.FromVector3(rotation);
            entity.Transform.Scale = Vector3Dto.FromVector3(scale);
            entity.IsPlaying = isPlaying;
            entity.PlaybackSpeed = playbackSpeed;
            entity.LoopMotion = loopMotion;
            entity.EnablePhysics = enablePhysics;
            if (physicsGravityChanged)
            {
                entity.PhysicsGravityDirection = Vector3Dto.FromVector3(physicsGravityDirection);
                entity.PhysicsGravityMagnitude = physicsGravityMagnitude;
            }

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

            if (DrawTextInputWithPaste("Language", ref language, 64, "scriptLanguage"))
            {
                script.Language = language;
            }

            bool pathEdited = DrawPathInput("Path", ref path, 512, "scriptPath", out bool pathCommitted);
            if (pathEdited)
            {
                script.Path = path;
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

        ImGui.PopID();
    }

    private void DrawSceneInspector()
    {
        GameProjectScene scene = _editorGame.Project.Scene;
        ImGui.PushID("sceneInspector");
        ImGui.TextUnformatted("Scene");
        string sceneName = scene.Name;
        if (DrawTextInputWithPaste("Scene name", ref sceneName, 256, "sceneName"))
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

        cameraChanged |= DrawCameraControlSettings(scene.Camera, "sceneCameraControl");
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
        Vector3 lightColor = scene.Lighting.LightColor.ToVector3();
        Vector3 ambientColor = scene.Lighting.AmbientColor.ToVector3();
        float ambientStrength = scene.Lighting.AmbientStrength;
        Vector4 clearColor = scene.Lighting.ClearColor.ToVector4();
        bool lightingChanged = false;
        lightingChanged |= ImGui.DragFloat3("Light direction", ref lightDirection, 0.02f);
        lightingChanged |= ImGui.ColorEdit3("Light color", ref lightColor);
        lightingChanged |= ImGui.ColorEdit3("Ambient color", ref ambientColor);
        lightingChanged |= ImGui.SliderFloat("Ambient strength", ref ambientStrength, 0.0f, 2.0f);
        lightingChanged |= ImGui.ColorEdit4("Clear color", ref clearColor);
        if (lightingChanged)
        {
            scene.Lighting.LightDirection = Vector3Dto.FromVector3(lightDirection);
            scene.Lighting.LightColor = Vector3Dto.FromVector3(lightColor);
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
        ImGui.PopID();
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
                    ControlMode = scene.Camera.ControlMode,
                    TargetEntity = scene.Camera.TargetEntity,
                    SubjectEntity = scene.Camera.SubjectEntity,
                    Distance = scene.Camera.Distance,
                    Height = scene.Camera.Height,
                    ShoulderOffset = scene.Camera.ShoulderOffset,
                    Smoothing = scene.Camera.Smoothing,
                    MoveSpeed = scene.Camera.MoveSpeed,
                    MouseSensitivity = scene.Camera.MouseSensitivity,
                    SafeRadius = scene.Camera.SafeRadius,
                    AutoOrbitSpeed = scene.Camera.AutoOrbitSpeed,
                    EnableMouseLook = scene.Camera.EnableMouseLook,
                    RequireRightMouseForMouseLook = scene.Camera.RequireRightMouseForMouseLook,
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
                bool viewportEnabled = camera.Viewport.Enabled;
                string viewportLayoutMode = camera.Viewport.LayoutMode;
                Vector2 viewportPosition = new(camera.Viewport.X, camera.Viewport.Y);
                Vector2 viewportSize = new(camera.Viewport.Width, camera.Viewport.Height);
                bool changed = false;

                changed |= DrawTextInputWithPaste("Name", ref name, 256, "cameraName");
                changed |= ImGui.Checkbox("Enabled", ref enabled);
                changed |= ImGui.DragFloat3("Position", ref position, 0.05f);
                changed |= ImGui.DragFloat3("Target", ref target, 0.05f);
                if (ImGui.Combo("Projection", ref projectionIndex, projectionModes, projectionModes.Length))
                {
                    changed = true;
                }

                changed |= DrawCameraControlSettings(camera.Camera, $"cameraControl{i}");
                changed |= ImGui.SliderFloat("FOV", ref fov, 10.0f, 90.0f);
                changed |= ImGui.DragFloat("Orthographic size", ref orthoSize, 0.05f, 0.01f, 10000.0f);
                changed |= ImGui.DragFloat("Near clip", ref nearClip, 0.01f, 0.001f, 10000.0f);
                changed |= ImGui.DragFloat("Far clip", ref farClip, 1.0f, 0.01f, 1000000.0f);
                if (ImGui.TreeNode("Viewport"))
                {
                    changed |= ImGui.Checkbox("Enable viewport", ref viewportEnabled);
                    changed |= DrawStringCombo("Layout mode", ref viewportLayoutMode, ["absolute", "relative"]);
                    changed |= ImGui.DragFloat2("Viewport position", ref viewportPosition, 1.0f);
                    changed |= ImGui.DragFloat2("Viewport size", ref viewportSize, 1.0f, 1.0f, 8192.0f);
                    ImGui.TextWrapped("Disabled means this camera is not drawn directly to the window. The main camera still renders full screen when no camera viewport is enabled.");
                    ImGui.TreePop();
                }

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
                    camera.Viewport.Enabled = viewportEnabled;
                    camera.Viewport.LayoutMode = LayoutResolver.NormalizeLayoutMode(viewportLayoutMode);
                    camera.Viewport.X = Math.Max(0.0f, viewportPosition.X);
                    camera.Viewport.Y = Math.Max(0.0f, viewportPosition.Y);
                    camera.Viewport.Width = Math.Max(1.0f, viewportSize.X);
                    camera.Viewport.Height = Math.Max(1.0f, viewportSize.Y);
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
            string[] refreshModes = ["every_frame", "fixed_rate", "on_demand"];
            int refreshModeIndex = Math.Max(0, Array.FindIndex(refreshModes, mode => string.Equals(mode, renderTexture.RefreshMode, StringComparison.OrdinalIgnoreCase)));
            float refreshIntervalSeconds = renderTexture.RefreshIntervalSeconds;
            bool changed = false;

            changed |= DrawTextInputWithPaste("Name", ref name, 256, "renderTextureName");
            changed |= ImGui.Checkbox("Enabled", ref enabled);
            if (cameraNames.Length > 0 && ImGui.Combo("Camera", ref cameraIndex, cameraNames, cameraNames.Length))
            {
                changed = true;
            }

            changed |= ImGui.DragInt("Width", ref width, 1.0f, 1, 8192);
            changed |= ImGui.DragInt("Height", ref height, 1.0f, 1, 8192);
            changed |= ImGui.ColorEdit4("Clear color", ref clearColor);
            if (ImGui.Combo("Refresh mode", ref refreshModeIndex, refreshModes, refreshModes.Length))
            {
                changed = true;
            }
            if (refreshModes[Math.Clamp(refreshModeIndex, 0, refreshModes.Length - 1)] == "fixed_rate")
            {
                changed |= ImGui.DragFloat("Refresh interval", ref refreshIntervalSeconds, 0.01f, 0.01f, 60.0f, "%.2f s");
            }
            if (changed)
            {
                renderTexture.Name = string.IsNullOrWhiteSpace(name) ? renderTexture.Name : name.Trim();
                renderTexture.Enabled = enabled;
                renderTexture.Camera = cameraNames.Length == 0 ? renderTexture.Camera : cameraNames[cameraIndex];
                renderTexture.Width = Math.Max(1, width);
                renderTexture.Height = Math.Max(1, height);
                renderTexture.ClearColor = Vector4Dto.FromVector4(clearColor);
                renderTexture.RefreshMode = refreshModes[Math.Clamp(refreshModeIndex, 0, refreshModes.Length - 1)];
                renderTexture.RefreshIntervalSeconds = Math.Max(0.01f, refreshIntervalSeconds);
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

    private bool DrawCameraControlSettings(CameraSettings camera, string id)
    {
        ImGui.PushID(id);
        string controlMode = NormalizeCameraControlMode(camera.ControlMode);
        string targetEntity = camera.TargetEntity;
        string subjectEntity = camera.SubjectEntity;
        float distance = camera.Distance;
        float height = camera.Height;
        float shoulderOffset = camera.ShoulderOffset;
        float smoothing = camera.Smoothing;
        float moveSpeed = camera.MoveSpeed;
        float mouseSensitivity = camera.MouseSensitivity;
        float safeRadius = camera.SafeRadius;
        float autoOrbitSpeed = camera.AutoOrbitSpeed;
        bool enableMouseLook = camera.EnableMouseLook;
        bool requireRightMouse = camera.RequireRightMouseForMouseLook;
        bool changed = false;

        changed |= DrawStringCombo("Control mode", ref controlMode, CameraControlModes);
        if (ImGui.TreeNode("Runtime control"))
        {
            changed |= DrawTextInputWithPaste("Target entity", ref targetEntity, 256, "cameraControlTarget");
            changed |= DrawTextInputWithPaste("Subject entity", ref subjectEntity, 256, "cameraControlSubject");
            changed |= ImGui.DragFloat("Distance", ref distance, 0.05f, 0.01f, 10000.0f);
            changed |= ImGui.DragFloat("Height / eye height", ref height, 0.05f, -10000.0f, 10000.0f);
            changed |= ImGui.DragFloat("Shoulder offset", ref shoulderOffset, 0.05f, -1000.0f, 1000.0f);
            changed |= ImGui.DragFloat("Smoothing", ref smoothing, 0.05f, 0.0f, 120.0f);
            changed |= ImGui.DragFloat("Move speed", ref moveSpeed, 0.05f, 0.0f, 1000.0f);
            changed |= ImGui.DragFloat("Mouse sensitivity", ref mouseSensitivity, 0.005f, 0.0f, 10.0f);
            changed |= ImGui.SliderFloat("Safe radius", ref safeRadius, 0.0f, 0.45f);
            changed |= ImGui.DragFloat("Auto orbit speed", ref autoOrbitSpeed, 0.5f, -360.0f, 360.0f);
            changed |= ImGui.Checkbox("Mouse look", ref enableMouseLook);
            changed |= ImGui.Checkbox("Require right mouse", ref requireRightMouse);
            ImGui.TreePop();
        }

        if (changed)
        {
            camera.ControlMode = NormalizeCameraControlMode(controlMode);
            camera.TargetEntity = targetEntity.Trim();
            camera.SubjectEntity = subjectEntity.Trim();
            camera.Distance = Math.Max(0.01f, distance);
            camera.Height = height;
            camera.ShoulderOffset = shoulderOffset;
            camera.Smoothing = Math.Max(0.0f, smoothing);
            camera.MoveSpeed = Math.Max(0.0f, moveSpeed);
            camera.MouseSensitivity = Math.Max(0.0f, mouseSensitivity);
            camera.SafeRadius = Math.Clamp(safeRadius, 0.0f, 0.45f);
            camera.AutoOrbitSpeed = autoOrbitSpeed;
            camera.EnableMouseLook = enableMouseLook;
            camera.RequireRightMouseForMouseLook = requireRightMouse;
        }

        ImGui.PopID();
        return changed;
    }

    private static string NormalizeCameraControlMode(string controlMode)
    {
        string normalized = (controlMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            "firstperson" or "first_person" or "fp" or "fps" => "first_person",
            "fpscontrol" or "fps_control" or "locked_fps" or "fps_locked" or "first_person_control" or "firstpersoncontrol" => "fps_control",
            "thirdperson" or "third_person" or "third_person_follow" or "tps" or "tp" => "third_person",
            "lockon" or "hard_lock" => "lock_on",
            "fly" or "flycam" or "free" => "free_fly",
            "topdown" => "top_down",
            "side" or "side_scroller" or "sidescroller" or "side_scroll" => "side_scroller",
            "cinematic" or "cinematic_follow" or "smooth_follow" => "cinematic_follow",
            "orbital" or "orbital_follow" or "auto_orbit" => "orbital_follow",
            "static" => "fixed",
            "script" or "scripted" => "custom",
            _ => CameraControlModes.Contains(normalized) ? normalized : "editor"
        };
    }

    private void DrawLoadingScreenInspector(GameProjectScene scene)
    {
        if (!ImGui.CollapsingHeader("Loading Screen"))
        {
            return;
        }

        ImGui.PushID("loadingScreen");

        LoadingScreenSettings loadingScreen = scene.LoadingScreen;
        Vector4 backgroundColor = loadingScreen.BackgroundColor.ToVector4();
        string backgroundImagePath = loadingScreen.BackgroundImagePath;
        float backgroundImageOpacity = loadingScreen.BackgroundImageOpacity;
        bool changed = false;

        changed |= ImGui.ColorEdit4("Background color", ref backgroundColor);
        if (DrawPathInput("Background image", ref backgroundImagePath, 1024, "backgroundImagePath"))
        {
            changed = true;
        }

        changed |= ImGui.SliderFloat("Image opacity", ref backgroundImageOpacity, 0.0f, 1.0f);

        if (changed)
        {
            loadingScreen.BackgroundColor = Vector4Dto.FromVector4(backgroundColor);
            loadingScreen.BackgroundImagePath = backgroundImagePath;
            loadingScreen.BackgroundImageOpacity = Math.Clamp(backgroundImageOpacity, 0.0f, 1.0f);
        }

        LoadingProgressBarSettings progressBar = loadingScreen.ProgressBar;
        if (ImGui.TreeNode("Loading progress bar"))
        {
            bool visible = progressBar.Visible;
            string layoutMode = progressBar.LayoutMode;
            Vector2 position = new(progressBar.X, progressBar.Y);
            Vector2 size = new(progressBar.Width, progressBar.Height);
            Vector4 barBackground = progressBar.BackgroundColor.ToVector4();
            Vector4 trackColor = progressBar.TrackColor.ToVector4();
            Vector4 fillColor = progressBar.FillColor.ToVector4();
            Vector4 borderColor = progressBar.BorderColor.ToVector4();
            float borderThickness = progressBar.BorderThickness;
            float rounding = progressBar.Rounding;
            float padding = progressBar.Padding;
            bool progressChanged = false;

            progressChanged |= ImGui.Checkbox("Visible", ref visible);
            progressChanged |= DrawStringCombo("Layout mode", ref layoutMode, ["absolute", "relative"]);
            progressChanged |= ImGui.DragFloat2("Position", ref position, 1.0f);
            progressChanged |= ImGui.DragFloat2("Size", ref size, 1.0f, 1.0f, 8192.0f);
            progressChanged |= ImGui.ColorEdit4("Bar background", ref barBackground);
            progressChanged |= ImGui.ColorEdit4("Track", ref trackColor);
            progressChanged |= ImGui.ColorEdit4("Fill", ref fillColor);
            progressChanged |= ImGui.ColorEdit4("Border", ref borderColor);
            progressChanged |= ImGui.DragFloat("Border thickness", ref borderThickness, 0.05f, 0.0f, 32.0f, "%.2f");
            progressChanged |= ImGui.DragFloat("Rounding", ref rounding, 0.1f, 0.0f, 64.0f, "%.1f");
            progressChanged |= ImGui.DragFloat("Padding", ref padding, 0.1f, 0.0f, 64.0f, "%.1f");

            if (progressChanged)
            {
                progressBar.Visible = visible;
                progressBar.LayoutMode = LayoutResolver.NormalizeLayoutMode(layoutMode);
                progressBar.X = Math.Max(0.0f, position.X);
                progressBar.Y = Math.Max(0.0f, position.Y);
                progressBar.Width = Math.Max(1.0f, size.X);
                progressBar.Height = Math.Max(1.0f, size.Y);
                progressBar.BackgroundColor = Vector4Dto.FromVector4(barBackground);
                progressBar.TrackColor = Vector4Dto.FromVector4(trackColor);
                progressBar.FillColor = Vector4Dto.FromVector4(fillColor);
                progressBar.BorderColor = Vector4Dto.FromVector4(borderColor);
                progressBar.BorderThickness = Math.Max(0.0f, borderThickness);
                progressBar.Rounding = Math.Max(0.0f, rounding);
                progressBar.Padding = Math.Max(0.0f, padding);
            }

            ImGui.TreePop();
        }

        ImGui.PopID();
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

            if (DrawTextInputWithPaste("Language", ref language, 64, "loadingScriptLanguage"))
            {
                script.Language = language;
            }

            bool pathEdited = DrawPathInput("Path", ref path, 512, "loadingScriptPath", out bool pathCommitted);
            if (pathEdited)
            {
                script.Path = path;
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

        ImGui.PushID("guiInspector");

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

        ImGui.SameLine();
        if (ImGui.Button("Add Textbox"))
        {
            scene.GuiControls.Add(new GuiControlSettings
            {
                Name = $"Textbox {scene.GuiControls.Count + 1}",
                Type = "textbox",
                Text = string.Empty,
                Width = 260.0f,
                Height = 96.0f,
                Multiline = true,
                WordWrap = true,
                EventName = "changed"
            });
        }

        ImGui.SameLine();
        if (ImGui.Button("Add Progress"))
        {
            scene.GuiControls.Add(new GuiControlSettings
            {
                Name = $"Progress {scene.GuiControls.Count + 1}",
                Type = "progress_bar",
                Text = string.Empty,
                Width = 260.0f,
                Height = 28.0f,
                Progress = 0.35f
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
            string layoutMode = control.LayoutMode;
            bool wordWrap = control.WordWrap;
            bool multiline = control.Multiline;
            bool checkedValue = control.Checked;
            float progress = control.Progress;
            int selectedIndex = control.SelectedIndex;
            bool changed = false;

            changed |= ImGui.Checkbox("Visible", ref visible);
            changed |= DrawTextInputWithPaste("Name", ref name, 128, "guiControlName");
            string[] types = ["button", "label", "checkbox", "dropdown", "textbox", "progress_bar"];
            int typeIndex = Array.FindIndex(types, item => string.Equals(item, type, StringComparison.OrdinalIgnoreCase));
            typeIndex = Math.Max(0, typeIndex);
            if (ImGui.Combo("Type", ref typeIndex, types, types.Length))
            {
                type = types[typeIndex];
                changed = true;
            }

            if (string.Equals(type, "textbox", StringComparison.OrdinalIgnoreCase))
            {
                changed |= DrawTextInputMultilineWithPaste("Text", ref text, 8192, new Vector2(Math.Max(size.X, 180.0f), 96.0f), "guiControlTextMultiline");
                changed |= ImGui.Checkbox("Multiline", ref multiline);
            }
            else
            {
                changed |= DrawTextInputWithPaste("Text", ref text, 512, "guiControlText");
            }

            changed |= ImGui.Checkbox("Word wrap", ref wordWrap);
            changed |= DrawStringCombo("Layout mode", ref layoutMode, ["absolute", "relative"]);
            changed |= ImGui.DragFloat2("Position", ref position, 1.0f);
            changed |= ImGui.DragFloat2("Size", ref size, 1.0f, 1.0f, 4096.0f);
            if (string.Equals(type, "checkbox", StringComparison.OrdinalIgnoreCase))
            {
                changed |= ImGui.Checkbox("Checked", ref checkedValue);
            }

            if (string.Equals(type, "progress_bar", StringComparison.OrdinalIgnoreCase))
            {
                changed |= ImGui.SliderFloat("Progress", ref progress, 0.0f, 1.0f, "%.3f");
            }

            if (string.Equals(type, "dropdown", StringComparison.OrdinalIgnoreCase))
            {
                changed |= DrawDropdownItemsInspector(control, ref selectedIndex);
            }

            changed |= DrawEntityTargetCombo("Target entity", ref targetEntity);
            changed |= DrawTextInputWithPaste("Event name", ref eventName, 128, "guiControlEventName");
            changed |= DrawGuiStyleInspector(control.Style);

            if (changed)
            {
                control.Visible = visible;
                control.Name = name;
                control.Type = type;
                control.Text = text;
                control.LayoutMode = LayoutResolver.NormalizeLayoutMode(layoutMode);
                control.X = Math.Max(0.0f, position.X);
                control.Y = Math.Max(0.0f, position.Y);
                control.Width = Math.Max(1.0f, size.X);
                control.Height = Math.Max(1.0f, size.Y);
                control.TargetEntity = targetEntity;
                control.EventName = eventName;
                control.WordWrap = wordWrap;
                control.Multiline = multiline;
                control.Checked = checkedValue;
                control.Progress = Math.Clamp(progress, 0.0f, 1.0f);
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

        DrawContextMenuInspector(scene);
        ImGui.PopID();
    }

    private void DrawContextMenuInspector(GameProjectScene scene)
    {
        scene.ContextMenus ??= [];

        ImGui.Separator();
        if (!ImGui.TreeNodeEx("Context Menus", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ImGui.TextWrapped("Right-click context menus are dispatched as GUI events. Menu Id/Name becomes GuiControlId/GuiControlName, and the selected menu item's Script event becomes GuiEventName.");

        if (ImGui.Button("Add Context Menu"))
        {
            scene.ContextMenus.Add(new ContextMenuSettings
            {
                Name = $"Context Menu {scene.ContextMenus.Count + 1}"
            });
        }

        int removeIndex = -1;
        for (int i = 0; i < scene.ContextMenus.Count; i++)
        {
            ContextMenuSettings menu = scene.ContextMenus[i];
            menu.Items ??= [];

            ImGui.PushID($"contextMenu{i}");
            ImGui.Separator();

            string label = string.IsNullOrWhiteSpace(menu.Name) ? $"Context Menu {i + 1}" : menu.Name;
            if (ImGui.TreeNodeEx($"{label}##contextMenuNode{i}", ImGuiTreeNodeFlags.DefaultOpen))
            {
                bool enabled = menu.Enabled;
                string name = menu.Name;
                string targetType = NormalizeContextMenuTargetType(menu.TargetType);
                string targetId = menu.TargetId;
                string targetCollider = menu.TargetCollider;
                string layoutMode = menu.LayoutMode;
                float width = menu.Width;
                float itemHeight = menu.ItemHeight;
                float paddingX = menu.PaddingX;
                float paddingY = menu.PaddingY;
                bool changed = false;

                changed |= ImGui.Checkbox("Enabled", ref enabled);
                changed |= DrawTextInputWithPaste("Name", ref name, 128, "contextMenuName");
                changed |= DrawStringCombo("Target type", ref targetType, ["window", "gui_control", "sprite", "entity"]);
                changed |= DrawContextMenuTargetSelector(targetType, ref targetId, ref targetCollider);
                changed |= DrawStringCombo("Layout mode", ref layoutMode, ["absolute", "relative"]);
                changed |= ImGui.DragFloat("Width", ref width, 1.0f, 48.0f, 2048.0f, "%.0f");
                changed |= ImGui.DragFloat("Item height", ref itemHeight, 1.0f, 12.0f, 256.0f, "%.0f");
                changed |= ImGui.DragFloat("Padding X", ref paddingX, 0.5f, 0.0f, 128.0f, "%.1f");
                changed |= ImGui.DragFloat("Padding Y", ref paddingY, 0.5f, 0.0f, 128.0f, "%.1f");
                changed |= DrawGuiStyleInspector(menu.Style);
                changed |= DrawContextMenuItemsInspector(menu);

                if (changed)
                {
                    menu.Enabled = enabled;
                    menu.Name = string.IsNullOrWhiteSpace(name) ? "Context Menu" : name.Trim();
                    menu.TargetType = NormalizeContextMenuTargetType(targetType);
                    menu.TargetId = menu.TargetType == "window" ? string.Empty : targetId;
                    menu.TargetCollider = menu.TargetType == "entity" ? targetCollider : string.Empty;
                    menu.LayoutMode = LayoutResolver.NormalizeLayoutMode(layoutMode);
                    menu.Width = Math.Max(48.0f, width);
                    menu.ItemHeight = Math.Max(12.0f, itemHeight);
                    menu.PaddingX = Math.Max(0.0f, paddingX);
                    menu.PaddingY = Math.Max(0.0f, paddingY);
                }

                if (ImGui.SmallButton("Remove Context Menu"))
                {
                    removeIndex = i;
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            scene.ContextMenus.RemoveAt(removeIndex);
        }

        ImGui.TreePop();
    }

    private bool DrawContextMenuItemsInspector(ContextMenuSettings menu)
    {
        bool changed = false;
        if (!ImGui.TreeNode("Menu Items"))
        {
            return false;
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new ContextMenuItemSettings
            {
                Text = "Menu Item",
                EventName = "context_menu_clicked"
            });
            changed = true;
        }

        int removeIndex = -1;
        for (int i = 0; i < menu.Items.Count; i++)
        {
            ContextMenuItemSettings item = menu.Items[i];
            ImGui.PushID($"contextMenuItem{i}");

            string label = string.IsNullOrWhiteSpace(item.Text) ? $"Item {i + 1}" : item.Text;
            if (ImGui.TreeNodeEx($"{label}##contextMenuItemNode{i}", ImGuiTreeNodeFlags.DefaultOpen))
            {
                string id = item.Id;
                string text = item.Text;
                string eventName = item.EventName;
                bool enabled = item.Enabled;

                if (DrawTextInputWithPaste("Id", ref id, 128, "contextMenuItemId"))
                {
                    item.Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
                    changed = true;
                }

                if (DrawTextInputWithPaste("Text", ref text, 256, "contextMenuItemText"))
                {
                    item.Text = string.IsNullOrWhiteSpace(text) ? "Menu Item" : text;
                    changed = true;
                }

                if (ImGui.Checkbox("Enabled", ref enabled))
                {
                    item.Enabled = enabled;
                    changed = true;
                }

                if (DrawTextInputWithPaste("Script event", ref eventName, 128, "contextMenuItemEvent"))
                {
                    item.EventName = NormalizeScriptEventName(eventName);
                    changed = true;
                }

                if (ImGui.SmallButton("Remove Item"))
                {
                    removeIndex = i;
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            menu.Items.RemoveAt(removeIndex);
            changed = true;
        }

        if (ImGui.Button("Add Menu Item"))
        {
            menu.Items.Add(new ContextMenuItemSettings
            {
                Text = $"Menu Item {menu.Items.Count + 1}",
                EventName = "context_menu_clicked"
            });
            changed = true;
        }

        ImGui.TreePop();
        return changed;
    }

    private bool DrawContextMenuTargetSelector(string targetType, ref string targetId, ref string targetCollider)
    {
        string normalized = NormalizeContextMenuTargetType(targetType);
        if (normalized == "window")
        {
            bool targetCleared = !string.IsNullOrWhiteSpace(targetId) || !string.IsNullOrWhiteSpace(targetCollider);
            targetId = string.Empty;
            targetCollider = string.Empty;
            ImGui.TextWrapped("Empty/window target: right-click anywhere in the window opens this menu. In desktop sprite click-through mode, transparent areas are already excluded by the native hit-test.");
            return targetCleared;
        }

        bool changed = normalized switch
        {
            "gui_control" => DrawGuiControlTargetCombo("Target GUI control", ref targetId),
            "sprite" => DrawSpriteTargetCombo("Target 2D sprite", ref targetId),
            "entity" => DrawEntityTargetCombo("Target entity", ref targetId),
            _ => false
        };

        if (normalized == "entity")
        {
            changed |= DrawEntityColliderCombo("Target collider", targetId, ref targetCollider);
        }
        else
        {
            changed |= !string.IsNullOrWhiteSpace(targetCollider);
            targetCollider = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(targetId))
        {
            ImGui.TextWrapped("No target selected. Runtime will treat this as a whole-window context menu.");
        }

        return changed;
    }

    private bool DrawDropdownItemsInspector(GuiControlSettings control, ref int selectedIndex)
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
            if (DrawTextInputWithPaste("Item", ref item, 256, "dropdownItemText"))
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
        float fontSize = style.FontSize <= 0.0f ? 18.0f : style.FontSize;
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
        changed |= ImGui.DragFloat("Font size", ref fontSize, 0.25f, 8.0f, 96.0f, "%.1f px");
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
            style.FontSize = Math.Clamp(fontSize, 8.0f, 96.0f);
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

    private bool DrawPmxBoneBindingCombo(GameEntity entity, ref string boundBoneName)
    {
        IReadOnlyList<string> bones = _editorGame.GetPmxBoneNames(entity);
        string current = (boundBoneName ?? string.Empty).Trim();
        bool hasMatchedBone = bones.Any(bone => string.Equals(bone, current, StringComparison.OrdinalIgnoreCase));
        string preview = string.IsNullOrWhiteSpace(current)
            ? "(entity transform)"
            : hasMatchedBone ? current : $"Missing: {current}";
        bool changed = false;

        if (!ImGui.BeginCombo("Bound bone", preview))
        {
            return false;
        }

        bool entitySelected = string.IsNullOrWhiteSpace(current);
        if (ImGui.Selectable("(entity transform)", entitySelected))
        {
            boundBoneName = string.Empty;
            changed = !entitySelected;
        }

        if (entitySelected)
        {
            ImGui.SetItemDefaultFocus();
        }

        if (bones.Count == 0)
        {
            ImGui.TextDisabled("PMX bones unavailable.");
        }

        for (int i = 0; i < bones.Count; i++)
        {
            string bone = bones[i];
            bool selected = string.Equals(bone, current, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{bone}##boundBone{i}", selected))
            {
                boundBoneName = bone;
                changed = !selected;
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static string NormalizeChoice(string value, string fallback, string[] choices)
    {
        return choices.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static string NormalizeDesktopSpriteDragButton(string value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return normalized switch
        {
            "left" or "mouse_left" or "left_mouse" => "left",
            "right" or "mouse_right" or "right_mouse" => "right",
            "middle" or "mouse_middle" or "middle_mouse" => "middle",
            _ => "none"
        };
    }

    private static string NormalizeContextMenuTargetType(string value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return normalized switch
        {
            "gui" or "control" or "gui_control" or "gui_controls" => "gui_control",
            "sprite" or "2d_sprite" or "2d" => "sprite",
            "rigidbody" or "rigid_body" or "collider" or "entity" or "object" => "entity",
            _ => "window"
        };
    }

    private static string NormalizeTrayBuiltInAction(string value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return normalized switch
        {
            "toggle_visibility" or "toggle" or "show_hide" or "showhide" => "toggle_visibility",
            "exit" or "quit" => "exit",
            _ => "none"
        };
    }

    private static string NormalizeScriptEventName(string value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        string normalized = Regex.Replace(trimmed, @"[^\p{L}\p{Nd}_]+", "_");
        return normalized.Trim('_');
    }

    private static string GetRelationLabel(GameEntity entity)
    {
        return string.IsNullOrWhiteSpace(entity.Name)
            ? entity.Id
            : $"{entity.Name} ({entity.Id[..Math.Min(entity.Id.Length, 8)]})";
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

    private bool DrawGuiControlTargetCombo(string label, ref string targetId)
    {
        GameProjectScene scene = _editorGame.Project.Scene;
        string normalizedTarget = targetId.Trim();
        GuiControlSettings? selectedControl = scene.GuiControls.FirstOrDefault(control =>
            string.Equals(control.Id, normalizedTarget, StringComparison.OrdinalIgnoreCase)
            || string.Equals(control.Name, normalizedTarget, StringComparison.OrdinalIgnoreCase));
        string preview = selectedControl is not null
            ? selectedControl.Name
            : string.IsNullOrWhiteSpace(normalizedTarget)
                ? "(none)"
                : $"Missing: {normalizedTarget}";

        bool changed = false;
        if (ImGui.BeginCombo(label, preview))
        {
            bool noneSelected = string.IsNullOrWhiteSpace(normalizedTarget);
            if (ImGui.Selectable("(none)", noneSelected))
            {
                targetId = string.Empty;
                changed = true;
            }

            foreach (GuiControlSettings control in scene.GuiControls)
            {
                bool selected = selectedControl is not null && string.Equals(selectedControl.Id, control.Id, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable($"{control.Name}##targetGuiControl{control.Id}", selected))
                {
                    targetId = control.Id;
                    changed = true;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            if (selectedControl is null && !string.IsNullOrWhiteSpace(normalizedTarget))
            {
                ImGui.Separator();
                ImGui.TextDisabled($"Current value is not found: {normalizedTarget}");
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private bool DrawSpriteTargetCombo(string label, ref string targetId)
    {
        GameProjectScene scene = _editorGame.Project.Scene;
        string normalizedTarget = targetId.Trim();
        SpriteSettings? selectedSprite = scene.Sprites.FirstOrDefault(sprite =>
            string.Equals(sprite.Id, normalizedTarget, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sprite.Name, normalizedTarget, StringComparison.OrdinalIgnoreCase));
        string preview = selectedSprite is not null
            ? selectedSprite.Name
            : string.IsNullOrWhiteSpace(normalizedTarget)
                ? "(none)"
                : $"Missing: {normalizedTarget}";

        bool changed = false;
        if (ImGui.BeginCombo(label, preview))
        {
            bool noneSelected = string.IsNullOrWhiteSpace(normalizedTarget);
            if (ImGui.Selectable("(none)", noneSelected))
            {
                targetId = string.Empty;
                changed = true;
            }

            foreach (SpriteSettings sprite in scene.Sprites)
            {
                bool selected = selectedSprite is not null && string.Equals(selectedSprite.Id, sprite.Id, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable($"{sprite.Name}##targetSprite{sprite.Id}", selected))
                {
                    targetId = sprite.Id;
                    changed = true;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            if (selectedSprite is null && !string.IsNullOrWhiteSpace(normalizedTarget))
            {
                ImGui.Separator();
                ImGui.TextDisabled($"Current value is not found: {normalizedTarget}");
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private bool DrawEntityColliderCombo(string label, string targetEntity, ref string targetCollider)
    {
        GameProjectScene scene = _editorGame.Project.Scene;
        string normalizedTarget = targetEntity.Trim();
        GameEntity? entity = scene.Entities.FirstOrDefault(item =>
            string.Equals(item.Id, normalizedTarget, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, normalizedTarget, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            targetCollider = string.Empty;
            ImGui.TextDisabled("Target collider: select an entity first.");
            return false;
        }

        List<ColliderSettings> colliders = GameEntityCollision.GetEffectiveColliders(entity).ToList();
        string normalizedCollider = targetCollider.Trim();
        ColliderSettings? selectedCollider = colliders.FirstOrDefault(collider =>
            string.Equals(collider.Id, normalizedCollider, StringComparison.OrdinalIgnoreCase)
            || string.Equals(collider.Name, normalizedCollider, StringComparison.OrdinalIgnoreCase));
        string preview = selectedCollider is not null
            ? selectedCollider.Name
            : string.IsNullOrWhiteSpace(normalizedCollider)
                ? "(any collider)"
                : $"Missing: {normalizedCollider}";

        bool changed = false;
        if (ImGui.BeginCombo(label, preview))
        {
            bool anySelected = string.IsNullOrWhiteSpace(normalizedCollider);
            if (ImGui.Selectable("(any collider)", anySelected))
            {
                targetCollider = string.Empty;
                changed = true;
            }

            foreach (ColliderSettings collider in colliders)
            {
                bool selected = selectedCollider is not null && string.Equals(selectedCollider.Id, collider.Id, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable($"{collider.Name}##targetCollider{collider.Id}", selected))
                {
                    targetCollider = collider.Id;
                    changed = true;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            if (selectedCollider is null && !string.IsNullOrWhiteSpace(normalizedCollider))
            {
                ImGui.Separator();
                ImGui.TextDisabled($"Current value is not found: {normalizedCollider}");
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private void DrawAssetsPanel()
    {
        ImGui.PushID("assetsPanel");

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
            if (DrawTextInputWithPaste("Name", ref audioName, 256, "audioName"))
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
            if (DrawTextInputWithPaste("Motion name", ref motionName, 256, "motionName"))
            {
                motion.Name = motionName;
            }

            if (DrawPathInput("Motion path", ref motionAssetPath, 1024, "motionAssetPath"))
            {
                motion.Path = motionAssetPath;
            }

            ImGui.PopID();
        }

        ImGui.PopID();
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
            string layoutMode = sprite.LayoutMode;
            string targetEntity = sprite.TargetEntity;
            Vector2 position = new(sprite.X, sprite.Y);
            Vector2 size = new(sprite.Width, sprite.Height);
            float rotation = sprite.RotationDegrees;
            float opacity = sprite.Opacity;
            int drawOrder = sprite.DrawOrder;
            bool visible = sprite.Visible;
            bool changed = false;

            changed |= DrawTextInputWithPaste("Name", ref name, 256, "spriteName");
            if (DrawPathInput("Path", ref path, 1024, "spriteAssetPath"))
            {
                changed = true;
            }
            if (DrawRenderTextureCombo("Render texture", ref path))
            {
                changed = true;
            }

            changed |= DrawStringCombo("Layout mode", ref layoutMode, ["absolute", "relative"]);
            changed |= ImGui.DragFloat2("Position", ref position, 1.0f);
            changed |= ImGui.DragFloat2("Size", ref size, 1.0f, 1.0f, 4096.0f);
            changed |= ImGui.DragFloat("Rotation", ref rotation, 1.0f, -360.0f, 360.0f);
            changed |= ImGui.SliderFloat("Opacity", ref opacity, 0.0f, 1.0f);
            changed |= ImGui.DragInt("Draw order", ref drawOrder, 1.0f);
            changed |= ImGui.Checkbox("Visible", ref visible);
            changed |= DrawEntityTargetCombo("Target entity", ref targetEntity);

            if (changed)
            {
                sprite.Name = name;
                sprite.Path = path;
                sprite.LayoutMode = LayoutResolver.NormalizeLayoutMode(layoutMode);
                sprite.TargetEntity = targetEntity;
                sprite.X = position.X;
                sprite.Y = position.Y;
                sprite.Width = Math.Max(1.0f, size.X);
                sprite.Height = Math.Max(1.0f, size.Y);
                sprite.RotationDegrees = rotation;
                sprite.Opacity = Math.Clamp(opacity, 0.0f, 1.0f);
                sprite.DrawOrder = drawOrder;
                sprite.Visible = visible;
            }

            ImGui.TextWrapped("If Target entity is set, GamePlayer will dispatch sprite pointer events like entered / exited / pressed / released / clicked to that entity's scripts.");

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

        ImGui.SeparatorText("Log");
        if (ImGui.SmallButton("Clear"))
        {
            _editorGame.ClearStatusLog();
        }

        ImGui.BeginChild("StatusLog", Vector2.Zero, ImGuiChildFlags.Borders);
        IReadOnlyList<string> statusLog = _editorGame.StatusLog;
        for (int i = statusLog.Count - 1; i >= 0; i--)
        {
            string entry = statusLog[i];
            Vector4 color = entry.Contains("[Error]", StringComparison.Ordinal)
                ? new Vector4(1.0f, 0.38f, 0.34f, 1.0f)
                : entry.Contains("[Warning]", StringComparison.Ordinal)
                    ? new Vector4(1.0f, 0.72f, 0.25f, 1.0f)
                    : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(entry);
            ImGui.PopStyleColor();
            if (i > 0)
            {
                ImGui.Separator();
            }
        }

        ImGui.EndChild();
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
        float startSizeScale = particle.StartSizeScale;
        float endSizeScale = particle.EndSizeScale;
        float widthScale = particle.WidthScale;
        float heightScale = particle.HeightScale;
        float minRotationSpeedRadians = particle.MinRotationSpeedRadians;
        float maxRotationSpeedRadians = particle.MaxRotationSpeedRadians;
        float simulationSpeed = particle.SimulationSpeed;
        float opacity = particle.Opacity;
        bool enableWaterInteraction = particle.EnableWaterInteraction;
        bool killOnWaterContact = particle.KillOnWaterContact;
        bool randomizeInitialAge = particle.RandomizeInitialAge;
        bool useTextureColor = particle.UseTextureColor;
        bool preventDarkening = particle.PreventDarkening;
        string[] blendModes = ["alpha", "additive"];
        string[] orientationModes = ["billboard", "velocityAligned"];
        string[] texturePresets = ["softCircle", "streak", "flame"];
        int blendModeIndex = Math.Max(0, Array.FindIndex(blendModes, item => string.Equals(item, particle.BlendMode, StringComparison.OrdinalIgnoreCase)));
        int orientationModeIndex = Math.Max(0, Array.FindIndex(orientationModes, item => string.Equals(item, particle.OrientationMode, StringComparison.OrdinalIgnoreCase)));
        int texturePresetIndex = Math.Max(0, Array.FindIndex(texturePresets, item => string.Equals(item, particle.TexturePreset, StringComparison.OrdinalIgnoreCase)));
        Vector4 startColor = particle.StartColor.ToVector4();
        Vector4 endColor = particle.EndColor.ToVector4();
        string texturePath = particle.TexturePath ?? string.Empty;

        ImGui.TextUnformatted("Water Contact Presets");
        if (ImGui.SmallButton("Rain Contact"))
        {
            enableWaterInteraction = true;
            killOnWaterContact = true;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Waterfall Contact"))
        {
            enableWaterInteraction = true;
            killOnWaterContact = true;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Fountain Contact"))
        {
            enableWaterInteraction = true;
            killOnWaterContact = false;
            changed = true;
        }

        changed |= ImGui.DragInt("Particle count", ref particleCount, 8.0f, 1, 10000);
        changed |= ImGui.DragFloat3("Spawn half extents", ref spawnBox, 0.05f);
        changed |= ImGui.DragFloat3("Base velocity", ref baseVelocity, 0.05f);
        changed |= ImGui.DragFloat3("Velocity jitter", ref velocityJitter, 0.05f);
        changed |= ImGui.DragFloat3("Acceleration", ref acceleration, 0.05f);
        changed |= ImGui.DragFloat("Min lifetime", ref minLifetime, 0.05f, 0.05f, 120.0f);
        changed |= ImGui.DragFloat("Max lifetime", ref maxLifetime, 0.05f, 0.05f, 120.0f);
        changed |= ImGui.DragFloat("Min size", ref minSize, 0.01f, 0.001f, 100.0f);
        changed |= ImGui.DragFloat("Max size", ref maxSize, 0.01f, 0.001f, 100.0f);
        changed |= ImGui.DragFloat("Start size scale", ref startSizeScale, 0.01f, 0.0f, 10.0f);
        changed |= ImGui.DragFloat("End size scale", ref endSizeScale, 0.01f, 0.0f, 10.0f);
        changed |= ImGui.DragFloat("Width scale", ref widthScale, 0.01f, 0.01f, 10.0f);
        changed |= ImGui.DragFloat("Height scale", ref heightScale, 0.01f, 0.01f, 10.0f);
        changed |= ImGui.DragFloat("Min rotation speed", ref minRotationSpeedRadians, 0.01f, -20.0f, 20.0f, "%.3f");
        changed |= ImGui.DragFloat("Max rotation speed", ref maxRotationSpeedRadians, 0.01f, -20.0f, 20.0f, "%.3f");
        changed |= ImGui.SliderFloat("Simulation speed", ref simulationSpeed, 0.0f, 5.0f);
        changed |= ImGui.SliderFloat("Opacity", ref opacity, 0.0f, 1.0f);
        changed |= ImGui.Checkbox("Enable water interaction", ref enableWaterInteraction);
        changed |= ImGui.Checkbox("Kill particle on water contact", ref killOnWaterContact);
        changed |= ImGui.Checkbox("Randomize initial age", ref randomizeInitialAge);
        changed |= ImGui.Combo("Blend mode", ref blendModeIndex, blendModes, blendModes.Length);
        changed |= ImGui.Combo("Orientation", ref orientationModeIndex, orientationModes, orientationModes.Length);
        changed |= ImGui.Combo("Texture preset", ref texturePresetIndex, texturePresets, texturePresets.Length);
        changed |= ImGui.Checkbox("Use texture color", ref useTextureColor);
        changed |= ImGui.Checkbox("Prevent darkening", ref preventDarkening);
        changed |= ImGui.ColorEdit4("Start color", ref startColor);
        changed |= ImGui.ColorEdit4("End color", ref endColor);
        if (DrawPathInput("Texture path", ref texturePath, 1024, "particleTexturePath"))
        {
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
            particle.StartSizeScale = Math.Max(0.0f, startSizeScale);
            particle.EndSizeScale = Math.Max(0.0f, endSizeScale);
            particle.WidthScale = Math.Max(0.01f, widthScale);
            particle.HeightScale = Math.Max(0.01f, heightScale);
            particle.MinRotationSpeedRadians = minRotationSpeedRadians;
            particle.MaxRotationSpeedRadians = maxRotationSpeedRadians;
            particle.SimulationSpeed = simulationSpeed;
            particle.Opacity = opacity;
            particle.EnableWaterInteraction = enableWaterInteraction;
            particle.KillOnWaterContact = killOnWaterContact;
            particle.RandomizeInitialAge = randomizeInitialAge;
            particle.BlendMode = blendModes[Math.Clamp(blendModeIndex, 0, blendModes.Length - 1)];
            particle.OrientationMode = orientationModes[Math.Clamp(orientationModeIndex, 0, orientationModes.Length - 1)];
            particle.TexturePreset = texturePresets[Math.Clamp(texturePresetIndex, 0, texturePresets.Length - 1)];
            particle.UseTextureColor = useTextureColor;
            particle.PreventDarkening = preventDarkening;
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
            if (DrawPathInput("Layer path", ref path, 1024, "motionLayerPath"))
            {
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

        ImGui.SameLine();
        if (ImGui.Button("Add Mesh Collider"))
        {
            entity.Colliders.Add(new ColliderSettings
            {
                Name = $"Mesh Collider {entity.Colliders.Count + 1}",
                Shape = "mesh",
                Position = Vector3Dto.Zero,
                Size = Vector3Dto.One,
                Walkable = true,
                MaxSlopeDegrees = 55.0f
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
                string shape = NormalizeChoice(collider.Shape, "capsule", ["capsule", "box", "mesh"]);
                Vector3 position = collider.Position.ToVector3();
                Vector3 rotation = collider.RotationDegrees.ToVector3();
                string boundBoneName = collider.BoundBoneName;
                bool changed = false;

                changed |= ImGui.Checkbox("Enabled", ref enabled);
                changed |= DrawTextInputWithPaste("Name", ref name, 256, "colliderName");
                changed |= DrawStringCombo("Shape", ref shape, ["capsule", "box", "mesh"]);
                bool canBindBone = string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(shape, "mesh", StringComparison.OrdinalIgnoreCase);
                if (canBindBone)
                {
                    changed |= DrawPmxBoneBindingCombo(entity, ref boundBoneName);
                }
                else if (!string.IsNullOrWhiteSpace(boundBoneName))
                {
                    boundBoneName = string.Empty;
                    changed = true;
                }

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
                else if (shape == "mesh")
                {
                    Vector3 size = collider.Size.ToVector3();
                    bool walkable = collider.Walkable;
                    float maxSlopeDegrees = collider.MaxSlopeDegrees;
                    changed |= ImGui.DragFloat3("Local mesh scale", ref size, 0.02f, 0.001f, 10000.0f);
                    changed |= ImGui.Checkbox("Walkable for NavMesh", ref walkable);
                    changed |= ImGui.SliderFloat("Max slope degrees", ref maxSlopeDegrees, 0.0f, 89.9f);
                    if (changed)
                    {
                        collider.Size = new Vector3Dto(
                            Math.Max(0.001f, size.X),
                            Math.Max(0.001f, size.Y),
                            Math.Max(0.001f, size.Z));
                        collider.Walkable = walkable;
                        collider.MaxSlopeDegrees = Math.Clamp(maxSlopeDegrees, 0.0f, 89.9f);
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
                    collider.Shape = NormalizeChoice(shape, "capsule", ["capsule", "box", "mesh"]);
                    collider.BoundBoneName = canBindBone ? boundBoneName.Trim() : string.Empty;
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

        ImGui.TextWrapped("Colliders are local to the entity, or to a selected PMX bone when bone binding is set. Mesh Collider uses the entity mesh triangles for raycast, ground sampling and NavMesh baking.");
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
            GameEntity? matchedRelation = pmxEntities.FirstOrDefault(item =>
                string.Equals(item.Id, relationEntity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Name, relationEntity, StringComparison.OrdinalIgnoreCase));

            if (enabled && string.IsNullOrWhiteSpace(relationEntity) && pmxEntities.Count == 1)
            {
                matchedRelation = pmxEntities[0];
                relationEntity = matchedRelation.Id;
                changed = true;
            }

            List<string> labels = ["(none)"];
            labels.AddRange(pmxEntities.Select(GetRelationLabel));
            int relationIndex = matchedRelation is null ? 0 : pmxEntities.IndexOf(matchedRelation) + 1;
            if (ImGui.Combo("Relation PMX", ref relationIndex, labels.ToArray(), labels.Count))
            {
                relationEntity = relationIndex <= 0 ? string.Empty : pmxEntities[relationIndex - 1].Id;
                changed = true;
            }
        }
        else
        {
            ImGui.TextDisabled("No other PMX entity can be used as relation target.");
        }

        changed |= DrawTextInputWithPaste("Relation entity", ref relationEntity, 256, "relationEntity");
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
        bool gerstnerWavesEnabled = water.GerstnerWavesEnabled;
        int gerstnerMeshResolution = water.GerstnerMeshResolution;
        int gerstnerWaveCount = water.GerstnerWaveCount;
        float gerstnerAmplitude = water.GerstnerAmplitude;
        float gerstnerWavelength = water.GerstnerWavelength;
        float gerstnerSpeed = water.GerstnerSpeed;
        float gerstnerSteepness = water.GerstnerSteepness;
        float gerstnerDirectionDegrees = water.GerstnerDirectionDegrees;
        Vector3 deepColor = water.DeepColor.ToVector3();
        Vector3 reflectionTint = water.ReflectionTint.ToVector3();
        float skyReflectionStrength = water.SkyReflectionStrength;
        bool mirrorReflectionEnabled = water.MirrorReflectionEnabled;
        bool underwaterEffectEnabled = water.UnderwaterEffectEnabled;
        Vector3 underwaterTint = water.UnderwaterTint.ToVector3();
        Vector3 underwaterFogColor = water.UnderwaterFogColor.ToVector3();
        float underwaterFogDensity = water.UnderwaterFogDensity;
        float underwaterVisibilityDistance = water.UnderwaterVisibilityDistance;
        float underwaterDistortionStrength = water.UnderwaterDistortionStrength;
        float underwaterCausticsStrength = water.UnderwaterCausticsStrength;
        float underwaterBubbleStrength = water.UnderwaterBubbleStrength;

        changed |= ImGui.DragFloat("Water size", ref size, 0.5f, 0.1f, 10000.0f);
        changed |= ImGui.SliderFloat("Water alpha", ref alpha, 0.0f, 1.0f);
        changed |= ImGui.DragFloat("Normal animation speed", ref animationSpeed, 0.001f, 0.0f, 10.0f, "%.3f");
        changed |= ImGui.DragFloat("Normal tiling", ref normalTiling, 0.5f, 0.001f, 10000.0f);
        changed |= ImGui.Checkbox("Gerstner waves", ref gerstnerWavesEnabled);
        if (gerstnerWavesEnabled)
        {
            changed |= ImGui.SliderInt("Gerstner mesh resolution", ref gerstnerMeshResolution, 16, 192);
            changed |= ImGui.SliderInt("Gerstner wave count", ref gerstnerWaveCount, 1, 4);
            changed |= ImGui.DragFloat("Gerstner amplitude", ref gerstnerAmplitude, 0.01f, 0.0f, 10.0f, "%.2f");
            changed |= ImGui.DragFloat("Gerstner wavelength", ref gerstnerWavelength, 0.1f, 0.1f, 1000.0f, "%.2f");
            changed |= ImGui.DragFloat("Gerstner speed", ref gerstnerSpeed, 0.01f, 0.0f, 50.0f, "%.2f");
            changed |= ImGui.SliderFloat("Gerstner steepness", ref gerstnerSteepness, 0.0f, 1.0f, "%.2f");
            changed |= ImGui.DragFloat("Gerstner direction", ref gerstnerDirectionDegrees, 0.5f, -360.0f, 360.0f, "%.1f deg");
        }
        changed |= ImGui.ColorEdit3("Deep color", ref deepColor);
        changed |= ImGui.Checkbox("Mirror reflection", ref mirrorReflectionEnabled);
        changed |= ImGui.ColorEdit3("Reflection tint", ref reflectionTint);
        changed |= ImGui.SliderFloat("Sky reflection", ref skyReflectionStrength, 0.0f, 1.0f);
        changed |= ImGui.Checkbox("Underwater effect", ref underwaterEffectEnabled);
        if (underwaterEffectEnabled)
        {
            changed |= ImGui.ColorEdit3("Underwater tint", ref underwaterTint);
            changed |= ImGui.ColorEdit3("Underwater fog color", ref underwaterFogColor);
            changed |= ImGui.DragFloat("Underwater visibility", ref underwaterVisibilityDistance, 0.25f, 0.1f, 1000.0f, "%.2f");
            changed |= ImGui.SliderFloat("Underwater fog density", ref underwaterFogDensity, 0.0f, 4.0f);
            changed |= ImGui.SliderFloat("Underwater distortion", ref underwaterDistortionStrength, 0.0f, 0.05f, "%.3f");
            changed |= ImGui.SliderFloat("Underwater caustics", ref underwaterCausticsStrength, 0.0f, 1.0f);
            changed |= ImGui.SliderFloat("Underwater bubbles", ref underwaterBubbleStrength, 0.0f, 1.0f);
        }

        bool enableInteraction = water.EnableInteraction;
        float interactionRadius = water.InteractionRadius;
        float interactionStrength = water.InteractionStrength;
        float particleRippleMinIntervalSeconds = water.ParticleRippleMinIntervalSeconds;
        float particleRippleMergeDistance = water.ParticleRippleMergeDistance;
        float rippleLifetimeSeconds = water.RippleLifetimeSeconds;
        float rippleWaveSpeed = water.RippleWaveSpeed;
        float rippleFrequency = water.RippleFrequency;
        float rippleNormalStrength = water.RippleNormalStrength;
        ImGui.TextUnformatted("Particle Ripple Presets");
        if (ImGui.SmallButton("Rain Preset"))
        {
            enableInteraction = true;
            particleRippleMinIntervalSeconds = 0.08f;
            particleRippleMergeDistance = 0.45f;
            rippleLifetimeSeconds = 2.8f;
            rippleWaveSpeed = 12.0f;
            rippleFrequency = 16.0f;
            rippleNormalStrength = 0.65f;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Waterfall Preset"))
        {
            enableInteraction = true;
            particleRippleMinIntervalSeconds = 0.04f;
            particleRippleMergeDistance = 0.9f;
            rippleLifetimeSeconds = 2.1f;
            rippleWaveSpeed = 14.0f;
            rippleFrequency = 18.0f;
            rippleNormalStrength = 0.55f;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Fountain Preset"))
        {
            enableInteraction = true;
            particleRippleMinIntervalSeconds = 0.12f;
            particleRippleMergeDistance = 0.35f;
            rippleLifetimeSeconds = 3.2f;
            rippleWaveSpeed = 10.0f;
            rippleFrequency = 14.0f;
            rippleNormalStrength = 0.78f;
            changed = true;
        }
        changed |= ImGui.Checkbox("Enable water interaction", ref enableInteraction);
        changed |= ImGui.DragFloat("Interaction radius", ref interactionRadius, 0.01f, 0.001f, 100.0f);
        changed |= ImGui.SliderFloat("Interaction strength", ref interactionStrength, 0.0f, 4.0f);
        changed |= ImGui.DragFloat("Particle ripple min interval", ref particleRippleMinIntervalSeconds, 0.005f, 0.0f, 10.0f, "%.3f s");
        changed |= ImGui.DragFloat("Particle ripple merge distance", ref particleRippleMergeDistance, 0.01f, 0.0f, 100.0f);
        changed |= ImGui.DragFloat("Ripple lifetime", ref rippleLifetimeSeconds, 0.01f, 0.05f, 20.0f, "%.2f s");
        changed |= ImGui.DragFloat("Ripple wave speed", ref rippleWaveSpeed, 0.1f, 0.0f, 100.0f, "%.2f");
        changed |= ImGui.DragFloat("Ripple frequency", ref rippleFrequency, 0.1f, 0.0f, 100.0f, "%.2f");
        changed |= ImGui.DragFloat("Ripple normal strength", ref rippleNormalStrength, 0.01f, 0.0f, 2.0f, "%.2f");

        if (changed)
        {
            water.Size = Math.Max(size, 0.1f);
            water.Alpha = Math.Clamp(alpha, 0.0f, 1.0f);
            water.AnimationSpeed = Math.Max(animationSpeed, 0.0f);
            water.NormalTiling = Math.Max(normalTiling, 0.001f);
            water.GerstnerWavesEnabled = gerstnerWavesEnabled;
            water.GerstnerMeshResolution = Math.Clamp(gerstnerMeshResolution, 8, 256);
            water.GerstnerWaveCount = Math.Clamp(gerstnerWaveCount, 1, 4);
            water.GerstnerAmplitude = Math.Max(0.0f, gerstnerAmplitude);
            water.GerstnerWavelength = Math.Max(0.1f, gerstnerWavelength);
            water.GerstnerSpeed = Math.Max(0.0f, gerstnerSpeed);
            water.GerstnerSteepness = Math.Clamp(gerstnerSteepness, 0.0f, 1.0f);
            water.GerstnerDirectionDegrees = gerstnerDirectionDegrees;
            water.DeepColor = Vector3Dto.FromVector3(deepColor);
            water.ReflectionTint = Vector3Dto.FromVector3(reflectionTint);
            water.SkyReflectionStrength = Math.Clamp(skyReflectionStrength, 0.0f, 1.0f);
            water.MirrorReflectionEnabled = mirrorReflectionEnabled;
            water.UnderwaterEffectEnabled = underwaterEffectEnabled;
            water.UnderwaterTint = Vector3Dto.FromVector3(underwaterTint);
            water.UnderwaterFogColor = Vector3Dto.FromVector3(underwaterFogColor);
            water.UnderwaterFogDensity = Math.Clamp(underwaterFogDensity, 0.0f, 4.0f);
            water.UnderwaterVisibilityDistance = Math.Max(0.1f, underwaterVisibilityDistance);
            water.UnderwaterDistortionStrength = Math.Clamp(underwaterDistortionStrength, 0.0f, 0.05f);
            water.UnderwaterCausticsStrength = Math.Clamp(underwaterCausticsStrength, 0.0f, 1.0f);
            water.UnderwaterBubbleStrength = Math.Clamp(underwaterBubbleStrength, 0.0f, 1.0f);
            water.EnableInteraction = enableInteraction;
            water.InteractionRadius = Math.Max(0.001f, interactionRadius);
            water.InteractionStrength = Math.Clamp(interactionStrength, 0.0f, 4.0f);
            water.ParticleRippleMinIntervalSeconds = Math.Max(0.0f, particleRippleMinIntervalSeconds);
            water.ParticleRippleMergeDistance = Math.Max(0.0f, particleRippleMergeDistance);
            water.RippleLifetimeSeconds = Math.Max(0.05f, rippleLifetimeSeconds);
            water.RippleWaveSpeed = rippleWaveSpeed;
            water.RippleFrequency = Math.Max(0.0f, rippleFrequency);
            water.RippleNormalStrength = Math.Max(0.0f, rippleNormalStrength);
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
        bool receiveShadow = plane.ReceiveShadow;
        bool mirrorReflectionEnabled = plane.MirrorReflectionEnabled;
        float mirrorReflectionStrength = plane.MirrorReflectionStrength;
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
        changed |= ImGui.Checkbox("Receive shadow", ref receiveShadow);
        changed |= ImGui.Checkbox("Mirror reflection", ref mirrorReflectionEnabled);
        if (mirrorReflectionEnabled)
        {
            changed |= ImGui.SliderFloat("Mirror reflection strength", ref mirrorReflectionStrength, 0.0f, 1.0f);
            ImGui.TextWrapped("Mirror reflection renders the scene again from a reflected camera before drawing this plane.");
        }

        if (changed)
        {
            plane.TexturePath = texturePath;
            entity.AssetPath = texturePath;
            plane.Width = Math.Max(0.001f, width);
            plane.Height = Math.Max(0.001f, height);
            plane.Billboard = billboard;
            plane.Opacity = Math.Clamp(opacity, 0.0f, 1.0f);
            plane.Tint = Vector4Dto.FromVector4(tint);
            plane.ReceiveShadow = receiveShadow;
            plane.MirrorReflectionEnabled = mirrorReflectionEnabled;
            plane.MirrorReflectionStrength = Math.Clamp(mirrorReflectionStrength, 0.0f, 1.0f);
            _editorGame.ApplySelectedPlaneToRuntime();
        }
    }

    private bool DrawPathInput(string label, ref string value, uint maxLength, string id)
    {
        return DrawTextInputWithPaste(label, ref value, maxLength, id);
    }

    private bool DrawPathInput(string label, ref string value, uint maxLength, string id, out bool committed)
    {
        return DrawTextInputWithPaste(label, ref value, maxLength, id, out committed);
    }

    private bool DrawTextInputWithPaste(
        string label,
        ref string value,
        uint maxLength,
        string id,
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        return DrawTextInputWithPaste(label, ref value, maxLength, id, out _, flags);
    }

    private bool DrawTextInputWithPaste(
        string label,
        ref string value,
        uint maxLength,
        string id,
        out bool committed,
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        bool changed = ImGui.InputText(WithControlId(label, id), ref value, maxLength, flags);
        committed = changed && ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine();
        if (ImGui.SmallButton($"Paste##{id}"))
        {
            PasteClipboard(ref value);
            changed = true;
            committed = true;
        }

        return changed;
    }

    private bool DrawTextInputMultilineWithPaste(
        string label,
        ref string value,
        uint maxLength,
        Vector2 size,
        string id,
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        ImGui.BeginGroup();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        bool changed = false;
        if (ImGui.SmallButton($"Paste##{id}"))
        {
            PasteClipboard(ref value);
            changed = true;
        }
        ImGui.EndGroup();

        Vector2 inputSize = size;
        if (inputSize.X < 0.0f)
        {
            float availableWidth = ImGui.GetContentRegionAvail().X;
            inputSize.X = Math.Max(120.0f, availableWidth);
        }

        changed |= ImGui.InputTextMultiline($"##{id}", ref value, maxLength, inputSize, flags);

        return changed;
    }

    private static string WithControlId(string label, string id)
    {
        return string.IsNullOrWhiteSpace(id) ? label : $"{label}##{id}";
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

}
