using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FdrSystem.App.Services;
using FdrSystem.Core;
using FdrSystem.Core.Tools;

namespace FdrSystem.App.Views.Pages;

public partial class DiagnosticoView : UserControl
{
    private const int SyslogMaxLines = 2600;
    private const int SyslogMaxVisible = 1600;
    private const int SyslogMaxChars = 160_000;

    private readonly MainWindow _owner;
    private readonly ToolRunner _tools;
    private readonly List<string> _syslogLines = [];
    private readonly ConcurrentQueue<string> _syslogQueue = new();
    private readonly DispatcherTimer _syslogTimer;

    private Process? _syslogProcess;
    private string? _lastReportFolder;

    // Construtor público sem dependências para o carregador XAML/designer.
    // A aplicação usa sempre o construtor com owner e ToolRunner.
    public DiagnosticoView() : this(null!, new ToolRunner())
    {
    }

    public DiagnosticoView(MainWindow owner, ToolRunner tools)
    {
        InitializeComponent();
        _owner = owner;
        _tools = tools;

        _syslogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _syslogTimer.Tick += (_, _) => DrainSyslog();

        UpdateToolsStatus();
    }

    /// <summary>Para o processo de syslog quando a janela principal fecha.</summary>
    public void Shutdown() => StopSyslog(null, log: false);

    private void UpdateToolsStatus()
    {
        var required = new[] { "idevicesyslog", "idevicecrashreport", "idevicepair", "ideviceprovision" };
        var missing = required.Where(tool => !_tools.Has(tool)).ToArray();

        if (missing.Length == 0)
        {
            ToolsStatusText.Text = "Ferramentas libimobiledevice: OK";
            ToolsStatusText.Foreground = Application.Current?.FindResource("GreenText") as IBrush;
        }
        else
        {
            ToolsStatusText.Text = "Ferramentas em falta: " + string.Join(", ", missing);
            ToolsStatusText.Foreground = Application.Current?.FindResource("AmberText") as IBrush;
        }
    }

    // ---------------------------------------------------------------- syslog

    private void SyslogToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (_syslogProcess is { HasExited: false })
        {
            StopSyslog("Registo parado. As linhas ficam no ecrã até limpar.");
            return;
        }

        StartSyslog();
    }

    private void StartSyslog()
    {
        var exe = _tools.Locate("idevicesyslog");
        if (exe is null)
        {
            SyslogStatusText.Text = "Ferramenta em falta: idevicesyslog. Instala libimobiledevice-utils.";
            return;
        }

        try
        {
            StopSyslog(null, log: false);

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
            psi.ArgumentList.Add("--no-colors");
            psi.ArgumentList.Add("-x"); // termina quando o iPhone se desliga

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null) _syslogQueue.Enqueue(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null) _syslogQueue.Enqueue("[stderr] " + args.Data);
            };
            process.Exited += (_, _) => Dispatcher.UIThread.Post(() => OnSyslogExited(process));

            if (!process.Start())
            {
                process.Dispose();
                SyslogStatusText.Text = "Não foi possível iniciar o registo.";
                return;
            }

            _syslogProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _syslogTimer.Start();

            AddSyslogLine($"--- Sessão iniciada em {DateTime.Now:dd/MM/yyyy HH:mm:ss} ---");
            UpdateSyslogUi(true);
            SyslogStatusText.Text = "A ouvir o registo. Se o iPhone desligar, o registo termina sozinho.";
            AppLog.Info("idevicesyslog iniciado.", "Diagnóstico");
        }
        catch (Exception ex)
        {
            SyslogStatusText.Text = "Falhou: " + ex.Message;
            AppLog.Error("idevicesyslog: " + ex.Message, "Diagnóstico");
        }
    }

    private void OnSyslogExited(Process process)
    {
        if (!ReferenceEquals(_syslogProcess, process)) return;

        _syslogProcess = null;
        _syslogTimer.Stop();
        DrainSyslog();
        UpdateSyslogUi(false);

        var noDevice = _syslogLines.TakeLast(8)
            .Any(line => line.Contains("No device found", StringComparison.OrdinalIgnoreCase));
        SyslogStatusText.Text = noDevice
            ? "Nenhum iPhone detetado. Liga-o por USB, desbloqueia e tenta outra vez."
            : "O registo terminou (iPhone desligado ou ferramenta fechada).";

        try { process.Dispose(); } catch { }
    }

    private void StopSyslog(string? message, bool log = true)
    {
        var process = _syslogProcess;
        _syslogProcess = null;
        _syslogTimer.Stop();

        if (process is not null)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { process.WaitForExit(2000); } catch { }
            try { process.Dispose(); } catch { }
        }

        DrainSyslog();
        UpdateSyslogUi(false);

        if (message is not null) SyslogStatusText.Text = message;
        if (log) AppLog.Info("idevicesyslog parado.", "Diagnóstico");
    }

    private void UpdateSyslogUi(bool running)
    {
        SyslogToggleText.Text = running ? "Parar" : "Começar a ouvir";
        SyslogSaveButton.IsEnabled = _syslogLines.Count > 0;
    }

    private void AddSyslogLine(string line)
    {
        _syslogLines.Add(line);
        TrimSyslogLines();

        if (!MatchesFilter(line)) return;
        AppendSyslogText(line + Environment.NewLine);
        SyslogSaveButton.IsEnabled = true;
    }

    private void DrainSyslog()
    {
        if (_syslogQueue.IsEmpty) return;

        var filter = SyslogFilterBox.Text?.Trim() ?? string.Empty;
        var appended = new StringBuilder();
        var added = false;

        while (_syslogQueue.TryDequeue(out var line))
        {
            _syslogLines.Add(line);
            added = true;
            if (filter.Length == 0 || line.Contains(filter, StringComparison.OrdinalIgnoreCase))
                appended.AppendLine(line);
        }

        if (!added) return;

        TrimSyslogLines();
        if (appended.Length > 0) AppendSyslogText(appended.ToString());
        SyslogSaveButton.IsEnabled = _syslogLines.Count > 0;
    }

    private void TrimSyslogLines()
    {
        if (_syslogLines.Count > SyslogMaxLines)
            _syslogLines.RemoveRange(0, _syslogLines.Count - SyslogMaxLines);
    }

    private void AppendSyslogText(string text)
    {
        if ((SyslogText.Text?.Length ?? 0) > SyslogMaxChars)
        {
            RebuildSyslogText();
            return;
        }

        SyslogText.Text = (SyslogText.Text ?? string.Empty) + text;
        SyslogText.CaretIndex = SyslogText.Text?.Length ?? 0;
    }

    private void RebuildSyslogText()
    {
        var visible = _syslogLines.Where(MatchesFilter).TakeLast(SyslogMaxVisible).ToList();
        SyslogText.Text = visible.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, visible) + Environment.NewLine;
        SyslogText.CaretIndex = SyslogText.Text?.Length ?? 0;
    }

    private bool MatchesFilter(string line)
    {
        var filter = SyslogFilterBox.Text?.Trim() ?? string.Empty;
        return filter.Length == 0 || line.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void SyslogFilter_Changed(object? sender, TextChangedEventArgs e)
    {
        if (SyslogText is null) return;
        RebuildSyslogText();

        var filter = SyslogFilterBox.Text?.Trim() ?? string.Empty;
        SyslogStatusText.Text = filter.Length == 0
            ? "A mostrar todas as linhas recolhidas."
            : $"A mostrar apenas linhas com «{filter}».";
    }

    private void SyslogClear_Click(object? sender, RoutedEventArgs e)
    {
        _syslogLines.Clear();
        while (_syslogQueue.TryDequeue(out _)) { }

        SyslogText.Text = string.Empty;
        SyslogSaveButton.IsEnabled = false;
        SyslogStatusText.Text = "Registo limpo.";
    }

    private async void SyslogSave_Click(object? sender, RoutedEventArgs e)
    {
        if (_syslogLines.Count == 0)
        {
            SyslogStatusText.Text = "Ainda não há linhas para guardar.";
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            SyslogStatusText.Text = "Não foi possível abrir o seletor de ficheiros.";
            return;
        }

        try
        {
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar registo do iOS",
                SuggestedFileName = $"syslog-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                DefaultExtension = "txt",
                FileTypeChoices =
                [
                    new FilePickerFileType("Ficheiro de texto") { Patterns = ["*.txt"] }
                ],
            });

            if (file is null) return;

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteLineAsync(
                $"FDR SYSTEM — registo do iOS — {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            await writer.WriteLineAsync();
            foreach (var line in _syslogLines)
                await writer.WriteLineAsync(line);

            SyslogStatusText.Text = "Registo guardado em " + file.Path.LocalPath;
            AppLog.Info("Registo do iOS guardado em " + file.Path.LocalPath, "Diagnóstico");
        }
        catch (Exception ex)
        {
            SyslogStatusText.Text = "Não foi possível guardar: " + ex.Message;
            AppLog.Error("guardar syslog: " + ex.Message, "Diagnóstico");
        }
    }

    // --------------------------------------------------------- crash reports

    private async void CrashCollect_Click(object? sender, RoutedEventArgs e)
    {
        if (!_tools.Has("idevicecrashreport"))
        {
            CrashStatusText.Text = "Ferramenta em falta: idevicecrashreport. Instala libimobiledevice-utils.";
            return;
        }

        CrashCollectButton.IsEnabled = false;
        CrashOpenButton.IsEnabled = false;
        CrashStatusText.Text = "A recolher relatórios do iPhone…";

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var folder = Path.Combine(AppPaths.ReportsFolder, "falhas-" + stamp);

        try
        {
            Directory.CreateDirectory(folder);
            var run = await _tools.RunAsync(
                "idevicecrashreport", ["-e", "-k", folder], TimeSpan.FromMinutes(3));

            if (run is null)
            {
                CrashStatusText.Text = "Ferramenta em falta: idevicecrashreport.";
                return;
            }

            var files = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                : [];

            if (files.Length == 0)
            {
                try { Directory.Delete(folder, true); } catch { }
                CrashStatusText.Text = run.Ok
                    ? "Não há relatórios de falhas no iPhone neste momento."
                    : "Não foi possível recolher: " + FirstLine(run.StdOut + run.StdErr);
                return;
            }

            var zip = Path.Combine(AppPaths.ReportsFolder, "falhas-" + stamp + ".zip");
            await Task.Run(() => ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Optimal, false));

            _lastReportFolder = folder;
            CrashOpenButton.IsEnabled = true;
            CrashStatusText.Text = $"{files.Length} ficheiro(s) guardados: {Path.GetFileName(zip)} (e na pasta falhas-{stamp}).";
            AppLog.Info($"Relatórios de falhas: {files.Length} ficheiro(s) em {folder}", "Diagnóstico");
        }
        catch (Exception ex)
        {
            CrashStatusText.Text = "Falhou: " + ex.Message;
            AppLog.Error("idevicecrashreport: " + ex.Message, "Diagnóstico");
        }
        finally
        {
            CrashCollectButton.IsEnabled = true;
        }
    }

    private void CrashOpen_Click(object? sender, RoutedEventArgs e)
    {
        var path = _lastReportFolder is not null && Directory.Exists(_lastReportFolder)
            ? _lastReportFolder
            : AppPaths.ReportsFolder;

        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashStatusText.Text = "Não foi possível abrir a pasta: " + ex.Message;
        }
    }

    // -------------------------------------------------------------- pairing

    private async void PairValidate_Click(object? sender, RoutedEventArgs e) =>
        await RunPairCommandAsync("validate");

    private async void PairRepair_Click(object? sender, RoutedEventArgs e) =>
        await RunPairCommandAsync("pair");

    private async Task RunPairCommandAsync(string command)
    {
        if (!_tools.Has("idevicepair"))
        {
            PairStatusText.Text = "Ferramenta em falta: idevicepair. Instala libimobiledevice-utils.";
            return;
        }

        PairValidateButton.IsEnabled = false;
        PairRepairButton.IsEnabled = false;
        PairOutputText.Text = string.Empty;
        PairStatusText.Text = command == "pair"
            ? "A emparelhar… Desbloqueia o iPhone e aceita «Confiar neste computador»."
            : "A validar o emparelhamento…";

        try
        {
            var run = await _tools.RunAsync(
                "idevicepair",
                [command],
                command == "pair" ? TimeSpan.FromSeconds(90) : TimeSpan.FromSeconds(30));

            if (run is null)
            {
                PairStatusText.Text = "Ferramenta em falta: idevicepair.";
                return;
            }

            var text = (run.StdOut + run.StdErr).Trim();
            PairOutputText.Text = text;

            if (text.Contains("No device", StringComparison.OrdinalIgnoreCase))
            {
                PairStatusText.Text = "Nenhum iPhone detetado. Liga-o por USB e desbloqueia.";
                return;
            }

            if (command == "validate")
            {
                var ok = run.Ok && text.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase);
                PairStatusText.Text = ok
                    ? "Emparelhamento válido — o iPhone já confia neste computador."
                    : "Ainda não há emparelhamento válido. Usa «Emparelhar» e aceita o pedido no iPhone.";
            }
            else
            {
                var ok = run.Ok &&
                         (text.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                          text.Contains("paired", StringComparison.OrdinalIgnoreCase));
                PairStatusText.Text = ok
                    ? "Emparelhado. Já podes ler o iPhone normalmente."
                    : "Não foi possível emparelhar. Desbloqueia o iPhone, aceita «Confiar» e tenta outra vez.";
            }
        }
        catch (Exception ex)
        {
            PairStatusText.Text = "Falhou: " + ex.Message;
            AppLog.Error("idevicepair: " + ex.Message, "Diagnóstico");
        }
        finally
        {
            PairValidateButton.IsEnabled = true;
            PairRepairButton.IsEnabled = true;
        }
    }

    // ----------------------------------------------------------- provisioning

    private async void ProvisionList_Click(object? sender, RoutedEventArgs e)
    {
        if (!_tools.Has("ideviceprovision"))
        {
            ProvisionStatusText.Text = "Ferramenta em falta: ideviceprovision. Instala libimobiledevice-utils.";
            return;
        }

        ProvisionListButton.IsEnabled = false;
        ProvisionCopyButton.IsEnabled = false;
        ProvisionOutputText.Text = "A listar…";
        ProvisionStatusText.Text = "A pedir a lista ao iPhone…";

        try
        {
            var run = await _tools.RunAsync("ideviceprovision", ["list"], TimeSpan.FromSeconds(30));
            if (run is null)
            {
                ProvisionStatusText.Text = "Ferramenta em falta: ideviceprovision.";
                return;
            }

            var text = (run.StdOut + run.StdErr).Trim();
            if (text.Contains("No device", StringComparison.OrdinalIgnoreCase))
            {
                ProvisionOutputText.Text = text;
                ProvisionStatusText.Text = "Nenhum iPhone detetado. Liga-o por USB e desbloqueia.";
                return;
            }

            var empty = text.Length == 0 ||
                        text.Contains("No provisioning profiles", StringComparison.OrdinalIgnoreCase);
            ProvisionOutputText.Text = empty ? "(sem perfis de aprovisionamento)" : text;
            ProvisionCopyButton.IsEnabled = !empty && run.Ok;
            ProvisionStatusText.Text = run.Ok
                ? (empty ? "O iPhone não tem perfis instalados." : "Lista obtida (só leitura).")
                : "Falhou: " + FirstLine(text);
        }
        catch (Exception ex)
        {
            ProvisionStatusText.Text = "Falhou: " + ex.Message;
            AppLog.Error("ideviceprovision: " + ex.Message, "Diagnóstico");
        }
        finally
        {
            ProvisionListButton.IsEnabled = true;
        }
    }

    private async void ProvisionCopy_Click(object? sender, RoutedEventArgs e)
    {
        var text = ProvisionOutputText.Text?.Trim() ?? string.Empty;
        if (text.Length == 0) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
            ProvisionStatusText.Text = "Lista copiada.";
        }
    }

    private static string FirstLine(string text)
    {
        var line = (text ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(line) ? "sem detalhes" : line;
    }
}
