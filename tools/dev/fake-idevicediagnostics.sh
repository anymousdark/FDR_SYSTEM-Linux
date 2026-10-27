#!/usr/bin/env bash
# Ferramenta falsa: emula `idevicediagnostics ioregentry AppleSmartBattery`.
#   cp tools/dev/fake-idevicediagnostics.sh ~/fakebin/idevicediagnostics
#   chmod +x ~/fakebin/idevicediagnostics
set -u

cat <<'XML'
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
XML
