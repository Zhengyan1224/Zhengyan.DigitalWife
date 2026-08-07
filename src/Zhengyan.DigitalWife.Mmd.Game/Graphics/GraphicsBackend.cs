namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public enum GraphicsBackend
{
    Auto = 0,
    OpenGL = 1,
    Vulkan = 2
}

public static class GraphicsBackendNames
{
    public static GraphicsBackend Parse(string? value, GraphicsBackend fallback = GraphicsBackend.Auto)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => GraphicsBackend.Auto,
            "opengl" or "open_gl" or "gl" => GraphicsBackend.OpenGL,
            "vulkan" or "vk" => GraphicsBackend.Vulkan,
            _ => fallback
        };
    }

    public static string ToSettingValue(this GraphicsBackend backend) => backend switch
    {
        GraphicsBackend.OpenGL => "OpenGL",
        GraphicsBackend.Vulkan => "Vulkan",
        _ => "Auto"
    };
}
