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
    Func<GameWindowSettings> getWindowSettings,
    Func<string, string> resolvePath,
    Action<GuiControlSettings, string> dispatchEvent) : DrawableGameComponent
{
    private const float BaseFontSize = 18.0f;

    private readonly Func<IReadOnlyList<GuiControlSettings>> _getControls = getControls;
    private readonly Func<IReadOnlyList<SpriteSettings>> _getSprites = getSprites;
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

            Vector2 min = new(sprite.X, sprite.Y);
            Vector2 spriteMax = min + new Vector2(Math.Max(sprite.Width, 1.0f), Math.Max(sprite.Height, 1.0f));
            uint tint = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, Math.Clamp(sprite.Opacity, 0.0f, 1.0f)));
            AddSpriteImage(drawList, textureId, min, spriteMax, sprite.RotationDegrees, tint);
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

    private void DrawControl(GuiControlSettings control)
    {
        LayoutRect rect = ResolveGuiRect(control);
        ImGui.SetNextWindowPos(new Vector2(rect.X, rect.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(Math.Max(rect.Width, 1.0f), Math.Max(rect.Height, 1.0f)), ImGuiCond.Always);
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoResize;

        GuiControlStyleSettings style = control.Style;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.Zero);
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
            ImGui.PopStyleVar(6);
            ImGui.End();
            return;
        }

        ImGui.SetWindowFontScale(ResolveGuiFontScale(control));
        Vector2 controlSize = new(Math.Max(rect.Width, 1.0f), Math.Max(rect.Height, 1.0f));
        ImGui.SetCursorPos(Vector2.Zero);
        string type = control.Type.ToLowerInvariant();
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
            bool changed = control.Multiline
                ? ImGui.InputTextMultiline($"##textbox{control.Id}", ref value, 8192, controlSize)
                : ImGui.InputText($"##textbox{control.Id}", ref value, 8192);
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
            if (ImGui.Button($"{buttonText}##{control.Id}", controlSize))
            {
                _dispatchEvent(control, string.IsNullOrWhiteSpace(control.EventName) ? "clicked" : control.EventName);
            }
        }

        ImGui.PopStyleColor(15);
        ImGui.PopStyleVar(6);
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
