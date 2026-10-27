namespace FdrSystem.Core;

/// <summary>
/// Caminhos da app no Linux. Usa XDG quando existe e cai para ~/Documents.
/// Windows mantém %LOCALAPPDATA% e Documentos para o modo de desenvolvimento.
/// </summary>
public static class AppPaths
{
    public const string AppName = "FDR_SYSTEM";

    public static string Documents
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var xdg = Environment.GetEnvironmentVariable("XDG_DOCUMENTS_DIR");
            if (!string.IsNullOrWhiteSpace(xdg) && Directory.Exists(xdg)) return xdg;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var documents = Path.Combine(home, "Documents");
            return Directory.Exists(documents) ? documents : home;
        }
    }

    public static string DataDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var root = !string.IsNullOrWhiteSpace(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

            return Path.Combine(root, AppName);
        }
    }

    public static string FirmwareFolder => Path.Combine(Documents, "iDevice-Firmware");

    public static string ReportsFolder => Path.Combine(Documents, AppName, "Relatorios");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
