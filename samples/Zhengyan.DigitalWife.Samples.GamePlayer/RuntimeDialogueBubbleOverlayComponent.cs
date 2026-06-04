using System.Numerics;
using ImGuiNET;
using Silk.NET.OpenGLES.Extensions.ImGui;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed class RuntimeDialogueBubbleOverlayComponent(
    Func<RuntimeScene?> getScene,
    OrbitCamera camera,
    Func<GameWindowSettings> getWindowSettings) : DrawableGameComponent
{
    private const float BaseFontSize = 18.0f;

    private readonly Func<RuntimeScene?> _getScene = getScene;
    private readonly OrbitCamera _camera = camera;
    private readonly Func<GameWindowSettings> _getWindowSettings = getWindowSettings;
    private ImGuiController? _controller;

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        if (ImGuiFontResolver.TryGetCjkFontPath(out string fontPath))
        {
            try
            {
                _controller = new ImGuiController(
                    Game.GraphicsDevice.Gl,
                    Game.Window,
                    Game.Input.Context,
                    () => ConfigureIoFontAtlas(fontPath));
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
    }

    public override void Draw(GameTime gameTime)
    {
        if (Game is null || _controller is null)
        {
            return;
        }

        _controller.Update((float)gameTime.ElapsedSeconds);
        DrawBubbles();
        _controller.Render();
    }

    public override void Dispose()
    {
        _controller?.Dispose();
        _controller = null;
        base.Dispose();
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
            ResolveScalar(bubble.LayoutMode, bubble.PaddingX),
            ResolveScalar(bubble.LayoutMode, bubble.PaddingY));
        float width = ResolveDimension(bubble.LayoutMode, bubble.Width);
        float rounding = ResolveScalar(bubble.LayoutMode, bubble.Rounding);
        float borderSize = ResolveScalar(bubble.LayoutMode, bubble.BorderThickness);

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
                DrawTextBlock(
                    bubble.HeaderText,
                    width,
                    ResolveFontScale(bubble.LayoutMode, bubble.HeaderFontSize),
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

                DrawTextBlock(
                    bubble.Text,
                    width,
                    ResolveFontScale(bubble.LayoutMode, bubble.FontSize),
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

                DrawTextBlock(
                    bubble.FooterText,
                    width,
                    ResolveFontScale(bubble.LayoutMode, bubble.FooterFontSize),
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
        Vector2 offset = ResolveVector2(bubble.LayoutMode, bubble.ScreenOffset);

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

        return ResolveVector2(bubble.LayoutMode, bubble.ScreenPosition) + offset;
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

    private void DrawTextBlock(
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

    private Vector2 ResolveVector2(string layoutMode, Vector2 value)
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

    private float ResolveDimension(string layoutMode, float value)
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

    private float ResolveFontScale(string layoutMode, float fontSize)
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

    private float ResolveScalar(string layoutMode, float value)
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
        return (alignment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "right" => Math.Max(0.0f, available - content),
            "center" => Math.Max(0.0f, (available - content) * 0.5f),
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
