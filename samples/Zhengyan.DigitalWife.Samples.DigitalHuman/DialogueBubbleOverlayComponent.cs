using System.Numerics;
using ImGuiNET;
using Silk.NET.OpenGLES.Extensions.ImGui;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Samples.DigitalHuman;

internal sealed class DialogueBubbleOverlayComponent(
    DigitalHumanGame game,
    OrbitCamera camera,
    Func<DialogueBubbleSnapshot> getSnapshot,
    Func<Vector3?> getAnchorWorldPosition,
    Func<StartupStatusSnapshot>? getStartupSnapshot = null) : DrawableGameComponent
{
    private readonly DigitalHumanGame _game = game;
    private readonly OrbitCamera _camera = camera;
    private readonly Func<DialogueBubbleSnapshot> _getSnapshot = getSnapshot;
    private readonly Func<Vector3?> _getAnchorWorldPosition = getAnchorWorldPosition;
    private readonly Func<StartupStatusSnapshot>? _getStartupSnapshot = getStartupSnapshot;
    private ImGuiController? _controller;

    protected override void Initialize()
    {
        if (Game is null)
        {
            throw new InvalidOperationException("Game is not attached.");
        }

        if (ImGuiFontResolver.TryGetCjkFontPath(out string fontPath))
        {
            _controller = new ImGuiController(
                Game.GraphicsDevice.Gl,
                Game.Window,
                Game.Input.Context,
                () => ConfigureIoFontAtlas(fontPath));
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
        DrawStartupWindow(gameTime);
        DrawBubbleWindow();
        _controller.Render();
    }

    public override void Dispose()
    {
        _controller?.Dispose();
        _controller = null;
        base.Dispose();
    }

    private void DrawBubbleWindow()
    {
        DialogueBubbleSnapshot snapshot = _getSnapshot();
        if (!snapshot.Visible)
        {
            return;
        }

        Vector2 position = ResolveBubblePosition(snapshot);

        ImGui.SetNextWindowPos(position, ImGuiCond.Always, new Vector2(0.5f, 1.0f));
        ImGui.SetNextWindowBgAlpha(0.82f);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoInputs;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 14.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14.0f, 10.0f));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.08f, 0.11f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.42f, 0.64f, 0.95f, 0.85f));

        if (ImGui.Begin("DialogueBubble", flags))
        {
            ImGui.PushTextWrapPos(snapshot.Width);

            if (snapshot.ShowUserText && !string.IsNullOrWhiteSpace(snapshot.UserText))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.78f, 0.84f, 0.95f, 1.0f));
                ImGui.TextWrapped($"你：{snapshot.UserText}");
                ImGui.PopStyleColor();
                ImGui.Separator();
            }

            if (!string.IsNullOrWhiteSpace(snapshot.AssistantText))
            {
                ImGui.TextWrapped(snapshot.AssistantText);
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.HintText))
            {
                ImGui.TextWrapped(snapshot.HintText);
            }

            if (!string.IsNullOrWhiteSpace(snapshot.AssistantText) && !string.IsNullOrWhiteSpace(snapshot.HintText))
            {
                ImGui.Separator();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.72f, 0.76f, 0.82f, 1.0f));
                ImGui.TextWrapped(snapshot.HintText);
                ImGui.PopStyleColor();
            }

            ImGui.PopTextWrapPos();
        }

        ImGui.End();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(2);
    }

    private void DrawStartupWindow(GameTime gameTime)
    {
        if (_getStartupSnapshot is null)
        {
            return;
        }

        StartupStatusSnapshot snapshot = _getStartupSnapshot();
        if (!snapshot.Visible)
        {
            return;
        }

        Vector2 windowSize = Game is null
            ? Vector2.Zero
            : new Vector2(
                Math.Max(Game.GraphicsDevice.BackBufferSize.X, 1),
                Math.Max(Game.GraphicsDevice.BackBufferSize.Y, 1));

        if (windowSize == Vector2.Zero)
        {
            return;
        }

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.92f);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoInputs;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.03f, 0.05f, 0.08f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.52f, 0.86f, 0.55f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0.0f, 0.0f));

        if (ImGui.Begin("StartupLoadingOverlay", flags))
        {
            float spinnerValue = (float)((gameTime.TotalSeconds * 1.25) % 1.0);
            float panelWidth = Math.Max(320.0f, Math.Min(680.0f, windowSize.X - 80.0f));
            float panelHeight = 240.0f;
            Vector2 panelPos = new(
                (windowSize.X - panelWidth) * 0.5f,
                (windowSize.Y - panelHeight) * 0.5f);

            ImGui.SetCursorPos(panelPos);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(30.0f, 26.0f));
            ImGui.BeginChild(
                "LoadingPanel",
                new Vector2(panelWidth, panelHeight),
                ImGuiChildFlags.None);

            ImGui.TextColored(new Vector4(0.78f, 0.88f, 1.0f, 1.0f), "Zhengyan Digital Wife");
            ImGui.Spacing();

            string statusTitle = string.IsNullOrWhiteSpace(snapshot.Title) ? "正在初始化" : snapshot.Title;
            string statusMessage = string.IsNullOrWhiteSpace(snapshot.Message) ? "请稍候..." : snapshot.Message;

            ImGui.TextWrapped(statusTitle);
            ImGui.Spacing();
            ImGui.TextWrapped(statusMessage);
            ImGui.Spacing();

            ImGui.ProgressBar(snapshot.Progress, new Vector2(-1.0f, 0.0f), $"{snapshot.Progress:P0}");
            ImGui.Spacing();

            string spinner = spinnerValue switch
            {
                < 0.25f => "加载中。",
                < 0.50f => "加载中。。",
                < 0.75f => "加载中。。。",
                _ => "加载中。。。。"
            };

            ImGui.TextColored(new Vector4(0.70f, 0.76f, 0.84f, 1.0f), spinner);
            ImGui.TextWrapped("语音识别和语音合成模型准备完成后，会自动进入主界面。");

            ImGui.PopStyleVar();
            ImGui.EndChild();
        }

        ImGui.End();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private Vector2 ResolveBubblePosition(DialogueBubbleSnapshot snapshot)
    {
        if (TryProjectAnchor(out Vector2 projected))
        {
            return projected + snapshot.ScreenOffset;
        }

        return new Vector2(220.0f, 140.0f) + snapshot.ScreenOffset;
    }

    private bool TryProjectAnchor(out Vector2 screenPosition)
    {
        Vector3? anchor = _getAnchorWorldPosition();
        if (anchor is null || Game is null)
        {
            screenPosition = default;
            return false;
        }

        Vector4 clip = Vector4.Transform(
            Vector4.Transform(new Vector4(anchor.Value, 1.0f), _camera.View),
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

    private static void ConfigureIoFontAtlas(string fontPath)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.Clear();
        io.Fonts.AddFontFromFileTTF(fontPath, 18.0f, default, io.Fonts.GetGlyphRangesChineseFull());
    }

}

internal readonly record struct DialogueBubbleSnapshot(
    bool Visible,
    float Width,
    Vector2 ScreenOffset,
    bool ShowUserText,
    string HintText,
    string UserText,
    string AssistantText);
