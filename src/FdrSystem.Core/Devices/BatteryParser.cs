using System.Globalization;
using System.Xml.Linq;

namespace FdrSystem.Core.Devices;

/// <summary>
/// Interpretação do ioregistry AppleSmartBattery e resumo da bateria.
/// Portado de MainWindow.xaml.cs (ParseBattery/BatteryRows/BatteryInfoList/
/// ChargingSummary), incluindo o fallback do iOS 26.
/// </summary>
public static class BatteryParser
{
    public static BatteryReport? Parse(XDocument doc)
    {
        var root = doc.Root?.Element("dict");
        if (root is null) return null;

        var ioKey = root.Elements("key").FirstOrDefault(k => k.Value == "IORegistry");
        var io = ioKey?.ElementsAfterSelf().FirstOrDefault();

        static Dictionary<string, XElement> Nodes(XElement parent)
        {
            var map = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var key in parent.Elements("key"))
            {
                var node = key.ElementsAfterSelf().FirstOrDefault();
                if (node is not null && !map.ContainsKey(key.Value)) map[key.Value] = node;
            }
            return map;
        }

        var top = Nodes(io is not null && io.Name.LocalName == "dict" ? io : root);
        var data = top.TryGetValue("BatteryData", out var bd) && bd.Name.LocalName == "dict"
            ? Nodes(bd)
            : new Dictionary<string, XElement>(StringComparer.Ordinal);
        var adapter = top.TryGetValue("AdapterDetails", out var ad) && ad.Name.LocalName == "dict"
            ? Nodes(ad)
            : new Dictionary<string, XElement>(StringComparer.Ordinal);

        static double? Num(Dictionary<string, XElement> map, string key) =>
            map.TryGetValue(key, out var n) && double.TryParse(n.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

        static double? Positive(Dictionary<string, XElement> map, params string[] keys)
        {
            foreach (var key in keys)
                if (Num(map, key) is { } value && value > 0) return value;
            return null;
        }

        static double? Watts(double? raw) => raw is null ? null : raw.Value > 1000 ? raw.Value / 1000.0 : raw.Value;

        static bool? Flag(Dictionary<string, XElement> map, string key) =>
            map.TryGetValue(key, out var n) ? n.Name.LocalName switch { "true" => true, "false" => false, _ => null } : null;

        static string? Str(Dictionary<string, XElement> map, string key) =>
            map.TryGetValue(key, out var n) && n.Name.LocalName == "string" ? n.Value : null;

        double? FirstPositive(string key) => Positive(data, key) ?? Positive(top, key);

        var designMah = FirstPositive("DesignCapacity");
        // iOS 26 pode omitir FullChargeCapacity; usa as capacidades alternativas do registo.
        var maxMah = FirstPositive("FullChargeCapacity")
                     ?? FirstPositive("AppleRawMaxCapacity")
                     ?? FirstPositive("NominalChargeCapacity")
                     ?? FirstPositive("ShutDownFullChargeCapacity");
        var nominalMah = FirstPositive("NominalChargeCapacity");
        var remainingMah = FirstPositive("RemainingCapacity") ?? FirstPositive("AppleRawCurrentCapacity");

        var report = new BatteryReport(
            Level: Num(top, "CurrentCapacity") ?? Num(data, "CurrentCapacity"),
            CycleCount: Num(top, "CycleCount") ?? Num(data, "CycleCount"),
            Serial: Str(top, "Serial") ?? Str(data, "Serial"),
            DesignMah: designMah,
            MaxMah: maxMah,
            NominalMah: nominalMah,
            RemainingMah: remainingMah,
            VoltageMv: Num(top, "Voltage"),
            RawVoltageMv: Num(top, "AppleRawBatteryVoltage"),
            BootVoltageMv: Num(top, "BootVoltage"),
            AmperageMa: Num(top, "Amperage"),
            InstantAmperageMa: Num(top, "InstantAmperage"),
            Charging: Flag(top, "IsCharging"),
            ExternalConnected: Flag(top, "ExternalConnected"),
            AtCriticalLevel: Flag(top, "AtCriticalLevel"),
            TimeRemainingMin: Num(top, "TimeRemaining"),
            AvgTimeToEmptyMin: Num(top, "AvgTimeToEmpty"),
            AdapterName: Str(adapter, "Description"),
            AdapterWatts: Watts(Num(adapter, "Watts")),
            AdapterVoltageMv: Num(adapter, "AdapterVoltage"),
            AdapterCurrentMa: Num(adapter, "Current"));

        return report.Level is null && report.CycleCount is null && report.DesignMah is null && report.MaxMah is null
            ? null
            : report;
    }

    public static IEnumerable<(string Key, string Label, string Value)> Rows(BatteryReport b)
    {
        static string N(double? v, string format) => v is null ? "—" : v.Value.ToString(format, CultureInfo.InvariantCulture);
        static string Vm(double? mv) => mv is null ? "—" : (mv.Value / 1000.0).ToString("0.00", CultureInfo.InvariantCulture);
        static string Sim(bool? v) => v is null ? "—" : v.Value ? "Sim" : "Não";

        yield return ("CurrentCapacity", "Carga atual (%)", N(b.Level, "0"));
        yield return ("DesignCapacity", "Capacidade de projeto (mAh)", N(b.DesignMah, "0"));
        yield return ("FullChargeCapacity", "Capacidade máxima atual (mAh)", N(b.MaxMah, "0"));
        yield return ("NominalChargeCapacity", "Capacidade nominal (mAh)", N(b.NominalMah, "0"));
        yield return ("RemainingCapacity", "Carga restante (mAh)", N(b.RemainingMah, "0"));
        yield return ("CycleCount", "Ciclos de carga", N(b.CycleCount, "0"));
        yield return ("Health", "Saúde estimada (%)", N(b.Health, "0"));
        yield return ("HealthRaw", "Saúde bruta (%)", N(b.HealthRaw, "0.0"));
        yield return ("Voltage", "Voltagem atual (V)", Vm(b.VoltageMv));
        yield return ("BootVoltage", "Voltagem de arranque (V)", Vm(b.BootVoltageMv));
        yield return ("Amperage", "Corrente (mA)", N(b.AmperageMa, "0"));
        yield return ("InstantAmperage", "Corrente instantânea (mA)", N(b.InstantAmperageMa, "0"));
        yield return ("IsCharging", "A carregar", Sim(b.Charging));
    }

    public static List<(string Label, string Value)> InfoList(BatteryReport b, bool reveal)
    {
        static string Vm(double? mv) => mv is null ? "—" : (mv.Value / 1000.0).ToString("0.00", CultureInfo.InvariantCulture);

        var list = new List<(string, string)>();
        if (b.Serial is { Length: > 0 }) list.Add(("Nº de série da bateria", reveal ? b.Serial : DeviceLabels.Mask(b.Serial)));
        if (b.Level is not null) list.Add(("Carga atual", $"{b.Level:0}%"));
        if (b.DesignMah is not null) list.Add(("Capacidade de projeto", $"{b.DesignMah:0} mAh"));
        if (b.MaxMah is not null) list.Add(("Capacidade máxima atual", $"{b.MaxMah:0} mAh"));
        if (b.NominalMah is not null) list.Add(("Capacidade nominal", $"{b.NominalMah:0} mAh"));
        if (b.RemainingMah is not null) list.Add(("Carga restante", $"{b.RemainingMah:0} mAh"));
        if (b.CycleCount is not null) list.Add(("Ciclos de carga", $"{b.CycleCount:0}"));
        if (b.HealthRaw is not null) list.Add(("Saúde bruta (sem arredondamento)", $"{b.HealthRaw:0.0}%"));
        if (b.HealthRaw is { } raw && raw >= 79.5 && raw < 80.5) list.Add(("Limite de serviço", "80% (no limite)"));
        if (b.VoltageMv is not null) list.Add(("Voltagem atual", Vm(b.VoltageMv) + " V"));
        if (b.BootVoltageMv is not null) list.Add(("Voltagem de arranque", Vm(b.BootVoltageMv) + " V"));
        if (b.AmperageMa is not null) list.Add(("Corrente", $"{b.AmperageMa:0} mA"));
        if (b.InstantAmperageMa is not null) list.Add(("Corrente instantânea", $"{b.InstantAmperageMa:0} mA"));
        if (b.Charging == true && b.AdapterWatts is not null)
            list.Add(("Carregador", (b.AdapterName ?? "ligado") + $" · {b.AdapterWatts:0.#} W"));
        if (b.AtCriticalLevel is not null) list.Add(("Nível crítico", b.AtCriticalLevel.Value ? "Sim" : "Não"));
        if (b.Charging != true && b.AvgTimeToEmptyMin is { } autonomy && autonomy is > 0 and < 1440)
            list.Add(("Autonomia estimada", DeviceLabels.FormatMinutes(autonomy)));
        return list;
    }

    public static string ChargingSummary(BatteryReport b)
    {
        if (b.Charging == true)
        {
            var extra = "";
            if (b.AdapterWatts is not null)
            {
                var volts = b.AdapterVoltageMv is not null
                    ? (b.AdapterVoltageMv.Value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " V · "
                    : "";
                extra = " (" + volts + $"{b.AdapterWatts:0.#} W" + (b.AdapterWatts < 10 ? " — carregamento lento" : "") + ")";
            }
            return "A carregar" + extra;
        }

        if (b.ExternalConnected == true) return "Ligado à corrente (sem carga ativa)";
        if (b.AvgTimeToEmptyMin is { } autonomy && autonomy is > 0 and < 1440)
            return "A descarregar · autonomia estimada " + DeviceLabels.FormatMinutes(autonomy);
        return "A descarregar";
    }
}
