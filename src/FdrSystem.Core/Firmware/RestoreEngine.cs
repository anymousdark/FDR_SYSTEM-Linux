using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using FdrSystem.Core.Tools;

namespace FdrSystem.Core.Firmware;

/// <summary>Informação lida de um IPSW com <c>idevicerestore --ipsw-info</c>.</summary>
public sealed record IpswInspection(string? ProductType, string? Version, string? Build, string RawOutput)
{
    /// <summary>Modelos citados no nome do ficheiro (ex.: <c>iPhone14,5</c>).</summary>
    public static List<string> ModelsInFileName(string fileName) =>
        Regex.Matches(fileName, @"(?:iPhone|iPad|iPod)\d+,\d+")
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>
/// Progresso do restauro: barra 1 (preparação/extração do IPSW) e barra 2
/// (gravação no iPhone), com a fase e o detalhe em português.
/// </summary>
public sealed record RestoreProgress(
    double PrepPercent,
    double InstallPercent,
    string Phase,
    string Detail,
    string Line);

/// <summary>Resultado do motor: código de saída e se alguma linha era ERROR.</summary>
public sealed record RestoreResult(int ExitCode, bool HadError)
{
    public bool Ok => ExitCode == 0 && !HadError;
}

/// <summary>
/// Corre o <c>idevicerestore</c> do sistema e traduz a saída em progresso.
/// Mesmo formato do projeto Windows: linhas <c>progress: &lt;passo&gt; &lt;fração&gt;</c>
/// e <c>Etiqueta: valor</c>. A app não executa <c>sudo</c>: se faltar permissão,
/// a UI mostra o erro e o comando.
/// </summary>
public sealed class RestoreEngine(ToolRunner tools)
{
    // O idevicerestore escreve "progress: 1 0.25" (passo + fração 0..1).
    private static readonly Regex StepRe =
        new(@"^progress:\s*(\d+)\s+([0-9.]+)\s*$", RegexOptions.Compiled);

    // Linhas do tipo "Restoring image: 42.5" ou "IPSW: 100".
    private static readonly Regex LabelRe =
        new(@"^([A-Za-z0-9_ ]+):\s+([0-9.]+)\s*$", RegexOptions.Compiled);

    public bool HasEngine => tools.Has("idevicerestore");

    public string? EnginePath => tools.Locate("idevicerestore");

    /// <summary>Lê o cabeçalho do IPSW sem tocar no aparelho.</summary>
    public async Task<IpswInspection?> InspectAsync(string ipswPath, CancellationToken ct = default)
    {
        var run = await tools.RunAsync("idevicerestore", ["--ipsw-info", ipswPath], TimeSpan.FromMinutes(2), ct)
            .ConfigureAwait(false);

        if (run is null) return null;

        var text = run.StdOut + "\n" + run.StdErr;
        return new IpswInspection(
            FindValue(text, "Product Type", "ProductType", "Device") ?? FindFirstModel(text),
            FindValue(text, "Product Version", "Version"),
            FindValue(text, "Build", "BuildVersion"),
            text);
    }

    /// <summary>
    /// O <c>idevicerestore --ipsw-info</c> lista os modelos em linhas depois de
    /// <c>Supported Product Types:</c>. Devolve o primeiro identificador.
    /// </summary>
    private static string? FindFirstModel(string text)
    {
        var inList = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith("Supported Product Types", StringComparison.OrdinalIgnoreCase))
            {
                inList = true;
                continue;
            }

            if (!inList) continue;
            if (line.Length == 0) continue;
            if (line.Contains(':')) break; // secção seguinte (Build Identities:)

            if (Regex.IsMatch(line, @"^(?:iPhone|iPad|iPod|AppleTV|AudioAccessory|Mac)\w*\d+,\d+$"))
                return line;
        }

        return null;
    }

    private static string? FindValue(string text, params string[] keys)
    {
        foreach (var key in keys)
        {
            var match = Regex.Match(text, $@"{Regex.Escape(key)}\s*[:=]\s*(\S+)", RegexOptions.IgnoreCase);
            if (!match.Success) continue;

            var value = match.Groups[1].Value.Trim();
            if (value is "(null)" or "null" or "unknown" or "Unknown") continue;
            return value;
        }
        return null;
    }

    /// <summary>
    /// Executa o restauro destrutivo (<c>--erase --no-input</c>) e reporta progresso.
    /// Devolve o código de saída e se houve linhas <c>ERROR</c>.
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(
        string ipswPath,
        IProgress<RestoreProgress> progress,
        CancellationToken ct = default)
    {
        var exe = EnginePath ?? throw new InvalidOperationException("idevicerestore não está instalado.");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        psi.ArgumentList.Add("--erase");
        psi.ArgumentList.Add("--no-input");
        psi.ArgumentList.Add(ipswPath);

        using var process = new Process { StartInfo = psi };

        var prep = 0.0;
        var install = 0.0;
        var phase = "A iniciar o motor de restauro";
        var detail = "A arrancar o idevicerestore…";
        var hadError = false;

        void Handle(string? raw)
        {
            if (raw is null) return;
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Length == 0) return;

            if (trimmed.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("ERROR:", StringComparison.Ordinal))
                hadError = true;

            var step = StepRe.Match(trimmed);
            if (step.Success)
            {
                ApplyStep(
                    int.Parse(step.Groups[1].Value, CultureInfo.InvariantCulture),
                    double.Parse(step.Groups[2].Value, CultureInfo.InvariantCulture),
                    ref prep, ref install, ref phase, ref detail);
            }
            else
            {
                var label = LabelRe.Match(trimmed);
                if (label.Success &&
                    double.TryParse(label.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var value))
                {
                    ApplyLabel(label.Groups[1].Value.Trim(), value, ref prep, ref install, ref phase, ref detail);
                }
                else if (trimmed.Length <= 160)
                {
                    detail = trimmed;
                }
            }

            progress.Report(new RestoreProgress(prep, install, phase, detail, line));
        }

        process.OutputDataReceived += (_, e) => Handle(e.Data);
        process.ErrorDataReceived += (_, e) => Handle(e.Data);

        if (!process.Start()) throw new InvalidOperationException($"Não foi possível iniciar {exe}.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* já terminou */ }
            throw;
        }

        var ok = process.ExitCode == 0 && !hadError;
        progress.Report(new RestoreProgress(
            ok ? 100 : prep,
            ok ? 100 : install,
            ok ? "Concluído" : "Terminou com erros",
            ok
                ? "O iPhone vai reiniciar no ecrã de configuração (bem-vindo)."
                : $"O motor saiu com o código {process.ExitCode}. Veja as linhas ERROR no registo.",
            $"idevicerestore terminou com o código {process.ExitCode}."));

        return new RestoreResult(process.ExitCode, hadError);
    }

    /// <summary>Traduz <c>progress: &lt;passo&gt; &lt;fração&gt;</c> (igual ao Windows).</summary>
    private static void ApplyStep(
        int step,
        double fraction,
        ref double prep,
        ref double install,
        ref string phase,
        ref string detail)
    {
        var percent = Math.Clamp(fraction * 100.0, 0, 100);

        switch (step)
        {
            case 0:
                phase = "A procurar o iPhone";
                detail = "Ligue o iPhone por USB (normal, recovery ou DFU).";
                break;

            case 1:
                prep = Math.Max(prep, percent);
                phase = "A preparar o firmware";
                detail = percent >= 99.5
                    ? "Extração concluída."
                    : "A extrair componentes do IPSW (pode demorar alguns minutos).";
                break;

            case 2:
                install = Math.Max(install, percent);
                phase = "A gravar no iPhone";
                detail = "A enviar o sistema de ficheiros — a fase mais demorada.";
                break;

            case 3:
                install = Math.Max(install, percent);
                phase = "A verificar";
                detail = "A verificar os ficheiros enviados ao iPhone.";
                break;

            case 4:
                install = Math.Max(install, percent);
                phase = "A gravar o firmware";
                detail = "A gravar bootloader e firmware no aparelho.";
                break;

            case 5:
                install = Math.Max(install, percent);
                phase = "A gravar o modem";
                detail = "A atualizar o firmware do modem (baseband).";
                break;

            case 6:
                install = Math.Max(install, percent);
                phase = "A finalizar";
                detail = "A aplicar o complemento de firmware (FUD).";
                break;

            default:
                install = Math.Max(install, percent);
                phase = "A terminar";
                detail = "A enviar os últimos componentes.";
                break;
        }
    }

    /// <summary>Traduz linhas <c>Etiqueta: valor</c> (igual ao Windows).</summary>
    private static void ApplyLabel(
        string label,
        double value,
        ref double prep,
        ref double install,
        ref string phase,
        ref string detail)
    {
        var percent = value <= 1.0001 ? Math.Clamp(value * 100.0, 0, 100) : Math.Clamp(value, 0, 100);

        switch (label)
        {
            case "IPSW":
            case "SHA1":
                prep = Math.Max(prep, percent);
                break;

            case "Restoring image":
                install = Math.Max(install, percent);
                phase = "A gravar no iPhone";
                detail = "A restaurar a imagem do sistema.";
                break;

            case "Verifying restore":
                install = Math.Max(install, percent);
                phase = "A verificar";
                detail = "A verificar o restauro.";
                break;

            case "Flashing firmware":
                install = Math.Max(install, percent);
                phase = "A gravar o firmware";
                detail = "A gravar firmware no aparelho.";
                break;

            case "Updating baseband":
                install = Math.Max(install, percent);
                phase = "A gravar o modem";
                detail = "A atualizar o modem (baseband).";
                break;

            case "Creating partition map":
            case "Creating filesystem":
            case "Checking filesystems":
            case "Mounting filesystems":
            case "Fixing up /var":
                phase = "A preparar o armazenamento";
                detail = "A preparar o sistema de ficheiros do iPhone: " + label + ".";
                break;

            case "Rebooting device":
                phase = "A reiniciar";
                detail = "O iPhone está a reiniciar.";
                break;
        }
    }
}
