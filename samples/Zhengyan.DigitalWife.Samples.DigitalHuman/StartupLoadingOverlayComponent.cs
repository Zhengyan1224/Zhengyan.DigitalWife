using System.Numerics;
using ImGuiNET;
using Silk.NET.OpenGLES.Extensions.ImGui;
using Zhengyan.DigitalWife.Mmd.Game;

namespace Zhengyan.DigitalWife.Samples.DigitalHuman;

internal sealed class StartupLoadingOverlayComponent(
    Func<StartupStatusSnapshot> getSnapshot) : DrawableGameComponent
{
    private readonly Func<StartupStatusSnapshot> _getSnapshot = getSnapshot;
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

        StartupStatusSnapshot snapshot = _getSnapshot();
        if (!snapshot.Visible)
        {
            return;
        }

        _controller.Update((float)gameTime.ElapsedSeconds);
        DrawLoadingOverlay(snapshot, gameTime);
        _controller.Render();
    }

    public override void Dispose()
    {
        _controller?.Dispose();
        _controller = null;
        base.Dispose();
    }

    private void DrawLoadingOverlay(StartupStatusSnapshot snapshot, GameTime gameTime)
    {
        if (Game is null)
        {
            return;
        }

        Vector2 windowSize = new(
            Math.Max(Game.GraphicsDevice.BackBufferSize.X, 1),
            Math.Max(Game.GraphicsDevice.BackBufferSize.Y, 1));

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

    private static void ConfigureIoFontAtlas(string fontPath)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.Clear();
        io.Fonts.AddFontFromFileTTF(fontPath, 18.0f, default, io.Fonts.GetGlyphRangesChineseFull());
    }

}
