namespace FdrSystem.Core.Devices;

/// <summary>Leitura da bateria a partir do ioregistry (AppleSmartBattery).</summary>
public sealed record BatteryReport(
    double? Level,
    double? CycleCount,
    string? Serial,
    double? DesignMah,
    double? MaxMah,
    double? NominalMah,
    double? RemainingMah,
    double? VoltageMv,
    double? RawVoltageMv,
    double? BootVoltageMv,
    double? AmperageMa,
    double? InstantAmperageMa,
    bool? Charging,
    bool? ExternalConnected,
    bool? AtCriticalLevel,
    double? TimeRemainingMin,
    double? AvgTimeToEmptyMin,
    string? AdapterName,
    double? AdapterWatts,
    double? AdapterVoltageMv,
    double? AdapterCurrentMa)
{
    public double? HealthRaw => DesignMah is > 0 && MaxMah is > 0
        ? Math.Clamp(MaxMah.Value * 100.0 / DesignMah.Value, 0, 100)
        : null;

    public double? Health => HealthRaw is { } raw
        ? Math.Round(raw, MidpointRounding.AwayFromZero)
        : null;
}
