using System.Text.Json;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GameEditor;

internal static class EditorGraphicsSettingsStore
{
    private const string FileName = "GameEditor.settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static GraphicsBackend Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return GraphicsBackend.Auto;
            }

            EditorGraphicsSettings? settings = JsonSerializer.Deserialize<EditorGraphicsSettings>(
                File.ReadAllText(SettingsPath),
                JsonOptions);
            return GraphicsBackendNames.Parse(settings?.GraphicsBackend);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read editor graphics settings: {ex.Message}");
            return GraphicsBackend.Auto;
        }
    }

    public static void Save(GraphicsBackend backend)
    {
        EditorGraphicsSettings settings = new()
        {
            GraphicsBackend = backend.ToSettingValue()
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private sealed class EditorGraphicsSettings
    {
        public string GraphicsBackend { get; set; } = "Auto";
    }
}
