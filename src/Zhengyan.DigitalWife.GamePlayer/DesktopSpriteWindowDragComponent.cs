using Silk.NET.Input;
using Silk.NET.Maths;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class DesktopSpriteWindowDragComponent(Func<GameWindowSettings> getWindowSettings) : GameComponent
{
    private readonly Func<GameWindowSettings> _getWindowSettings = getWindowSettings;
    private bool _dragging;
    private MouseButton _dragButton;
    private Vector2D<int> _dragStartWindowPosition;
    private System.Numerics.Vector2 _dragStartCursorScreenPosition;

    public bool IsDragging => _dragging;

    public override void Update(GameTime gameTime)
    {
        _ = gameTime;

        if (Game is null)
        {
            return;
        }

        GameWindowSettings settings = _getWindowSettings();
        if (!settings.DesktopSpriteMode || !TryParseDragButton(settings.DesktopSpriteDragButton, out MouseButton configuredButton))
        {
            _dragging = false;
            return;
        }

        if (!Game.Input.IsMouseButtonDown(configuredButton))
        {
            _dragging = false;
            return;
        }

        System.Numerics.Vector2 currentMousePosition = Game.Input.MousePosition;
        if (!_dragging || _dragButton != configuredButton)
        {
            _dragging = true;
            _dragButton = configuredButton;
            _dragStartWindowPosition = Game.Window.Position;
            _dragStartCursorScreenPosition = GetCursorScreenPosition();
            return;
        }

        System.Numerics.Vector2 delta = GetCursorScreenPosition() - _dragStartCursorScreenPosition;
        Game.Window.Position = new Vector2D<int>(
            _dragStartWindowPosition.X + (int)MathF.Round(delta.X),
            _dragStartWindowPosition.Y + (int)MathF.Round(delta.Y));
    }

    private System.Numerics.Vector2 GetCursorScreenPosition()
    {
        if (Game is null)
        {
            return System.Numerics.Vector2.Zero;
        }

        if (DesktopSpritePlatform.TryGetGlobalCursorPosition(Game.Window, out System.Numerics.Vector2 globalPosition))
        {
            return globalPosition;
        }

        Vector2D<int> windowPosition = Game.Window.Position;
        System.Numerics.Vector2 mousePosition = Game.Input.MousePosition;
        return new System.Numerics.Vector2(windowPosition.X + mousePosition.X, windowPosition.Y + mousePosition.Y);
    }

    private static bool TryParseDragButton(string value, out MouseButton button)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        switch (normalized)
        {
            case "left":
            case "mouse_left":
            case "left_mouse":
                button = MouseButton.Left;
                return true;
            case "right":
            case "mouse_right":
            case "right_mouse":
                button = MouseButton.Right;
                return true;
            case "middle":
            case "mouse_middle":
            case "middle_mouse":
                button = MouseButton.Middle;
                return true;
            default:
                button = default;
                return false;
        }
    }
}
