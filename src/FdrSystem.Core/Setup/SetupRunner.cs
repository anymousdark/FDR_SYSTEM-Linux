using System.Diagnostics;
using System.Text;

namespace FdrSystem.Core.Setup;

/// <summary>Resultado de uma instalação automática.</summary>
public sealed record SetupRun(int ExitCode, bool Cancelled, string ScriptPath)
{
    public bool Ok => !Cancelled && ExitCode == 0;

    /// <summary>Mensagem curta para a interface e para o log.</summary>
    public string Summary => Cancelled
        ? "Instalação cancelada pelo utilizador."
        : ExitCode switch
        {
            0 => "Dependências instaladas com sucesso.",
            126 => "Pedido de administrador cancelado (ou sem autorização no polkit).",
            127 => "Não foi possível autenticar: o agente polkit não respondeu.",
            1 => "O script terminou com erros — vê o registo.",
            _ => $"O script terminou com o código {ExitCode}.",
        };
}

/// <summary>
/// Corre o script de instalação através de <c>pkexec</c>, que pede a palavra-passe
/// ao agente polkit da sessão. A aplicação nunca lê nem guarda credenciais e não
/// corre <c>sudo</c> diretamente.
/// </summary>
public static class SetupRunner
{
    public static string ScriptFolder => Path.Combine(Path.GetTempPath(), "fdr-system");

    public static async Task<SetupRun> RunAsync(
        string script,
        Action<string> onLine,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(ScriptFolder);

        var path = Path.Combine(ScriptFolder, $"instalar-{DateTime.Now:yyyyMMdd-HHmmss}.sh");
        await File.WriteAllTextAsync(path, script.ReplaceLineEndings("\n"), new UTF8Encoding(false), ct)
            .ConfigureAwait(false);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "pkexec",
            WorkingDirectory = ScriptFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        psi.ArgumentList.Add("/bin/bash");
        psi.ArgumentList.Add(path);

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) onLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) onLine(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException("Não foi possível iniciar o pkexec.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            process.WaitForExit();
            return new SetupRun(process.ExitCode, false, path);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new SetupRun(-1, true, path);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // o processo já terminou
        }
    }
}
