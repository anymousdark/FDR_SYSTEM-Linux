// Ferramenta falsa para testar a app sem iPhone.
// Copia o binÃ¡rio para ideviceinfo.exe / idevicediagnostics.exe e pÃµe no PATH
// (ou em tools/linux/). O nome do executÃ¡vel decide o que Ã© devolvido.
using System.Diagnostics;

var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "ideviceinfo");
var toolArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
File.AppendAllText(Path.Combine(Path.GetTempPath(), "fakeidevice.log"), $"{name} {string.Join(' ', toolArgs)}\n");

const string Info = """
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>DeviceName</key><string>iPhone de Teste</string>
  <key>ProductType</key><string>iPhone14,5</string>
  <key>ProductVersion</key><string>17.4</string>
  <key>BuildVersion</key><string>21E219</string>
  <key>ProductName</key><string>iPhone 13</string>
  <key>ModelNumber</key><string>MLPF3</string>
  <key>RegionInfo</key><string>PT/A</string>
  <key>SerialNumber</key><string>F2LX9A1BQ1GH</string>
  <key>UniqueDeviceID</key><string>00008030-000A1B2C3D4E5F6G</string>
  <key>UniqueChipID</key><string>12345678901234</string>
  <key>ActivationState</key><string>Activated</string>
  <key>HardwareModel</key><string>D16AP</string>
  <key>HardwarePlatform</key><string>t8110</string>
  <key>BoardId</key><string>14</string>
  <key>ChipID</key><string>32789</string>
  <key>CPUArchitecture</key><string>arm64e</string>
  <key>DeviceColor</key><string>1</string>
  <key>TimeZone</key><string>Europe/Lisbon</string>
</dict>
</plist>
""";

const string Disk = """
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0">
<dict>
  <key>TotalDataCapacity</key><string>127824117760</string>
  <key>TotalDataAvailable</key><string>48234598400</string>
  <key>TotalDiskCapacity</key><string>128000000000</string>
</dict>
</plist>
""";

const string BatteryDomain = """
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0">
<dict>
  <key>BatteryCurrentCapacity</key><string>87</string>
  <key>BatteryIsCharging</key><false/>
  <key>AppleRawMaxCapacity</key><string>2980</string>
  <key>DesignCapacity</key><string>3227</string>
  <key>CycleCount</key><string>412</string>
  <key>Serial</key><string>F5D1234567890ABCD</string>
</dict>
</plist>
""";

const string Ioreg = """
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0">
<dict>
  <key>IORegistry</key>
  <dict>
    <key>CurrentCapacity</key><integer>87</integer>
    <key>CycleCount</key><integer>412</integer>
    <key>Voltage</key><integer>4123</integer>
    <key>Amperage</key><integer>-320</integer>
    <key>IsCharging</key><false/>
    <key>ExternalConnected</key><false/>
    <key>AvgTimeToEmpty</key><integer>600</integer>
    <key>BatteryData</key>
    <dict>
      <key>DesignCapacity</key><integer>3227</integer>
      <key>FullChargeCapacity</key><integer>2980</integer>
      <key>NominalChargeCapacity</key><integer>3005</integer>
      <key>RemainingCapacity</key><integer>2600</integer>
      <key>Serial</key><string>F5D1234567890ABCD</string>
    </dict>
  </dict>
</dict>
</plist>
""";

if (name.StartsWith("idevicediagnostics", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(Ioreg);
    return 0;
}

// ideviceinfo
if (toolArgs.Contains("-q") && toolArgs.Contains("com.apple.disk_usage"))
{
    Console.WriteLine(Disk);
    return 0;
}

if (toolArgs.Contains("-q") && toolArgs.Contains("com.apple.mobile.battery"))
{
    Console.WriteLine(BatteryDomain);
    return 0;
}

if (toolArgs.Contains("-k"))
{
    Console.WriteLine("iPhone14,5");
    return 0;
}

Console.WriteLine(Info);
return 0;

