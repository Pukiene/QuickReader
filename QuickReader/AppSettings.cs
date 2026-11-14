using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickReader;

/// <summary>User settings, saved as %AppData%\QuickReader\settings.json.</summary>
internal sealed class AppSettings
{
    public string? VoiceId { get; set; }
    public double Speed { get; set; } = 1.0;
    public int? ToolbarX { get; set; }
    public int? ToolbarY { get; set; }

    public string ReadHotkey { get; set; } = "Ctrl+Alt+R";
    public string SnipHotkey { get; set; } = "Ctrl+Alt+S";
    public string PauseHotkey { get; set; } = "Ctrl+Alt+P";
    public string StopHotkey { get; set; } = "Ctrl+Alt+X";

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickReader");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJsonContext.Default.AppSettings)
                       ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
