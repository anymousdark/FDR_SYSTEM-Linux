# FDR_SYSTEM — Linux (Avalonia)

> Port do **FDR_SYSTEM** (C# + WPF, Windows) para **Linux**, em **Avalonia UI**.
> **Versão Linux: 1.6.0** (o projeto Windows mantém a sua própria numeração).
> Repositório **privado**. A aplicação Windows original não é alterada por este projeto.

**Estado atual:** as dez páginas estão implementadas — **Offline**, **Online**, **Restauro**,
**Reparo**, **Diagnóstico**, **Firmware**, **Acessibilidade (AFC)**, **Driver**, **Instalação** e
**Jailbreak**. A 1.6.0 acrescenta a página **Instalação**, que instala as dependências Linux
(`usbmuxd`, `libimobiledevice`, motores opcionais) por `pkexec`, e faz a página **Offline** mostrar
modelo e ficha técnica também em recovery/DFU. Diagnóstico e AFC foram testados com um iPhone real
no ambiente Windows de desenvolvimento; falta validar tudo numa instalação Linux. O projeto compila
e gera um binário `linux-x64` self-contained.

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
| Página **Instalação** | ✅ implementada (`pkexec`, script revisto, registo e verificação final) |
| Página **Online** | ✅ implementada (catálogo `api.ipsw.me`, download segmentado com retoma, SHA-256) |
| Página **Restauro** | ✅ implementada (`idevicerestore`, duas barras, confirmação destrutiva, cancelar) |
| Página **Reparo** | ✅ implementada (modo normal/recovery/DFU, entrar/sair do recovery, reiniciar, ativação e MDM só leitura) |
| Página **Firmware** | ✅ implementada (catálogo completo, filtro, cache 12 h, download por linha) |
| Página **Jailbreak** | ✅ implementada (deteta `palera1n`/`checkra1n`, mostra caminho/versão, comandos e avisos; não executa nem corre `sudo`) |
| Empacotamento `.deb` | ✅ amd64 e arm64 (`Architecture` derivada do RID) |
| AppImage | ✅ x86_64 e aarch64 (gerada no host da mesma arquitectura) |
| Ícone e `.desktop` | ✅ SVG + PNG 16–512, `Icon=fdr-system`, `StartupWMClass` |
| Releases | ✅ `.github/workflows/release.yml` anexa `.deb` e AppImage à tag `v*` |
| Teste com iPhone real em Linux | 🟡 Offline validado com iPhone 12 (iOS 27.2); falta Restauro/Reparo/AFC/Diagnóstico |

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

O script gera ainda um `.deb` se `dpkg-deb` estiver instalado. A `Architecture` do pacote
segue o RID (`linux-x64` → `amd64`, `linux-arm64` → `arm64`).

A CI em `.github/workflows/linux.yml` compila as duas arquitecturas e publica os `.deb`
como artefactos em cada push para `main`. O workflow `.github/workflows/release.yml`
corre em tags `v*` (ou manualmente) e anexa os `.deb` e a AppImage à release.

### AppImage

```bash
bash packaging/build-appimage.sh
# → publish/FDR_SYSTEM-1.6.0-x86_64.AppImage
```

A AppImage é self-contained (não precisa de .NET nem de dependências instaladas) e só
pode ser gerada **no host da mesma arquitectura**: `RID=linux-arm64` exige um host
`aarch64`. O `.deb` continua a poder ser cross-compilado.

### Pacote `.deb`

O script instala em `/opt/fdr-system`, cria o symlink `/usr/bin/fdr-system` e o ficheiro
`.desktop`. Dependências declaradas: `libimobiledevice-utils` e `usbmuxd`; a biblioteca
`libimobiledevice-1.0-6` (ou `-6t64` no Ubuntu 24.04) fica como recomendação.

```bash
# x64
sudo apt install ./publish/fdr-system_1.6.0_amd64.deb

# ARM64
sudo apt install ./publish/fdr-system_1.6.0_arm64.deb

# ou AppImage (sem instalar)
chmod +x FDR_SYSTEM-1.6.0-x86_64.AppImage
./FDR_SYSTEM-1.6.0-x86_64.AppImage
```

### Ainda por fazer

- atualizações via `.deb`/`apt` ou `AppImageUpdate`;
- teste de instalação numa máquina Linux limpa (dependências, polkit, udev);
- validação em hardware real das operações destrutivas (Restauro/Reparo) e de AFC/Diagnóstico;
- testes automáticos dos parsers (progresso, IPSW/modelo, MDM, CPID/BDID).

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
- [x] **Firmware** — catálogo completo com filtro e cache, DataGrid por modelo e download partilhado.
- [x] **Restauro** — `Core/Firmware/RestoreEngine.cs` + `Views/Pages/RestauroView.axaml(.cs)`:
      valida IPSW, confirma o apagamento, progresso por `stdout` e cancelamento com confirmação.
- [x] **Reparo** — `Views/Pages/ReparoView.axaml(.cs)`: modo por USB, recovery/restart com
      confirmação, `ideviceactivation state` e MDM apenas como deteção; nunca corre `sudo`.
- [x] **Jailbreak** — `Views/Pages/JailbreakView.axaml(.cs)`: deteta `palera1n`/`checkra1n`,
      mostra caminho/versão e comandos para copiar, com avisos checkm8 (A5–A11); nunca executa
      as ferramentas nem corre `sudo`, e não serve para contornar iCloud/Activation Lock/MDM.
- [x] **Instalação** — `Views/Pages/InstalacaoView.axaml(.cs)` + `Core/Setup/`:
      deteta distribuição/gestor, mostra o estado dos pacotes e instala com uma ação explícita
      por `pkexec`; nunca corre `sudo` nem guarda credenciais.
- [x] **Empacotamento `.deb` + CI** — `packaging/build-linux.sh` (amd64/arm64) e
      `.github/workflows/linux.yml` (build + publish + artefactos).
- [x] **AppImage + ícone + release** — `packaging/build-appimage.sh`, ícones 16–512 no
      `.deb`/`.desktop` e `.github/workflows/release.yml` para anexar assets à tag.
- [ ] **Teste real** — correr numa instalação Linux limpa com iPhone físico
      (Offline já validado com iPhone 12).
- [ ] **Testes automáticos** — projeto de testes para parsers e motores.

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

### Instalação: dependências, `pkexec` e versões

A página **Instalação** substitui o passo manual `sudo bash packaging/install-linux-deps.sh`:

- deteta a distribuição, o gestor de pacotes e o estado de cada dependência;
- instala `usbmuxd`, `libimobiledevice-utils` e os motores opcionais com um script gerado
  pela app e revisto antes de correr;
- a elevação é pedida pelo **pkexec** (polkit) — a app não corre `sudo` nem guarda a palavra-passe;
- no fim verifica ferramenta a ferramenta e diz o que ficou em falta;
- se preferires o terminal, os botões copiam o script completo ou o comando `sudo` equivalente.

A versão vive num único sítio, `Directory.Build.props`, e é usada pelo csproj, pelo cabeçalho
da app e pelo nome do `.deb`:

```bash
bash packaging/bump-version.sh patch     # 1.6.0 -> 1.6.1
bash packaging/bump-version.sh minor     # 1.6.0 -> 1.7.0
bash packaging/bump-version.sh 1.6.2 --tag --push
bash packaging/build-linux.sh            # usa a versão atual, sem números à mão
```

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
