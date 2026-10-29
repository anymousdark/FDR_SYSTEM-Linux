# FDR_SYSTEM no Linux — guia completo de build e implementação

> **Repositório privado:** `https://github.com/anymousdark/FDR_SYSTEM-Linux`
> **Branch:** `main` · **Data:** 29/10/2026
>
> Este é o guia principal para instalar, compilar, testar e continuar o port no Linux.
> Confirma sempre o commit atual com `git log -1 --oneline`; não confies num número de
> commit escrito num documento antigo.
>
> Lê também: [`README.md`](../README.md), [`AGENTS.md`](../AGENTS.md),
> [`docs/PORTING-MAP.md`](PORTING-MAP.md) e [`docs/CONTINUE-ON-LINUX.md`](CONTINUE-ON-LINUX.md).

---

## 0. Primeiro: só a página Offline aparece?

Isto tem duas explicações possíveis, e **nenhuma** significa que seja preciso reescrever
a aplicação.

### 0.1 Páginas implementadas

No `main` atual, todas as páginas do Windows estão implementadas e ligadas:

- **Offline**
- **Online**
- **Restauro**
- **Reparo**
- **Diagnóstico**
- **Acessibilidade (AFC)**
- **Driver**
- **Firmware**
- **Jailbreak**

Já não há placeholders: se vires o cartão “Falta: …” numa destas páginas, estás a
correr um binário antigo (ver 0.2).

### 0.2 Build antigo, `.deb` antigo ou commit antigo

Se só vês Offline, ou se páginas que o README dá como implementadas aparecem como
placeholder, estás quase certamente a correr um binário antigo. Confirma:

```bash
cd ~/FDR_SYSTEM-Linux

git fetch origin
git status --short --branch
git log -1 --oneline

grep -n '"offline" =>\|"diagnostico" =>\|"acessibilidade" =>\|"driver" =>' \
  src/FdrSystem.App/Views/MainWindow.axaml.cs
```

Devem aparecer quatro linhas:

```text
"offline" =>
"diagnostico" =>
"acessibilidade" =>
"driver" =>
```

Se não aparecerem, o código está velho:

```bash
git pull --ff-only origin main
```

Se houver alterações locais, guarda-as primeiro:

```bash
git status
git stash push -m "alterações locais antes de atualizar"
git pull --ff-only origin main
```

**Não uses `git reset --hard`** sem confirmar que não tens trabalho teu.

Depois limpa e compila:

```bash
dotnet clean src/FdrSystem.App/FdrSystem.App.csproj
dotnet build src/FdrSystem.App/FdrSystem.App.csproj -c Debug
dotnet run --project src/FdrSystem.App/FdrSystem.App.csproj
```

Se estiveres a correr uma aplicação **instalada** por `.deb`, o binário do repositório
não atualiza `/opt/fdr-system` sozinho. Verifica:

```bash
which fdr-system
readlink -f "$(which fdr-system)"
stat -c '%y %n' "$(readlink -f "$(which fdr-system)")"
```

Se o caminho instalado for `/opt/fdr-system/FDR_SYSTEM`, ele pode ser uma versão antiga.
Reinstala depois de construir:

```bash
bash packaging/build-linux.sh
sudo apt install --reinstall ./publish/fdr-system_1.5.9_amd64.deb
```

Ou, para testar sem instalar:

```bash
./publish/linux-x64/FDR_SYSTEM
```

### 0.3 Ver o que a app está a fazer

A app escreve logs em:

```bash
~/.local/share/FDR_SYSTEM/logs/
```

Abre o mais recente:

```bash
ls -lt ~/.local/share/FDR_SYSTEM/logs | head
tail -n 120 ~/.local/share/FDR_SYSTEM/logs/app-*.log
```

A navegação regista linhas como `página -> diagnostico`. Se não houver linha nenhuma
quando clicas, o problema é de navegação/binário; se houver linha e a página falhar,
o problema é da página ou de uma ferramenta em falta.

### 0.4 Ferramenta em falta não é placeholder

Se faltar `idevicesyslog`, a página Diagnóstico deve mostrar uma mensagem clara
(“Ferramenta em falta: …”), não o cartão de placeholder. Se vires placeholder numa
página implementada, o código é antigo.

---

## 1. Mapa real das páginas

| Página | Estado no `main` | Ficheiro principal | Precisa de |
|---|---|---|---|
| Offline | funcional | `OfflineView.axaml(.cs)` + `DeviceService.cs` | `ideviceinfo`, `idevicediagnostics`, `usbmuxd` |
| Diagnóstico | implementado; teste Linux pendente | `DiagnosticoView.axaml(.cs)` | `idevicesyslog`, `idevicecrashreport`, `idevicepair`, `ideviceprovision` |
| Acessibilidade | implementado; teste Linux pendente | `AcessibilidadeView.axaml(.cs)` + `AfcSession.cs` | `libimobiledevice-1.0-6`, `idevice_id`, `ideviceinfo`, `idevicescreenshot` |
| Driver | implementado; teste Linux pendente | `DriverView.axaml(.cs)` + `DriverEnvironment.cs` | `usbmuxd`, udev, `systemctl`, `id` |
| Online | funcional | `OnlineView.axaml(.cs)` + `Core/Firmware/` | `api.ipsw.me`; `idevicerestore` só na página Restauro |
| Restauro | funcional | `RestauroView.axaml(.cs)` + `Core/Firmware/RestoreEngine.cs` | `idevicerestore --erase --no-input`, duas barras e log |
| Reparo | funcional | `ReparoView.axaml(.cs)` | `ideviceenterrecovery`, `idevicediagnostics restart`, `irecovery -n`, ativação/MDM só leitura |
| Firmware | funcional | `FirmwareView.axaml(.cs)` | catálogo `api.ipsw.me` + cache local; download partilhado com Online |
| Jailbreak | funcional | `JailbreakView.axaml(.cs)` | deteta `palera1n`/`checkra1n`, mostra caminho/versão e comandos; não executa |

O encaminhamento está em:

```text
src/FdrSystem.App/Views/MainWindow.axaml.cs  →  ShowPage(string tag)
```

O `switch` atual liga `offline`, `diagnostico`, `acessibilidade` e `driver`; tudo o resto
cai em `PlaceholderView.For(tag, this)`.

### Testes já feitos

Com um iPhone 13 real, no Windows de desenvolvimento:

- deteção USB e leitura de dados;
- AFC: listar `/`, `/DCIM`, entrar em `100APPLE` e descarregar um ficheiro pequeno;
- Diagnóstico: `idevicepair validate`, `ideviceprovision list` e syslog a iniciar/parar;
- Driver: deteção do iPhone, atualização e cópia dos comandos.

**Ainda falta validar tudo numa instalação Linux real.** Não declares uma página
“pronta para produção” só porque compila.

---

## 2. Tudo o que é necessário baixar

### 2.1 Base

| Necessidade | Pacote / origem |
|---|---|
| SDK .NET 8 | `dotnet-sdk-8.0` ou instalador oficial da Microsoft |
| Git | `git` |
| Download HTTPS | `curl`, `ca-certificates` |
| Compilação | SDK .NET 8 (não é preciso Visual Studio) |
| GUI | X11 ou Wayland; bibliotecas gráficas da distribuição |

### 2.2 USB e iPhone

| Necessidade | Pacote |
|---|---|
| Daemon USB | `usbmuxd` |
| Ferramentas `idevice*` | `libimobiledevice-utils` |
| Biblioteca nativa AFC | `libimobiledevice-1.0-6` |
| Regras udev | incluídas no `usbmuxd` |
| Diagnóstico USB | `usbutils` (`lsusb`) |

### 2.3 Por página

| Página | Ferramentas / bibliotecas |
|---|---|
| Offline | `ideviceinfo`, `idevicediagnostics` |
| Diagnóstico | `idevicesyslog`, `idevicecrashreport`, `idevicepair`, `ideviceprovision` |
| Acessibilidade | `libimobiledevice-1.0.so`, `idevice_id`, `ideviceinfo`, `idevicescreenshot` |
| Driver | `usbmuxd`, regras udev, `systemctl`, `id` |
| Online | rede + `api.ipsw.me`; `DownloadManager` (a implementar) |
| Firmware | rede + `api.ipsw.me` (cache em `~/.local/share/FDR_SYSTEM/cache`) |
| Restauro | `idevicerestore` (opcional no sistema; necessário para restaurar) |
| Reparo | `ideviceenterrecovery`, `idevicediagnostics`, `irecovery` |
| Jailbreak | `palera1n` ou `checkra1n` instalados pelo utilizador; não vêm no FDR |
| Empacotamento | `dpkg-deb`, `fakeroot`; AppImage opcional |

### 2.4 O que **não** é necessário

No Linux **não** uses:

- `usbaapl64`, `Apple Mobile Device Support` ou `.inf` da Apple;
- UsbDk;
- libwdi;
- `pnputil`;
- WinRa1n;
- certificados self-signed;
- drivers de terceiros descarregados de sites duvidosos.

O `usbmuxd` oficial e o `libimobiledevice` da distribuição substituem essa camada.

---

## 3. Instalar o Linux e as dependências

O alvo documentado é **Debian/Ubuntu/Kali**. Noutra distribuição, adapta os nomes.

### 3.1 Script automático (recomendado)

```bash
cd ~/FDR_SYSTEM-Linux
sudo bash packaging/install-linux-deps.sh
```

O script:

- instala .NET 8, `usbmuxd`, `libimobiledevice-utils`, `libimobiledevice-1.0-6`;
- tenta instalar `idevicerestore` e `libirecovery-utils` (opcionais);
- instala bibliotecas gráficas necessárias ao Avalonia;
- ativa o `usbmuxd` e recarrega as regras udev;
- adiciona o teu utilizador ao grupo `plugdev`;
- mostra as ferramentas encontradas.

No fim, **termina a sessão e volta a entrar** para o grupo `plugdev` passar a valer.

### 3.2 Instalação manual (Debian/Ubuntu/Kali)

```bash
sudo apt update

sudo apt install -y \
  ca-certificates curl git usbutils \
  libimobiledevice-utils libimobiledevice-1.0-6 usbmuxd \
  dpkg-deb fakeroot xdg-utils \
  libx11-6 libice6 libsm6 libfontconfig1 libfreetype6 \
  libxrandr2 libxi6 libxcursor1 libxinerama1 libgl1 libegl1 \
  fonts-dejavu-core

sudo apt install -y dotnet-sdk-8.0
dotnet --info
```

Opcionais:

```bash
sudo apt install -y idevicerestore libirecovery-utils || true
```

`idevicerestore` pode não existir no repositório da tua distribuição. Nesse caso,
instala-o a partir das instruções oficiais do projeto; não descarregues binários
aleatórios.

### 3.3 Se `dotnet-sdk-8.0` não existir no `apt`

Instala o SDK 8 pela página oficial da Microsoft para Linux:

- <https://learn.microsoft.com/dotnet/core/install/linux>

Ou, com o instalador oficial:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 8.0

export PATH="$HOME/.dotnet:$PATH"
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc

dotnet --list-sdks
```

O projeto fixa **`net8.0`**. Não mudes para `net9.0` sem migrar o código.

### 3.4 Fedora (referência)

```bash
sudo dnf install -y git curl usbmuxd libimobiledevice-utils \
  libimobiledevice libirecovery-utils idevicerestore \
  dotnet-sdk-8.0 dpkg fakeroot
```

Se algum pacote não existir, usa os equivalentes da tua versão do Fedora.

### 3.5 Arch (referência)

```bash
sudo pacman -S --needed git curl usbmuxd libimobiledevice \
  libirecovery idevicerestore dotnet-sdk-8.0
```

### 3.6 Permissões USB

```bash
sudo usermod -aG plugdev "$USER"
sudo systemctl enable --now usbmuxd
```

Depois **termina a sessão e entra novamente**. Confirma:

```bash
groups
systemctl status usbmuxd --no-pager
idevice_id -l
```

### 3.7 Verificação automática

```bash
bash packaging/check-linux-env.sh
```

O script mostra:

- commit e branch;
- versão do .NET;
- ferramentas `idevice*` encontradas;
- biblioteca AFC;
- estado do `usbmuxd`, socket e regras udev;
- grupos do utilizador;
- iPhone detetado;
- rotas das páginas no código;
- se a app instalada é diferente do build do repositório.

Se o script disser que a app instalada é outro ficheiro, é sinal de que podes estar a
abrir uma versão antiga.

---

## 4. Obter o código e compilar

### 4.1 Clonar o repositório privado

Usa a conta GitHub que já tem acesso:

```bash
git clone https://github.com/anymousdark/FDR_SYSTEM-Linux.git
cd FDR_SYSTEM-Linux
git switch main
git pull --ff-only
git log -1 --oneline
```

Se o HTTPS pedir autenticação, usa um token pessoal ou configura SSH:

```bash
git clone git@github.com:anymousdark/FDR_SYSTEM-Linux.git
```

**Não colar tokens em issues, logs, capturas de ecrã ou no chat.** O repositório é
privado; não o tornar público sem autorização.

### 4.2 Compilar

```bash
dotnet restore FDR_SYSTEM.sln
dotnet build src/FdrSystem.App/FdrSystem.App.csproj -c Debug
```

Resultado esperado: **0 erros, 0 avisos**.

Se houver dúvidas sobre cache antiga:

```bash
dotnet clean src/FdrSystem.App/FdrSystem.App.csproj
rm -rf src/FdrSystem.App/bin src/FdrSystem.App/obj
rm -rf src/FdrSystem.Core/bin src/FdrSystem.Core/obj
dotnet restore FDR_SYSTEM.sln
dotnet build src/FdrSystem.App/FdrSystem.App.csproj -c Debug
```

### 4.3 Correr

```bash
dotnet run --project src/FdrSystem.App/FdrSystem.App.csproj
```

Precisa de sessão gráfica X11 ou Wayland. Não há modo headless.

### 4.4 Publicar

```bash
dotnet publish src/FdrSystem.App/FdrSystem.App.csproj \
  -c Release -r linux-x64 --self-contained true \
  -o publish/linux-x64

./publish/linux-x64/FDR_SYSTEM
```

Ou:

```bash
bash packaging/build-linux.sh
```

Para ARM64:

```bash
RID=linux-arm64 bash packaging/build-linux.sh
```

**Limitação atual:** o script `packaging/build-linux.sh` grava `Architecture: amd64` no
`.deb`. Para ARM64 é preciso ajustar o `control` antes de instalar. O binário
`linux-arm64` em si é gerado corretamente.

### 4.5 Instalar o `.deb`

```bash
sudo apt install ./publish/fdr-system_1.5.9_amd64.deb
fdr-system
```

Se já tinhas a mesma versão instalada e queres substituir:

```bash
sudo apt install --reinstall ./publish/fdr-system_1.5.9_amd64.deb
```

Confirma que estás a abrir o binário novo:

```bash
which fdr-system
readlink -f "$(which fdr-system)"
stat -c '%y %n' "$(readlink -f "$(which fdr-system)")"
```

### 4.6 Caminhos usados pela app

| O quê | Onde |
|---|---|
| Dados | `~/.local/share/FDR_SYSTEM/` |
| Logs | `~/.local/share/FDR_SYSTEM/logs/` |
| Definições | `~/.local/share/FDR_SYSTEM/settings.json` |
| Firmware | `~/Documents/iDevice-Firmware/` |
| Relatórios | `~/Documents/FDR_SYSTEM/Relatorios/` |

`XDG_DATA_HOME` e `XDG_DOCUMENTS_DIR` são respeitados quando definidos.

---

## 5. Testar as páginas que já existem

### 5.1 Offline

Pré-condições:

```bash
idevice_id -l
ideviceinfo -k ProductType
```

Dentro da app:

- ligar o iPhone, desbloquear e aceitar “Confiar neste computador”;
- a página Offline deve ler automaticamente;
- confirmar modelo, iOS, bateria e armazenamento;
- confirmar que UDID, série e IMEI aparecem mascarados por omissão;
- testar “Revelar IDs” e voltar a esconder;
- exportar os detalhes.

Comandos úteis:

```bash
ideviceinfo -x
ideviceinfo -q com.apple.disk_usage -x
idevicediagnostics ioregentry AppleSmartBattery
ideviceinfo -q com.apple.mobile.battery -x
```

### 5.2 Diagnóstico

Pré-condições:

```bash
idevicesyslog --help
idevicecrashreport --help
idevicepair --help
ideviceprovision --help
```

Na app:

- confirmar `Ferramentas libimobiledevice: OK`;
- **Validar** o emparelhamento;
- **Listar perfis** (só leitura);
- iniciar o syslog, esperar linhas e parar;
- filtrar, limpar e guardar o syslog;
- opcional: recolher crash logs para ZIP.

Comandos equivalentes:

```bash
idevicepair validate
idevicepair pair
ideviceprovision list
idevicesyslog --no-colors
idevicecrashreport -e -k /tmp/fdr-crash
```

Não apagar relatórios do iPhone. A app copia, não remove.

### 5.3 Acessibilidade (AFC)

Pré-condições:

```bash
idevice_id -l
ideviceinfo -k ProductType
idevicescreenshot --help
```

Na app:

- abrir a página com o iPhone desbloqueado;
- listar `/`;
- entrar em `DCIM` e depois em `100APPLE`;
- exportar **um ficheiro pequeno** para `/tmp`;
- confirmar o tamanho do ficheiro;
- opcional: criar uma pasta de teste e apagá-la com confirmação;
- opcional: captura de ecrã.

Notas:

- importar/apagar no iPhone só com confirmação explícita;
- em iOS 17+, a captura de ecrã pode exigir a imagem de programador montada;
- se a biblioteca nativa faltar, a página deve mostrar uma mensagem clara, não crashar.

### 5.4 Driver

Na app, a página deve mostrar:

- ambiente Linux com `sysfs` + `usbmuxd`;
- socket `/run/usbmuxd` presente;
- serviço `usbmuxd` ativo;
- regras udev encontradas;
- grupos do utilizador e indicação de `plugdev`;
- iPhone na tabela, em modo Normal/Recovery/DFU;
- ferramentas essenciais `OK`.

Comandos de verificação:

```bash
systemctl status usbmuxd --no-pager
ls -l /run/usbmuxd
dpkg -L usbmuxd | grep -E '\.rules$'
id -nG
lsusb | grep -i Apple
```

Se o socket estiver ausente:

```bash
sudo systemctl enable --now usbmuxd
systemctl status usbmuxd --no-pager
```

Se as regras udev não forem encontradas:

```bash
dpkg -L usbmuxd | grep udev
sudo udevadm control --reload-rules
sudo udevadm trigger
```

A página **não executa `sudo`**. Os comandos são mostrados/copiados para o utilizador.

### 5.5 Critérios de aceitação

| Página | Passa se… |
|---|---|
| Offline | lê um iPhone real e mascara IDs |
| Diagnóstico | valida par, lista perfis, inicia/para syslog e recolhe crash logs |
| Acessibilidade | lista `/`, entra em `DCIM/100APPLE` e exporta um ficheiro |
| Driver | mostra usbmuxd/udev/grupos e o iPhone sem pedir privilégios |
| Build | `dotnet build` dá 0 erros e 0 avisos |
| Publish | `publish/linux-x64/FDR_SYSTEM` arranca numa sessão gráfica |

**Não testar restauro, apagar ficheiros ou jailbreak num iPhone principal.** Usa um
aparelho de teste e faz backup.

---

## 6. Implementar as cinco páginas que faltam

Ordem recomendada:

1. **Online** — catálogo e download.
2. **Firmware** — lista completa, reutilizando o catálogo/download.
3. **Restauro** — `idevicerestore` e progresso.
4. **Reparo** — recovery/restart/estado; sem contornar bloqueios.
5. ~~**Jailbreak**~~ — feito em `JailbreakView` (deteta e mostra comandos; nunca executa).
6. **Empacotamento** — AppImage e `.deb` completo.

Cada página deve:

- ser um `UserControl` em `.axaml` com code-behind;
- ser ligada em `MainWindow.ShowPage(string tag)`;
- sair de `PlaceholderView.cs`;
- usar `ToolRunner` para ferramentas do sistema;
- registar erros com `AppLog`;
- mascarar IDs;
- mostrar uma mensagem clara quando falta uma ferramenta;
- ser testada num Linux real com iPhone.

### 6.1 Online

**Objetivo:** consultar o firmware que a Apple ainda assina, descarregar o IPSW e
verificar o SHA-256.

**Fonte:** catálogo público `api.ipsw.me`, não a API privada da Apple.

```text
GET https://api.ipsw.me/v4/device/{ProductType}?type=ipsw
```

Campos usados:

- `name`;
- `firmwares[].version`;
- `firmwares[].buildid`;
- `firmwares[].url`;
- `firmwares[].filesize`;
- `firmwares[].sha256sum`;
- `firmwares[].signed`.

**Implementação:**

- criar `src/FdrSystem.Core/Firmware/DownloadManager.cs`;
- portar a lógica do `DownloadManager.cs` do projeto Windows:
  - download por partes com HTTP Range;
  - ficheiro parcial `.download`;
  - metadados `.meta.json`;
  - retoma após interrupção;
  - SHA-256 no fim;
  - progresso e cancelamento;
- criar `src/FdrSystem.App/Views/Pages/OnlineView.axaml(.cs)`;
- usar `AppPaths.FirmwareFolder`;
- detetar o `ProductType` via `ideviceinfo -k ProductType` ou campo manual;
- mostrar apenas firmware assinado para restauro;
- permitir download de firmware não assinado apenas para arquivo, com aviso;
- nunca iniciar restauro automaticamente;
- exigir SHA-256 válido antes de permitir restauro.

**Critérios de aceitação:**

- consulta um iPhone real e mostra a versão assinada;
- descarrega um IPSW pequeno e verifica o SHA-256;
- interrompe e retoma sem corromper;
- mostra erro de rede sem crashar;
- não pede Apple ID nem palavra-passe.

### 6.2 Firmware

**Objetivo:** listar todos os iPhones e todos os firmwares, assinados e expirados.

**Fonte:**

```text
GET https://api.ipsw.me/v4/devices
GET https://api.ipsw.me/v4/device/{ProductType}?type=ipsw
```

**Implementação:**

- criar `FirmwareView.axaml(.cs)`;
- cache do catálogo em memória e em disco;
- filtro por nome (`iPhone 12`) e identificador (`iPhone13,2`);
- `DataGrid` com versão, build, data, tamanho, assinado e ação;
- partilhar o `DownloadManager` com Online;
- botão “Ver downloads”;
- não duplicar lógica de download.

**Critérios de aceitação:**

- carrega a lista sem bloquear a UI;
- filtra corretamente;
- abre o firmware de um modelo;
- descarrega pela linha;
- estados “assinado/expirado” corretos.

### 6.3 Restauro

**Objetivo:** executar `idevicerestore` com progresso em direto e confirmação destrutiva.

O projeto Windows usa `-P --erase --no-input`, mas a versão Linux do `idevicerestore`
(1.0.0+, Ubuntu 24.04+) já não aceita `-P`. A implementação Linux usa:

```bash
idevicerestore --erase --no-input <ficheiro.ipsw>
```

Confirma sempre a sintaxe da versão instalada:

```bash
idevicerestore --help
```

**Estado:** implementado em `Core/Firmware/RestoreEngine.cs` +
`Views/Pages/RestauroView.axaml(.cs)`. O progresso é lido de
`progress: <passo> <fração>` e de linhas `Etiqueta: valor` (mesmo formato do Windows).
O IPSW é recusado se tiver menos de 200 MB ou se não for possível identificar o modelo
(cabeçalho `--ipsw-info` ou nome do ficheiro).

**Implementação:**

- criar `RestauroView.axaml(.cs)`;
- validar que o IPSW está completo e com SHA-256 verificado;
- validar que o modelo do firmware corresponde ao iPhone ligado;
- pedir confirmação explícita: o restauro **apaga** o conteúdo;
- correr `idevicerestore` com `ProcessStartInfo`, redirecionando `stdout` e `stderr`;
- ler o progresso e atualizar duas barras (preparação e gravação);
- mostrar a fase em português e o log completo;
- permitir cancelar apenas com confirmação e `Kill(entireProcessTree: true)`;
- no fim, mostrar o código de saída e não prometer ativação automática;
- não contornar iCloud/Activation Lock/MDM.

**Critérios de aceitação:**

- recusa firmware incompatível;
- recusa IPSW incompleto ou SHA-256 inválido;
- mostra progresso e log;
- não apaga nada sem confirmação;
- falha de forma segura se `idevicerestore` não existir.

### 6.4 Reparo

**Objetivo:** ver o modo, entrar/sair de recovery, reiniciar e mostrar estado de
ativação/MDM **sem contornar bloqueios**.

Comandos de referência:

```bash
idevice_id -l
ideviceinfo -k ProductType
ideviceenterrecovery [UDID]
idevicediagnostics restart
irecovery -n
ideviceactivation state
```

**Implementação:**

- criar `ReparoView.axaml(.cs)`;
- mostrar modo Normal / Recovery / DFU;
- usar `UsbScanner` e `IrecoveryDevices` para ler CPID/BDID/ECID em recovery;
- pedir confirmação antes de entrar em recovery ou reiniciar;
- `irecovery -n` só se `irecovery` estiver instalado;
- `ideviceactivation state` apenas para **leitura**; não automatizar ativação;
- MDM: ler chaves expostas por `ideviceinfo -x` (por exemplo `Supervised`,
  `IsSupervised`, `MDM…`, `SetupState`) e mostrar “sinais de gestão”;
- **não remover MDM, não saltar Activation Lock, não pedir Apple ID**;
- o “pular configuração inicial” do Windows depende de `LockdownClient`, que ainda
  não foi portado; não o implementar como contorno de ativação.

**Critérios de aceitação:**

- deteta o modo correto;
- entra em recovery com confirmação;
- reinicia com confirmação;
- mostra estado de ativação/MDM apenas como leitura;
- ferramenta ausente mostra mensagem, não crash.

**Estado:** implementado em `Views/Pages/ReparoView.axaml(.cs)`. O ECID aparece completo
(é necessário para recovery) e não sai da máquina. A app nunca corre `sudo`: quando o
`irecovery` falha por permissão, mostra `sudo irecovery -n` para o utilizador executar.
A ativação é só `ideviceactivation state`; não há botão para ativar nem para saltar a
configuração inicial.

### 6.5 Jailbreak

**Objetivo:** abrir uma ferramenta externa de jailbreak, sem a incluir no FDR.

**Implementação:**

- criar `JailbreakView.axaml(.cs)`;
- detetar `palera1n` e `checkra1n` com `command -v`;
- mostrar caminho e versão (`--version` quando suportado);
- mostrar aviso de compatibilidade (checkm8; não prometer modelos/iOS);
- **não executar automaticamente** ferramentas que precisam de root; mostrar o
  comando e deixar o utilizador executá-lo no terminal;
- não incluir binários de terceiros no repositório;
- ligar ao site oficial para instruções atualizadas;
- não usar isto para contornar iCloud/Activation Lock/MDM.

**Critérios de aceitação:**

- mostra “não instalado” quando não existe;
- mostra o caminho quando existe;
- não corre `sudo` dentro da app;
- avisos de compatibilidade visíveis.

**Estado:** implementado em `Views/Pages/JailbreakView.axaml(.cs)`. A página usa
`ToolRunner.Locate` para o caminho; corre `palera1n --version` (4 s) para a versão, mas
**não executa o checkra1n** (é uma app gráfica). Mostra os comandos `sudo palera1n -l`,
`sudo palera1n -c -f` e `sudo checkra1n` para copiar, liga aos sites oficiais e avisa que
o jailbreak pode quebrar o sistema. Não há botão para executar, não corre `sudo` e não
contorna iCloud/Activation Lock/MDM.

### 6.6 Empacotamento e CI

- `.deb` básico já existe em `packaging/build-linux.sh`;
- corrigir `Architecture` para ARM64 quando `RID=linux-arm64`;
- adicionar AppImage (`appimagetool`) numa fase posterior;
- criar CI Linux para `dotnet build` e `dotnet publish`;
- não colocar tokens ou chaves no repositório;
- não publicar o repositório.

---

## 7. Instruções para o próximo agente de IA

Copia este bloco para o agente que vai trabalhar no Linux:

```text
Estás no repositório privado FDR_SYSTEM-Linux, branch main.

Antes de alterar código:
1. Lê README.md, AGENTS.md, docs/LINUX-FULL-GUIDE.md e docs/PORTING-MAP.md.
2. Corre: git status --short --branch && git log -1 --oneline
3. Corre: bash packaging/check-linux-env.sh
4. Confirma que existem as rotas offline, diagnostico, acessibilidade e driver
   em src/FdrSystem.App/Views/MainWindow.axaml.cs.
5. Se só a Offline aparecer, diagnostica o binário antigo antes de reescrever páginas.

Regras:
- Não alterar a app Windows OfflineDeviceDiagnostics.
- Manter Avalonia 11.3.22, DataGrid 11.3.13 e net8.0.
- UI em code-behind; ficheiros XAML em .axaml.
- Usar ToolRunner para ferramentas do sistema.
- Mascarar IDs por omissão.
- Não pedir/guardar Apple ID, palavra-passe ou 2FA.
- Não contornar iCloud, Activation Lock ou MDM.
- Não instalar libwdi, UsbDk, pnputil, WinRa1n ou drivers self-signed.
- A app não executa sudo; mostra comandos para o utilizador.
- Uma página por commit; atualizar README/AGENTS/PORTING-MAP.

Tarefa:
Implementa apenas a página <Online|Firmware|Restauro|Reparo|Jailbreak>.
Liga em ShowPage, remove o placeholder correspondente, trata ferramenta ausente,
adiciona logs e testa num Linux real com iPhone.

No fim:
- dotnet build ... -c Debug (0 erros, 0 avisos)
- dotnet publish ... -r linux-x64 --self-contained true
- testar a página
- atualizar docs
- git add, git commit, git push origin main
```

### Ordem de trabalho sugerida

1. Corrigir/confirmar o build Linux e o `.deb`.
2. Implementar **Online** + `DownloadManager`.
3. Implementar **Firmware** reutilizando o catálogo.
4. ~~Implementar **Restauro**~~ — feito em `RestoreEngine.cs` + `RestauroView`.
5. ~~Implementar **Reparo**~~ — feito em `ReparoView` (sem `sudo`, sem contornar MDM/iCloud).
6. ~~Implementar **Jailbreak**~~ — feito em `JailbreakView`.
7. AppImage/CI.

Não tentes portar as cinco páginas de uma vez. O projeto Windows tem milhares de
linhas; uma página por vez reduz o risco de regressões.

---

## 8. Problemas comuns

### Só a Offline aparece

Ordem de diagnóstico:

```bash
git log -1 --oneline
grep -n '"diagnostico" =>\|"acessibilidade" =>\|"driver" =>' \
  src/FdrSystem.App/Views/MainWindow.axaml.cs

which fdr-system
readlink -f "$(which fdr-system)"

dotnet run --project src/FdrSystem.App/FdrSystem.App.csproj
```

Se as rotas existem mas a app instalada é antiga:

```bash
bash packaging/build-linux.sh
sudo apt install --reinstall ./publish/fdr-system_1.5.9_amd64.deb
```

Se estás a abrir `fdr-system` e o `readlink` aponta para outro sítio, remove ou
reinstala o `.deb`. Não apagues ficheiros sem confirmar o que são.

### Uma página implementada mostra “Falta…”

Já não há placeholders no `main`. Se vires o cartão “Falta: …”, estás a correr um
binário antigo: confirma o commit com `git log --oneline -1`, reconstrói e reinstala o
`.deb` (ver 0.2). Não é um erro de build.

### `idevice_id -l` não devolve nada

```bash
systemctl status usbmuxd --no-pager
ls -l /run/usbmuxd
groups
idevicepair validate
lsusb | grep -i Apple
```

Confirma:

- cabo de dados (não apenas de carga);
- iPhone desbloqueado;
- “Confiar neste computador” aceite;
- sessão reiniciada depois do `usermod`;
- `usbmuxd` ativo.

### `libimobiledevice-1.0.so` em falta

```bash
sudo apt install libimobiledevice-1.0-6
ldconfig -p | grep libimobiledevice
```

### O `usbmuxd` não arranca

```bash
sudo systemctl status usbmuxd --no-pager
journalctl -u usbmuxd -n 80 --no-pager
```

Em container/WSL sem systemd, o socket pode não existir; a página Driver mostra
“systemctl indisponível” e a app deve continuar a funcionar para o resto.

### `dotnet publish` falha com `ResponseEnded`

Repetir o comando. O runtime pack fica em cache; a segunda tentativa costuma passar.

```bash
dotnet publish src/FdrSystem.App/FdrSystem.App.csproj \
  -c Release -r linux-x64 --self-contained true \
  -o publish/linux-x64
```

### `AVLN2000: Unable to resolve XAML resource`

O ficheiro tem de ser `.axaml`. Um `.xaml` não é compilado e o erro engana.

### A janela não abre

```bash
echo "$DISPLAY"
echo "$WAYLAND_DISPLAY"
```

Precisas de sessão X11 ou Wayland. Não há modo headless.

### `idevicerestore` não encontrado

```bash
command -v idevicerestore
idevicerestore --help
```

Instala o pacote da distribuição ou segue as instruções oficiais do projeto. Não
descarregues um `.exe` nem um binário aleatório.

### `irecovery` não encontrado

```bash
command -v irecovery
```

Instala `libirecovery-utils` ou o equivalente. Sem ele, “sair do recovery” deve
mostrar uma mensagem clara.

### Captura de ecrã falha em iOS 17+

O iOS pode exigir a imagem de programador montada. A app deve explicar isso, não
crashar.

### A API do firmware falha

```bash
curl -I 'https://api.ipsw.me/v4/device/iPhone13,2?type=ipsw'
```

Verifica rede, DNS, proxy e certificados. A app deve mostrar um erro legível e
permitir tentar novamente.

### O `.deb` não substitui a versão antiga

```bash
sudo apt install --reinstall ./publish/fdr-system_1.5.9_amd64.deb
```

Confirma depois:

```bash
readlink -f "$(which fdr-system)"
stat -c '%y %n' "$(readlink -f "$(which fdr-system)")"
```

---

## 9. Segurança e limites

- A app **não** pede nem guarda Apple ID, palavra-passe ou código de dois fatores.
- A app **não** contorna iCloud, Activation Lock ou MDM.
- A app **não** remove gestão empresarial/escolar.
- A app **não** executa `sudo`; os comandos de sistema são mostrados ao utilizador.
- A app **não** instala drivers Apple, UsbDk, libwdi, pnputil ou WinRa1n.
- A app **não** envia dados do dispositivo para servidores próprios.
- Nada é apagado do iPhone sem confirmação explícita.
- UDID, série, IMEI e outros identificadores ficam mascarados por omissão.
- O restauro é destrutivo: testa num aparelho de teste, com backup.
- Só firmware assinado pela Apple deve ser usado para restauro.
- Verifica sempre o SHA-256 do IPSW.
- O repositório é privado; não o tornar público sem autorização.
- Ferramentas de jailbreak são externas e opcionais; não são incluídas no FDR.

---

## 10. Ligações úteis

- libimobiledevice — <https://libimobiledevice.org/>
- usbmuxd — <https://github.com/libimobiledevice/usbmuxd>
- idevicerestore — <https://github.com/libimobiledevice/idevicerestore>
- API pública de firmware — <https://api.ipsw.me/v4/>
- .NET no Linux — <https://learn.microsoft.com/dotnet/core/install/linux>
- Avalonia — <https://docs.avaloniaui.net/>
- palera1n — <https://palera.in/>
- checkra1n — <https://checkra.in/>

Antes de usar uma ferramenta de jailbreak, confirma a compatibilidade atual no site
oficial. Não confies em tutoriais antigos nem em binários de terceiros.

---

## 11. Comandos rápidos

```bash
# 1. Instalar dependências (Debian/Ubuntu/Kali)
sudo bash packaging/install-linux-deps.sh

# 2. Verificar ambiente
bash packaging/check-linux-env.sh

# 3. Atualizar código
cd ~/FDR_SYSTEM-Linux
git pull --ff-only origin main
git log -1 --oneline

# 4. Compilar
dotnet restore FDR_SYSTEM.sln
dotnet build src/FdrSystem.App/FdrSystem.App.csproj -c Debug

# 5. Correr
dotnet run --project src/FdrSystem.App/FdrSystem.App.csproj

# 6. Publicar
bash packaging/build-linux.sh

# 7. Instalar/substituir .deb
sudo apt install --reinstall ./publish/fdr-system_1.5.9_amd64.deb

# 8. Estado USB
systemctl status usbmuxd --no-pager
idevice_id -l
ideviceinfo -k ProductType

# 9. Commit
git status
git add .
git commit -m "feat: ..."
git push origin main
```

Se algo falhar, junta a saída de:

```bash
git log -1 --oneline
dotnet --info
bash packaging/check-linux-env.sh
systemctl status usbmuxd --no-pager
idevice_id -l
```

e abre uma issue/nota no repositório privado com esses dados. **Não incluas UDID,
série, IMEI, Apple ID ou tokens.**
