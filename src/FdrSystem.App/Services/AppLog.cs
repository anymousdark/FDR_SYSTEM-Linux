using FdrSystem.Core;

namespace FdrSystem.App.Services;

/// <summary>Registo em ficheiro por dia, como os logs do WPF (%LOCALAPPDATA% → XDG).</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string CurrentFile =>
        Path.Combine(AppPaths.LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Write(string level, string message, string? category = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                Cleanup();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}]{(category is null ? "" : " [" + category + "]")} {message}";
                File.AppendAllText(CurrentFile, line + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // logging nunca deve derrubar a app
        }
    }

    public static void Info(string message, string? category = null) => Write("INFO", message, category);

    public static void Warn(string message, string? category = null) => Write("AVISO", message, category);

    public static void Error(string message, string? category = null) => Write("ERRO", message, category);

    private static void Cleanup()
    {
        var limit = DateTime.Now.AddDays(-14);
        foreach (var file in Directory.EnumerateFiles(AppPaths.LogDirectory, "app-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < limit) File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }
}
