using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidCameraTouchInput
{
    private readonly HashSet<int> _guiContacts = [];
    private RuntimeScene? _scene;

    public RuntimeCameraInput GetInput(RuntimeScene? scene, AndroidInputSnapshot input,
        int width, int height, int referenceWidth, int referenceHeight)
    {
        if (!ReferenceEquals(_scene, scene))
        {
            _guiContacts.Clear();
            _scene = scene;
        }
        if (scene is null) return RuntimeCameraInput.None;
        foreach (AndroidTouchPoint touch in input.TouchTransitions)
        {
            if (touch.Phase == AndroidTouchPhase.Began && GuiPointerHitTest.Contains(scene.Definition,
                touch.PixelPosition.X, touch.PixelPosition.Y, width, height, referenceWidth, referenceHeight))
                _guiContacts.Add(touch.Id);
        }
        // Capture lasts until release, including a drag outside the GUI bounds.
        AndroidTouchPoint[] active = input.Touches.Where(t => t.IsActive && !_guiContacts.Contains(t.Id)).ToArray();
        _guiContacts.RemoveWhere(id => !input.Touches.Any(t => t.Id == id && t.IsActive));
        if (active.Length == 0) return RuntimeCameraInput.None;
        if (active.Length == 1) return new(active[0].Delta, Vector2.Zero, 0);

        Vector2 currentA = active[0].PixelPosition;
        Vector2 currentB = active[1].PixelPosition;
        Vector2 previousA = currentA - active[0].Delta;
        Vector2 previousB = currentB - active[1].Delta;
        float zoom = (Vector2.Distance(previousA, previousB) - Vector2.Distance(currentA, currentB)) * 0.01f;
        return new(Vector2.Zero, (active[0].Delta + active[1].Delta) * 0.5f, zoom);
    }
}
