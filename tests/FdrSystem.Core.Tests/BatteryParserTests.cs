using System.Xml.Linq;
using FdrSystem.Core.Devices;
using Xunit;

namespace FdrSystem.Core.Tests;

public class BatteryParserTests
{
    private const string FullXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <plist version="1.0">
        <dict>
          <key>IORegistry</key>
          <dict>
            <key>CurrentCapacity</key><integer>52</integer>
            <key>CycleCount</key><integer>410</integer>
            <key>DesignCapacity</key><integer>2815</integer>
            <key>FullChargeCapacity</key><integer>2500</integer>
            <key>NominalChargeCapacity</key><integer>2600</integer>
            <key>RemainingCapacity</key><integer>1300</integer>
            <key>Voltage</key><integer>3900</integer>
            <key>Amperage</key><integer>-120</integer>
            <key>IsCharging</key><true/>
            <key>ExternalConnected</key><true/>
            <key>Serial</key><string>F8Y1234567890</string>
            <key>AdapterDetails</key>
            <dict>
              <key>Description</key><string>USB-C 20W</string>
              <key>Watts</key><integer>20000</integer>
              <key>AdapterVoltage</key><integer>9000</integer>
            </dict>
          </dict>
        </dict>
        </plist>
        """;

    private static BatteryReport ParseOrFail(string xml) =>
        BatteryParser.Parse(XDocument.Parse(xml)) ?? throw new InvalidOperationException("Parse devolveu null.");

    [Fact]
    public void Parse_ReadsCapacityCyclesAndCharging()
    {
        var report = ParseOrFail(FullXml);

        Assert.Equal(52, report.Level);
        Assert.Equal(410, report.CycleCount);
        Assert.Equal(2815, report.DesignMah);
        Assert.Equal(2500, report.MaxMah);
        Assert.Equal(2600, report.NominalMah);
        Assert.Equal(1300, report.RemainingMah);
        Assert.Equal(3900, report.VoltageMv);
        Assert.Equal(-120, report.AmperageMa);
        Assert.True(report.Charging);
        Assert.True(report.ExternalConnected);
        Assert.Equal("F8Y1234567890", report.Serial);
        Assert.Equal("USB-C 20W", report.AdapterName);
        Assert.Equal(20, report.AdapterWatts);
        Assert.Equal(9000, report.AdapterVoltageMv);
    }

    [Fact]
    public void Parse_ComputesHealthFromMaxAndDesignCapacity()
    {
        var report = ParseOrFail(FullXml);

        Assert.NotNull(report.HealthRaw);
        Assert.Equal(88.8, report.HealthRaw!.Value, 1);
        Assert.Equal(89, report.Health);
    }

    [Fact]
    public void Parse_FallsBackToAppleRawMaxCapacityOnIos26()
    {
        const string xml = """
            <plist><dict><key>IORegistry</key><dict>
              <key>CurrentCapacity</key><integer>80</integer>
              <key>DesignCapacity</key><integer>3000</integer>
              <key>AppleRawMaxCapacity</key><integer>2400</integer>
            </dict></dict></plist>
            """;

        var report = ParseOrFail(xml);

        Assert.Equal(2400, report.MaxMah);
        Assert.Equal(80, report.Health);
    }

    [Fact]
    public void Parse_ReturnsNullWhenThereIsNoBatteryData()
    {
        const string xml = "<plist><dict><key>Outro</key><string>valor</string></dict></plist>";

        Assert.Null(BatteryParser.Parse(XDocument.Parse(xml)));
    }

    [Fact]
    public void Rows_IncludeHealthAndFlags()
    {
        var rows = BatteryParser.Rows(ParseOrFail(FullXml)).ToDictionary(r => r.Key, r => r.Value);

        Assert.Equal("89", rows["Health"]);
        Assert.Equal("88.8", rows["HealthRaw"]);
        Assert.Equal("52", rows["CurrentCapacity"]);
        Assert.Equal("Sim", rows["IsCharging"]);
    }

    [Fact]
    public void ChargingSummary_DescribesChargerAndSlowCharging()
    {
        var report = ParseOrFail(FullXml) with { AdapterWatts = 5 };

        Assert.StartsWith("A carregar", BatteryParser.ChargingSummary(report));
        Assert.Contains("5 W", BatteryParser.ChargingSummary(report));
        Assert.Contains("lento", BatteryParser.ChargingSummary(report));
    }

    [Fact]
    public void ChargingSummary_HandlesExternalPowerAndDischarging()
    {
        var report = ParseOrFail(FullXml);

        Assert.Equal("Ligado à corrente (sem carga ativa)",
            BatteryParser.ChargingSummary(report with { Charging = false }));
        Assert.Equal("A descarregar",
            BatteryParser.ChargingSummary(report with { Charging = false, ExternalConnected = false }));
    }

    [Fact]
    public void ChargingSummary_ShowsEstimatedAutonomyWhenDischarging()
    {
        var report = ParseOrFail(FullXml) with
        {
            Charging = false,
            ExternalConnected = false,
            AvgTimeToEmptyMin = 95,
        };

        Assert.Contains("autonomia estimada 1 h 35 min", BatteryParser.ChargingSummary(report));
    }
}
