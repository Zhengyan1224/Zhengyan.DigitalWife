using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidCameraTouchInput
{
    private readonly HashSet<int> _guiContacts = [];
    private RuntimeScene? _scene;
    public Func<bool>? CursorLockReader {get;set;}
    public Func<bool,bool,bool>? CursorLockSetter {get;set;}

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
        var device=input.DeviceInput;
        bool locked=CursorLockReader?.Invoke()==true;
        var look=locked||device.IsMouseButtonDown(0)?device.MouseDelta:Vector2.Zero;
        var pan=!locked&&device.IsMouseButtonDown(1)?device.MouseDelta:Vector2.Zero;
        float scroll=device.ScrollDelta.Y;
        if(active.Length==1)look+=active[0].Delta;
        if(active.Length<2)return CompleteInput(new(look,pan,scroll),input);

        Vector2 currentA = active[0].PixelPosition;
        Vector2 currentB = active[1].PixelPosition;
        Vector2 previousA = currentA - active[0].Delta;
        Vector2 previousB = currentB - active[1].Delta;
        float zoom = (Vector2.Distance(previousA, previousB) - Vector2.Distance(currentA, currentB)) * 0.01f;
        return CompleteInput(new(look,pan+(active[0].Delta + active[1].Delta) * 0.5f,zoom+scroll),input);
    }

    private RuntimeCameraInput CompleteInput(RuntimeCameraInput camera,AndroidInputSnapshot snapshot)
    {
        var device=snapshot.DeviceInput;var pad=device.Gamepad;
        string[] buttons=["ButtonA","ButtonB","ButtonX","ButtonY","ButtonL1","ButtonR1","ButtonL2","ButtonR2","ButtonSelect","ButtonStart","ButtonThumbl","ButtonThumbr","DpadUp","DpadDown","DpadLeft","DpadRight","ButtonMode"];
        float[] buttonValues=buttons.Select(b=>pad.Buttons.Any(k=>k.ToString()==b)?1f:0f).ToArray();
        buttonValues[6]=Math.Max(buttonValues[6],pad.LeftTrigger);
        buttonValues[7]=Math.Max(buttonValues[7],pad.RightTrigger);
        return camera with{HorizontalScroll=device.ScrollDelta.X,PointerPosition=device.MousePosition,PointerDelta=device.MouseDelta,PrimaryButtonDown=device.IsMouseButtonDown(0),SecondaryButtonDown=device.IsMouseButtonDown(1),MiddleButtonDown=device.IsMouseButtonDown(2),
            KeyState=key=>device.Keys.Any(k=>k.ToString().Equals(key,StringComparison.OrdinalIgnoreCase)),CursorLockReader=CursorLockReader,CursorLockSetter=CursorLockSetter,
            Gamepad=new(pad.Connected,pad.Name,pad.Index,[pad.LeftStick.X,pad.LeftStick.Y,pad.RightStick.X,pad.RightStick.Y],buttonValues)};
    }
}
