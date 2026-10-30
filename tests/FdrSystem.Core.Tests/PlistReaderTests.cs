using FdrSystem.Core.Devices;
using Xunit;

namespace FdrSystem.Core.Tests;

public class PlistReaderTests
{
    [Fact]
    public void ToDictionary_ReadsTopLevelStringValues()
    {
        const string xml = """
            <plist version="1.0"><dict>
              <key>DeviceName</key><string>iPhone de Teste</string>
              <key>ProductVersion</key><string>27.2</string>
              <key>ActivationState</key><string>Activated</string>
            </dict></plist>
            """;

        var values = PlistReader.ToDictionary(xml);

        Assert.Equal(3, values.Count);
        Assert.Equal("iPhone de Teste", values["DeviceName"]);
        Assert.Equal("27.2", values["ProductVersion"]);
        Assert.Equal("Activated", values["ActivationState"]);
    }

    [Fact]
    public void ToDictionary_IgnoresNestedContainers()
    {
        const string xml = """
            <plist><dict>
              <key>Simple</key><string>ok</string>
              <key>Nested</key><dict><key>Inner</key><string>nao</string></dict>
            </dict></plist>
            """;

        var values = PlistReader.ToDictionary(xml);

        Assert.Single(values);
        Assert.Equal("ok", values["Simple"]);
    }

    [Fact]
    public void ToDictionary_ReturnsEmptyForBlankInput()
    {
        Assert.Empty(PlistReader.ToDictionary(""));
        Assert.Empty(PlistReader.ToDictionary("   "));
        Assert.Empty(PlistReader.ToDictionary((string)null!));
    }
}
