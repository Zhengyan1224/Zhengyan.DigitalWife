// Bind this script once to the shared event target of LowQuality, MediumQuality and HighQuality.
// The same C# script works on PC, Android and Web. Only clicked events change quality.
if (IsGuiEvent && GuiEventName == "clicked")
{
    var preset = GuiControlName switch
    {
        "LowQuality" => (Scene: 1, Directional: 512, Local: 256, Filter: 4, Radius: 1.0f, Mirror: 1, Water: 1),
        "MediumQuality" => (Scene: 2, Directional: 1024, Local: 512, Filter: 9, Radius: 1.5f, Mirror: 2, Water: 2),
        "HighQuality" => (Scene: 4, Directional: 2048, Local: 1024, Filter: 25, Radius: 2.0f, Mirror: 4, Water: 4),
        _ => (Scene: 0, Directional: 0, Local: 0, Filter: 0, Radius: 0.0f, Mirror: 0, Water: 0)
    };

    // Ignore unrelated GUI buttons instead of changing their quality implicitly.
    if (preset.Scene != 0)
    {
        Graphics.SceneSamples = preset.Scene;
        Graphics.Shadows.DirectionalResolution = preset.Directional;
        Graphics.Shadows.LocalLightResolution = preset.Local;
        Graphics.Shadows.FilterSamples = preset.Filter;
        Graphics.Shadows.FilterRadius = preset.Radius;

        // Add the names of other surfaces here. GetEntity is shared by PC, Android and Web.
        foreach (var name in new[] { "Mirror" })
        {
            var surface = Scene.GetEntity(name);
            if (surface != null) surface.ReflectionSamples = preset.Mirror;
        }
        foreach (var name in new[] { "Water" })
        {
            var surface = Scene.GetEntity(name);
            if (surface != null) surface.ReflectionSamples = preset.Water;
        }
    }
}
