using FdrSystem.Core.Devices;
using Xunit;

namespace FdrSystem.Core.Tests;

public class DeviceLabelsTests
{
    [Theory]
    [InlineData("Battery.CycleCount", "—")]
    [InlineData("Disk.TotalDiskCapacity", "—")]
    [InlineData("SerialNumber", "Identificação")]
    [InlineData("UniqueDeviceID", "Identificação")]
    [InlineData("ProductVersion", "Sistema")]
    [InlineData("BuildVersion", "Sistema")]
    [InlineData("BasebandVersion", "Rede / Modem")]
    [InlineData("WiFiAddress", "Conectividade")]
    [InlineData("TimeZone", "Data / Hora")]
    [InlineData("HardwareModel", "Hardware")]
    [InlineData("PkHash", "Segurança")]
    [InlineData("CampoDesconhecido", "Outros")]
    public void GroupOf_GroupsKnownKeys(string key, string expected) =>
        Assert.Equal(expected, DeviceLabels.GroupOf(key));

    [Theory]
    [InlineData("DeviceName", "Nome do dispositivo")]
    [InlineData("ProductVersion", "Versão do iOS")]
    [InlineData("UniqueDeviceID", "UDID")]
    [InlineData("IMEI", "IMEI")]
    [InlineData("WiFiAddress", "Endereço Wi-Fi (MAC)")]
    [InlineData("CycleCount", "CycleCount")]
    public void LabelOf_TranslatesKnownKeys(string key, string expected) =>
        Assert.Equal(expected, DeviceLabels.LabelOf(key));

    [Theory]
    [InlineData("ABCD1234", "••••1234")]
    [InlineData("AB", "••")]
    [InlineData("A", "•")]
    [InlineData("", "•")]
    [InlineData("123456789", "•••••6789")]
    public void Mask_KeepsOnlyTheLastFourCharacters(string value, string expected) =>
        Assert.Equal(expected, DeviceLabels.Mask(value));

    [Theory]
    [InlineData("SerialNumber", true)]
    [InlineData("UniqueChipID", true)]
    [InlineData("InternationalMobileEquipmentIdentity", true)]
    [InlineData("WiFiAddress", true)]
    [InlineData("ProductType", false)]
    [InlineData("DeviceName", false)]
    [InlineData("Battery.CycleCount", false)]
    public void IsSensitive_ProtectsIdentifiers(string key, bool expected) =>
        Assert.Equal(expected, DeviceLabels.IsSensitive(key));

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(-1, "—")]
    public void FormatBytes_UsesBinaryUnits(long bytes, string expected) =>
        Assert.Equal(expected, DeviceLabels.FormatBytes(bytes));

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(30, "30 min")]
    [InlineData(59, "59 min")]
    [InlineData(60, "1 h 0 min")]
    [InlineData(90, "1 h 30 min")]
    public void FormatMinutes_FormatsHoursAndMinutes(double minutes, string expected) =>
        Assert.Equal(expected, DeviceLabels.FormatMinutes(minutes));

    [Theory]
    [InlineData(95, "Boa")]
    [InlineData(90, "Boa")]
    [InlineData(89.9, "Razoável")]
    [InlineData(80, "Razoável")]
    [InlineData(79.9, "Fraca")]
    public void HealthWord_UsesWindowsThresholds(double health, string expected) =>
        Assert.Equal(expected, DeviceLabels.HealthWord(health));

    [Fact]
    public void DisplayValue_MasksSensitiveFieldsUnlessRevealed()
    {
        var row = new DetailRow("SerialNumber", "Identificação", "Número de série (SN)", "F17G12345678");

        Assert.Equal("••••••••5678", DeviceLabels.DisplayValue(row, reveal: false));
        Assert.Equal("F17G12345678", DeviceLabels.DisplayValue(row, reveal: true));
    }

    [Fact]
    public void DisplayValue_FormatsDiskBytesInGigabytes()
    {
        var row = new DetailRow("Disk.TotalDiskCapacity", "—", "Capacidade total", "64000000000");

        Assert.Equal("59,6 GB", DeviceLabels.DisplayValue(row, reveal: false));
    }
}
