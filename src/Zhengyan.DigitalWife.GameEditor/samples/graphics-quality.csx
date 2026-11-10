// Bind this script once to the shared event target of LowQuality, MediumQuality and HighQuality.
// Plain variables avoid System.ValueTuple dependencies in trimmed Android packages.
// The same C# script works on PC, Android and Web. Only clicked events change quality.
if (IsGuiEvent && GuiEventName == "clicked")
{
    int sceneSamples = 0;
    int directionalResolution = 0;
    int localResolution = 0;
    int filterSamples = 0;
    float filterRadius = 0.0f;
    int mirrorSamples = 0;
    int waterSamples = 0;

    switch (GuiControlName)
    {
        case "LowQuality":
            sceneSamples = 1;
            directionalResolution = 512;
            localResolution = 256;
            filterSamples = 4;
            filterRadius = 1.0f;
            mirrorSamples = 1;
            waterSamples = 1;
            break;
        case "MediumQuality":
            sceneSamples = 2;
            directionalResolution = 1024;
            localResolution = 512;
            filterSamples = 9;
            filterRadius = 1.5f;
            mirrorSamples = 2;
            waterSamples = 2;
            break;
        case "HighQuality":
            sceneSamples = 4;
            directionalResolution = 2048;
            localResolution = 1024;
            filterSamples = 25;
            filterRadius = 2.0f;
            mirrorSamples = 4;
            waterSamples = 4;
            break;
    }

    // Ignore unrelated GUI buttons instead of changing their quality implicitly.
    if (sceneSamples != 0)
    {
        Graphics.SceneSamples = sceneSamples;
        Graphics.Shadows.DirectionalResolution = directionalResolution;
        Graphics.Shadows.LocalLightResolution = localResolution;
        Graphics.Shadows.FilterSamples = filterSamples;
        Graphics.Shadows.FilterRadius = filterRadius;

        // Add the names of other surfaces here. GetEntity is shared by PC, Android and Web.
        foreach (var name in new[] { "Mirror" })
        {
            var surface = Scene.GetEntity(name);
            if (surface != null) surface.ReflectionSamples = mirrorSamples;
        }
        foreach (var name in new[] { "Water" })
        {
            var surface = Scene.GetEntity(name);
            if (surface != null) surface.ReflectionSamples = waterSamples;
        }
    }
}
