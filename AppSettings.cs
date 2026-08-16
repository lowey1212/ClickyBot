using System.IO;
using System.Text.Json;

namespace ClickyBot;

public sealed class AppSettings
{
    public string ReferenceImageFolder { get; set; } = DefaultReferenceImageFolder();
    public string MacroFolder { get; set; } = DefaultMacroFolder();
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    public static string DefaultReferenceImageFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return Path.Combine(string.IsNullOrWhiteSpace(pictures) ? AppContext.BaseDirectory : pictures, "ClickyBot References");
    }

    public static string DefaultMacroFolder() => Path.Combine(AppContext.BaseDirectory, "macros");
}

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClickyBot",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Options) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // A broken settings file should not prevent the app from opening.
        }

        return new AppSettings();
    }

    public static bool Save(AppSettings settings, out string error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }
}
