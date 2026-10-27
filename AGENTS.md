# AGENTS.md — instruções para agentes de IA

Port de **FDR_SYSTEM** (WPF/Windows) para **Linux** com **Avalonia 11.3.x**.
Lê o `README.md` primeiro; este ficheiro é o resumo operacional.

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
| Ler dados do iPhone | `src/FdrSystem.Core/Devices/DeviceService.cs` |
| Etiquetas/máscaras PT | `src/FdrSystem.Core/Devices/DeviceLabels.cs` |
| Cores/temas | `src/FdrSystem.App/Themes/*.axaml` + `Services/ThemeManager.cs` |
| Estilos de botões/cartões/nav | `src/FdrSystem.App/Styles/Controls.axaml` |
| Caminhos XDG | `src/FdrSystem.Core/AppPaths.cs` |

## Tarefas em aberto (por ordem)

- [ ] **Diagnóstico** — `idevicesyslog` (stream), `idevicecrashreport -e -k` → ZIP,
      `idevicepair validate|pair`, `ideviceprovision list`. Página nova: `Views/Pages/DiagnosticoView`.
- [ ] **Acessibilidade (AFC)** — portar `AfcClient.cs` do original (P/Invoke igual; muda só a lib).
- [ ] **Driver** — estado do `usbmuxd`, regras udev, `UsbScanner.DescribeEnvironment()`.
- [ ] **Online / Firmware / Restauro** — portar `DownloadManager.cs` (segmentado, SHA-256) e
      `idevicerestore` com progresso por stdout.
- [ ] **Reparo** — recovery/restart/ativação; MDM só deteção.
- [ ] **Jailbreak** — abrir `palera1n`/`checkra1n` do sistema.
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
