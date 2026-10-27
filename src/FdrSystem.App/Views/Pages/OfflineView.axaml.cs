using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FdrSystem.App.Services;
using FdrSystem.Core;
using FdrSystem.Core.Devices;
using FdrSystem.Core.Specs;
using FdrSystem.Core.Tools;

namespace FdrSystem.App.Views.Pages;

public sealed record DisplayRow(string Group, string Label, string Value, string Key);

public partial class OfflineView : UserControl
{
    private readonly MainWindow _owner;
    private readonly DeviceService _service;
    private readonly ToolRunner _tools;

    private DeviceSnapshot? _snapshot;
    private bool _showAll;
    private bool _reading;
    private DateTime _lastReadUtc = DateTime.MinValue;

    public OfflineView(MainWindow owner, ToolRunner tools)
    {
        InitializeComponent();
        _owner = owner;
        _tools = tools;
        _service = new DeviceService(tools);
        RevealCheck.IsChecked = AppSettings.Current.RevealIdentifiers;
        RefreshGrid();
    }

    public void EnsureAutoRead(bool force = false)
    {
        if (_reading) return;
        if (!force && DateTime.UtcNow - _lastReadUtc < TimeSpan.FromSeconds(3)) return;
        _ = ReadAsync();
    }

    private async Task ReadAsync()
    {
        _reading = true;
        ScanButton.IsEnabled = false;
        _owner.SetStatus("A ler o iPhone…");

        try
        {
            var snapshot = await Task.Run(() => _service.ReadAsync()).ConfigureAwait(true);
            _snapshot = snapshot;
            _lastReadUtc = DateTime.UtcNow;
            FillCards(snapshot);
            RefreshGrid();
            _owner.SetStatus(snapshot.Paired
                ? $"iPhone lido — {snapshot.Model} · iOS {FindValue("ProductVersion") ?? "—"}"
                : "iPhone ligado sem emparelhamento — dados do USB apenas. Desbloqueie e aceite 'Confiar neste computador'.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            AppLog.Warn("leitura: " + ex.Message, "Offline");
            _owner.SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            AppLog.Error("leitura falhou: " + ex, "Offline");
            _owner.SetStatus("Falha ao ler o iPhone: " + ex.Message);
        }
        finally
        {
            _reading = false;
            ScanButton.IsEnabled = true;
        }
    }

    private void FillCards(DeviceSnapshot snapshot)
    {
        var productType = FindValue("ProductType");
        var spec = DeviceSpecs.Get(productType);

        ModelName.Text = snapshot.Model;
        ModelChip.Text = string.IsNullOrWhiteSpace(snapshot.Cpu) ? "—" : snapshot.Cpu;
        ModelRam.Text = spec is null ? "—" : $"{spec.RamGb} GB";
        ModelScreen.Text = spec?.Screen ?? "—";
        ModelCamera.Text = spec?.Camera ?? "—";
        ModelYear.Text = spec is null ? "—" : spec.Year.ToString(CultureInfo.InvariantCulture);
        ModelIos.Text = FindValue("ProductVersion") is { Length: > 0 } version
            ? version + (FindValue("BuildVersion") is { Length: > 0 } build ? $" ({build})" : "")
            : "—";

        var battery = snapshot.Battery;
        if (battery?.Level is { } level)
        {
            BatteryLevel.Text = $"{level:0}%";
            BatteryBar.Value = Math.Clamp(level, 0, 100);
        }
        else
        {
            BatteryLevel.Text = snapshot.Charge is { Length: > 0 } charge ? charge + "%" : "—";
            if (double.TryParse(snapshot.Charge, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                BatteryBar.Value = Math.Clamp(parsed, 0, 100);
        }

        BatteryHealth.Text = snapshot.Health is { } health
            ? $"Saúde {health:0}% ({DeviceLabels.HealthWord(health)})" +
              (battery?.HealthRaw is { } raw ? $" · bruto {raw:0.0}%" : "")
            : "Saúde —";

        BatteryCharging.Text = battery is null ? "Estado de carga indisponível" : BatteryParser.ChargingSummary(battery);
        BatteryCycles.Text = snapshot.Cycles is { Length: > 0 } cycles ? $"Ciclos: {cycles}" : "Ciclos —";

        FillStorage(snapshot);
    }

    private void FillStorage(DeviceSnapshot snapshot)
    {
        var summary = snapshot.Rows.FirstOrDefault(r => r.Key == "Summary.Storage")?.Value;
        if (summary is not null)
        {
            StorageText.Text = summary;
            var parts = summary.Split(" de ", StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var free) &&
                double.TryParse(parts[1].Replace("GB", "", StringComparison.Ordinal), NumberStyles.Any, CultureInfo.InvariantCulture, out var total) &&
                total > 0)
            {
                StorageBar.Value = Math.Clamp(100 - free / total * 100, 0, 100);
            }
            return;
        }

        var totalRow = snapshot.Rows.FirstOrDefault(r => r.Key is "Disk.TotalDataCapacity" or "Disk.TotalDiskCapacity")?.Value;
        var freeRow = snapshot.Rows.FirstOrDefault(r => r.Key is "Disk.TotalDataAvailable" or "Disk.AmountDataAvailable")?.Value;
        if (long.TryParse(totalRow, out var totalBytes) && long.TryParse(freeRow, out var freeBytes) && totalBytes > 0)
        {
            StorageText.Text = $"{freeBytes / 1024.0 / 1024 / 1024:0.0} de {totalBytes / 1024.0 / 1024 / 1024:0.0} GB";
            StorageBar.Value = Math.Clamp(100 - freeBytes * 100.0 / totalBytes, 0, 100);
        }
        else
        {
            StorageText.Text = "—";
        }
    }

    private string? FindValue(string key) => _snapshot?.Rows.FirstOrDefault(r => r.Key == key)?.Value;

    private void RefreshGrid()
    {
        var reveal = RevealCheck.IsChecked == true;
        var filter = SearchBox.Text?.Trim() ?? string.Empty;
        var rows = _snapshot?.Rows ?? [];
        var keyFields = _service.KeyFieldKeys;

        var visible = rows
            .Where(r => filter.Length > 0
                ? r.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                  || r.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)
                  || r.Group.Contains(filter, StringComparison.OrdinalIgnoreCase)
                  || r.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)
                : _showAll || rows.Count <= 9 || keyFields.Contains(r.Key))
            .Select(r => new DisplayRow(r.Group, r.Label, DeviceLabels.DisplayValue(r, reveal), r.Key))
            .ToList();

        DetailsGrid.ItemsSource = visible;
        RowCountText.Text = $"{visible.Count} de {rows.Count} campo(s)";
        CopyButton.IsEnabled = CopyAllButton.IsEnabled = visible.Count > 0;
        ExportButton.IsEnabled = rows.Count > 0;
        DetailsButton.IsEnabled = rows.Count > 0;
        BatteryButton.IsEnabled = _snapshot?.Battery is not null;
    }

    private void Search_Changed(object? sender, TextChangedEventArgs e) => RefreshGrid();

    private void Reveal_Changed(object? sender, RoutedEventArgs e)
    {
        AppSettings.Current.RevealIdentifiers = RevealCheck.IsChecked == true;
        AppSettings.Save();
        RefreshGrid();
    }

    private void ShowAll_Click(object? sender, RoutedEventArgs e)
    {
        _showAll = !_showAll;
        ShowAllButton.Content = _showAll ? "Ver principais" : "Ver todos";
        RefreshGrid();
    }

    private async void Scan_Click(object? sender, RoutedEventArgs e) => await ReadAsync();

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (DetailsGrid.SelectedItem is not DisplayRow row)
        {
            _owner.SetStatus("Selecione uma linha na tabela para copiar o valor.");
            return;
        }

        await CopyTextAsync(row.Value);
        _owner.SetStatus($"Copiado: {row.Label}.");
    }

    private async void CopyAll_Click(object? sender, RoutedEventArgs e)
    {
        if (DetailsGrid.ItemsSource is not IEnumerable<DisplayRow> rows) return;
        var text = string.Join(Environment.NewLine, rows.Select(r => $"{r.Label}: {r.Value}"));
        await CopyTextAsync(text);
        _owner.SetStatus("Todos os campos visíveis copiados.");
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (_snapshot is null) return;

        try
        {
            var folder = AppPaths.ReportsFolder;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"relatorio-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            var reveal = RevealCheck.IsChecked == true;

            var payload = new
            {
                app = "FDR_SYSTEM",
                versao = typeof(OfflineView).Assembly.GetName().Version?.ToString(),
                modo = "Linux",
                data = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                modelo = _snapshot.Model,
                chip = _snapshot.Cpu,
                campos = _snapshot.Rows.ToDictionary(
                    r => r.Key,
                    r => DeviceLabels.DisplayValue(r, reveal)),
            };

            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            _owner.SetStatus($"Relatório exportado: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("exportar JSON: " + ex.Message, "Offline");
            _owner.SetStatus("Não foi possível exportar o relatório.");
        }
    }

    private void Details_Click(object? sender, RoutedEventArgs e)
    {
        if (_snapshot is null) return;
        var reveal = RevealCheck.IsChecked == true;
        var rows = _snapshot.Rows.Select(r => new DisplayRow(r.Group, r.Label, DeviceLabels.DisplayValue(r, reveal), r.Key)).ToList();
        Dialogs.ShowTable(_owner, "Detalhes completos", rows);
    }

    private void Battery_Click(object? sender, RoutedEventArgs e)
    {
        if (_snapshot?.Battery is not { } battery) return;
        var reveal = RevealCheck.IsChecked == true;
        Dialogs.ShowBattery(_owner, battery, reveal);
    }

    private async Task CopyTextAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(text).ConfigureAwait(true);
    }
}
