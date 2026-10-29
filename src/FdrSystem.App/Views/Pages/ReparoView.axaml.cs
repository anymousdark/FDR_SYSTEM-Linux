using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FdrSystem.App.Services;
using FdrSystem.Core.Devices;
using FdrSystem.Core.Tools;
using FdrSystem.Core.Usb;

namespace FdrSystem.App.Views.Pages;

/// <summary>
/// Página Reparo: mostra o modo (normal/recovery/DFU), entra e sai de recovery,
/// reinicia e lê o estado de ativação/MDM — sempre sem contornar bloqueios.
/// Nenhuma ação destrutiva acontece sem confirmação; a app nunca corre <c>sudo</c>.
/// </summary>
public partial class ReparoView : UserControl
{
    private readonly MainWindow? _owner;
    private readonly ToolRunner _tools;
    private readonly StringBuilder _log = new();

    private UsbDevice? _usb;
    private AppleUsb.RecoveryIds? _ids;
    private string? _udid;
    private bool _busy;

    // Construtor público sem dependências para o carregador XAML/designer.
    public ReparoView() : this(null!, new ToolRunner())
    {
    }

    public ReparoView(MainWindow owner, ToolRunner tools)
    {
        InitializeComponent();
        _owner = owner;
        _tools = tools;

        UpdateToolsStatus();
        _ = RefreshAsync();

    }

    /// <summary>Chamado pelo MainWindow ao abrir a página.</summary>
    public void EnsureRefreshed()
    {
        if (_busy) return;
        _ = RefreshAsync();
    }

    // ------------------------------------------------------------------ ferramentas

    private static readonly string[] RequiredTools =
    [
        "idevice_id",
        "ideviceinfo",
        "ideviceenterrecovery",
        "idevicediagnostics",
        "irecovery",
        "ideviceactivation",
    ];

    private void UpdateToolsStatus()
    {
        var missing = new List<string>();
        var present = new List<string>();

        foreach (var tool in RequiredTools)
        {
            if (_tools.Has(tool)) present.Add(tool);
            else missing.Add(tool);
        }

        ToolsStatusText.Text = missing.Count == 0
            ? "Todas as ferramentas presentes: " + string.Join(", ", present) + "."
            : "Presentes: " + (present.Count == 0 ? "nenhuma" : string.Join(", ", present))
              + ".\nEm falta: " + string.Join(", ", missing) + "."
              + (missing.Contains("irecovery") ? "\n«irecovery» vem do pacote libirecovery." : "")
              + (missing.Contains("ideviceactivation") ? "\n«ideviceactivation» vem do libimobiledevice." : "");
    }

    // ------------------------------------------------------------------ modo

    private async void Mode_Click(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        RModeButton.IsEnabled = false;

        try
        {
            var usb = UsbScanner.FindAppleDevices();
            _usb = usb.Count > 0 ? usb[0] : null;
            _udid = null;
            _ids = null;

            if (_usb is null)
            {
                ReparoModeText.Text = "Modo: sem aparelho";
                ReparoDeviceText.Text = "Nenhum iPhone detetado por USB. Liga o cabo e volta a tentar.";
                ResetDeviceInfo();
                UpdateButtons();
                return;
            }

            var mode = _usb.Mode;
            _ids = AppleUsb.ParseRecoveryIds(_usb.Serial ?? _usb.Path);

            HeaderStatusText.Text = $"iPhone detetado · {mode}";
            ReparoModeText.Text = $"Modo: {ModeLabel(mode)}";
            AppendLog($"aparelho detetado em modo {mode} (VID 0x{_usb.VendorId:X4} PID 0x{_usb.ProductId:X4})");

            if (mode == "Normal")
                await ReadNormalAsync();
            else
                ReadRecovery();

            UpdateButtons();
        }
        catch (Exception ex)
        {
            HeaderStatusText.Text = "Falha ao ler o USB";
            AppendLog("ERRO: " + ex.Message);
            AppLog.Error("reparo: ler USB: " + ex.Message, "Reparo");
        }
        finally
        {
            _busy = false;
            RModeButton.IsEnabled = true;
        }
    }

    private async Task ReadNormalAsync()
    {
        var type = await _tools.RunAsync("ideviceinfo", ["-k", "ProductType"], TimeSpan.FromSeconds(6));
        var version = await _tools.RunAsync("ideviceinfo", ["-k", "ProductVersion"], TimeSpan.FromSeconds(6));
        var ids = await _tools.RunAsync("idevice_id", ["-l"], TimeSpan.FromSeconds(6));

        var productType = CleanValue(type?.StdOut);
        var productVersion = CleanValue(version?.StdOut);
        _udid = ids is { Ok: true }
            ? ids.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(AppleUsb.LooksLikeUdid)
            : null;

        ReparoDeviceText.Text = productType is null
            ? "Aparelho em modo normal, mas os dados não estão acessíveis. Desbloqueia e aceita «Confiar neste computador»."
            : $"{productType}" + (productVersion is null ? "" : $" · iOS {productVersion}")
              + " · emparelhado";

        RecHintText.Text = "Modo normal: estes dados vêm do sistema, pelo usbmuxd.";
        RecModelText.Text = "Modelo: " + (productType ?? "—");
        RecSerialText.Text = "Serial: " + (_ids?.Serial ?? "—");
        RecEcidText.Text = "ECID: " + (_ids?.EcidDec ?? _ids?.EcidHex ?? "—");
        RecChipText.Text = "CPU/Board: " + FormatChip(_ids);
        RecIbootText.Text = "iBoot: " + (_ids?.Srtg ?? "—");

        if (productType is not null)
            AppendLog($"normal: {productType}" + (productVersion is null ? "" : $" iOS {productVersion}"));
        else
            AppendLog("normal: sem dados (por emparelhar ou bloqueado)");
    }

    private void ReadRecovery()
    {
        var ids = _ids;
        string? modelName = null, productType = null;

        if (ids is not null && IrecoveryDevices.TryResolve(ids.Cpid, ids.Bdid, out var pt, out var nm))
        {
            productType = pt;
            modelName = nm;
        }

        ReparoDeviceText.Text = _usb is null
            ? "—"
            : $"Modo {_usb.Mode}: sem emparelhamento, os dados vêm do USB.";

        RecHintText.Text = $"Modo {_usb?.Mode}: dados lidos do USB (CPID/BDID/ECID/iBoot).";

        var model = modelName is null
            ? "—"
            : productType is null ? modelName : $"{modelName} ({productType})";
        if (modelName is null && ids?.Cpid is not null)
            model = "iPhone (modelo não reconhecido)";

        RecModelText.Text = "Modelo: " + model;
        RecSerialText.Text = "Serial: " + (ids?.Serial ?? "—");
        // ECID completo é necessário para recovery; fica visível nesta página.
        RecEcidText.Text = "ECID: " + (ids?.EcidDec ?? ids?.EcidHex ?? "—");
        RecChipText.Text = "CPU/Board: " + FormatChip(ids);
        RecIbootText.Text = "iBoot: " + (ids?.Srtg ?? "—");

        AppendLog($"{_usb?.Mode}: modelo={model} ecid={ids?.EcidDec ?? "—"} cpid={ids?.Cpid ?? "—"} bdid={ids?.Bdid ?? "—"}");
    }

    private void ResetDeviceInfo()
    {
        HeaderStatusText.Text = "Sem iPhone";
        RecModelText.Text = "Modelo: —";
        RecSerialText.Text = "Serial: —";
        RecEcidText.Text = "ECID: —";
        RecChipText.Text = "CPU/Board: —";
        RecIbootText.Text = "iBoot: —";
        RecHintText.Text = "Liga o iPhone (normal, recovery ou DFU) para ver os dados.";
    }

    private static string ModeLabel(string mode) => mode switch
    {
        "DFU" => "DFU (ecrã preto)",
        "Recovery" => "Recovery (cabo no ecrã)",
        _ => "Normal",
    };

    private static string FormatChip(AppleUsb.RecoveryIds? ids)
    {
        if (ids is null) return "—";

        var parts = new List<string>();
        if (ids.Cpid is not null) parts.Add("CPID 0x" + ids.Cpid);
        if (ids.Bdid is not null) parts.Add("BDID 0x" + ids.Bdid);
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    private static string? CleanValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = raw.Trim();
        if (text.Contains('<'))
        {
            var match = Regex.Match(text, @"<key>[^<]+</key>\s*<string>([^<]*)</string>");
            if (match.Success) text = match.Groups[1].Value.Trim();
        }

        return text.Length is > 0 and < 80 ? text : null;
    }

    private void UpdateButtons()
    {
        var mode = _usb?.Mode;
        var hasDevice = _usb is not null;

        RRecoveryButton.IsEnabled = hasDevice && mode == "Normal" && _tools.Has("ideviceenterrecovery");
        RExitRecoveryButton.IsEnabled = hasDevice && mode is "Recovery" or "DFU" && _tools.Has("irecovery");
        RRestartButton.IsEnabled = hasDevice && (mode == "Normal" && _tools.Has("idevicediagnostics")
                                                   || mode != "Normal" && _tools.Has("irecovery"));
    }

    // ------------------------------------------------------------------ ações

    private async void Recovery_Click(object? sender, RoutedEventArgs e)
    {
        if (_usb is null) return;
        if (!_tools.Has("ideviceenterrecovery"))
        {
            ActionStatusText.Text = "«ideviceenterrecovery» não está instalado (pacote libimobiledevice-utils).";
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var ok = await Dialogs.ConfirmAsync(owner, "Entrar em modo recovery",
            "O iPhone vai reiniciar para o modo recovery (aparece um cabo no ecrã).\n\n"
            + "Nenhum dado é apagado, mas para sair precisas de «irecovery -n» ou de reiniciar o aparelho.\n\nContinuar?",
            "Entrar em recovery");
        if (!ok) return;

        await RunAsync(
            "A entrar em modo recovery…",
            "ideviceenterrecovery",
            _udid is null ? [] : [_udid],
            TimeSpan.FromSeconds(40));
    }

    private async void ExitRecovery_Click(object? sender, RoutedEventArgs e)
    {
        if (_usb is null) return;
        if (!_tools.Has("irecovery"))
        {
            ActionStatusText.Text = "«irecovery» não está instalado. Instala o pacote libirecovery "
                                    + "ou sai do recovery com um reinício forçado.";
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var ok = await Dialogs.ConfirmAsync(owner, "Sair do modo recovery",
            "O iPhone vai reiniciar para o modo normal.\n\nSe estiveres a meio de um restauro, isso será interrompido.\n\nContinuar?",
            "Reiniciar agora");
        if (!ok) return;

        await RunAsync("A sair do recovery…", "irecovery", ["-n"], TimeSpan.FromSeconds(30));
    }

    private async void Restart_Click(object? sender, RoutedEventArgs e)
    {
        if (_usb is null) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var ok = await Dialogs.ConfirmAsync(owner, "Reiniciar dispositivo",
            "O iPhone vai reiniciar agora.\n\n"
            + "Desliga antes qualquer restauro em curso. Não há perda de dados por reiniciar.\n\nContinuar?",
            "Reiniciar");
        if (!ok) return;

        if (_usb.Mode == "Normal")
        {
            if (!_tools.Has("idevicediagnostics"))
            {
                ActionStatusText.Text = "«idevicediagnostics» não está instalado.";
                return;
            }

            await RunAsync("A reiniciar o iPhone…", "idevicediagnostics", ["restart"], TimeSpan.FromSeconds(40));
        }
        else
        {
            if (!_tools.Has("irecovery"))
            {
                ActionStatusText.Text = "Em recovery/DFU é preciso «irecovery» para reiniciar.";
                return;
            }

            await RunAsync("A reiniciar o iPhone…", "irecovery", ["-n"], TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>Corre uma ferramenta, regista a saída e explica falhas de permissão.</summary>
    private async Task RunAsync(string busyText, string tool, string[] args, TimeSpan timeout)
    {
        ActionStatusText.Text = busyText;
        AppendLog("$ " + tool + (args.Length == 0 ? "" : " " + string.Join(" ", args)));

        var run = await _tools.RunAsync(tool, args, timeout);

        if (run is null)
        {
            ActionStatusText.Text = $"«{tool}» não está instalado.";
            AppendLog($"ERRO: {tool} não encontrado.");
            AppLog.Warn($"{tool} não encontrado", "Reparo");
            return;
        }

        var output = (run.StdOut + "\n" + run.StdErr).Trim();
        if (output.Length > 0) AppendLog(output);

        if (run.Ok)
        {
            ActionStatusText.Text = "Concluído. O modo é relido dentro de instantes.";
            AppLog.Info($"{tool} terminou com código 0", "Reparo");

            await Task.Delay(3500);
            await RefreshAsync();
            return;
        }

        var permission = output.Contains("perm", StringComparison.OrdinalIgnoreCase)
                         || output.Contains("access denied", StringComparison.OrdinalIgnoreCase)
                         || output.Contains("unable to open", StringComparison.OrdinalIgnoreCase);

        ActionStatusText.Text = permission
            ? $"Falta permissão para «{tool}». A app não corre sudo — executa no terminal:\n"
              + "  sudo " + tool + (args.Length == 0 ? "" : " " + string.Join(" ", args))
            : $"«{tool}» terminou com código {run.ExitCode}. Vê o registo abaixo.";

        AppendLog($"código de saída {run.ExitCode}");
        AppLog.Error($"{tool} terminou com código {run.ExitCode}", "Reparo");
    }

    // ------------------------------------------------------------------ ativação

    private async void ActivationCheck_Click(object? sender, RoutedEventArgs e)
    {
        if (!_tools.Has("ideviceactivation"))
        {
            ActivationStateText.Text = "Estado de ativação: ferramenta ausente";
            IcloudStateText.Text = "Instala o pacote libimobiledevice (ideviceactivation) para ler o estado.";
            AppendLog("ERRO: ideviceactivation não encontrado.");
            return;
        }

        ActivationStateText.Text = "Estado de ativação: a consultar…";
        var run = await _tools.RunAsync("ideviceactivation", ["state"], TimeSpan.FromSeconds(20));

        if (run is null)
        {
            ActivationStateText.Text = "Estado de ativação: ferramenta ausente";
            return;
        }

        var output = (run.StdOut + "\n" + run.StdErr).Trim();
        if (output.Length > 0) AppendLog(output);

        var state = ParseState(output);

        if (!run.Ok)
        {
            ActivationStateText.Text = "Estado de ativação: não foi possível ler";
            IcloudStateText.Text = "O iPhone tem de estar no ecrã de ativação (ou ligado e desbloqueado). "
                                   + "Isto é só leitura: a app não ativa o aparelho.";
            AppendLog("ideviceactivation state: código " + run.ExitCode);
            return;
        }

        ActivationStateText.Text = "Estado de ativação: " + (state ?? "desconhecido");
        IcloudStateText.Text = state switch
        {
            "Activated" => "O aparelho reporta-se ativado.",
            "Unactivated" => "O aparelho não está ativado. Se no ecrã pedir uma conta Apple, está "
                             + "associado a essa conta — só a conta original o ativa (a app não contorna).",
            _ => "Bloqueio de ativação (iCloud): não é possível confirmar só por leitura.",
        };

        AppLog.Info("estado de ativação: " + (state ?? "desconhecido"), "Reparo");
    }

    /// <summary>Extrai <c>State: X</c> da saída do ideviceactivation.</summary>
    private static string? ParseState(string output)
    {
        var match = Regex.Match(output, @"State\s*[:=]\s*(\S+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    // ------------------------------------------------------------------ MDM

    private async void MdmCheck_Click(object? sender, RoutedEventArgs e)
    {
        if (!_tools.Has("ideviceinfo"))
        {
            MdmStatusText.Text = "Estado de gestão: ferramenta ausente";
            MdmDetailsText.Text = "«ideviceinfo» não está instalado.";
            return;
        }

        MdmStatusText.Text = "Estado de gestão: a consultar…";
        MdmDetailsText.Text = "A ler as chaves públicas do aparelho (só leitura).";

        var run = await _tools.RunAsync("ideviceinfo", ["-x"], TimeSpan.FromSeconds(20));

        if (run is null)
        {
            MdmStatusText.Text = "Estado de gestão: ferramenta ausente";
            return;
        }

        if (!run.Ok)
        {
            MdmStatusText.Text = "Estado de gestão: não foi possível ler";
            MdmDetailsText.Text = "O aparelho tem de estar ligado, desbloqueado e emparelhado. "
                                  + "Sem emparelhamento o USB não expõe as chaves.";
            AppendLog("ideviceinfo -x: código " + run.ExitCode);
            return;
        }

        var xml = run.StdOut;
        var findings = new List<string>();

        var supervised = FindBool(xml, "Supervised") ?? FindBool(xml, "IsSupervised");
        if (supervised is not null)
            findings.Add("Supervised = " + (supervised.Value ? "sim" : "não"));

        foreach (var key in MdmKeys(xml))
        {
            var value = FindString(xml, key) ?? FindInteger(xml, key) ?? FindBool(xml, key)?.ToString();
            findings.Add(value is null ? key : $"{key} = {value}");
        }

        var hasMdmKey = findings.Any(f => f.Contains("MDM", StringComparison.OrdinalIgnoreCase)
                                           || f.Contains("CloudConfig", StringComparison.OrdinalIgnoreCase));

        if (supervised == true)
            MdmStatusText.Text = "Estado de gestão: aparelho SUPERVISIONADO";
        else if (hasMdmKey)
            MdmStatusText.Text = "Estado de gestão: sinais de MDM";
        else
            MdmStatusText.Text = "Estado de gestão: sem sinais nas chaves públicas";

        MdmDetailsText.Text = findings.Count == 0
            ? "Nenhuma chave de supervisão ou MDM apareceu. Isto não prova que o aparelho não está "
              + "gerido — só que o USB não o revela."
            : string.Join("\n", findings.Take(8))
              + "\n\nDeteção apenas: a app não remove perfis, não limpa MDM e não salta o Bloqueio de Ativação.";

        AppendLog("MDM: " + MdmStatusText.Text);
        AppLog.Info("verificação de gestão: " + MdmStatusText.Text, "Reparo");
    }

    /// <summary>Chaves de gestão que o iOS expõe via <c>ideviceinfo</c>.</summary>
    private static IEnumerable<string> MdmKeys(string xml)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(xml, @"<key>\s*([^<]+?)\s*</key>"))
        {
            var key = match.Groups[1].Value;
            if (!seen.Add(key)) continue;
            if (key.Equals("Supervised", StringComparison.OrdinalIgnoreCase)
                || key.Equals("IsSupervised", StringComparison.OrdinalIgnoreCase)) continue;

            if (key.Contains("MDM", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Supervis", StringComparison.OrdinalIgnoreCase)
                || key.Contains("CloudConfig", StringComparison.OrdinalIgnoreCase)
                || key.Contains("SetupState", StringComparison.OrdinalIgnoreCase)
                || key.Contains("AwaitingConfiguration", StringComparison.OrdinalIgnoreCase))
                yield return key;
        }
    }

    private static bool? FindBool(string xml, string key)
    {
        var match = Regex.Match(xml,
            $@"<key>\s*{Regex.Escape(key)}\s*</key>\s*<(true|false)\s*/>", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return match.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindString(string xml, string key)
    {
        var match = Regex.Match(xml,
            $@"<key>\s*{Regex.Escape(key)}\s*</key>\s*<string>([^<]*)</string>", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? FindInteger(string xml, string key)
    {
        var match = Regex.Match(xml,
            $@"<key>\s*{Regex.Escape(key)}\s*</key>\s*<integer>([^<]*)</integer>", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    // ------------------------------------------------------------------ registo

    private void ClearLog_Click(object? sender, RoutedEventArgs e)
    {
        _log.Clear();
        LogText.Text = "";
    }

    private void AppendLog(string line)
    {
        void Do()
        {
            _log.AppendLine($"{DateTime.Now:HH:mm:ss}  {line}");
            LogText.Text = _log.ToString();

            // Rolar só o registo; mexer no caret fazia a página inteira saltar.
            LogText.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd();
        }

        if (Dispatcher.UIThread.CheckAccess()) Do();
        else Dispatcher.UIThread.Post(Do);
    }
}
