using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FdrSystem.App.Services;
using FdrSystem.Core;
using FdrSystem.Core.Firmware;
using FdrSystem.Core.Tools;

namespace FdrSystem.App.Views.Pages;

/// <summary>
/// Página Online: catálogo público de firmware, descarga segmentada com retoma,
/// verificação SHA-256 e importação de IPSW já existente.
/// </summary>
public partial class OnlineView : UserControl
{
    private readonly MainWindow _owner;
    private readonly ToolRunner _tools;
    private readonly DownloadManager _downloads;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<DownloadItem, RowUi> _rows = new();

    private string? _detectedProductType;
    private string? _firmwareUrl;
    private string? _firmwareFile;
    private long? _firmwareSize;
    private string? _firmwareSha;
    private string? _firmwareVersion;
    private string? _firmwareBuild;
    private string? _firmwareModelName;

    private sealed record RowUi(TextBlock Status, ProgressBar Bar, Button Pause);

    // Construtor público sem dependências para o carregador XAML/designer.
    public OnlineView() : this(null!, new ToolRunner(), new DownloadManager(AppPaths.FirmwareFolder))
    {
    }

    public OnlineView(MainWindow owner, ToolRunner tools, DownloadManager downloads)
    {
        InitializeComponent();
        _owner = owner;
        _tools = tools;
        _downloads = downloads;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _timer.Tick += (_, _) => RenderDownloads();
        _timer.Start();

        FolderText.Text = AppPaths.FirmwareFolder;

        _downloads.RefreshDisk();
        RenderDownloads();
        UpdateDetection();

        // O DownloadManager é partilhado (MainWindow) e só é libertado no fim da app.
        DetachedFromVisualTree += (_, _) => _timer.Stop();

        AppLog.Info("página Online aberta (api.ipsw.me)", "Online");
    }

    private async void UpdateDetection()
    {
        try
        {
            var run = await _tools.RunAsync("ideviceinfo", ["-k", "ProductType"], TimeSpan.FromSeconds(6));
            if (run is { Ok: true } && !string.IsNullOrWhiteSpace(run.StdOut))
            {
                var value = ParseProductType(run.StdOut);
                if (value is not null)
                {
                    _detectedProductType = value;
                    OnlineDetectText.Text = $"Detetado por USB: {value}. Será usado se o identificador acima ficar vazio.";
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("deteção USB: " + ex.Message, "Online");
        }

        _detectedProductType = null;
        OnlineDetectText.Text = "Nenhum aparelho detetado por USB — ligue o iPhone (normal, recovery ou DFU) ou escreva o identificador.";
    }

    /// <summary>
    /// Aceita a resposta normal (<c>iPhone14,5</c>) e, por segurança, extrai
    /// <c>ProductType</c> de um plist completo caso a ferramenta ignore <c>-k</c>.
    /// </summary>
    private static string? ParseProductType(string raw)
    {
        var text = raw.Trim();
        if (text.Length is > 0 and < 40 && !text.Contains('<') && !text.Contains('\n')) return text;

        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"<key>ProductType</key>\s*<string>([^<]+)</string>");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private string? TargetIdentifier()
    {
        var typed = ModelOverrideBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(typed)) return typed;
        if (!string.IsNullOrWhiteSpace(_detectedProductType)) return _detectedProductType;
        return null;
    }

    private async void Lookup_Click(object? sender, RoutedEventArgs e)
    {
        var id = TargetIdentifier();
        if (id is null)
        {
            FirmwareInfoText.Text = "Falta o identificador do aparelho.";
            FirmwareUrlText.Text = "Escreva o identificador (ex.: iPhone13,2) ou ligue o iPhone por USB.";
            return;
        }

        LookupButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        FirmwareInfoText.Text = $"A consultar o catálogo público da Apple para {id}…";
        FirmwareUrlText.Text = "";
        NetStatusText.Text = "A consultar api.ipsw.me…";
        AppLog.Info($"consultar firmware de {id}", "Online");

        try
        {
            var info = await IpswCatalog.GetDeviceAsync(id);

            var latest = info.LatestSigned;
            if (latest is null)
            {
                FirmwareInfoText.Text = $"Sem firmware assinado pela Apple para {id} neste momento (a Apple pode ter parado de assinar).";
                NetStatusText.Text = "Catálogo consultado · sem firmware assinado";
                RestoreStatusText.Text = "Não há firmware assinado para este modelo neste momento.";
                AppLog.Warn($"sem firmware assinado para {id}", "Online");
                return;
            }

            _firmwareUrl = latest.Url;
            _firmwareFile = Path.Combine(AppPaths.FirmwareFolder, Path.GetFileName(new Uri(latest.Url).LocalPath));
            _firmwareSize = latest.Size > 0 ? latest.Size : null;
            _firmwareSha = latest.Sha256;
            _firmwareVersion = latest.Version;
            _firmwareBuild = latest.Build;
            _firmwareModelName = info.Name;

            FirmwareInfoText.Text = $"{info.Name}  ·  iOS {latest.Version} ({latest.Build})";
            FirmwareUrlText.Text = $"{(latest.Size > 0 ? latest.Size / 1024.0 / 1024 / 1024 : 0):0.00} GB  ·  {latest.Url}"
                + (IsCompleteFirmware(_firmwareFile) ? "  ·  já descarregado" : "");

            DownloadButton.IsEnabled = true;
            VerifyButton.IsEnabled = true;
            NetStatusText.Text = "Catálogo consultado · firmware assinado disponível";
            UpdateRestoreAvailability();

            AppLog.Info($"firmware assinado: iOS {latest.Version} ({latest.Build}) para {id}", "Online");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            FirmwareInfoText.Text = $"Identificador «{id}» não reconhecido pelo catálogo. Verifique (ex.: iPhone13,2) ou leia o iPhone na página Offline.";
            NetStatusText.Text = "Identificador desconhecido no catálogo";
            AppLog.Warn($"identificador desconhecido: {id}", "Online");
        }
        catch (Exception ex)
        {
            FirmwareInfoText.Text = "Falha na consulta: " + ex.Message;
            NetStatusText.Text = "Falha de rede ao consultar o catálogo";
            AppLog.Error("consulta ao catálogo: " + ex.Message, "Online");
        }
        finally
        {
            LookupButton.IsEnabled = true;
        }
    }

    private void Download_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_firmwareUrl) || string.IsNullOrWhiteSpace(_firmwareFile))
        {
            FirmwareInfoText.Text = "Consulte primeiro o firmware assinado.";
            return;
        }

        var item = new DownloadItem
        {
            Url = _firmwareUrl,
            FileName = Path.GetFileName(_firmwareFile),
            FilePath = _firmwareFile,
            Title = $"{_firmwareModelName} · iOS {_firmwareVersion} ({_firmwareBuild})",
            Total = _firmwareSize ?? 0,
            Sha256 = _firmwareSha,
            Model = _firmwareModelName,
            Version = _firmwareVersion,
            Build = _firmwareBuild,
            Signed = true,
        };

        var message = _downloads.Enqueue(item);
        FirmwareInfoText.Text = message;
        AppLog.Info(message, "Online");
        RenderDownloads();
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_downloads.Folder);
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{_downloads.Folder}\"")
            {
                UseShellExecute = false,
            });
            FirmwareInfoText.Text = "A abrir " + _downloads.Folder;
            _downloads.RefreshDisk();
        }
        catch (Exception ex)
        {
            FirmwareInfoText.Text = "Não foi possível abrir a pasta: " + ex.Message + " — caminho: " + _downloads.Folder;
            AppLog.Error("abrir pasta de firmware: " + ex.Message, "Online");
        }
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importar IPSW",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Firmware IPSW") { Patterns = ["*.ipsw"] },
                ],
            });

            var file = files.FirstOrDefault();
            if (file is null) return;

            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                FirmwareInfoText.Text = "Não foi possível ler o ficheiro escolhido.";
                return;
            }

            var info = new FileInfo(path);
            _firmwareFile = path;
            _firmwareSize = info.Length;
            _firmwareSha = null;
            _firmwareUrl = null;
            _firmwareVersion = null;
            _firmwareBuild = null;
            _firmwareModelName = null;

            FirmwareInfoText.Text = "Firmware importado: " + info.Name;
            FirmwareUrlText.Text = $"{info.Length / 1024.0 / 1024 / 1024:0.00} GB (ficheiro local, sem referência do catálogo)";
            VerifyButton.IsEnabled = true;
            UpdateRestoreAvailability();

            AppLog.Info("firmware importado: " + path, "Online");
        }
        catch (Exception ex)
        {
            FirmwareInfoText.Text = "Falha ao importar: " + ex.Message;
            AppLog.Error("importar IPSW: " + ex.Message, "Online");
        }
    }

    private async void Verify_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_firmwareFile) || !File.Exists(_firmwareFile))
        {
            FirmwareInfoText.Text = "Não há firmware completo para verificar.";
            return;
        }

        VerifyButton.IsEnabled = false;
        var previous = FirmwareInfoText.Text;
        FirmwareInfoText.Text = "A calcular SHA-256 de " + Path.GetFileName(_firmwareFile) + "…";

        try
        {
            var hash = await DownloadManager.ComputeSha256Async(_firmwareFile);

            if (string.IsNullOrWhiteSpace(_firmwareSha))
            {
                FirmwareInfoText.Text = $"SHA-256 calculado: {hash} (sem referência no catálogo para comparar)";
                AppLog.Info($"SHA-256 de {Path.GetFileName(_firmwareFile)}: {hash}", "Online");
            }
            else if (string.Equals(hash, _firmwareSha, StringComparison.OrdinalIgnoreCase))
            {
                FirmwareInfoText.Text = "Integridade verificada ✓ — o SHA-256 coincide com o catálogo.";
                AppLog.Info("SHA-256 confirmado: " + hash, "Online");
            }
            else
            {
                FirmwareInfoText.Text = "O SHA-256 NÃO coincide com o catálogo. O ficheiro pode estar corrompido ou adulterado.";
                AppLog.Error($"SHA-256 não coincide: {hash} (esperado {_firmwareSha})", "Online");
            }
        }
        catch (Exception ex)
        {
            FirmwareInfoText.Text = "Falha ao verificar: " + ex.Message;
            AppLog.Error("verificar SHA-256: " + ex.Message, "Online");
        }
        finally
        {
            VerifyButton.IsEnabled = true;
            UpdateRestoreAvailability(previous);
        }
    }

    private void RefreshDownloads_Click(object? sender, RoutedEventArgs e)
    {
        _downloads.RefreshDisk();
        RenderDownloads();
        FirmwareInfoText.Text = "Lista da pasta atualizada.";
    }

    private void ClearFinished_Click(object? sender, RoutedEventArgs e)
    {
        _downloads.ClearFinished();
        RenderDownloads();
        FirmwareInfoText.Text = "Downloads concluídos removidos da lista.";
    }

    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_firmwareFile) || !IsCompleteFirmware(_firmwareFile))
        {
            FirmwareInfoText.Text = "Descarregue primeiro o firmware completo.";
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var target = TargetIdentifier() ?? "desconhecido";
        var ok = await Dialogs.ConfirmAsync(
            owner,
            "Confirmar restauro",
            $"ATENÇÃO: o restauro APAGA todo o conteúdo do iPhone e repõe os dados de fábrica.\n\n"
            + $"Firmware: {Path.GetFileName(_firmwareFile)}\nAparelho: {target}\n\n"
            + "Só é possível com firmware que a Apple ainda assina. Não remove o bloqueio de ativação (iCloud).\n\nContinuar?",
            "Restaurar");

        if (!ok) return;

        AppLog.Info("restauro confirmado pelo utilizador: " + _firmwareFile, "Online");
        _owner?.RequestRestore(_firmwareFile);
    }

    private bool IsCompleteFirmware(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        var length = new FileInfo(path).Length;
        return _firmwareSize is null or <= 0 || length == _firmwareSize;
    }

    private void UpdateRestoreAvailability(string? info = null)
    {
        var complete = IsCompleteFirmware(_firmwareFile);
        RestoreButton.IsEnabled = complete;

        RestoreStatusText.Text = complete
            ? "Firmware completo. O restauro abre na página Restauro com o progresso em direto."
            : "Descarregue o firmware completo para ativar o restauro.";

        if (info is not null && !string.IsNullOrWhiteSpace(info)) FirmwareInfoText.Text = info;
    }

    // ---------------------------------------------------------------- downloads

    private void RenderDownloads()
    {
        var items = _downloads.Snapshot();

        if (items.Count != _rows.Count || items.Any(i => !_rows.ContainsKey(i)))
        {
            BuildRows(items);
        }

        foreach (var item in items)
        {
            if (!_rows.TryGetValue(item, out var ui)) continue;
            ui.Status.Text = item.Status;
            ui.Bar.Value = Math.Min(item.ProgressValue, ui.Bar.Maximum);
            ui.Bar.IsVisible = item.HasProgress;
            ui.Pause.Content = item.PauseLabel;
            ui.Pause.IsEnabled = item.CanPause;
        }
    }

    private void BuildRows(IReadOnlyList<DownloadItem> items)
    {
        _rows.Clear();
        DownloadsPanel.Children.Clear();

        if (items.Count == 0)
        {
            DownloadsPanel.Children.Add(new TextBlock
            {
                Text = "Sem downloads. Consulte o firmware assinado e clique «Baixar IPSW».",
                FontSize = 10.5,
                Foreground = Brush("TextFaint"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var item in items)
        {
            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(item.Title) ? item.FileName : item.Title,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("TextPrimary"),
                TextWrapping = TextWrapping.Wrap,
            };

            var status = new TextBlock
            {
                Text = item.Status,
                FontSize = 10.5,
                Foreground = Brush("TextMuted"),
                TextWrapping = TextWrapping.Wrap,
            };

            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = item.ProgressMax,
                Value = Math.Min(item.ProgressValue, item.ProgressMax),
                Height = 6,
                IsVisible = item.HasProgress,
            };

            var pause = new Button
            {
                Content = item.PauseLabel,
                IsEnabled = item.CanPause,
                Padding = new Thickness(9, 4),
                FontSize = 10.5,
            };
            pause.Classes.Add("tool");

            var delete = new Button
            {
                Content = "Eliminar",
                Padding = new Thickness(9, 4),
                FontSize = 10.5,
            };
            delete.Classes.Add("tool");

            var actions = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            actions.Children.Add(pause);
            actions.Children.Add(delete);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var text = new StackPanel { Spacing = 4 };
            text.Children.Add(title);
            text.Children.Add(status);
            text.Children.Add(bar);
            Grid.SetColumn(text, 0);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(text);
            grid.Children.Add(actions);

            var border = new Border
            {
                Background = Brush("Surface2"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Child = grid,
            };

            pause.Click += (_, _) =>
            {
                if (item.State is DownloadState.Downloading or DownloadState.Queued)
                {
                    _downloads.Pause(item);
                    AppLog.Info("download pausado: " + item.FileName, "Descargas");
                }
                else
                {
                    _downloads.Resume(item);
                    AppLog.Info("download retomado: " + item.FileName, "Descargas");
                }
                RenderDownloads();
            };

            delete.Click += async (_, _) =>
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                if (owner is null) return;

                var ok = await Dialogs.ConfirmAsync(owner, "Eliminar ficheiro",
                    $"Eliminar «{item.FileName}» do disco?\n\nO ficheiro e os dados de retoma são removidos. Esta ação não pode ser anulada.",
                    "Eliminar");

                if (!ok) return;
                _downloads.Delete(item);
                AppLog.Info("download eliminado: " + item.FileName, "Descargas");
                _rows.Clear();
                RenderDownloads();
            };

            DownloadsPanel.Children.Add(border);
            _rows[item] = new RowUi(status, bar, pause);
        }
    }

    private static IBrush? Brush(string key) => Application.Current?.FindResource(key) as IBrush;
}
