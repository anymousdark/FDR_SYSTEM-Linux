using FdrSystem.Core.Firmware;
using Xunit;

namespace FdrSystem.Core.Tests;

public class CatalogTests
{
    [Fact]
    public void ModelsInFileName_ExtractsDistinctIdentifiers()
    {
        var models = IpswInspection.ModelsInFileName("iPhone14,5_iPhone14,5_17.0_21A329.ipsw");

        Assert.Single(models);
        Assert.Equal("iPhone14,5", models[0]);
    }

    [Fact]
    public void ModelsInFileName_HandlesMultipleModelsAndCase()
    {
        var models = IpswInspection.ModelsInFileName("iPhone13,2,iPhone13,3,iPhone13,4.ipsw");

        Assert.Equal(new[] { "iPhone13,2", "iPhone13,3", "iPhone13,4" }, models);
    }

    [Fact]
    public void ModelsInFileName_ReturnsEmptyForUnrelatedNames() =>
        Assert.Empty(IpswInspection.ModelsInFileName("firmware-17.0.ipsw"));

    [Fact]
    public void IpswFirmware_FormatsSignedStateSizeAndDate()
    {
        var firmware = new IpswFirmware(
            "17.0", "21A329", "https://example.test/a.ipsw", 5_000_000_000, "abc", true,
            new DateTimeOffset(2023, 9, 18, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal("Assinado", firmware.SignedText);
        Assert.Equal("4,66 GB", firmware.SizeText);
        Assert.Equal("18/09/2023", firmware.DateText);
    }

    [Fact]
    public void IpswFirmware_MarksExpiredFirmware()
    {
        var firmware = new IpswFirmware("16.0", "20A", "", 0, null, false, null);

        Assert.Equal("Expirado", firmware.SignedText);
        Assert.Equal("—", firmware.SizeText);
        Assert.Equal("—", firmware.DateText);
    }

    [Fact]
    public void IpswDeviceInfo_FindsLatestSignedAndLatestOverall()
    {
        var firmwares = new List<IpswFirmware>
        {
            new("18.0", "22A", "u1", 1, null, false, null),
            new("17.7", "21H", "u2", 2, null, true, null),
            new("17.6", "21G", "u3", 3, null, true, null),
        };
        var device = new IpswDeviceInfo("iPhone14,5", "iPhone 13", firmwares);

        Assert.Equal("18.0", device.Latest!.Version);
        Assert.Equal("17.7", device.LatestSigned!.Version);
    }
}
