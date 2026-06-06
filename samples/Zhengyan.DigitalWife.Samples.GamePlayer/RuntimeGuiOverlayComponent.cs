using System.Numerics;
using ImGuiNET;
using Silk.NET.OpenGLES;
using Silk.NET.OpenGLES.Extensions.ImGui;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed class RuntimeGuiOverlayComponent(
    Func<IReadOnlyList<GuiControlSettings>> getControls,
    Func<IReadOnlyList<SpriteSettings>> getSprites,
    Func<RuntimeScene?> getScene,
    OrbitCamera camera,
    Func<GameWindowSettings> getWindowSettings,
    Func<string, string> resolvePath,
    Action<GuiControlSettings, string> dispatchEvent) : DrawableGameComponent
{
    private const float BaseFontSize = 18.0f;

    private readonly Func<IReadOnlyList<GuiControlSettings>> _getControls = getControls;
    private readonly Func<IReadOnlyList<SpriteSettings>> _getSprites = getSprites;
    private readonly Func<RuntimeScene?> _getScene = getScene;
    private readonly OrbitCamera _camera = camera;
    private readonly Func<GameWindowSettings> _getWindowSettings = getWindowSettings;
    private readonly Func<string, string> _resolvePath = resolvePath;
    private readonly Action<GuiControlSettings, string> _dispatchEvent = dispatchEvent;
    private readonly Dictionary<string, Texture2D> _spriteTextures = new(StringComparer.OrdinalIgnoreCase);
    private ImGuiController? _controller;

    public IRuntimeTextureProvider? RuntimeTextureProvider { get; set; }

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        if (ImGuiFontResolver.TryGetCjkFontPath(out string cjkFontPath))
        {
            try
            {
                _controller = new ImGuiController(
                    Game.GraphicsDevice.Gl,
                    Game.Window,
                    Game.Input.Context,
                    () => ConfigureIoFontAtlas(cjkFontPath));
                return;
            }
            catch
            {
                // Fall through to the default font setup.
            }
        }

        _controller = new ImGuiController(Game.GraphicsDevice.Gl, Game.Window, Game.Input.Context);
    }

    public override void Draw(GameTime gameTime)
    {
        if (Game is null || _controller is null)
        {
            return;
        }

        _controller.Update((float)gameTime.ElapsedSeconds);
        DrawSprites();
        DrawBubbles();

        foreach (GuiControlSettings control in _getControls())
        {
            if (!control.Visible)
            {
                continue;
            }

            DrawControl(control);
        }

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

    private void DrawSprites()
    {
        if (Game is null)
        {
            return;
        }

        IReadOnlyList<SpriteSettings> sprites = _getSprites();
        PruneSpriteTextureCache(sprites);

        ImDrawListPtr drawList = ImGui.GetBackgroundDrawList();
        Vector2 origin = Vector2.Zero;
        Vector2 max = new(Game.Window.Size.X, Game.Window.Size.Y);
        drawList.PushClipRect(origin, max, true);

        foreach (SpriteSettings sprite in sprites
            .Where(sprite => sprite.Visible && !string.IsNullOrWhiteSpace(sprite.Path))
            .OrderBy(sprite => sprite.DrawOrder))
        {
            uint textureId = GetSpriteTextureId(sprite.Path);
            if (textureId == 0)
            {
                continue;
            }

            GameWindowSettings window = _getWindowSettings();
            LayoutRect rect = SpriteLayoutResolver.Resolve(
                sprite,
                Game.Window.Size.X,
                Game.Window.Size.Y,
                window.Width,
                window.Height);
            Vector2 min = new(rect.X, rect.Y);
            Vector2 spriteMax = min + new Vector2(Math.Max(rect.Width, 1.0f), Math.Max(rect.Height, 1.0f));
            uint tint = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, Math.Clamp(sprite.Opacity, 0.0f, 1.0f)));
            AddSpriteImage(drawList, textureId, min, spriteMax, sprite.RotationDegrees, tint, IsRuntimeTextureReference(sprite.Path));
        }

        drawList.PopClipRect();
    }

    private void DrawBubbles()
    {
        RuntimeScene? scene = _getScene();
        if (scene is null)
        {
            return;
        }

        foreach (RuntimeDialogueBubble bubble in scene.Bubble.GetOrderedVisibleBubbles())
        {
            DrawBubble(scene, bubble);
        }
    }

    private void DrawBubble(RuntimeScene scene, RuntimeDialogueBubble bubble)
    {
        Vector2 position = ResolveBubblePosition(scene, bubble);
        Vector2 padding = new(
            ResolveBubbleScalar(bubble.LayoutMode, bubble.PaddingX),
            ResolveBubbleScalar(bubble.LayoutMode, bubble.PaddingY));
        float width = ResolveBubbleDimension(bubble.LayoutMode, bubble.Width);
        float rounding = ResolveBubbleScalar(bubble.LayoutMode, bubble.Rounding);
        float borderSize = ResolveBubbleScalar(bubble.LayoutMode, bubble.BorderThickness);

        ImGui.SetNextWindowPos(position, ImGuiCond.Always, bubble.Pivot);
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoInputs;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, borderSize);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, bubble.BackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, bubble.BorderColor);

        if (ImGui.Begin($"DialogueBubble##{bubble.Name}", flags))
        {
            bool hasContent = false;

            if (!string.IsNullOrWhiteSpace(bubble.HeaderText))
            {
                DrawBubbleTextBlock(
                    bubble.HeaderText,
                    width,
                    ResolveBubbleFontScale(bubble.LayoutMode, bubble.HeaderFontSize),
                    bubble.TextAlignment,
                    bubble.HeaderTextColor,
                    bubble.WordWrap);
                hasContent = true;
            }

            if (!string.IsNullOrWhiteSpace(bubble.Text))
            {
                if (hasContent)
                {
                    ImGui.Separator();
                }

                DrawBubbleTextBlock(
                    bubble.Text,
                    width,
                    ResolveBubbleFontScale(bubble.LayoutMode, bubble.FontSize),
                    bubble.TextAlignment,
                    bubble.TextColor,
                    bubble.WordWrap);
                hasContent = true;
            }

            if (!string.IsNullOrWhiteSpace(bubble.FooterText))
            {
                if (hasContent)
                {
                    ImGui.Separator();
                }

                DrawBubbleTextBlock(
                    bubble.FooterText,
                    width,
                    ResolveBubbleFontScale(bubble.LayoutMode, bubble.FooterFontSize),
                    bubble.TextAlignment,
                    bubble.FooterTextColor,
                    bubble.WordWrap);
            }

            ImGui.SetWindowFontScale(1.0f);
        }

        ImGui.End();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }

    private Vector2 ResolveBubblePosition(RuntimeScene scene, RuntimeDialogueBubble bubble)
    {
        Vector2 offset = ResolveBubbleVector2(bubble.LayoutMode, bubble.ScreenOffset);

        if (string.Equals(bubble.AnchorMode, "entity", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(bubble.AnchorEntity)
            && scene.GetEntity(bubble.AnchorEntity) is RuntimeEntity entity
            && entity.TryGetBubbleAnchorWorldPosition(bubble.UseEntityTopAnchor, out Vector3 anchor)
            && TryProjectWorld(anchor + bubble.WorldOffset, out Vector2 projected))
        {
            return projected + offset;
        }

        if (string.Equals(bubble.AnchorMode, "world", StringComparison.OrdinalIgnoreCase)
            && TryProjectWorld(bubble.WorldPosition + bubble.WorldOffset, out Vector2 worldProjected))
        {
            return worldProjected + offset;
        }

        return ResolveBubbleVector2(bubble.LayoutMode, bubble.ScreenPosition) + offset;
    }

    private bool TryProjectWorld(Vector3 worldPosition, out Vector2 screenPosition)
    {
        if (Game is null)
        {
            screenPosition = default;
            return false;
        }

        Vector4 clip = Vector4.Transform(
            Vector4.Transform(new Vector4(worldPosition, 1.0f), _camera.View),
            _camera.Projection);

        if (clip.W <= 0.0001f)
        {
            screenPosition = default;
            return false;
        }

        Vector3 ndc = new(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
        if (ndc.Z < 0.0f || ndc.Z > 1.0f)
        {
            screenPosition = default;
            return false;
        }

        int width = Math.Max(Game.GraphicsDevice.BackBufferSize.X, 1);
        int height = Math.Max(Game.GraphicsDevice.BackBufferSize.Y, 1);

        screenPosition = new Vector2(
            ((ndc.X * 0.5f) + 0.5f) * width,
            (1.0f - ((ndc.Y * 0.5f) + 0.5f)) * height);
        return true;
    }

    private static void AddSpriteImage(ImDrawListPtr drawList, uint textureId, Vector2 min, Vector2 max, float rotationDegrees, uint tint, bool flipV)
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

    private Texture2D? GetSpriteTexture(string path)
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

        if (_spriteTextures.TryGetValue(fullPath, out Texture2D? texture))
        {
            return texture;
        }

        texture = new Texture2D(Game.GraphicsDevice.Gl, GLEnum.ClampToEdge);
        texture.LoadFromFile(fullPath);
        _spriteTextures[fullPath] = texture;
        return texture;
    }

    private uint GetSpriteTextureId(string path)
    {
        if (RuntimeTextureProvider is not null && RuntimeTextureProvider.TryGetTexture(path, out uint textureId))
        {
            return textureId;
        }

        return GetSpriteTexture(path)?.Id ?? 0;
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
            fullPath = _resolvePath(path);
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

    private unsafe void DrawControl(GuiControlSettings control)
    {
        LayoutRect rect = ResolveGuiRect(control);
        ImGui.SetNextWindowPos(new Vector2(rect.X, rect.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(Math.Max(rect.Width, 1.0f), Math.Max(rect.Height, 1.0f)), ImGuiCond.Always);
        string type = control.Type.ToLowerInvariant();
        bool useWindowBackground = type is "label" or "checkbox";
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoResize;
        if (!useWindowBackground)
        {
            flags |= ImGuiWindowFlags.NoBackground;
        }

        GuiControlStyleSettings style = control.Style;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Math.Max(style.Rounding, 0.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, useWindowBackground ? Math.Max(style.BorderThickness, 0.0f) : 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Math.Max(style.Rounding, 0.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, Math.Max(style.BorderThickness, 0.0f));
        ImGui.PushStyleColor(ImGuiCol.Text, style.TextColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.Button, style.BackgroundColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.HoverColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, style.ActiveColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.Border, style.BorderColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.WindowBg, style.BackgroundColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.FrameBg, style.BackgroundColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, style.HoverColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, style.ActiveColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.CheckMark, style.TextColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.Header, style.BackgroundColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, style.HoverColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, style.ActiveColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, style.ActiveColor.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.PlotHistogramHovered, style.HoverColor.ToVector4());

        string windowId = $"gui:{control.Id}";
        if (!ImGui.Begin(windowId, flags))
        {
            ImGui.PopStyleColor(15);
            ImGui.PopStyleVar(8);
            ImGui.End();
            return;
        }

        ImGui.SetWindowFontScale(ResolveGuiFontScale(control));
        Vector2 controlSize = new(Math.Max(rect.Width, 1.0f), Math.Max(rect.Height, 1.0f));
        ImGui.SetCursorPos(Vector2.Zero);
        if (type == "label")
        {
            DrawAlignedTextBlock(control);
        }
        else if (type == "checkbox")
        {
            bool value = control.Checked;
            if (ImGui.Checkbox(control.Text, ref value))
            {
                control.Checked = value;
                _dispatchEvent(control, string.IsNullOrWhiteSpace(control.EventName) ? "changed" : control.EventName);
            }
        }
        else if (type == "dropdown")
        {
            string[] items = control.Items.Count == 0 ? ["Option"] : control.Items.ToArray();
            int selectedIndex = Math.Clamp(control.SelectedIndex, 0, items.Length - 1);
            if (ImGui.Combo($"##dropdown{control.Id}", ref selectedIndex, items, items.Length))
            {
                control.SelectedIndex = selectedIndex;
                _dispatchEvent(control, string.IsNullOrWhiteSpace(control.EventName) ? "changed" : control.EventName);
            }
        }
        else if (type == "textbox")
        {
            string value = control.Text ?? string.Empty;
            ImGuiInputTextFlags inputFlags = ImGuiInputTextFlags.CallbackAlways;
            bool changed = control.Multiline
                ? ImGui.InputTextMultiline($"##textbox{control.Id}", ref value, 8192, controlSize, inputFlags, data =>
                {
                    ImGuiInputTextCallbackDataPtr callbackData = new(data);
                    UpdateTextBoxSelectionState(control, callbackData.CursorPos, callbackData.SelectionStart, callbackData.SelectionEnd);
                    return 0;
                })
                : ImGui.InputText($"##textbox{control.Id}", ref value, 8192, inputFlags, data =>
                {
                    ImGuiInputTextCallbackDataPtr callbackData = new(data);
                    UpdateTextBoxSelectionState(control, callbackData.CursorPos, callbackData.SelectionStart, callbackData.SelectionEnd);
                    return 0;
                });
            ClampTextBoxSelectionState(control, value);
            if (changed)
            {
                control.Text = value;
                _dispatchEvent(control, string.IsNullOrWhiteSpace(control.EventName) ? "changed" : control.EventName);
            }
        }
        else if (type == "progress_bar")
        {
            float progress = Math.Clamp(control.Progress, 0.0f, 1.0f);
            ImGui.ProgressBar(progress, controlSize, string.IsNullOrWhiteSpace(control.Text) ? $"{progress:P0}" : control.Text);
        }
        else
        {
            string buttonText = string.IsNullOrWhiteSpace(control.Text) ? control.Name : control.Text;
            bool clicked = ImGui.Button($"{buttonText}##{control.Id}", controlSize);
            if (ImGui.IsItemActivated())
            {
                _dispatchEvent(control, "pressed");
            }

            if (ImGui.IsItemDeactivated())
            {
                _dispatchEvent(control, "released");
            }

            if (clicked)
            {
                _dispatchEvent(control, string.IsNullOrWhiteSpace(control.EventName) ? "clicked" : control.EventName);
            }
        }

        ImGui.PopStyleColor(15);
        ImGui.PopStyleVar(8);
        ImGui.End();
    }

    private LayoutRect ResolveGuiRect(GuiControlSettings control)
    {
        if (Game is null)
        {
            return new LayoutRect(control.X, control.Y, control.Width, control.Height);
        }

        GameWindowSettings window = _getWindowSettings();
        return LayoutResolver.Resolve(
            control.LayoutMode,
            control.X,
            control.Y,
            control.Width,
            control.Height,
            Game.Window.Size.X,
            Game.Window.Size.Y,
            window.Width,
            window.Height);
    }

    private void DrawBubbleTextBlock(
        string text,
        float maxWidth,
        float fontScale,
        string alignment,
        Vector4 color,
        bool wordWrap)
    {
        ImGui.SetWindowFontScale(fontScale);

        string[] lines = BuildTextLines(text, Math.Max(maxWidth, 1.0f), wordWrap);
        float lineHeight = Math.Max(ImGui.CalcTextSize("Ag").Y, 1.0f);
        Vector2 blockSize = new(maxWidth, lineHeight * lines.Length);
        Vector2 cursor = ImGui.GetCursorScreenPos();
        Vector2 clipMin = cursor;
        Vector2 clipMax = cursor + blockSize;
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint textColor = ImGui.GetColorU32(color);

        drawList.PushClipRect(clipMin, clipMax, true);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            Vector2 lineSize = ImGui.CalcTextSize(line);
            float x = ResolveHorizontalOffset(alignment, maxWidth, lineSize.X);
            drawList.AddText(cursor + new Vector2(x, lineHeight * i), textColor, line);
        }

        drawList.PopClipRect();
        ImGui.Dummy(blockSize);
        ImGui.SetWindowFontScale(1.0f);
    }

    private Vector2 ResolveBubbleVector2(string layoutMode, Vector2 value)
    {
        if (Game is null)
        {
            return value;
        }

        GameWindowSettings window = _getWindowSettings();
        LayoutRect rect = LayoutResolver.Resolve(
            layoutMode,
            value.X,
            value.Y,
            1.0f,
            1.0f,
            Game.Window.Size.X,
            Game.Window.Size.Y,
            window.Width,
            window.Height);
        return new Vector2(rect.X, rect.Y);
    }

    private float ResolveBubbleDimension(string layoutMode, float value)
    {
        if (Game is null)
        {
            return value;
        }

        GameWindowSettings window = _getWindowSettings();
        LayoutRect rect = LayoutResolver.Resolve(
            layoutMode,
            0.0f,
            0.0f,
            value,
            1.0f,
            Game.Window.Size.X,
            Game.Window.Size.Y,
            window.Width,
            window.Height);
        return rect.Width;
    }

    private float ResolveBubbleFontScale(string layoutMode, float fontSize)
    {
        if (Game is null)
        {
            return 1.0f;
        }

        GameWindowSettings window = _getWindowSettings();
        float resolved = LayoutResolver.ResolveFontSize(
            layoutMode,
            fontSize,
            Game.Window.Size.X,
            Game.Window.Size.Y,
            window.Width,
            window.Height);
        return resolved / BaseFontSize;
    }

    private float ResolveBubbleScalar(string layoutMode, float value)
    {
        if (Game is null || !LayoutResolver.IsRelative(layoutMode))
        {
            return value;
        }

        GameWindowSettings window = _getWindowSettings();
        float safeReferenceWidth = Math.Max(window.Width, 1.0f);
        float safeReferenceHeight = Math.Max(window.Height, 1.0f);
        float scaleX = Math.Max(Game.Window.Size.X, 1.0f) / safeReferenceWidth;
        float scaleY = Math.Max(Game.Window.Size.Y, 1.0f) / safeReferenceHeight;
        return value * MathF.Min(scaleX, scaleY);
    }

    private float ResolveGuiFontScale(GuiControlSettings control)
    {
        if (Game is null)
        {
            return 1.0f;
        }

        GuiControlStyleSettings style = control.Style;
        GameWindowSettings window = _getWindowSettings();
        float fontSize = Math.Clamp(style.FontSize <= 0.0f ? 18.0f : style.FontSize, 8.0f, 96.0f);
        fontSize = LayoutResolver.ResolveFontSize(
            control.LayoutMode,
            fontSize,
            Game.Window.Size.X,
            Game.Window.Size.Y,
            window.Width,
            window.Height);
        return fontSize / BaseFontSize;
    }

    private static void UpdateTextBoxSelectionState(GuiControlSettings control, int cursorPosition, int selectionStart, int selectionEnd)
    {
        control.CursorPosition = Math.Max(0, cursorPosition);
        control.SelectionStart = Math.Max(0, selectionStart);
        control.SelectionEnd = Math.Max(0, selectionEnd);
    }

    private static void ClampTextBoxSelectionState(GuiControlSettings control, string text)
    {
        int length = (text ?? string.Empty).Length;
        control.CursorPosition = Math.Clamp(control.CursorPosition, 0, length);
        control.SelectionStart = Math.Clamp(control.SelectionStart, 0, length);
        control.SelectionEnd = Math.Clamp(control.SelectionEnd, 0, length);
    }

    private static void DrawAlignedTextBlock(GuiControlSettings control)
    {
        Vector2 available = ImGui.GetContentRegionAvail();
        GuiControlStyleSettings style = control.Style;
        string[] lines = BuildTextLines(control.Text, Math.Max(available.X, 1.0f), control.WordWrap);
        float lineHeight = Math.Max(ImGui.CalcTextSize("Ag").Y, 1.0f);
        float blockHeight = lineHeight * lines.Length;
        float y = ResolveVerticalOffset(style.VerticalAlignment, available.Y, blockHeight);
        Vector2 cursor = ImGui.GetCursorScreenPos();
        Vector2 clipMin = cursor;
        Vector2 clipMax = cursor + available;
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint textColor = ImGui.GetColorU32(style.TextColor.ToVector4());

        drawList.PushClipRect(clipMin, clipMax, true);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            Vector2 lineSize = ImGui.CalcTextSize(line);
            float x = ResolveHorizontalOffset(style.HorizontalAlignment, available.X, lineSize.X);
            drawList.AddText(cursor + new Vector2(x, y + (lineHeight * i)), textColor, line);
        }

        drawList.PopClipRect();
        ImGui.Dummy(available);
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

    private static void ConfigureIoFontAtlas(string fontPath)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.Clear();
        io.Fonts.AddFontFromFileTTF(fontPath, BaseFontSize, default, io.Fonts.GetGlyphRangesChineseFull());
    }

}
