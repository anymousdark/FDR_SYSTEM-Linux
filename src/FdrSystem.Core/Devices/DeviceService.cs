using System.Globalization;
using FdrSystem.Core.Specs;
using FdrSystem.Core.Tools;
using FdrSystem.Core.Usb;

namespace FdrSystem.Core.Devices;

/// <summary>
/// Leitura completa do iPhone: USB (modo e identificadores), ideviceinfo -x,
/// armazenamento, bateria (ioregistry) e domínio de bateria.
/// Portado de MainWindow.xaml.cs (ReadDeviceAsync), trocando os .exe por
/// ferramentas do sistema no Linux.
/// </summary>
public sealed class DeviceService(ToolRunner tools)
{
    private static readonly string[] KeyFields =
    [
        "DeviceName", "ProductType", "ProductVersion", "ModelNumber", "RegionInfo", "ActivationState",
        "SerialNumber", "UniqueChipID", "UniqueDeviceID", "Battery.CurrentCapacity",
        "BatteryQ.BatteryCurrentCapacity", "Battery.CycleCount", "BatteryQ.CycleCount", "Summary.Storage",
    ];

    public IReadOnlyList<string> KeyFieldKeys => KeyFields;

    public async Task<DeviceSnapshot> ReadAsync(CancellationToken ct = default)
    {
        var rows = new List<DetailRow>();
        var usbDevices = UsbScanner.FindAppleDevices();

        foreach (var device in usbDevices)
        {
            var mode = device.Mode;
            rows.Add(new DetailRow("UsbMode", "Identificação", "Modo (USB)", mode));

            var udid = AppleUsb.FormatUdid(device.Serial);
            if (mode == "Normal")
            {
                if (AppleUsb.LooksLikeUdid(udid))
                    rows.Add(new DetailRow("UsbUdid", "Identificação", "UDID (USB, sem emparelhamento)", udid!));
            }
            else
            {
                var rec = AppleUsb.ParseRecoveryIds(device.Serial ?? device.Path);
                if (IrecoveryDevices.TryResolve(rec.Cpid, rec.Bdid, out var pt, out var nm))
                    rows.Add(new DetailRow("UsbModel", "Identificação", $"Modelo (USB, {mode})", $"{nm} ({pt})"));
                if (!string.IsNullOrWhiteSpace(rec.EcidDec))
                    rows.Add(new DetailRow("UsbEcid", "Identificação", $"ECID (USB, {mode})", rec.EcidDec));
                else if (!string.IsNullOrWhiteSpace(rec.EcidHex))
                    rows.Add(new DetailRow("UsbEcid", "Identificação", $"ECID hex (USB, {mode})", rec.EcidHex));
                else if (!string.IsNullOrWhiteSpace(device.Serial))
                    rows.Add(new DetailRow("UsbEcid", "Identificação", $"ECID (USB, {mode})", device.Serial));
                if (!string.IsNullOrWhiteSpace(rec.Serial))
                    rows.Add(new DetailRow("UsbSerial", "Identificação", $"Serial (USB, {mode})", rec.Serial));
                if (!string.IsNullOrWhiteSpace(rec.Cpid))
                    rows.Add(new DetailRow("UsbCpid", "Identificação", $"CPU / CPID (USB, {mode})", "0x" + rec.Cpid));
                if (!string.IsNullOrWhiteSpace(rec.Bdid))
                    rows.Add(new DetailRow("UsbBdid", "Identificação", $"Board / BDID (USB, {mode})", "0x" + rec.Bdid));
                if (!string.IsNullOrWhiteSpace(rec.Srtg))
                    rows.Add(new DetailRow("UsbSrtg", "Identificação", $"iBoot (USB, {mode})", rec.Srtg));
            }

            if (!string.IsNullOrWhiteSpace(device.Product))
                rows.Add(new DetailRow("UsbName", "Identificação", "Dispositivo (USB)", device.Product!));
        }

        var dump = await tools.RunTextAsync("ideviceinfo", ["-x"], TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(dump))
        {
            try
            {
                foreach (var kv in PlistReader.ToDictionary(dump))
                {
                    if (string.IsNullOrWhiteSpace(kv.Value) || kv.Value == "NULL") continue;
                    if (rows.Any(x => x.Key == kv.Key)) continue;
                    rows.Add(new DetailRow(kv.Key, DeviceLabels.GroupOf(kv.Key), DeviceLabels.LabelOf(kv.Key), kv.Value));
                }
            }
            catch (System.Xml.XmlException) { }
        }

        if (rows.All(x => x.Key != "ProductType"))
        {
            if (usbDevices.Count == 0)
                throw new InvalidOperationException(
                    "iPhone não detectado. Verifique o cabo, desbloqueie o aparelho e aceite 'Confiar neste computador'.");

            return new DeviceSnapshot(rows, "—", "—", null, null, null, Paired: false);
        }

        var diskTask = tools.RunTextAsync("ideviceinfo", ["-q", "com.apple.disk_usage", "-x"], TimeSpan.FromSeconds(8), ct);
        var batteryTask = tools.RunTextAsync("idevicediagnostics", ["ioregentry", "AppleSmartBattery"], TimeSpan.FromSeconds(12), ct);
        var batteryDomainTask = tools.RunTextAsync("ideviceinfo", ["-q", "com.apple.mobile.battery", "-x"], TimeSpan.FromSeconds(8), ct);

        try
        {
            var disk = await diskTask.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(disk))
            {
                foreach (var kv in PlistReader.ToDictionary(disk))
                    rows.Add(new DetailRow("Disk." + kv.Key, "Armazenamento", DeviceLabels.DiskLabel(kv.Key), kv.Value));
            }
        }
        catch (Exception ex) when (ex is TimeoutException or System.Xml.XmlException or InvalidOperationException)
        {
            // armazenamento é opcional
        }

        BatteryReport? battery = null;
        try
        {
            var xml = await batteryTask.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(xml)) battery = BatteryParser.Parse(System.Xml.Linq.XDocument.Parse(xml));
        }
        catch (Exception ex) when (ex is TimeoutException or System.Xml.XmlException or InvalidOperationException)
        {
            // bateria é opcional
        }

        if (battery is not null)
        {
            foreach (var (key, label, value) in BatteryParser.Rows(battery))
                rows.Add(new DetailRow("Battery." + key, "Bateria", label, value));
        }

        string? charge = battery?.Level is { } level ? level.ToString("0", CultureInfo.InvariantCulture) : null;
        string? cycles = battery?.CycleCount is { } cycleCount ? cycleCount.ToString("0", CultureInfo.InvariantCulture) : null;
        double? health = battery?.Health;

        try
        {
            var bq = await batteryDomainTask.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(bq))
            {
                var values = PlistReader.ToDictionary(bq);
                foreach (var kv in values)
                    rows.Add(new DetailRow("BatteryQ." + kv.Key, "Bateria", DeviceLabels.BatteryQLabel(kv.Key), kv.Value));

                charge ??= values.GetValueOrDefault("BatteryCurrentCapacity");
                cycles ??= values.GetValueOrDefault("CycleCount");

                static double? BValue(Dictionary<string, string> map, params string[] keys)
                {
                    foreach (var key in keys)
                        if (map.TryGetValue(key, out var text) &&
                            double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) &&
                            value > 0)
                            return value;
                    return null;
                }

                if (health is null)
                {
                    var rawMax = BValue(values, "AppleRawMaxCapacity", "FullChargeCapacity", "NominalChargeCapacity", "ShutDownFullChargeCapacity");
                    var design = BValue(values, "DesignCapacity");
                    if (rawMax is { } max && design is { } designValue)
                        health = Math.Clamp(Math.Round(max / designValue * 100, MidpointRounding.AwayFromZero), 0, 100);
                }
            }
        }
        catch (Exception ex) when (ex is TimeoutException or System.Xml.XmlException or InvalidOperationException)
        {
            // domínio de bateria é opcional
        }

        var free = rows.FirstOrDefault(x => x.Key is "Disk.AmountDataAvailable" or "Disk.TotalDataAvailable" or "Disk.TotalSystemAvailable")?.Value;
        var total = rows.FirstOrDefault(x => x.Key is "Disk.TotalDataCapacity" or "Disk.TotalSystemCapacity" or "Disk.TotalDiskCapacity")?.Value;
        if (free is not null && total is not null &&
            long.TryParse(free, out var f) && long.TryParse(total, out var t) && t > 0)
        {
            rows.Add(new DetailRow("Summary.Storage", "Armazenamento", "Espaço livre",
                $"{f / 1024.0 / 1024 / 1024:0.0} de {t / 1024.0 / 1024 / 1024:0.0} GB"));
        }

        var type = rows.First(x => x.Key == "ProductType").Value;
        var spec = DeviceSpecs.Get(type);
        var model = spec?.Name ?? type;
        var cpu = spec?.Chip ?? rows.FirstOrDefault(x => x.Key == "CPUArchitecture")?.Value ?? "—";

        return new DeviceSnapshot(rows, model, cpu, charge, cycles, health, Paired: true, Battery: battery);
    }
}
