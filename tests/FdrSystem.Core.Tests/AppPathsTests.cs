using FdrSystem.Core;
using Xunit;

namespace FdrSystem.Core.Tests;

[CollectionDefinition("AppPaths", DisableParallelization = true)]
public sealed class AppPathsCollection;

[Collection("AppPaths")]
public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fdr-paths-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldData;
    private readonly string? _oldDocs;

    public AppPathsTests()
    {
        _oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        _oldDocs = Environment.GetEnvironmentVariable("XDG_DOCUMENTS_DIR");

        Directory.CreateDirectory(Path.Combine(_root, "data"));
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_root, "data"));
        Environment.SetEnvironmentVariable("XDG_DOCUMENTS_DIR", Path.Combine(_root, "docs"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldData);
        Environment.SetEnvironmentVariable("XDG_DOCUMENTS_DIR", _oldDocs);
        try { Directory.Delete(_root, recursive: true); } catch { /* melhor esforço */ }
    }

    [Fact]
    public void EnsureDataDirectories_MigratesLegacyDataAndReports()
    {
        var legacyData = Path.Combine(_root, "data", "FDR_SYSTEM");
        Directory.CreateDirectory(legacyData);
        File.WriteAllText(Path.Combine(legacyData, "settings.json"), """{"Theme":"Claro"}""");

        var legacyReports = Path.Combine(_root, "docs", "FDR_SYSTEM", "Relatorios");
        Directory.CreateDirectory(legacyReports);
        File.WriteAllText(Path.Combine(legacyReports, "relatorio.json"), "{}");

        AppPaths.EnsureDataDirectories();

        Assert.True(File.Exists(Path.Combine(_root, "data", "FDR-SYSTEM", "settings.json")));
        Assert.False(Directory.Exists(legacyData));
        Assert.True(File.Exists(Path.Combine(_root, "docs", "FDR-SYSTEM", "Relatorios", "relatorio.json")));
        Assert.False(Directory.Exists(legacyReports));
    }

    [Fact]
    public void EnsureDataDirectories_CopiesMissingSettingsWhenNewFolderExists()
    {
        var legacyData = Path.Combine(_root, "data", "FDR_SYSTEM");
        Directory.CreateDirectory(legacyData);
        File.WriteAllText(Path.Combine(legacyData, "settings.json"), """{"Theme":"Floresta"}""");

        Directory.CreateDirectory(Path.Combine(_root, "data", "FDR-SYSTEM"));

        AppPaths.EnsureDataDirectories();

        var settings = Path.Combine(_root, "data", "FDR-SYSTEM", "settings.json");
        Assert.True(File.Exists(settings));
        Assert.Contains("Floresta", File.ReadAllText(settings));
    }

    [Fact]
    public void EnsureDataDirectories_NeverOverwritesNewSettings()
    {
        var legacyData = Path.Combine(_root, "data", "FDR_SYSTEM");
        Directory.CreateDirectory(legacyData);
        File.WriteAllText(Path.Combine(legacyData, "settings.json"), """{"Theme":"Floresta"}""");

        var newData = Path.Combine(_root, "data", "FDR-SYSTEM");
        Directory.CreateDirectory(newData);
        File.WriteAllText(Path.Combine(newData, "settings.json"), """{"Theme":"Ametista"}""");

        AppPaths.EnsureDataDirectories();

        Assert.Contains("Ametista", File.ReadAllText(Path.Combine(newData, "settings.json")));
    }
}
