namespace FdrSystem.Core;

/// <summary>
/// Caminhos da app no Linux. Usa XDG quando existe e cai para ~/Documents.
/// Windows mantém %LOCALAPPDATA% e Documentos para o modo de desenvolvimento.
/// </summary>
public static class AppPaths
{
    public const string AppName = "FDR-SYSTEM";

    /// <summary>Nome usado até à 1.6.0, migrado automaticamente na primeira execução.</summary>
    private const string LegacyAppName = "FDR_SYSTEM";

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

    private static string DataRoot
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return !string.IsNullOrWhiteSpace(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }
    }

    public static string DataDirectory => Path.Combine(DataRoot, AppName);

    public static string FirmwareFolder => Path.Combine(Documents, "iDevice-Firmware");

    public static string ReportsFolder => Path.Combine(Documents, AppName, "Relatorios");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static void EnsureDataDirectories()
    {
        MigrateLegacyFolders();
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    /// <summary>
    /// A 1.6.0 usava <c>FDR_SYSTEM</c>; move os dados e os relatórios para o nome
    /// novo sem perder definições. Falhas de permissão são ignoradas: a app
    /// recria as pastas vazias.
    /// </summary>
    private static void MigrateLegacyFolders()
    {
        try
        {
            var legacyData = Path.Combine(DataRoot, LegacyAppName);
            if (Directory.Exists(legacyData))
            {
                if (!Directory.Exists(DataDirectory))
                {
                    Directory.Move(legacyData, DataDirectory);
                }
                else
                {
                    // A pasta nova já existe (por exemplo, criada por uma execução
                    // interrompida): preserva as definições e a cache antigas.
                    CopyIfMissing(
                        Path.Combine(legacyData, "settings.json"),
                        Path.Combine(DataDirectory, "settings.json"));

                    var legacyCache = Path.Combine(legacyData, "cache");
                    var cache = Path.Combine(DataDirectory, "cache");
                    if (!Directory.Exists(cache) && Directory.Exists(legacyCache))
                        Directory.Move(legacyCache, cache);
                }
            }

            var legacyReports = Path.Combine(Documents, LegacyAppName, "Relatorios");
            if (!Directory.Exists(ReportsFolder) && Directory.Exists(legacyReports))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportsFolder)!);
                Directory.Move(legacyReports, ReportsFolder);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (File.Exists(source) && !File.Exists(destination))
            File.Copy(source, destination);
    }
}
