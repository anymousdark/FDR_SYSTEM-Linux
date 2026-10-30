using System.Globalization;

namespace FdrSystem.Core.Devices;

/// <summary>
/// Etiquetas em português, agrupamento e mascaramento dos campos.
/// Portado de MainWindow.xaml.cs (GroupOf/LabelOf/DiskLabel/BatteryQLabel/Mask).
/// </summary>
public static class DeviceLabels
{
    public static string GroupOf(string key)
    {
        if (key.StartsWith("Battery.", StringComparison.Ordinal) || key.StartsWith("Disk.", StringComparison.Ordinal)) return "—";
        if (key is "PkHash" or "BasebandMasterKeyHash" or "BasebandCertId" or "CertID" or "PasswordProtected" or "PairRecordProtectionClass" or "UseRaptorCerts" or "FusingStatus" or "GID1" or "GID2" or "SoftwareBehavior") return "Segurança";
        if (key.StartsWith("Baseband", StringComparison.Ordinal) || key.StartsWith("InternationalMobile", StringComparison.Ordinal) || key.StartsWith("MobileSubscriber", StringComparison.Ordinal) || key.StartsWith("SIM", StringComparison.Ordinal) || key is "PhoneNumber" or "MobileEquipmentIdentifier" or "kCTPostponementStatus") return "Rede / Modem";
        if (key.Contains("Address", StringComparison.Ordinal) || key is "EthernetAddress" or "BluetoothAddress") return "Conectividade";
        if (key.StartsWith("Time", StringComparison.Ordinal) || key is "TimeZone" or "TimeZoneOffsetFromUTC") return "Data / Hora";
        if (key is "SerialNumber" or "UniqueChipID" or "UniqueDeviceID" or "DeviceName" or "DeviceClass" or "ProductType" or "ProductName" or "ModelNumber" or "RegionInfo" or "ActivationState" or "DeviceColor" or "DeviceEnclosureColor" or "DeviceBackingColor" or "DeviceCoverGlassColor" or "DeviceHousingColor" or "UsbUdid" or "UsbName") return "Identificação";
        if (key is "ProductVersion" or "BuildVersion" or "HumanReadableProductVersionString" or "FirmwareVersion" or "SoftwareBundleVersion") return "Sistema";
        if (key is "HardwareModel" or "HardwarePlatform" or "BoardId" or "ChipID" or "DieID" or "CPUArchitecture" or "ProductionSOC" or "ChipSerialNo" or "PartitionType" or "WirelessBoardSerialNumber" or "MLBSerialNumber") return "Hardware";
        return "Outros";
    }

    public static string LabelOf(string key) => key switch
    {
        "DeviceName" => "Nome do dispositivo",
        "ProductType" => "Tipo de produto",
        "ProductName" => "Nome do produto",
        "ProductVersion" => "Versão do iOS",
        "BuildVersion" => "Build do iOS",
        "HumanReadableProductVersionString" => "Versão legível",
        "DeviceClass" => "Classe do dispositivo",
        "ModelNumber" => "Número de modelo",
        "RegionInfo" => "Região",
        "SerialNumber" => "Número de série (SN)",
        "UniqueDeviceID" => "UDID",
        "UniqueChipID" => "ECID (UniqueChipID)",
        "ActivationState" => "Estado de ativação",
        "DeviceColor" => "Cor",
        "HardwareModel" => "Modelo de hardware",
        "HardwarePlatform" => "Plataforma de hardware",
        "BoardId" => "Board ID",
        "ChipID" => "Chip ID",
        "DieID" => "Die ID",
        "CPUArchitecture" => "Arquitetura da CPU",
        "ProductionSOC" => "SOC de produção",
        "FirmwareVersion" => "Versão de firmware",
        "BasebandVersion" => "Versão do modem",
        "BasebandChipID" => "Chip ID do modem",
        "BasebandSerialNumber" => "Nº de série do modem",
        "BasebandMasterKeyHash" => "Hash da chave do modem",
        "InternationalMobileEquipmentIdentity" => "IMEI",
        "InternationalMobileEquipmentIdentity2" => "IMEI 2",
        "MobileEquipmentIdentifier" => "MEID",
        "InternationalMobileSubscriberIdentity" => "IMSI",
        "IntegratedCircuitCardIdentity" => "ICCID",
        "PhoneNumber" => "Número de telefone",
        "WiFiAddress" => "Endereço Wi-Fi (MAC)",
        "BluetoothAddress" => "Endereço Bluetooth (MAC)",
        "EthernetAddress" => "Endereço Ethernet (MAC)",
        "PkHash" => "PkHash",
        "PasswordProtected" => "Protegido por palavra-passe",
        "TimeZone" => "Fuso horário",
        "TimeZoneOffsetFromUTC" => "Desvio do UTC",
        "DeviceSupportsEnhancedAC2" => "Suporta AC2 melhorado",
        _ => key,
    };

    public static string DiskLabel(string key) => key switch
    {
        "TotalDiskCapacity" => "Capacidade total (bytes)",
        "TotalDataCapacity" => "Capacidade total de dados (bytes)",
        "TotalDataAvailable" => "Espaço livre (bytes)",
        "TotalSystemCapacity" => "Capacidade do sistema (bytes)",
        "TotalSystemAvailable" => "Sistema livre (bytes)",
        "AmountDataAvailable" => "Disponível p/ restauro (bytes)",
        "AmountRestoreAvailable" => "Disponível p/ restauro máx. (bytes)",
        "AmountDataReserved" => "Reservado (bytes)",
        "UsedCapacity" => "Capacidade usada (bytes)",
        _ => key,
    };

    public static string BatteryQLabel(string key) => key switch
    {
        "BatteryCurrentCapacity" => "Carga atual (%)",
        "BatteryIsCharging" => "A carregar",
        "AppleRawMaxCapacity" => "Capacidade máxima bruta",
        "AppleRawCurrentCapacity" => "Capacidade atual bruta",
        "CycleCount" => "Ciclos de carga",
        "DesignCapacity" => "Capacidade de projeto",
        "Serial" => "Serial da bateria",
        _ => key,
    };

    public static bool IsSensitive(string key) => key switch
    {
        "SerialNumber" or "UniqueChipID" or "UniqueDeviceID" or "UsbUdid" or "UsbEcid" or "UsbSerial" or "DieID" or "ChipSerialNo"
            or "InternationalMobileEquipmentIdentity" or "InternationalMobileEquipmentIdentity2"
            or "MobileEquipmentIdentifier" or "InternationalMobileSubscriberIdentity"
            or "IntegratedCircuitCardIdentity" or "PhoneNumber"
            or "WiFiAddress" or "BluetoothAddress" or "EthernetAddress"
            or "PkHash" or "BasebandMasterKeyHash" or "BasebandSerialNumber" or "BasebandCertId"
            or "WirelessBoardSerialNumber" or "MLBSerialNumber" or "GID1" or "GID2" => true,
        _ => false,
    };

    public static string Mask(string value) =>
        value.Length <= 4
            ? new string('•', Math.Max(value.Length, 1))
            : new string('•', Math.Max(4, value.Length - 4)) + value[^4..];

    public static string DisplayValue(DetailRow row, bool reveal)
    {
        var value = IsSensitive(row.Key) && !reveal ? Mask(row.Value) : row.Value;
        if (!row.Key.StartsWith("Disk.", StringComparison.Ordinal)) return value;
        return long.TryParse(row.Value, out var bytes) && bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024.0 / 1024 / 1024:0.0} GB"
            : value;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "—";
        var sizes = new[] { "B", "KB", "MB", "GB", "TB" };
        var order = Math.Min(sizes.Length - 1, (int)Math.Floor(Math.Log10(Math.Max(bytes, 1)) / Math.Log10(1024)));
        var magnitude = Math.Pow(1024, order);
        return $"{bytes / magnitude:0.##} {sizes[order]}";
    }

    public static string FormatMinutes(double minutes) =>
        minutes >= 60
            ? $"{(int)(minutes / 60)} h {(int)minutes % 60:0} min"
            : $"{minutes:0} min";

    public static string HealthWord(double health) => health >= 90 ? "Boa" : health >= 80 ? "Razoável" : "Fraca";
}
