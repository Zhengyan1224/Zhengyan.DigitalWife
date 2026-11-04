// Called by Zhengyan.DigitalWife.GamePlayer.
// Available globals: Entity, Scene, Input, Audio, DeltaSeconds, GuiControlId, GuiControlName.
if (IsStart)
{
    // Entity.SetPosition(0, 0, 0);
    // Entity.Speak("你好，我是小雨", speakerId: 0, speed: 1.0f, volume: 1.0f, onCompleted: () =>
    // {
    //     Console.WriteLine("speech completed");
    // });
}


if (IsGuiEvent)
{
		Console.WriteLine($"{GuiControlName} ({GuiControlId}) -> {GuiEventName}");
    if(GuiControlName == "button_dance" && GuiEventName == "clicked")
    {
    		Audio.Play("Lamb");
		    Entity.ApplyMotion("assets/motions/Lamb.vmd");
    }
}

if (IsSpeechEvent && SpeechCallbackName == "after_speak")
{
    Console.WriteLine("speech completed by named callback");
}

if (IsUpdate)
{
    // Example: click the ground plane and move this entity there.
    // if (Input.IsMouseButtonDown("left"))
    // {
    //     RuntimeRay ray = Scene.Camera.ScreenPointToRay(Input.MouseX, Input.MouseY);
    //     if (ray.TryIntersectPlaneY(0.0f, out Vector3 hit))
    //     {
    //         Entity.SetPosition(hit.X, hit.Y, hit.Z);
    //     }
    // }
}