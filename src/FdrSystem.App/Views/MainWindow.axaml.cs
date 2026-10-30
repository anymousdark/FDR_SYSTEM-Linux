using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FdrSystem.App.Services;
using FdrSystem.App.Views.Pages;
using FdrSystem.Core;
using FdrSystem.Core.Firmware;
using FdrSystem.Core.Tools;
using FdrSystem.Core.Usb;

namespace FdrSystem.App.Views;

public partial class MainWindow : Window
{
    private readonly ToolRunner _tools = new();
    private readonly DispatcherTimer _usbTimer;
    private readonly DownloadManager _downloads = new(AppPaths.FirmwareFolder)
    {
        Log = (channel, message, level) => AppLog.Write(level, message, channel),
    };

    private OfflineView? _offline;
    private OnlineView? _online;
    private FirmwareView? _firmware;
    private RestauroView? _restauro;
    private ReparoView? _reparo;
    private JailbreakView? _jailbreak;
    private DiagnosticoView? _diagnostico;
    private AcessibilidadeView? _acessibilidade;
    private DriverView? _driver;
    private InstalacaoView? _instalacao;
    private string? _lastUsbSignature;

    /// <summary>IPSW escolhido na página Online e entregue à página Restauro.</summary>
    public string? PendingRestoreFile { get; private set; }

    /// <summary>Motor de downloads partilhado por Online, Firmware e Restauro.</summary>
    public DownloadManager Downloads => _downloads;

    public MainWindow()
    {
        InitializeComponent();

        VersionText.Text = "v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "—");

        var themeIndex = Array.IndexOf(["Dark", "Light", "Midnight", "Forest", "Amethyst"], AppSettings.Current.Theme);
        ThemeManager.Apply(themeIndex >= 0 ? themeIndex : 0);

        ShowPage("offline");

        // A app abre sempre em Offline: o grupo de rádio pode mudar de estado
        // durante a construção da janela e não queremos herdar essa escolha.
        Loaded += (_, _) =>
        {
            NavOffline.IsChecked = true;
            ShowPage("offline");
        };

        _usbTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _usbTimer.Tick += (_, _) => { PollUsb(); UpdateNetBadge(); };
        _usbTimer.Start();

        Closing += (_, _) =>
        {
            _usbTimer.Stop();
            _diagnostico?.Shutdown();
            _acessibilidade?.Shutdown();
            _downloads.Dispose();
        };

        PollUsb();
        UpdateNetBadge();
    }

    /// <summary>Badge do cabeçalho: diz se há rede disponível (não faz pedidos próprios).</summary>
    private void UpdateNetBadge()
    {
        var online = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        NetBadgeText.Text = online ? "REDE" : "SEM REDE";
        NetBadge.Background = online
            ? Avalonia.Application.Current?.FindResource("GreenBoxBg") as Avalonia.Media.IBrush
            : Avalonia.Application.Current?.FindResource("BadgeBg") as Avalonia.Media.IBrush;
        NetBadgeText.Foreground = online
            ? Avalonia.Application.Current?.FindResource("GreenText") as Avalonia.Media.IBrush
            : Avalonia.Application.Current?.FindResource("BadgeFg") as Avalonia.Media.IBrush;
    }

    private void Nav_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        AppLog.Info("página -> " + tag, "Nav");
        PageHost.Content = tag switch
        {
            "offline" => _offline ??= new OfflineView(this, _tools),
            "online" => _online ??= new OnlineView(this, _tools, _downloads),
            "firmware" => _firmware ??= new FirmwareView(this, _downloads),
            "restauro" => _restauro ??= new RestauroView(this, _tools, _downloads),
            "reparo" => _reparo ??= new ReparoView(this, _tools),
            "jailbreak" => _jailbreak ??= new JailbreakView(_tools),
            "diagnostico" => _diagnostico ??= new DiagnosticoView(this, _tools),
            "acessibilidade" => _acessibilidade ??= new AcessibilidadeView(this, _tools),
            "driver" => _driver ??= new DriverView(this, _tools),
            "instalacao" => _instalacao ??= new InstalacaoView(this, _tools),
            _ => PlaceholderView.For(tag, this),
        };

        if (tag == "offline") _offline?.EnsureAutoRead();
        if (tag == "acessibilidade") _acessibilidade?.EnsureConnected();
        if (tag == "driver") _driver?.EnsureRefreshed();
        if (tag == "instalacao") _instalacao?.EnsureRefreshed();
        if (tag == "restauro" && PendingRestoreFile is { Length: > 0 } ipsw) _restauro?.EnsureLoaded(ipsw);
        if (tag == "reparo") _reparo?.EnsureRefreshed();
        if (tag == "jailbreak") _jailbreak?.EnsureRefreshed();
    }

    /// <summary>Muda de página a pedido de outra página (ex.: Online → Restauro).</summary>
    public void Navigate(string tag)
    {
        var nav = this.FindControl<RadioButton>("Nav" + char.ToUpperInvariant(tag[0]) + tag[1..]);
        if (nav is not null) nav.IsChecked = true;
        ShowPage(tag);
    }

    /// <summary>Guarda o IPSW validado na página Online e abre a página Restauro.</summary>
    public void RequestRestore(string firmwarePath)
    {
        PendingRestoreFile = firmwarePath;
        Navigate("restauro");
    }

    /// <summary>Chamado pelo timer: mostra o estado do USB e dispara a leitura automática.</summary>
    private void PollUsb()
    {
        var devices = UsbScanner.FindAppleDevices();
        var signature = string.Join("|", devices.Select(d => $"{d.ProductId:X4}:{d.Serial}"));

        if (devices.Count == 0)
        {
            SetStatus("Conecte o iPhone por USB — a leitura começa automaticamente.");
            _lastUsbSignature = null;
            return;
        }

        var device = devices[0];
        var mode = device.Mode;
        SetStatus(mode == "Normal"
            ? $"iPhone ligado (USB) — {(string.IsNullOrWhiteSpace(device.Product) ? "a ler…" : device.Product)}"
            : $"iPhone em {mode} — dados lidos diretamente do USB.");

        if (signature == _lastUsbSignature) return;
        _lastUsbSignature = signature;
        _offline?.EnsureAutoRead(force: true);
    }

    public void SetStatus(string text) => StatusText.Text = text;

    private void ThemeButton_Click(object? sender, RoutedEventArgs e)
    {
        ThemeManager.Cycle();
        SetStatus("Tema: " + ThemeManager.Names[ThemeManager.CurrentIndex] + ".");
    }

    private void LogsButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureDataDirectories();
            var target = Path.Combine(AppPaths.Documents, AppPaths.AppName, "Relatorios");
            Directory.CreateDirectory(target);
            var zip = Path.Combine(target, $"logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(AppPaths.LogDirectory, "app-*.log"))
                    archive.CreateEntryFromFile(file, Path.GetFileName(file));

                var summary = archive.CreateEntry("resumo.txt");
                using var writer = new StreamWriter(summary.Open());
                writer.WriteLine($"FDR_SYSTEM {typeof(MainWindow).Assembly.GetName().Version}");
                writer.WriteLine($"SO: {Environment.OSVersion}");
                writer.WriteLine($"Runtime: {Environment.Version}");
                writer.WriteLine($"Ferramentas: ideviceinfo={( _tools.Has("ideviceinfo") ? "ok" : "ausente")}");
                writer.WriteLine($"USB: {UsbScanner.DescribeEnvironment()}");
            }

            SetStatus($"Logs exportados para {zip}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("exportar logs: " + ex.Message, "Sistema");
            SetStatus("Não foi possível exportar os logs.");
        }
    }
}
