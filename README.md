# FDR_SYSTEM — Linux (Avalonia)

> Port do **FDR_SYSTEM 1.5.9** (C# + WPF, Windows) para **Linux**, em **Avalonia UI**.
> Repositório **privado**. A aplicação Windows original não é alterada por este projeto.

**Estado atual:** alpha técnico. A shell, os temas e a página **Offline** funcionam; as páginas
**Diagnóstico**, **Acessibilidade (AFC)**, **Driver** e **Online** estão implementadas. Diagnóstico e AFC
foram testados com um iPhone real no ambiente Windows de desenvolvimento; falta validar tudo
numa instalação Linux. As outras cinco páginas ainda são placeholders. O projeto compila e
gera um binário `linux-x64` self-contained.

> **A continuar numa máquina Linux?** Começa pelo guia completo
> [`docs/LINUX-FULL-GUIDE.md`](docs/LINUX-FULL-GUIDE.md): dependências, build, testes,
> implementação das páginas e diagnóstico de “só aparece Offline”.
> Há também um resumo em [`docs/CONTINUE-ON-LINUX.md`](docs/CONTINUE-ON-LINUX.md).
>
> Atalhos: `sudo bash packaging/install-linux-deps.sh` e
> `bash packaging/check-linux-env.sh`.

---

## Índice

1. [O que é](#1-o-que-é)
2. [Estado atual](#2-estado-atual)
3. [Requisitos no Linux](#3-requisitos-no-linux)
4. [Clonar e compilar](#4-clonar-e-compilar)
5. [Correr a aplicação](#5-correr-a-aplicação)
6. [Publicar e empacotar](#6-publicar-e-empacotar)
7. [Estrutura do repositório](#7-estrutura-do-repositório)
8. [Testar sem iPhone](#8-testar-sem-iphone)
9. [Segurança e limites](#9-segurança-e-limites)
10. [Roadmap para o GUI completo](#10-roadmap-para-o-gui-completo)
11. [Problemas comuns](#11-problemas-comuns)
12. [Privacidade e licença](#12-privacidade-e-licença)

---

## 1. O que é

O FDR_SYSTEM é uma ferramenta de diagnóstico, leitura e recuperação de iPhones por USB.
Esta versão Linux mantém a lógica do projeto Windows, mas substitui a camada WPF por
**Avalonia 11.3.x** e usa as ferramentas oficiais do Linux:

- `usbmuxd` para comunicação USB;
- `libimobiledevice` (`ideviceinfo`, `idevicediagnostics`, `idevicepair`, …);
- `idevicerestore` para restauro, quando instalado;
- `sysfs` para deteção USB.

Não são necessários drivers Apple, UsbDk, WinRa1n, libwdi ou certificados self-signed.
No Linux, esse papel é desempenhado pelo `usbmuxd`.

---

## 2. Estado atual

| Área | Estado |
|---|---|
| Shell Avalonia, navegação, temas e definições | ✅ funcional |
| Página **Offline** | ✅ funcional |
| Leitura de dispositivo via `ideviceinfo` | ✅ funcional |
| Bateria via `idevicediagnostics` | ✅ funcional |
| Exportação JSON, cópia e máscara de IDs | ✅ funcional |
| Página **Diagnóstico** | ✅ implementada (syslog, crash ZIP, pair, provision; teste real no Windows, Linux pendente) |
| Página **Acessibilidade (AFC)** | ✅ implementada (navegar, importar/exportar, DCIM, captura; teste real no Windows, Linux pendente) |
| Página **Driver** | ✅ implementada (usbmuxd, udev, grupos, dispositivos; teste Linux pendente) |
| Página **Online** | ✅ implementada (catálogo `api.ipsw.me`, download segmentado com retoma, SHA-256) |
| Restauro | ⬜ placeholder |
| Reparo | ⬜ placeholder |
| Firmware | ⬜ placeholder |
| Jailbreak | ⬜ placeholder |
| Empacotamento `.deb` | 🟡 básico |
| AppImage | ⬜ por fazer |
| Teste com iPhone real em Linux | ⬜ por fazer |

O código dos placeholders está em
`src/FdrSystem.App/Views/Pages/PlaceholderView.cs`. Cada página nova entra no `switch`
de `MainWindow.ShowPage(string tag)`.

---

## 3. Requisitos no Linux

Alvo: Debian/Ubuntu/Kali. Comandos de instalação:

```bash
sudo apt update

# .NET 8 SDK
sudo apt install -y dotnet-sdk-8.0

# Comunicação com o iPhone
sudo apt install -y libimobiledevice-utils libimobiledevice-1.0-6 usbmuxd

# Opcional: restauro
sudo apt install -y idevicerestore || true

# Opcional: gerar o .deb
sudo apt install -y dpkg-deb fakeroot
```

Se o `apt` da tua distribuição não tiver `dotnet-sdk-8.0`, instala o SDK 8 pela página
oficial da Microsoft para Linux. O projeto fixa `net8.0`; não uses `net9.0` sem migrar
o código que depende de APIs antigas.

### Permissões USB

O `usbmuxd` traz as regras udev. Se o iPhone não aparecer sem `sudo`:

```bash
sudo usermod -aG plugdev "$USER"
sudo systemctl enable --now usbmuxd
```

Depois de alterar os grupos, termina a sessão e entra novamente.

### Confirmar que o Linux vê o iPhone

```bash
idevice_id -l
ideviceinfo -k ProductType
```

Se o primeiro comando não devolver nada, o problema é do sistema (`usbmuxd`, cabo,
pareamento ou permissões), não da interface.

---

## 4. Clonar e compilar

```bash
git clone https://github.com/anymousdark/FDR_SYSTEM-Linux.git
cd FDR_SYSTEM-Linux

dotnet restore FDR_SYSTEM.sln
dotnet build src/FdrSystem.App/FdrSystem.App.csproj -c Debug
```

Build esperado: **0 erros, 0 avisos** em cerca de 7–10 segundos numa máquina normal.
O aviso `CA2255` do `ModuleInitializer` é suprimido de propósito no Core.

### Versões fixas

| Pacote | Versão | Nota |
|---|---|---|
| Avalonia | `11.3.22` | |
| Avalonia.Controls.DataGrid | `11.3.13` | a `11.3.22` não existe no NuGet |
| .NET | `net8.0` | `System.Threading.Lock` é .NET 9; não usar |

Não subir versões sem compilar e testar. O DataGrid é particularmente sensível a
incompatibilidades entre versões menores.

---

## 5. Correr a aplicação

```bash
dotnet run --project src/FdrSystem.App/FdrSystem.App.csproj
```

Precisa de sessão gráfica X11 ou Wayland. A aplicação não foi feita para correr em
modo headless.

Para correr o binário já publicado:

```bash
./publish/linux-x64/FDR_SYSTEM
```

---

## 6. Publicar e empacotar

### Publicação self-contained

```bash
dotnet publish src/FdrSystem.App/FdrSystem.App.csproj \
  -c Release -r linux-x64 --self-contained true \
  -o publish/linux-x64

./publish/linux-x64/FDR_SYSTEM
```

Resultado medido no projeto: **91,6 MB**, **218 ficheiros**, binário ELF `FDR_SYSTEM`.
O destino não precisa de ter o .NET instalado.

Atalho:

```bash
bash packaging/build-linux.sh
```

Para ARM64:

```bash
RID=linux-arm64 bash packaging/build-linux.sh
```

O script gera ainda um `.deb` simples se `dpkg-deb` estiver instalado. O `.deb` atual é
**amd64**; para ARM64 falta ajustar a arquitetura no `control`.

### Pacote `.deb`

O script instala em `/opt/fdr-system`, cria o symlink `/usr/bin/fdr-system` e o ficheiro
`.desktop`. Dependências declaradas: `libimobiledevice-utils`, `libimobiledevice-1.0-6` e `usbmuxd`.

```bash
sudo apt install ./publish/fdr-system_1.5.9_amd64.deb
```

### Ainda por fazer

- AppImage com `linuxdeploy` + `appimagetool`;
- ícone PNG/SVG no `.desktop`;
- atualizações via `.deb`/`apt` ou `AppImageUpdate`;
- teste de instalação numa máquina Linux limpa.

---

## 7. Estrutura do repositório

```text
FDR_SYSTEM-Linux/
├─ FDR_SYSTEM.sln
├─ README.md
├─ AGENTS.md
├─ docs/
│  └─ PORTING-MAP.md
├─ packaging/
│  └─ build-linux.sh
├─ src/
│  ├─ FdrSystem.Core/              # sem UI; lógica portável
│  │  ├─ AppPaths.cs               # caminhos XDG
│  │  ├─ Interop/                  # carregamento de libimobiledevice/libplist
│  │  ├─ Tools/ToolRunner.cs       # localiza e executa idevice*
│  │  ├─ Usb/                      # sysfs (Linux) e SetupAPI (Windows, para testes)
│  │  ├─ Devices/                  # leitura, plists, bateria, etiquetas, snapshot
│  │  │  └─ AfcSession.cs          # AFC nativa (área Media)
│  │  └─ Specs/DeviceSpecs.cs      # tabela chip/RAM/ecrã
│  └─ FdrSystem.App/               # Avalonia
│     ├─ Views/                    # MainWindow + Pages/
│     ├─ Styles/                   # Controls.axaml e Icons.axaml
│     ├─ Themes/                   # cinco temas
│     └─ Services/                 # ThemeManager, AppSettings, AppLog
├─ tools/dev/                      # ferramentas falsas para testar sem iPhone
└─ publish/                        # gerado; ignorado pelo Git
```

Regras de arquitetura:

- **UI em code-behind**, como no WPF original. Mantém os nomes dos handlers
  (`Scan_Click`, `Nav_Click`, …) para facilitar o port.
- **Core sem UI**: tudo o que pode ser testado sem janela fica em `FdrSystem.Core`.
- **Nada de `sudo` dentro da aplicação.** Se faltar permissão, a UI explica o comando.
- **IDs mascarados por omissão**; revelar só com “Revelar IDs” ligado.
- **Logs** em `~/.local/share/FDR_SYSTEM/logs/app-AAAAMMDD.log`.
- **Caminhos XDG** sempre por `AppPaths`; nunca `Environment.SpecialFolder` diretamente.

---

## 8. Testar sem iPhone

O `ToolRunner` procura as ferramentas no `PATH`. Podes simular um iPhone com os scripts
de `tools/dev/`:

```bash
mkdir -p ~/fakebin
cp tools/dev/fake-ideviceinfo.sh        ~/fakebin/ideviceinfo
cp tools/dev/fake-idevicediagnostics.sh ~/fakebin/idevicediagnostics
chmod +x ~/fakebin/*

export PATH="$HOME/fakebin:$PATH"
dotnet run --project src/FdrSystem.App/FdrSystem.App.csproj
```

A página Offline passa a mostrar um iPhone 13 fictício (iOS 17.4, bateria a 87%,
412 ciclos). Isto permite iterar UI e parsing sem hardware.

---

## 9. Segurança e limites

Estas regras são obrigatórias e não devem ser contornadas:

- **Nunca pedir Apple ID, palavra-passe ou código de verificação.**
- **Não contornar iCloud/Activation Lock nem MDM.** A app pode detetar o estado, mas
  não remove bloqueios.
- **Não instalar libwdi nem confiar em certificados self-signed.** No Linux usa-se
  `usbmuxd` + `libimobiledevice` das fontes oficiais.
- **Não executar ações destrutivas sem confirmação explícita.** Restauro, apagamento,
  reinício e alterações de pareamento exigem aviso claro.
- **Não enviar dados do dispositivo para servidores próprios.** As páginas offline não
  usam rede; as online, quando existirem, só devem contactar catálogos públicos da Apple.
- **Mascarar UDID, IMEI, série e outros identificadores** por omissão.
- **Não guardar credenciais** em ficheiros de configuração ou logs.

O projeto Windows original em `OfflineDeviceDiagnostics` é apenas referência de leitura
e não deve ser alterado por este repositório.

---

## 10. Roadmap para o GUI completo

Ordem sugerida para completar o port:

- [x] **Diagnóstico** — `idevicesyslog` em direto, crash reports para ZIP,
      `idevicepair validate|pair` e `ideviceprovision list`.
- [x] **Acessibilidade (AFC)** — `AfcSession` sobre `libimobiledevice-1.0.so`; navegação,
      importar/exportar, DCIM, apagar com confirmação e captura de ecrã.
- [x] **Driver** — estado do `usbmuxd`, regras udev, grupos e dispositivos via
      `DriverEnvironment`; a UI mostra comandos sem executar `sudo`.
- [x] **Online** — catálogo `api.ipsw.me`, `DownloadManager` segmentado com retoma e SHA-256,
      importação de IPSW e verificação. `idevicerestore` fica na página Restauro.
- [ ] **Firmware / Restauro** — lista completa de firmwares e
      `idevicerestore` com progresso por `stdout`.
- [ ] **Reparo** — recovery/restart/ativação; MDM apenas deteção.
- [ ] **Jailbreak** — detetar `palera1n`/`checkra1n` e abrir a ferramenta do sistema,
      mantendo os avisos para A7–A11.
- [ ] **Empacotamento** — `.deb` completo e AppImage.
- [ ] **Teste real** — correr numa instalação Linux limpa com iPhone físico.
- [ ] **CI** — build e testes automáticos num runner Linux.

Definição de pronto por página: compila, aparece na navegação, tem os botões do original,
escreve logs por `AppLog`, mascara IDs e mostra uma mensagem clara quando falta uma
ferramenta — sem crash.

---

## 11. Problemas comuns

### `AVLN2000: Unable to resolve XAML resource`

Ficheiros XAML têm de ser **`.axaml`**. Um `.xaml` não é compilado e o erro engana.

### A janela não abre

Confirma que estás numa sessão gráfica X11/Wayland e que `DISPLAY`/`WAYLAND_DISPLAY`
estão definidos. Não há suporte headless.

### O iPhone não aparece

```bash
idevice_id -l
systemctl status usbmuxd
groups
```

Confirma o cabo, o pareamento, o grupo `plugdev` e o `usbmuxd` ativo.

### Driver: estado do usbmuxd e udev

A página **Driver** é só de leitura: mostra o socket, o serviço systemd, as regras udev e os
dispositivos Apple. Não instala pacotes nem corre `sudo`. Se aparecer `socket ausente`:

```bash
sudo systemctl enable --now usbmuxd
systemctl status usbmuxd
```

Se as regras udev não forem encontradas, confirma que o pacote `usbmuxd` está instalado e
que o utilizador está no grupo `plugdev` (termina e volta a entrar na sessão depois de
`usermod`).

### Acessibilidade: biblioteca nativa em falta

```bash
sudo apt install libimobiledevice-1.0-6
```

A página AFC usa P/Invoke para `libimobiledevice-1.0.so`; as ferramentas `idevice*` vêm
de `libimobiledevice-utils`.

### `dotnet publish` falha com `ResponseEnded`

Volta a correr o comando. O runtime pack fica em cache e a segunda tentativa costuma
passar.

### `FluentTheme.RequestedThemeVariant` não existe

Usa `Application.Current.RequestedThemeVariant`.

### Itens de navegação vazios

O `ContentPresenter` do `ControlTemplate` precisa de
`Content="{TemplateBinding Content}"`.

---

## 12. Privacidade e licença

Este é um repositório **privado**. O código e os binários não devem ser redistribuídos
sem autorização do autor. A aplicação Windows e a release pública são projetos separados.

Os dados lidos do iPhone ficam na máquina local. Os logs também são locais. Não há
telemetria, contas ou servidores próprios.

**FDR_SYSTEM** é um projeto de anymousdark. Todos os direitos reservados.
