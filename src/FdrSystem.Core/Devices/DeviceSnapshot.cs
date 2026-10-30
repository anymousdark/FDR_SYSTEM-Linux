namespace FdrSystem.Core.Devices;

/// <summary>Resultado de uma leitura do iPhone.</summary>
public sealed record DeviceSnapshot(
    List<DetailRow> Rows,
    string Model,
    string Cpu,
    string? Charge,
    string? Cycles,
    double? Health,
    bool Paired,
    BatteryReport? Battery = null,
    string? ProductType = null,
    string? Mode = null);
