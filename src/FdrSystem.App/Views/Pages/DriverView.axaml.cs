using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using FdrSystem.App.Services;
using FdrSystem.Core;
using FdrSystem.Core.Devices;
using FdrSystem.Core.Tools;
using FdrSystem.Core.Usb;

namespace FdrSystem.App.Views.Pages;

public sealed record DriverToolRow(string Label, bool Found, IBrush? StatusBrush)
{
    public string StatusText => Found ? "OK" : "em falta";
}

public sealed record DriverDeviceRow(string Mode, string Pid, string Product, string Serial);

public partial class DriverView : UserControl
{
    private static readonly (string Command, string Label, bool Required)[] ToolSpecs =
    [
        ("idevice_id", "idevice_id — detetar", true),
        ("ideviceinfo", "ideviceinfo — ler dados", true),
        ("idevicepair", "idevicepair — emparelhar", false),
        ("idevicesyslog", "idevicesyslog — registos", false),
        ("idevicecrashreport", "idevicecrashreport — crash", false),
        ("ideviceprovision", "ideviceprovision — perfis", false),
        ("idevicescreenshot", "idevicescreenshot — ecrã", false),
        ("idevicerestore", "idevicerestore — restauro", false),
        ("irecovery", "irecovery — recovery/DFU", false),
    ];

    private readonly MainWindow? _owner;
    private readonly ToolRunner _tools;
    private bool _refreshing;

    // Construtor público sem dependências para o carregador XAML/designer.
    // A aplicação usa sempre o construtor com owner e ToolRunner.
    public DriverView() : this(null, new ToolRunner())
    {
    }

    public DriverView(MainWindow? owner, ToolRunner tools)
    {
        InitializeComponent();
        _owner = owner;
        _tools = tools;

        CommandsBox.Text = BuildCommands();
        UpdateTools();
    }

    /// <summary>Chamado ao entrar na página; relê o estado do sistema.</summary>
    public void EnsureRefreshed()
    {
        if (_refreshing) return;
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        DriverRefreshButton.IsEnabled = false;
        SetStatus("A verificar o acesso USB…", "Surface2", "TextSecondary");

        try
        {
            var environment = await DriverEnvironment.CollectAsync(_tools);
            ApplyEnvironment(environment);

            AppLog.Info(
                $"Driver: {environment.Description}; usbmuxd={(environment.UsbmuxdSocket ? "socket" : "sem socket")}; " +
                $"serviço={environment.UsbmuxdService ?? "indisponível"}; udev={environment.UdevRulesPath ?? "ausente"}; " +
                $"dispositivos={environment.Devices.Count}",
                "Driver");
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível ler o estado do USB: " + ex.Message, "RedBoxBg", "RedText");
            AppLog.Error("Driver: " + ex.Message, "Driver");
        }
        finally
        {
            _refreshing = false;
            DriverRefreshButton.IsEnabled = true;
        }
    }

    private void ApplyEnvironment(DriverEnvironment environment)
    {
        EnvironmentText.Text = environment.Description;
        UsbmuxdText.Text = environment.IsLinux
            ? (environment.UsbmuxdSocket ? "presente (/run/usbmuxd)" : "ausente")
            : "—";

        ServiceText.Text = environment.IsLinux
            ? string.IsNullOrWhiteSpace(environment.UsbmuxdService)
                ? "systemctl indisponível (pode não ser systemd)"
                : environment.UsbmuxdService +
                  (string.IsNullOrWhiteSpace(environment.UsbmuxdEnabled) ? string.Empty : $" · {environment.UsbmuxdEnabled}")
            : "—";

        UdevText.Text = environment.IsLinux
            ? environment.UdevRulesPath ?? "não encontradas (instala usbmuxd)"
            : "—";

        GroupsText.Text = environment.IsLinux
            ? string.IsNullOrWhiteSpace(environment.UserGroups)
                ? "não foi possível ler (comando id indisponível)"
                : environment.UserGroups + (environment.UserInPlugdev ? " · plugdev: sim" : " · plugdev: não")
            : "—";

        UsbmuxdText.Foreground = environment.IsLinux
            ? environment.UsbmuxdSocket ? Brush("GreenText") : Brush("AmberText")
            : Brush("TextMuted");
        UdevText.Foreground = environment.IsLinux
            ? environment.UdevRulesPath is null ? Brush("AmberText") : Brush("GreenText")
            : Brush("TextMuted");
        ServiceText.Foreground = environment.IsLinux
            ? environment.UsbmuxdService?.Equals("active", StringComparison.OrdinalIgnoreCase) == true
                ? Brush("GreenText")
                : Brush("AmberText")
            : Brush("TextMuted");
        GroupsText.Foreground = environment.IsLinux ? Brush("TextPrimary") : Brush("TextMuted");

        if (!environment.IsLinux)
        {
            SetStatus(
                "Windows — ambiente de desenvolvimento. A página Driver só se aplica ao Linux; " +
                "os dispositivos abaixo continuam a ser lidos pelo SetupAPI.",
                "AmberBoxBg",
                "AmberText");
            _owner?.SetStatus("Driver: ambiente Windows (página Linux).");
        }
        else if (environment.UsbmuxdSocket && environment.UdevRulesPath is not null)
        {
            SetStatus(
                "Acesso USB pronto: usbmuxd e regras udev presentes.",
                "GreenBoxBg",
                "GreenText");
            _owner?.SetStatus("Driver: acesso USB pronto.");
        }
        else if (environment.UsbmuxdSocket)
        {
            SetStatus(
                "O usbmuxd está a correr, mas não encontrei as regras udev do pacote. " +
                "Confirma a instalação de usbmuxd e as permissões do utilizador.",
                "AmberBoxBg",
                "AmberText");
            _owner?.SetStatus("Driver: usbmuxd ativo; regras udev em falta.");
        }
        else if (environment.UsbmuxdService?.Equals("active", StringComparison.OrdinalIgnoreCase) == true)
        {
            SetStatus(
                "O serviço systemd diz que está ativo, mas o socket /run/usbmuxd não existe. " +
                "Reinicia o usbmuxd ou confirma o estado com systemctl status usbmuxd.",
                "AmberBoxBg",
                "AmberText");
            _owner?.SetStatus("Driver: serviço ativo sem socket.");
        }
        else
        {
            SetStatus(
                "O usbmuxd não está a correr. Instala e ativa o serviço com os comandos ao lado; " +
                "depois volta a entrar na sessão para o grupo plugdev passar a valer.",
                "AmberBoxBg",
                "AmberText");
            _owner?.SetStatus("Driver: usbmuxd não está ativo.");
        }

        var rows = environment.Devices
            .Select(device => new DriverDeviceRow(
                device.Mode,
                $"0x{device.ProductId:X4}",
                string.IsNullOrWhiteSpace(device.Product) ? "Apple" : device.Product.Trim(),
                MaskSerial(device.Serial)))
            .ToList();

        DevicesGrid.ItemsSource = rows;
        DevicesGrid.IsVisible = rows.Count > 0;
        DevicesGrid.Height = Math.Min(180, 36 + rows.Count * 30);
        DevicesEmptyText.IsVisible = rows.Count == 0;
        DevicesCountText.Text = rows.Count switch
        {
            0 => "nenhum",
            1 => "1 dispositivo",
            _ => $"{rows.Count} dispositivos",
        };
    }

    private void UpdateTools()
    {
        var green = Brush("GreenText");
        var amber = Brush("AmberText");
        var rows = ToolSpecs
            .Select(spec =>
            {
                var found = _tools.Has(spec.Command);
                return new DriverToolRow(spec.Label, found, found ? green : amber);
            })
            .ToList();

        ToolsList.ItemsSource = rows;

        var missing = ToolSpecs
            .Where(spec => spec.Required && !_tools.Has(spec.Command))
            .Select(spec => spec.Command)
            .ToArray();

        if (missing.Length == 0)
        {
            ToolsHintText.Text = "Ferramentas essenciais presentes. As opcionais podem faltar sem impedir o USB.";
            ToolsHintText.Foreground = Brush("GreenText");
        }
        else
        {
            ToolsHintText.Text = "Ferramentas essenciais em falta: " + string.Join(", ", missing) +
                                 ". Instala libimobiledevice-utils.";
            ToolsHintText.Foreground = amber;
        }
    }

    private void DriverRefresh_Click(object? sender, RoutedEventArgs e) => EnsureRefreshed();

    private async void CopyCommands_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                SetStatus("Não foi possível aceder à área de transferência.", "AmberBoxBg", "AmberText");
                return;
            }

            await clipboard.SetTextAsync(CommandsBox.Text ?? string.Empty);
            SetStatus("Comandos copiados para a área de transferência.", "GreenBoxBg", "GreenText");
            _owner?.SetStatus("Driver: comandos copiados.");
            AppLog.Info("Driver: comandos copiados para a área de transferência.", "Driver");
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível copiar os comandos: " + ex.Message, "RedBoxBg", "RedText");
            AppLog.Warn("Driver: copiar comandos: " + ex.Message, "Driver");
        }
    }

    private void SetStatus(string text, string boxKey, string textKey)
    {
        DriverStatusText.Text = text;
        DriverStatusText.Foreground = Brush(textKey);
        DriverStatusBox.Background = Brush(boxKey);
    }

    private static string MaskSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial)) return "—";
        var value = AppleUsb.FormatUdid(serial.Trim()) ?? serial.Trim();
        return DeviceLabels.Mask(value);
    }

    private static IBrush? Brush(string key) =>
        Application.Current?.FindResource(key) as IBrush;

    private static string BuildCommands() => string.Join(Environment.NewLine,
    [
        "# Debian / Ubuntu",
        "sudo apt update",
        "sudo apt install usbmuxd libimobiledevice-utils libimobiledevice-1.0-6",
        "sudo systemctl enable --now usbmuxd",
        "sudo usermod -aG plugdev \"$USER\"",
        "",
        "# Fedora",
        "sudo dnf install usbmuxd libimobiledevice-utils libimobiledevice",
        "",
        "# Verificar (não precisa de root)",
        "systemctl status usbmuxd",
        "idevice_id -l",
        "",
        "# Depois de usermod, termina sessão e volta a entrar.",
        "# Se as regras udev mudarem:",
        "sudo udevadm control --reload-rules",
        "sudo udevadm trigger",
    ]);
}
