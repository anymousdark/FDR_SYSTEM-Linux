#!/usr/bin/env bash
# Ferramenta falsa: emula `ideviceinfo` para testar a app sem iPhone.
#   mkdir -p ~/fakebin && cp tools/dev/fake-ideviceinfo.sh ~/fakebin/ideviceinfo
#   chmod +x ~/fakebin/ideviceinfo && export PATH="$HOME/fakebin:$PATH"
set -u

args="$*"

case "$args" in
  *com.apple.disk_usage*)
    cat <<'XML'
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0">
<dict>
  <key>TotalDataCapacity</key><string>127824117760</string>
  <key>TotalDataAvailable</key><string>48234598400</string>
  <key>TotalDiskCapacity</key><string>128000000000</string>
</dict>
</plist>
XML
    ;;
  *com.apple.mobile.battery*)
    cat <<'XML'
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
XML
    ;;
  *" -k "*)
    echo "iPhone14,5"
    ;;
  *)
    cat <<'XML'
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
XML
    ;;
esac
