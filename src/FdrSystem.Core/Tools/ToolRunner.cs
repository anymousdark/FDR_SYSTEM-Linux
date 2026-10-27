using System.Diagnostics;
using System.Text;

namespace FdrSystem.Core.Tools;

public sealed record ToolRun(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Executa as ferramentas de linha de comando do libimobiledevice.
/// No Linux vêm do pacote libimobiledevice-utils (ideviceinfo, idevicediagnostics,
/// ...); no Windows a app original trazia os .exe em tools\libimobiledevice.
/// </summary>
public sealed class ToolRunner
{
    private readonly List<string> _searchDirs = [];

    public ToolRunner(params string[] extraSearchDirs)
    {
        // 1) pasta da app (binário publicado)
        _searchDirs.Add(Path.Combine(AppContext.BaseDirectory, "tools", "linux"));
        _searchDirs.Add(Path.Combine(AppContext.BaseDirectory, "tools"));
        _searchDirs.Add(AppContext.BaseDirectory);
        _searchDirs.AddRange(extraSearchDirs);
    }

    public static string ExeName(string baseName) =>
        OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

    /// <summary>Devolve o caminho absoluto da ferramenta ou null se não existir.</summary>
    public string? Locate(string baseName)
    {
        var exe = ExeName(baseName);

        foreach (var dir in _searchDirs)
        {
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate)) return candidate;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    public bool Has(string baseName) => Locate(baseName) is not null;

    /// <summary>Corre a ferramenta; devolve null se não estiver instalada.</summary>
    public async Task<ToolRun?> RunAsync(
        string baseName,
        IEnumerable<string> args,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var exe = Locate(baseName);
        if (exe is null) return null;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidOperationException($"Não foi possível iniciar {exe}.");

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"A ferramenta {baseName} não respondeu em {timeout.TotalSeconds:0}s.");
        }

        return new ToolRun(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    public async Task<string> RunTextAsync(string baseName, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct = default)
    {
        var run = await RunAsync(baseName, args, timeout, ct).ConfigureAwait(false);
        return run?.StdOut ?? string.Empty;
    }
}
