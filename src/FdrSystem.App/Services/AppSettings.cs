using System.Text.Json;
using FdrSystem.Core;

namespace FdrSystem.App.Services;

/// <summary>Definições guardadas em ~/.local/share/FDR_SYSTEM/settings.json.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";

    public bool RevealIdentifiers { get; set; }

    public bool CheckUpdatesOnStart { get; set; } = true;

    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }

        return new AppSettings();
    }

    public static void Save()
    {
        try
        {
            AppPaths.EnsureDataDirectories();
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
        }
    }
}
