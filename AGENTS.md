# AGENTS.md — instruções para agentes de IA

Port de **FDR_SYSTEM** (WPF/Windows) para **Linux** com **Avalonia 11.3.x**.
Lê o `README.md` primeiro; este ficheiro é o resumo operacional. Para retomar numa máquina
Linux, lê `docs/LINUX-FULL-GUIDE.md` (guia completo) e `docs/CONTINUE-ON-LINUX.md`
(resumo).

## Regras de ouro

1. **Não mexer** em `C:\Users\p\Documents\OfflineDeviceDiagnostics` (ou `~/OfflineDeviceDiagnostics`).
   Essa é a app Windows, em produção, com 1.5.9 publicada. Serve **só** como referência de leitura.
2. **Não subir versões** de Avalonia/DataGrid sem testar: DataGrid tem de ficar em `11.3.13`
   (não existe 11.3.22). .NET fica em `net8.0` (não usar `System.Threading.Lock`).
3. **Ficheiros XAML têm de ser `.axaml`**, senão não compilam e o erro engana (`AVLN2000`).
4. **UI em code-behind**, com os mesmos nomes de handlers do original (`Scan_Click`, `Nav_Click`…).
   Não converter para MVVM a meio do port.
5. **Português de Portugal** nas strings, iguais às do original.
6. **Mascarar identificadores** por omissão; obedecer ao `Reveal IDs`.
7. A app **não** contorna iCloud/Activation Lock nem MDM. Não implementar isso, mesmo que peçam
   "para testar".

## Comandos

```bash
sudo bash packaging/install-linux-deps.sh                        # Debian/Ubuntu/Kali
bash packaging/check-linux-env.sh                                # estado do ambiente
dotnet build src/FdrSystem.App/FdrSystem.App.csproj -c Debug     # ~10 s
dotnet run   --project src/FdrSystem.App/FdrSystem.App.csproj    # precisa de display (X11/Wayland)
bash packaging/build-linux.sh                                    # publish + .deb (se houver dpkg-deb)
```

Sem iPhone nem display, o ciclo útil é: compilar → correr as ferramentas falsas de
`tools/dev/` para validar parsing → ver logs em `~/.local/share/FDR_SYSTEM/logs/`.

## Onde está o quê

| Quero… | Ficheiro |
|---|---|
| Adicionar/trocar página | `src/FdrSystem.App/Views/MainWindow.axaml.cs` → `ShowPage(string tag)` |
| Correr ferramentas `idevice*` | `src/FdrSystem.Core/Tools/ToolRunner.cs` |
| Diagnosticar (syslog, crash, pair, provision) | `src/FdrSystem.App/Views/Pages/DiagnosticoView.axaml(.cs)` |
| Acessibilidade/AFC | `src/FdrSystem.App/Views/Pages/AcessibilidadeView.axaml(.cs)` + `src/FdrSystem.Core/Devices/AfcSession.cs` |
| Driver / usbmuxd / udev | `src/FdrSystem.App/Views/Pages/DriverView.axaml(.cs)` + `src/FdrSystem.Core/Usb/DriverEnvironment.cs` |
| Ler dados do iPhone | `src/FdrSystem.Core/Devices/DeviceService.cs` |
| Etiquetas/máscaras PT | `src/FdrSystem.Core/Devices/DeviceLabels.cs` |
| Cores/temas | `src/FdrSystem.App/Themes/*.axaml` + `Services/ThemeManager.cs` |
| Estilos de botões/cartões/nav | `src/FdrSystem.App/Styles/Controls.axaml` |
| Caminhos XDG | `src/FdrSystem.Core/AppPaths.cs` |
| Instalar dependências / diagnosticar Linux | `packaging/install-linux-deps.sh`, `packaging/check-linux-env.sh` |

## Tarefas em aberto (por ordem)

- [x] **Diagnóstico** — `idevicesyslog` (stream), `idevicecrashreport -e -k` → ZIP,
      `idevicepair validate|pair`, `ideviceprovision list`. Implementado em
      `Views/Pages/DiagnosticoView.axaml(.cs)`.
- [x] **Acessibilidade (AFC)** — `AfcSession` (P/Invoke de `libimobiledevice-1.0.so`),
      navegação, importar/exportar, DCIM, apagar com confirmação e captura de ecrã.
      Implementado em `Views/Pages/AcessibilidadeView.axaml(.cs)`.
- [x] **Driver** — `DriverEnvironment` lê socket do `usbmuxd`, serviço systemd, regras udev,
      grupos e dispositivos; UI em `Views/Pages/DriverView.axaml(.cs)`. Testado no Windows
      de desenvolvimento; falta validar no Linux.
- [x] **Online** — `Core/Firmware/DownloadManager.cs` + `IpswCatalog.cs`, UI em
      `Views/Pages/OnlineView.axaml(.cs)`.
- [x] **Firmware** — `Views/Pages/FirmwareView.axaml(.cs)` com cache em `~/.local/share/FDR_SYSTEM/cache`.
- [x] **Restauro** — `Core/Firmware/RestoreEngine.cs` (progresso `progress: <passo> <fração>`)
      + `Views/Pages/RestauroView.axaml(.cs)`; recusa IPSW pequeno/incompatível.
- [x] **Reparo** — `Views/Pages/ReparoView.axaml(.cs)`: `UsbScanner` + `IrecoveryDevices`,
      `ideviceenterrecovery`, `idevicediagnostics restart`, `irecovery -n`, `ideviceactivation state`
      e chaves MDM de `ideviceinfo -x`; só leitura, sem `sudo`.
- [x] **Jailbreak** — `Views/Pages/JailbreakView.axaml(.cs)`: deteta `palera1n`/`checkra1n`
      (`Locate` + `--version` só no palera1n), mostra caminho, comandos e avisos; nunca executa
      as ferramentas, nunca corre `sudo`, não contorna iCloud/Activation Lock/MDM.
- [ ] **Empacotamento** — `.deb` completo + AppImage.
- [ ] **Teste no hardware** — correr no Linux com iPhone real e registar o que falha.

## Armadilhas que já custaram tempo

- `ContentPresenter` sem `Content="{TemplateBinding Content}"` → nav invisível.
- `FluentTheme.RequestedThemeVariant` não existe → usar `Application.Current.RequestedThemeVariant`.
- `avares://` usa o **AssemblyName** (`FDR_SYSTEM`), não o namespace.
- Bindings compilados desligados de propósito (`AvaloniaUseCompiledBindingsByDefault=false`).
- `dotnet publish -r linux-x64` demora (descarrega runtime pack) e **restore pode falhar** com
  `ResponseEnded`: repetir o comando, o cache fica.

## Definição de pronto (por página)

Uma página só conta como portada quando: compila, aparece na navegação, tem os botões do
original, escreve logs por `AppLog`, mascara IDs, e tem um caminho explícito para "ferramenta
ausente" (mensagem clara, sem crash).
