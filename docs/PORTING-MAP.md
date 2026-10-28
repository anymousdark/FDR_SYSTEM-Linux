# Mapa do port — WPF → Avalonia/Linux

Referência: `C:\Users\p\Documents\OfflineDeviceDiagnostics` (só leitura).

## Ficheiros já portados

| Original (WPF) | Linhas | Destino | Notas |
|---|---|---|---|
| `DeviceSpecs.cs` | 80 | `src/FdrSystem.Core/Specs/DeviceSpecs.cs` | cópia quase literal, `public` |
| `IrecoveryDevices.cs` | 367 | `src/FdrSystem.Core/Devices/IrecoveryDevices.cs` | tabela CPID/BDID → modelo, cópia |
| `PnpUsbClient.cs` | 147 | `src/FdrSystem.Core/Usb/UsbScanner.cs` | SetupAPI mantido p/ Windows + sysfs novo p/ Linux |
| `LockdownClient.cs` | 118 | *(por portar)* | `InteropBootstrap` já resolve `libimobiledevice-1.0.so.6` |
| `AfcClient.cs` | 328 | `src/FdrSystem.Core/Devices/AfcSession.cs` | AFC nativa sobre `libimobiledevice-1.0.so`; UI em `AcessibilidadeView` |
| `MainWindow.Diagnostics.cs` | 468 | `src/FdrSystem.App/Views/Pages/DiagnosticoView.axaml(.cs)` | syslog, crash ZIP, pair e provision |
| `DownloadManager.cs` | 605 | *(por portar)* | motor segmentado + SHA-256 |
| `MainWindow.xaml.cs` (leitura, 780–902) | ~120 | `Devices/DeviceService.cs` | troca de `.exe` por ferramentas do PATH |
| `MainWindow.xaml.cs` (etiquetas 1554–1694) | ~140 | `Devices/DeviceLabels.cs` | `GroupOf`, `LabelOf`, `DiskLabel`, `BatteryQLabel`, `Mask`, `DisplayValue` |
| `MainWindow.xaml.cs` (bateria 1676–1809) | ~130 | `Devices/BatteryParser.cs` | inclui fallback do iOS 26 |
| `Themes/*.xaml` (5) | 5×79 | `src/FdrSystem.App/Themes/*.axaml` | convertidos automaticamente |
| `MainWindow.xaml` (shell) | 1683 | `Views/MainWindow.axaml` | cabeçalho e sidebar; sem `WindowChrome` (decorations nativas) |

## Ainda por portar (com pistas)

### `MainWindow.Window.cs` (72 linhas)
Não é preciso: só resolvia a área de trabalho maximizada no Windows (`MonitorFromWindow`).
No Avalonia usar `WindowState="Maximized"`.

### `MainWindow.Updates.cs` (742 linhas)
**Não portar como está.** O updater usa `.hdiff` + `hpatchz.exe` + `Inno Setup`. No Linux:
`.deb` com `apt`, ou `AppImageUpdate`. Deixar para o fim.

### Restantes páginas
`MainWindow.xaml.cs` tem ~2750 linhas. Offline, Diagnóstico e Acessibilidade já estão
portados; faltam Online/Firmware/Reparo/Driver/Jailbreak. Portar por blocos, uma página por
vez, com o mesmo nome de handler.

## Diferenças de plataforma a respeitar

| Windows | Linux |
|---|---|
| `usbaapl64`, UsbDk, `appleusb.inf` | `usbmuxd` + regras udev (pacote `usbmuxd`) |
| `tools/libimobiledevice/*.exe` | `libimobiledevice-utils` do sistema (PATH) |
| `WinRa1n` (checkm8) | `palera1n`/`checkra1n` instalados pelo utilizador |
| `%LOCALAPPDATA%` | `~/.local/share` (XDG) |
| `Documentos` | `~/Documents` / `XDG_DOCUMENTS_DIR` |
| `.ico` | `.png`/`.svg` + `.desktop` |
| UAC / `runas` | `pkexec` (só para operações que realmente precisem) |
