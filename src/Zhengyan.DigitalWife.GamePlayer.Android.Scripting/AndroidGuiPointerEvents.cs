using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal enum AndroidGuiPointerPhase
{
    Pressed,
    Released,
    Cancelled
}

internal sealed class AndroidGuiPointerEvents
{
    private readonly Dictionary<int, GuiControlSettings> _pressedButtons = [];
    private RuntimeScene? _scene;

    public bool Dispatch(
        RuntimeScene scene,
        int pointerId,
        AndroidGuiPointerPhase phase,
        Vector2 pixelPosition,
        Vector2 position,
        Vector2 viewportSize,
        Vector2 referenceSize,
        Action<AndroidRuntimeEvent> dispatch)
    {
        if (!ReferenceEquals(_scene, scene))
        {
            Reset();
            _scene = scene;
        }

        GuiControlSettings? hit = phase == AndroidGuiPointerPhase.Cancelled
            ? null : FindControl(scene, pixelPosition, viewportSize, referenceSize);
        if (phase != AndroidGuiPointerPhase.Pressed && _pressedButtons.Remove(pointerId, out GuiControlSettings? pressed))
        {
            // A release belongs to the original button, including when the
            // pointer leaves its bounds or Android cancels the gesture.
            dispatch(CreateEvent(pressed, "released", position));
            if (phase == AndroidGuiPointerPhase.Released && ReferenceEquals(hit, pressed))
            {
                dispatch(CreateEvent(pressed, string.IsNullOrWhiteSpace(pressed.EventName) ? "clicked" : pressed.EventName, position));
            }
            return true;
        }

        if (hit is null) return false;
        if (IsButton(hit))
        {
            if (phase == AndroidGuiPointerPhase.Pressed && _pressedButtons.TryAdd(pointerId, hit))
            {
                dispatch(CreateEvent(hit, "pressed", position));
            }
            // Releasing over a button without pressing it must not click it.
            return true;
        }

        dispatch(CreateEvent(hit, phase == AndroidGuiPointerPhase.Pressed ? "pointer_down" : hit.EventName, position));
        return true;
    }

    public void Reset()
    {
        _pressedButtons.Clear();
        _scene = null;
    }

    private static GuiControlSettings? FindControl(RuntimeScene scene, Vector2 position, Vector2 viewportSize, Vector2 referenceSize)
    {
        foreach (GuiControlSettings control in scene.Definition.GuiControls.Where(control => control.Visible).Reverse())
        {
            LayoutRect rect = LayoutResolver.Resolve(control.LayoutMode, control.X, control.Y, control.Width, control.Height,
                viewportSize.X, viewportSize.Y, referenceSize.X, referenceSize.Y);
            if (position.X >= rect.X && position.X < rect.X + rect.Width
                && position.Y >= rect.Y && position.Y < rect.Y + rect.Height)
            {
                return control;
            }
        }
        return null;
    }

    private static bool IsButton(GuiControlSettings control)
        => control.Type.ToLowerInvariant() is not ("label" or "checkbox" or "dropdown" or "textbox" or "progress_bar" or "progress");

    private static AndroidRuntimeEvent CreateEvent(GuiControlSettings control, string eventName, Vector2 position)
        => new("gui", control.Id, eventName, position, control.Name, control.TargetEntity);
}
