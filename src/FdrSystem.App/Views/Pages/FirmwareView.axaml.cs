using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FdrSystem.App.Services;
using FdrSystem.Core;
using FdrSystem.Core.Firmware;

namespace FdrSystem.App.Views.Pages;

/// <summary>
/// Página Firmware: catálogo completo (api.ipsw.me) com filtro de modelos, cache em
/// disco e download por linha usando o mesmo motor da página Online.
/// </summary>
public partial class FirmwareView : UserControl
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

    private readonly MainWindow _owner;
    private readonly DownloadManager _downloads;
    private readonly List<IpswDevice> _allDevices = new();

    private IpswDeviceInfo? _deviceInfo;

    private sealed class FirmwareRow
    {
        public string Version { get; init; } = "";
        public string Build { get; init; } = "";
        public string DateText { get; init; } = "";
        public string SizeText { get; init; } = "";
        public bool Signed { get; init; }
        public string SignedText => Signed ? "Assinado" : "Expirado";
        public string Url { get; init; } = "";
        public long FileSize { get; init; }
        public string? Sha256 { get; init; }
        public string ModelName { get; init; } = "";
    }

    // Construtor público sem dependências para o carregador XAML/designer.
    public FirmwareView() : this(null!, new DownloadManager(AppPaths.FirmwareFolder))
    {
    }

    public FirmwareView(MainWindow owner, DownloadManager downloads)
    {
        InitializeComponent();
        _owner = owner;
        _downloads = downloads;

        _ = LoadDevicesAsync(force: false);
    }

    private static string CacheFolder => Path.Combine(AppPaths.DataDirectory, "cache");

    private static string DevicesCacheFile => Path.Combine(CacheFolder, "ipsw-devices.json");

    private static string DeviceCacheFile(string identifier) =>
        Path.Combine(CacheFolder, "ipsw-device-" + identifier.Replace(',', '_') + ".json");

    // ------------------------------------------------------------------ catálogo

    private async Task LoadDevicesAsync(bool force)
    {
        StatusText.Text = "A carregar o catálogo de modelos…";
        RefreshCatalogButton.IsEnabled = false;

        try
        {
            List<IpswDevice>? devices = null;

            if (!force) devices = TryReadDevicesCache();

            if (devices is null)
            {
                var fetched = await IpswCatalog.GetDevicesAsync();
                devices = fetched.ToList();
                WriteDevicesCache(devices);
                AppLog.Info($"catálogo de modelos obtido ({devices.Count})", "Firmware");
            }
            else
            {
                AppLog.Info($"catálogo de modelos em cache ({devices.Count})", "Firmware");
            }

            _allDevices.Clear();
            _allDevices.AddRange(devices.OrderBy(d => d.Name).ThenBy(d => d.Identifier));
            ApplyFilter();
            StatusText.Text = $"{_allDevices.Count} modelos no catálogo.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha ao carregar o catálogo: " + ex.Message;
            AppLog.Error("catálogo de modelos: " + ex.Message, "Firmware");
        }
        finally
        {
            RefreshCatalogButton.IsEnabled = true;
        }
    }

    private static List<IpswDevice>? TryReadDevicesCache()
    {
        try
        {
            if (!File.Exists(DevicesCacheFile)) return null;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(DevicesCacheFile) > CacheTtl) return null;

            var json = File.ReadAllText(DevicesCacheFile);
            return JsonSerializer.Deserialize<List<IpswDevice>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteDevicesCache(List<IpswDevice> devices)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            File.WriteAllText(DevicesCacheFile, JsonSerializer.Serialize(devices));
        }
        catch (Exception ex)
        {
            AppLog.Warn("guardar cache do catálogo: " + ex.Message, "Firmware");
        }
    }

    private static IpswDeviceInfo? TryReadDeviceCache(string identifier)
    {
        try
        {
            var file = DeviceCacheFile(identifier);
            if (!File.Exists(file)) return null;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > CacheTtl) return null;

            return JsonSerializer.Deserialize<IpswDeviceInfo>(File.ReadAllText(file));
        }
        catch
        {
            return null;
        }
    }

    private static void WriteDeviceCache(IpswDeviceInfo info)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            File.WriteAllText(DeviceCacheFile(info.Identifier), JsonSerializer.Serialize(info));
        }
        catch (Exception ex)
        {
            AppLog.Warn("guardar cache do modelo: " + ex.Message, "Firmware");
        }
    }

    // ------------------------------------------------------------------ filtro

    private void Filter_Changed(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnlyIphones_Click(object? sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var term = FilterBox.Text?.Trim() ?? "";
        var onlyIphones = OnlyIphonesBox.IsChecked != false;

        IEnumerable<IpswDevice> source = _allDevices;
        if (onlyIphones)
            source = source.Where(d => d.Identifier.StartsWith("iPhone", StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(term))
            source = source.Where(d =>
                d.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                d.Identifier.Contains(term, StringComparison.OrdinalIgnoreCase));

        var filtered = source.ToList();
        DevicesList.ItemsSource = filtered;

        StatusText.Text = onlyIphones
            ? $"{filtered.Count} iPhones" + (string.IsNullOrWhiteSpace(term) ? "" : $" (filtro: «{term}»)") + "."
            : $"{filtered.Count} de {_allDevices.Count} modelos.";
    }

    private async void RefreshCatalog_Click(object? sender, RoutedEventArgs e)
    {
        try { File.Delete(DevicesCacheFile); } catch { /* pode não existir */ }
        await LoadDevicesAsync(force: true);
    }

    // ------------------------------------------------------------------ modelo

    private async void Devices_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DevicesList.SelectedItem is not IpswDevice device) return;

        FirmwareGrid.ItemsSource = null;
        DownloadButton.IsEnabled = false;
        CopyUrlButton.IsEnabled = false;
        SelectedModelText.Text = $"{device.Name} · {device.Identifier}";
        StatusText.Text = "A carregar firmwares de " + device.Identifier + "…";

        try
        {
            var info = TryReadDeviceCache(device.Identifier);
            if (info is null)
            {
                info = await IpswCatalog.GetDeviceAsync(device.Identifier);
                WriteDeviceCache(info);
                AppLog.Info($"firmwares de {device.Identifier} obtidos ({info.Firmwares.Count})", "Firmware");
            }

            _deviceInfo = info;

            var rows = info.Firmwares
                .OrderByDescending(f => f.Released ?? DateTimeOffset.MinValue)
                .Select(f => new FirmwareRow
                {
                    Version = f.Version,
                    Build = f.Build,
                    DateText = f.DateText,
                    SizeText = f.SizeText,
                    Signed = f.Signed,
                    Url = f.Url,
                    FileSize = f.Size,
                    Sha256 = f.Sha256,
                    ModelName = info.Name,
                })
                .ToList();

            FirmwareGrid.ItemsSource = rows;

            var signed = rows.Count(r => r.Signed);
            StatusText.Text = $"{info.Name}: {rows.Count} versões · {signed} assinadas.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha ao carregar firmwares: " + ex.Message;
            AppLog.Error($"firmwares de {device.Identifier}: {ex.Message}", "Firmware");
        }
    }

    private void Firmware_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var has = FirmwareGrid.SelectedItem is FirmwareRow;
        DownloadButton.IsEnabled = has;
        CopyUrlButton.IsEnabled = has;
    }

    // ------------------------------------------------------------------ ações

    private async void Download_Click(object? sender, RoutedEventArgs e)
    {
        if (FirmwareGrid.SelectedItem is not FirmwareRow row) return;
        if (string.IsNullOrWhiteSpace(row.Url))
        {
            HintText.Text = "Esta linha não tem URL de download.";
            return;
        }

        if (!row.Signed)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null) return;

            var ok = await Dialogs.ConfirmAsync(owner, "Firmware expirado",
                $"A Apple já não assina o iOS {row.Version} ({row.Build}).\n\n"
                + "O download serve apenas para arquivo e NÃO pode ser usado para restauro. Continuar?",
                "Descarregar");
            if (!ok) return;
        }

        try
        {
            var fileName = Path.GetFileName(new Uri(row.Url).LocalPath);
            var item = new DownloadItem
            {
                Url = row.Url,
                FileName = fileName,
                FilePath = Path.Combine(AppPaths.FirmwareFolder, fileName),
                Title = $"{row.ModelName} · iOS {row.Version} ({row.Build})",
                Total = row.FileSize,
                Sha256 = row.Sha256,
                Model = row.ModelName,
                Version = row.Version,
                Build = row.Build,
                Signed = row.Signed,
            };

            var message = _downloads.Enqueue(item);
            HintText.Text = message;
            AppLog.Info(message, "Firmware");
        }
        catch (Exception ex)
        {
            HintText.Text = "Falha ao preparar o download: " + ex.Message;
            AppLog.Error("download de firmware: " + ex.Message, "Firmware");
        }
    }

    private void ViewDownloads_Click(object? sender, RoutedEventArgs e) => _owner?.Navigate("online");

    private async void CopyUrl_Click(object? sender, RoutedEventArgs e)
    {
        if (FirmwareGrid.SelectedItem is not FirmwareRow row || string.IsNullOrWhiteSpace(row.Url)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(row.Url);
        HintText.Text = "URL copiado: " + row.Url;
    }
}
