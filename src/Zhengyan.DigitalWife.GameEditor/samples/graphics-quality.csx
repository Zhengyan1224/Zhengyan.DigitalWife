// Bind to a settings button (GUI event target). The same C# script works on PC, Android and Web.
if (IsGuiEvent && GuiEventName == "clicked")
{
    bool high = GuiControlName == "HighQuality";
    Graphics.SceneSamples = high ? 4 : 1;
    Graphics.Shadows.DirectionalResolution = high ? 2048 : 512;
    Graphics.Shadows.LocalLightResolution = high ? 1024 : 256;
    Graphics.Shadows.FilterSamples = high ? 25 : 4;
    Graphics.Shadows.FilterRadius = high ? 2.0f : 1.0f;
    var mirror = Scene.GetEntity("Mirror");
    var water = Scene.GetEntity("Water");
    if (mirror != null) mirror.ReflectionSamples = high ? 4 : 1;
    if (water != null) water.ReflectionSamples = high ? 2 : 1;
}
