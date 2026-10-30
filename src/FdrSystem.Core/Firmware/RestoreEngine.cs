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

        var parser = new RestoreProgressParser();

        void Handle(string? raw)
        {
            if (parser.Apply(raw) is { } update) progress.Report(update);
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

        var ok = process.ExitCode == 0 && !parser.HadError;
        progress.Report(new RestoreProgress(
            ok ? 100 : parser.PrepPercent,
            ok ? 100 : parser.InstallPercent,
            ok ? "Concluído" : "Terminou com erros",
            ok
                ? "O iPhone vai reiniciar no ecrã de configuração (bem-vindo)."
                : $"O motor saiu com o código {process.ExitCode}. Veja as linhas ERROR no registo.",
            $"idevicerestore terminou com o código {process.ExitCode}."));

        return new RestoreResult(process.ExitCode, parser.HadError);
    }
}

/// <summary>
/// Traduz a saída do <c>idevicerestore</c> em duas barras de progresso.
/// Classe separada para poder ser testada sem lançar o processo.
/// </summary>
public sealed class RestoreProgressParser
{
    // O idevicerestore escreve "progress: 1 0.25" (passo + fração 0..1).
    private static readonly Regex StepRe =
        new(@"^progress:\s*(\d+)\s+([0-9.]+)\s*$", RegexOptions.Compiled);

    // Linhas do tipo "Restoring image: 42.5", "IPSW: 100" ou "Fixing up /var: 1".
    private static readonly Regex LabelRe =
        new(@"^([A-Za-z0-9_ /]+):\s+([0-9.]+)\s*$", RegexOptions.Compiled);

    public double PrepPercent { get; private set; }
    public double InstallPercent { get; private set; }
    public string Phase { get; private set; } = "A iniciar o motor de restauro";
    public string Detail { get; private set; } = "A arrancar o idevicerestore…";
    public bool HadError { get; private set; }

    /// <summary>Processa uma linha; devolve <c>null</c> para linhas vazias.</summary>
    public RestoreProgress? Apply(string? raw)
    {
        if (raw is null) return null;

        var line = raw.TrimEnd();
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return null;

        if (trimmed.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("ERROR:", StringComparison.Ordinal))
            HadError = true;

        var step = StepRe.Match(trimmed);
        if (step.Success)
        {
            ApplyStep(
                int.Parse(step.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(step.Groups[2].Value, CultureInfo.InvariantCulture));
        }
        else
        {
            var label = LabelRe.Match(trimmed);
            if (label.Success &&
                double.TryParse(label.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var value))
            {
                ApplyLabel(label.Groups[1].Value.Trim(), value);
            }
            else if (trimmed.Length <= 160)
            {
                Detail = trimmed;
            }
        }

        return new RestoreProgress(PrepPercent, InstallPercent, Phase, Detail, line);
    }

    /// <summary>Traduz <c>progress: &lt;passo&gt; &lt;fração&gt;</c> (igual ao Windows).</summary>
    private void ApplyStep(int step, double fraction)
    {
        var percent = Math.Clamp(fraction * 100.0, 0, 100);

        switch (step)
        {
            case 0:
                Phase = "A procurar o iPhone";
                Detail = "Ligue o iPhone por USB (normal, recovery ou DFU).";
                break;

            case 1:
                PrepPercent = Math.Max(PrepPercent, percent);
                Phase = "A preparar o firmware";
                Detail = percent >= 99.5
                    ? "Extração concluída."
                    : "A extrair componentes do IPSW (pode demorar alguns minutos).";
                break;

            case 2:
                InstallPercent = Math.Max(InstallPercent, percent);
                Phase = "A gravar no iPhone";
                Detail = "A enviar o sistema de ficheiros — a fase mais demorada.";
                break;

            case 3:
                InstallPercent = Math.Max(InstallPercent, percent);
                Phase = "A verificar";
                Detail = "A verificar os ficheiros enviados ao iPhone.";
                break;

            case 4:
                InstallPercent = Math.Max(InstallPercent, percent);
                Phase = "A gravar o firmware";
                Detail = "A gravar bootloader e firmware no aparelho.";
                break;

            case 5:
                InstallPercent = Math.Max(InstallPercent, percent);
                Phase = "A gravar o modem";
                Detail = "A atualizar o firmware do modem (baseband).";
                break;

            case 6:
                InstallPercent = Math.Max(InstallPercent, percent);
                Phase = "A finalizar";
                Detail = "A aplicar o complemento de firmware (FUD).";
                break;

            default:
                InstallPercent = Math.Max(InstallPercent, percent);
                Phase = "A terminar";
                Detail = "A enviar os últimos componentes.";
                break;
        }
    }

    /// <summary>Traduz linhas <c>Etiqueta: valor</c> (igual ao Windows).</summary>
    private void ApplyLabel(string label, double value)
    {
        var percent = value <= 1.0001 ? Math.Clamp(value * 100.0, 0, 100) : Math.Clamp(value, 0, 100);

        switch (label)
        {
            case "IPSW":
            case "SHA1":
                PrepPercent = Math.Max(PrepPercent, percent);
                break;

            case "Restoring image":
                InstallPercent = Math.Max(InstallPercent, percent);
                Phase = "A gravar no iPhone";
                Detail = "A restaurar a imagem do sistema.";
                break;

            case "Verifying restore":
                InstallPercent = Math.Max(InstallPercent, percent);
                Phase = "A verificar";
                Detail = "A verificar o restauro.";
                break;

            case "Flashing firmware":
                InstallPercent = Math.Max(InstallPercent, percent);
                Phase = "A gravar o firmware";
                Detail = "A gravar firmware no aparelho.";
                break;

            case "Updating baseband":
                InstallPercent = Math.Max(InstallPercent, percent);
                Phase = "A gravar o modem";
                Detail = "A atualizar o modem (baseband).";
                break;

            case "Creating partition map":
            case "Creating filesystem":
            case "Checking filesystems":
            case "Mounting filesystems":
            case "Fixing up /var":
                Phase = "A preparar o armazenamento";
                Detail = "A preparar o sistema de ficheiros do iPhone: " + label + ".";
                break;

            case "Rebooting device":
                Phase = "A reiniciar";
                Detail = "O iPhone está a reiniciar.";
                break;
        }
    }
}
