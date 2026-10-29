using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FdrSystem.App.Services;
using FdrSystem.Core;
using FdrSystem.Core.Firmware;
using FdrSystem.Core.Tools;
using FdrSystem.Core.Usb;

namespace FdrSystem.App.Views.Pages;

/// <summary>
/// Página Restauro: valida o IPSW, confirma o caracter destrutivo e corre o
/// <c>idevicerestore</c> do sistema com progresso e registo em direto.
/// </summary>
public partial class RestauroView : UserControl
{
    /// <summary>Tamanho mínimo plausível de um IPSW (os menores reais ~1 GB).</summary>
    private const long MinIpswBytes = 200L * 1024 * 1024;

    private readonly MainWindow? _owner;
    private readonly ToolRunner _tools;
    private readonly DownloadManager _downloads;
    private readonly RestoreEngine _engine;

    private string? _ipswPath;
    private IpswInspection? _inspection;
    private string? _deviceProductType;
    private bool _running;

    /// <summary>O último restauro já terminou: o estado final não deve ser substituído.</summary>
    private bool _completed;
    private CancellationTokenSource? _cts;
    private readonly StringBuilder _log = new();

    // Construtor público sem dependências para o carregador XAML/designer.
    public RestauroView() : this(null!, new ToolRunner(), new DownloadManager(AppPaths.FirmwareFolder))
    {
    }

    public RestauroView(MainWindow owner, ToolRunner tools, DownloadManager downloads)
    {
        InitializeComponent();
        _owner = owner;
        _tools = tools;
        _downloads = downloads;
        _engine = new RestoreEngine(tools);

        EngineStatusText.Text = _engine.HasEngine
            ? "Motor: " + _engine.EnginePath
            : "idevicerestore não instalado (sudo apt install idevicerestore)";

        AppendLog(_engine.HasEngine
            ? "idevicerestore encontrado: " + _engine.EnginePath
            : "idevicerestore NÃO encontrado. O restauro fica indisponível até instalar o pacote.");

        RefreshDevice();

        if (_owner?.PendingRestoreFile is { } pending)
            EnsureLoaded(pending);
    }

    /// <summary>Carrega um IPSW vindo da página Online.</summary>
    public void EnsureLoaded(string path)
    {
        if (_running) return;
        // A página Online chama isto ao navegar; não repetir se já está carregado.
        if (string.Equals(_ipswPath, path, StringComparison.Ordinal)) return;
        _ = LoadIpswAsync(path);
    }

    // ------------------------------------------------------------------ IPSW

    private async void Pick_Click(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Escolher firmware IPSW",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Firmware IPSW") { Patterns = ["*.ipsw"] }],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) await LoadIpswAsync(path);
    }

    private async Task LoadIpswAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                FileText.Text = "Ficheiro não encontrado: " + path;
                FileDetailText.Text = "";
                _ipswPath = null;
                _inspection = null;
                UpdateReadiness();
                return;
            }

            _ipswPath = path;
            _inspection = null;
            _completed = false;

            var info = new FileInfo(path);
            FileText.Text = info.Name;
            FileDetailText.Text = $"{DownloadItem.Fmt(info.Length)} · {path}";
            AppendLog($"IPSW escolhido: {path} ({DownloadItem.Fmt(info.Length)})");
            AppLog.Info("IPSW escolhido para restauro: " + path, "Restauro");

            if (info.Length < MinIpswBytes)
            {
                FileDetailText.Text += $" · ficheiro demasiado pequeno (mínimo {DownloadItem.Fmt(MinIpswBytes)})";
                AppendLog($"AVISO: {DownloadItem.Fmt(info.Length)} é pequeno demais para um IPSW real.");
                UpdateReadiness();
                return;
            }

            FileDetailText.Text += " · a ler o cabeçalho…";

            if (_engine.HasEngine)
            {
                _inspection = await _engine.InspectAsync(path, CancellationToken.None);
                if (_inspection is not null)
                {
                    var model = _inspection.ProductType
                                ?? string.Join(", ", IpswInspection.ModelsInFileName(info.Name));
                    var version = _inspection.Version is null ? "" : $" · iOS {_inspection.Version}";
                    var build = _inspection.Build is null ? "" : $" ({_inspection.Build})";

                    FileDetailText.Text = $"{DownloadItem.Fmt(info.Length)} · {model}{version}{build} · {path}";
                    AppendLog($"IPSW: {model}{version}{build}".TrimEnd());
                }
                else
                {
                    FileDetailText.Text = $"{DownloadItem.Fmt(info.Length)} · {path} · cabeçalho não reconhecido";
                    AppendLog("Aviso: não consegui ler o cabeçalho do IPSW.");
                }
            }

            UpdateReadiness();
        }
        catch (Exception ex)
        {
            FileText.Text = "Falha ao ler o IPSW: " + ex.Message;
            AppendLog("ERRO: " + ex.Message);
            AppLog.Error("ler IPSW: " + ex.Message, "Restauro");
            UpdateReadiness();
        }
    }

    private async void Verify_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_ipswPath) || !File.Exists(_ipswPath))
        {
            AppendLog("Não há IPSW para verificar.");
            return;
        }

        var model = IpswModel;
        if (model is null)
        {
            AppendLog("Sem modelo conhecido; não é possível comparar com o catálogo.");
            FileDetailText.Text = "Modelo desconhecido — não é possível comparar o SHA-256 com o catálogo.";
            return;
        }

        VerifyButton.IsEnabled = false;
        AppendLog("A calcular SHA-256 (pode demorar alguns minutos)…");

        try
        {
            var hash = await DownloadManager.ComputeSha256Async(_ipswPath);

            var expected = await FindCatalogShaAsync(model, _inspection?.Version);
            if (expected is null)
            {
                AppendLog($"SHA-256 calculado: {hash} (versão não encontrada no catálogo)");
                FileDetailText.Text = "SHA-256 calculado, sem referência no catálogo para comparar.";
            }
            else if (string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
            {
                AppendLog("SHA-256 confirmado ✓ — coincide com o catálogo.");
                FileDetailText.Text = "Integridade verificada (SHA-256 do catálogo).";
                AppLog.Info("SHA-256 do IPSW confirmado", "Restauro");
            }
            else
            {
                AppendLog("SHA-256 NÃO coincide com o catálogo. Não restaurar este ficheiro.");
                FileDetailText.Text = "SHA-256 NÃO coincide com o catálogo — ficheiro suspeito.";
                AppLog.Error("SHA-256 do IPSW não coincide com o catálogo", "Restauro");
            }
        }
        catch (Exception ex)
        {
            AppendLog("Falha ao verificar: " + ex.Message);
            AppLog.Error("verificar IPSW: " + ex.Message, "Restauro");
        }
        finally
        {
            VerifyButton.IsEnabled = true;
            UpdateReadiness();
        }
    }

    private static async Task<string?> FindCatalogShaAsync(string productType, string? version)
    {
        try
        {
            var info = await IpswCatalog.GetDeviceAsync(productType);
            var fw = version is null
                ? info.LatestSigned
                : info.Firmwares.FirstOrDefault(f => f.Version == version);
            return fw?.Sha256;
        }
        catch
        {
            return null;
        }
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folder = _downloads.Folder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{folder}\"") { UseShellExecute = false });
            AppendLog("A abrir " + folder);
        }
        catch (Exception ex)
        {
            AppendLog("Não foi possível abrir a pasta: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ aparelho

    private async void RefreshDevice_Click(object? sender, RoutedEventArgs e) => await RefreshDeviceAsync();

    private void RefreshDevice() => _ = RefreshDeviceAsync();

    private async Task RefreshDeviceAsync()
    {
        try
        {
            var usb = UsbScanner.FindAppleDevices();
            _deviceProductType = null;

            if (usb.Count == 0)
            {
                DeviceText.Text = "Nenhum iPhone detetado por USB.";
                CompatText.Text = "Liga o iPhone (normal, recovery ou DFU) e aceita «Confiar neste computador».";
                UpdateReadiness();
                return;
            }

            var device = usb[0];
            var run = await _tools.RunAsync("ideviceinfo", ["-k", "ProductType"], TimeSpan.FromSeconds(6));
            if (run is { Ok: true } && !string.IsNullOrWhiteSpace(run.StdOut))
                _deviceProductType = ParseProductType(run.StdOut);

            DeviceText.Text = _deviceProductType is null
                ? $"iPhone em modo {device.Mode} (USB)"
                : $"{_deviceProductType} em modo {device.Mode} (USB)";

            CompatText.Text = CompatibilityText();
            UpdateReadiness();
        }
        catch (Exception ex)
        {
            DeviceText.Text = "Falha ao ler o USB: " + ex.Message;
            AppLog.Error("deteção para restauro: " + ex.Message, "Restauro");
        }
    }

    private static string? ParseProductType(string raw)
    {
        var text = raw.Trim();
        if (text.Length is > 0 and < 40 && !text.Contains('<') && !text.Contains('\n')) return text;

        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"<key>ProductType</key>\s*<string>([^<]+)</string>");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>Modelo do IPSW: do cabeçalho ou, em alternativa, do nome do ficheiro.</summary>
    private string? IpswModel => _inspection?.ProductType
        ?? (_ipswPath is null
            ? null
            : IpswInspection.ModelsInFileName(Path.GetFileName(_ipswPath)).FirstOrDefault());

    /// <summary>
    /// O ficheiro é um IPSW plausível? Precisa de tamanho real e de um modelo
    /// identificável (cabeçalho ou nome), senão o motor falharia a meio.
    /// </summary>
    private bool IsPlausibleIpsw()
    {
        if (string.IsNullOrWhiteSpace(_ipswPath) || !File.Exists(_ipswPath)) return false;
        if (new FileInfo(_ipswPath).Length < MinIpswBytes) return false;
        return IpswModel is not null;
    }

    private string CompatibilityText()
    {
        var ipswModel = IpswModel;
        if (ipswModel is null)
            return "Compatibilidade por confirmar: o modelo do IPSW não foi identificado.";

        if (_deviceProductType is null)
            return $"IPSW para {ipswModel}. Ligado: nenhum iPhone identificado (o motor escolhe por USB).";

        return string.Equals(ipswModel, _deviceProductType, StringComparison.OrdinalIgnoreCase)
            ? $"Compatível: {ipswModel} corresponde ao aparelho ligado."
            : $"INCOMPATÍVEL: o IPSW é para {ipswModel} e o aparelho é {_deviceProductType}.";
    }

    private bool Compatible()
    {
        var ipswModel = IpswModel;
        if (ipswModel is null || _deviceProductType is null) return true; // o motor decide
        return string.Equals(ipswModel, _deviceProductType, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateReadiness()
    {
        var plausible = IsPlausibleIpsw();
        var hasEngine = _engine.HasEngine;
        var compatible = Compatible();

        StartButton.IsEnabled = plausible && hasEngine && compatible && !_running;
        VerifyButton.IsEnabled = plausible && !_running;

        // Depois de terminar, mantém «Concluído ✓» / «Terminou com erros» visível.
        if (_completed) return;

        if (!hasEngine)
        {
            PhaseText.Text = "Motor indisponível";
            DetailText.Text = "Instale o pacote «idevicerestore» para poder restaurar.";
        }
        else if (string.IsNullOrWhiteSpace(_ipswPath))
        {
            PhaseText.Text = "Em espera";
            DetailText.Text = "Escolha um IPSW completo para começar.";
        }
        else if (!plausible)
        {
            PhaseText.Text = "Firmware inválido";
            DetailText.Text = "O ficheiro não parece um IPSW completo (demasiado pequeno ou sem modelo identificável).";
        }
        else if (!compatible)
        {
            PhaseText.Text = "Firmware incompatível";
            DetailText.Text = $"Este IPSW é para {IpswModel}, mas o aparelho ligado é {_deviceProductType}.";
        }
        else if (!_running)
        {
            PhaseText.Text = "Pronto para restaurar";
            DetailText.Text = "Ação destrutiva: o conteúdo do iPhone será apagado.";
        }
    }

    // ------------------------------------------------------------------ restauro

    private async void Start_Click(object? sender, RoutedEventArgs e)
    {
        if (_ipswPath is null || !File.Exists(_ipswPath)) return;
        if (!IsPlausibleIpsw())
        {
            AppendLog("Recusado: o ficheiro não parece um IPSW completo.");
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var target = _deviceProductType ?? "aparelho ligado (o motor identifica por USB)";
        var ok = await Dialogs.ConfirmAsync(owner, "Confirmar restauro",
            "ATENÇÃO: o restauro APAGA todo o conteúdo do iPhone e repõe os dados de fábrica.\n\n"
            + $"Firmware: {Path.GetFileName(_ipswPath)}\nAparelho: {target}\n\n"
            + "Não remove o bloqueio de ativação (iCloud). Usa um aparelho de teste e faz backup.\n\nContinuar?",
            "Restaurar agora");

        if (!ok) return;

        await RunRestoreAsync();
    }

    private async Task RunRestoreAsync()
    {
        if (_ipswPath is null) return;

        _running = true;
        _completed = false;
        _cts = new CancellationTokenSource();
        PrepBar.Value = 0;
        InstallBar.Value = 0;
        PrepPercentText.Text = "—";
        InstallPercentText.Text = "—";
        PhaseText.Text = "A iniciar…";
        DetailText.Text = "A arrancar o motor de restauro.";
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        PickButton.IsEnabled = false;
        _log.Clear();
        AppendLog("=== restauro iniciado ===");
        AppendLog($"comando: idevicerestore --erase --no-input \"{_ipswPath}\"");
        AppLog.Info("restauro iniciado: " + _ipswPath, "Restauro");

        var progress = new Progress<RestoreProgress>(p =>
        {
            PrepBar.Value = p.PrepPercent;
            InstallBar.Value = p.InstallPercent;
            PrepPercentText.Text = p.PrepPercent > 0 ? $"{p.PrepPercent:0}%" : "—";
            InstallPercentText.Text = p.InstallPercent > 0 ? $"{p.InstallPercent:0}%" : "—";
            PhaseText.Text = p.Phase;
            DetailText.Text = p.Detail;
            AppendLog(p.Line);
        });

        try
        {
            var result = await _engine.RestoreAsync(_ipswPath, progress, _cts.Token);

            if (result.Ok)
            {
                PrepBar.Value = 100;
                InstallBar.Value = 100;
                PrepPercentText.Text = "100%";
                InstallPercentText.Text = "100%";
                PhaseText.Text = "Concluído ✓";
                DetailText.Text = "O iPhone vai reiniciar no ecrã de configuração (bem-vindo).";
                AppendLog("Restauro concluído com sucesso (código 0).");
                AppLog.Info("restauro concluído (código 0)", "Restauro");
            }
            else
            {
                PhaseText.Text = "Terminou com erros";
                DetailText.Text = $"O motor saiu com o código {result.ExitCode}"
                    + (result.HadError ? " e escreveu linhas ERROR no registo." : ".");
                AppendLog($"Restauro FALHOU (código {result.ExitCode}). Pode tentar novamente.");
                AppLog.Error($"restauro terminou com código {result.ExitCode}", "Restauro");
            }
        }
        catch (OperationCanceledException)
        {
            PhaseText.Text = "Cancelado";
            DetailText.Text = "O restauro foi cancelado. O iPhone pode ficar em recovery/DFU.";
            AppendLog("=== cancelado ===");
            AppLog.Warn("restauro cancelado", "Restauro");
        }
        catch (Exception ex)
        {
            PhaseText.Text = "Falha";
            DetailText.Text = "Não foi possível executar o motor: " + ex.Message;
            AppendLog("ERRO: " + ex.Message);
            AppLog.Error("executar idevicerestore: " + ex.Message, "Restauro");
        }
        finally
        {
            _running = false;
            _completed = true;
            _cts?.Dispose();
            _cts = null;
            CancelButton.IsEnabled = false;
            PickButton.IsEnabled = true;
            UpdateReadiness();
        }
    }

    private async void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (!_running) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var ok = await Dialogs.ConfirmAsync(owner, "Cancelar restauro",
            "Cancelar a meio pode deixar o iPhone em recovery/DFU. Vais precisar de voltar a restaurar.\n\nCancelar mesmo?",
            "Cancelar restauro");

        if (!ok) return;

        AppendLog("A cancelar…");
        try { _cts?.Cancel(); } catch { /* já terminou */ }
    }

    private void AppendLog(string line)
    {
        void Do()
        {
            _log.AppendLine($"{DateTime.Now:HH:mm:ss}  {line}");
            LogText.Text = _log.ToString();
            LogText.CaretIndex = LogText.Text?.Length ?? 0;
        }

        if (Dispatcher.UIThread.CheckAccess()) Do();
        else Dispatcher.UIThread.Post(Do);
    }
}
