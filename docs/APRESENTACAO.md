# FDR SYSTEM

**Por anymousdark.** Aplicação **Linux x64 e ARM64** (Avalonia 11.3 / .NET 8) para
**diagnóstico, leitura e recuperação** de um iPhone ligado por USB. Está dividida em
**dez páginas**, para deixar claro o que usa (ou não) a internet, e abre na página
**Offline** com a janela centrada (1240×760, mínimo 1120×620). A barra de estado fica
integrada no topo, para o conteúdo usar a janela toda. Há **cinco temas** — Escuro,
Claro, Meia-noite, Floresta e Ametista; o botão **Tema** abre o seletor e a escolha fica
guardada.

> Este é o **documento de apresentação** da versão Linux. Para compilar, testar e
> empacotar, vê o [`README.md`](../README.md) e o
> [`docs/LINUX-FULL-GUIDE.md`](LINUX-FULL-GUIDE.md). A aplicação Windows original
> mantém o seu próprio README e numeração.

| Página | Internet | O que faz |
|---|---|---|
| **Offline** | Não* | Lê os campos principais (modelo, iOS, serial, ECID, UDID, bateria, armazenamento); **Ver todos** mostra a tabela completa e **Detalhes** abre a ficha; **Detalhes da bateria** mostra nível, saúde, ciclos, capacidades, voltagens, corrente, carregador e autonomia; **ficha técnica** do modelo (chip, RAM, ecrã, câmara e ano); copiar/revelar IDs e exportar JSON. Em recovery/DFU lê modelo, serial, ECID, CPID/BDID e iBoot direto do USB |
| **Online** | Sim | Consulta o catálogo público de firmware da Apple (`api.ipsw.me`), descarrega o IPSW assinado e restaura — o aparelho é detetado sozinho, **mesmo em recovery/DFU**. Lista de downloads com **velocidade em direto**, **vários downloads em simultâneo**, pausa/retoma, eliminação e verificação SHA-256 |
| **Restauro** | Não | Progresso do restauro em direto: preparação/extração do IPSW numa barra, gravação no iPhone na outra, fase atual em português e saída completa do `idevicerestore`. Pede confirmação antes de apagar e permite cancelar |
| **Reparo** | Não | Estado do aparelho: verificar modo, entrar/sair de recovery, reiniciar, dados em recovery/DFU e guia de DFU (botões físicos). Mostra o **estado de ativação** e a **gestão empresarial/MDM** apenas como deteção — nunca pede credenciais nem contorna bloqueios |
| **Diagnóstico** | Não | **Registo do iOS em direto** (`idevicesyslog`), recolha de **crash logs e panics** para ZIP (`idevicecrashreport`), validação/reparação do **emparelhamento** (`idevicepair`) e lista de **perfis de aprovisionamento** (`ideviceprovision`) — tudo local e só de leitura ou com pedido explícito |
| **Firmware** | Sim | Catálogo de **todos** os iPhones: escolha o modelo, veja todas as versões (**assinadas e expiradas**) e descarregue qualquer uma — com aviso claro antes de versões que a Apple já não assina |
| **Acessibilidade** | Não | Fotos e ficheiros do iPhone no PC: navegar na área de ficheiros (AFC), importar/exportar (inclui **todas as fotos DCIM** de uma vez), criar/apagar pastas e **captura do ecrã do iPhone** |
| **Driver** | Não | Estado do acesso USB no Linux: `usbmuxd`, serviço systemd, regras udev, grupos do utilizador e dispositivos Apple. Só lê o sistema e mostra os comandos de correção — não instala nem altera nada |
| **Instalação** | Sim* | Deteta a distribuição, o gestor de pacotes e o estado de `usbmuxd`, `libimobiledevice` e motores opcionais. Instala com **uma ação explícita** por `pkexec` (polkit), com registo em direto e verificação final; a app nunca lê nem guarda a palavra-passe |
| **Jailbreak** | Não | Recurso **opcional**: deteta `palera1n`/`checkra1n`, mostra caminho, versão e comandos para copiar, com avisos checkm8 (A5–A11). **Não executa** as ferramentas, **não corre `sudo`** e não serve para contornar iCloud, Activation Lock ou MDM |

\* A página Offline só usa a rede para o aviso de iOS mais recente; a leitura do aparelho
e a ficha técnica são offline. A página Instalação só usa a rede para atualizar pacotes.

## Instalação

**Recomendado — pacote `.deb`** (amd64 ou arm64):

```bash
sudo apt install ./fdr-system_1.6.1_amd64.deb
# ou
sudo apt install ./fdr-system_1.6.1_arm64.deb
```

- Instala em `/opt/fdr-system`, cria o comando `fdr-system` e a entrada no menu com ícone.
- Dependências: `libimobiledevice-utils` e `usbmuxd`; `libimobiledevice-1.0-6`
  (ou `-6t64` no Ubuntu 24.04) fica como recomendação.
- O `idevicerestore` é opcional e só é necessário para a página Restauro.

**Portátil — AppImage** (x86_64):

```bash
chmod +x FDR-SYSTEM-1.6.1-x86_64.AppImage
./FDR-SYSTEM-1.6.1-x86_64.AppImage
```

Não precisa do .NET instalado nem de instalar dependências para arrancar; o acesso ao
iPhone continua a precisar de `usbmuxd` e `libimobiledevice` no sistema.

Os dois formatos são publicados na release
[`v1.6.1`](https://github.com/anymousdark/FDR_SYSTEM-Linux/releases/tag/v1.6.1), com
checksums SHA-256. O código-fonte completo também vai na release (arquivos automáticos
do GitHub e tarball `FDR-SYSTEM-1.6.1-source.tar.gz`).

### Permissões USB

O `usbmuxd` traz as regras udev. Se o iPhone não aparecer sem `sudo`:

```bash
sudo usermod -aG plugdev "$USER"
sudo systemctl enable --now usbmuxd
```

Depois de alterar os grupos, termina a sessão e entra novamente. A app **nunca** corre
`sudo`; quando precisa de elevação, a página Instalação usa `pkexec` e mostra o script
antes de o executar.

## Página Offline

1. Ligue e desbloqueie o iPhone; se pedido, confirme **Confiar neste computador**.
2. **A leitura começa automaticamente** ao ligar o iPhone; o botão **Ler iPhone** repete
   a leitura quando quiser. Uma só leitura (`ideviceinfo -x`) devolve **todos** os campos,
   agrupados e etiquetados em português. Os cartões mostram o resumo (modelo, bateria,
   armazenamento); a barra do topo reúne **Ler iPhone**, **Copiar**, **Copiar tudo**,
   **Exportar**, **Ver todos** e **Detalhes**, com **Revelar IDs** e pesquisa à direita.
3. **Ficha técnica:** o cartão **MODELO** mostra o nome comercial, chip, RAM, ecrã,
   câmara e ano de lançamento (tabela offline). A seguir à leitura, a app compara o iOS
   instalado com a versão pública mais recente do modelo e diz se está em dia ou se há
   versão nova — e se a Apple ainda a assina. Sem internet, a leitura continua a
   funcionar e o aviso é omitido.
4. **Detalhes da bateria:** nível e saúde estimada em barras, **valor bruto sem
   arredondamento**, capacidades (atual/design/máxima), ciclos, voltagens, corrente,
   carregador ligado e autonomia estimada; **Copiar** põe tudo em texto. O serial da
   bateria fica **mascarado** salvo **Revelar IDs**.

No iOS 26, o registo `AppleSmartBattery` pode deixar de expor `FullChargeCapacity`.
Nesse caso a app usa `AppleRawMaxCapacity`, `NominalChargeCapacity` ou
`ShutDownFullChargeCapacity` como fallback. A saúde é
`capacidade máxima / capacidade de projeto`, limitada a 0–100%; o valor arredondado é o
que aparece na etiqueta (Boa ≥90%, Razoável ≥80%, Fraca <80%) e o limiar de serviço
assinalado é 80%.

Mesmo antes do emparelhamento, a app deteta o iPhone por USB (sysfs) e mostra o UDID
sem abrir nem alterar o aparelho. Em modo recovery/DFU (PIDs USB 1227/1222/1281), mostra
o modo e os dados que o USB expõe: **modelo (CPID+BDID), serial, ECID e iBoot**. A
deteção do estado é automática (atualiza sozinha de 2 em 2 segundos).

## Página Online (usa a rede)

- **Consultar firmware** — pergunta ao catálogo público quais as versões que a Apple
  **ainda assina** para o modelo. O identificador é detetado sozinho por USB — normal,
  **recovery** ou **DFU** — ou escrito à mão.
- **Baixar IPSW** — descarrega para `~/Documents/iDevice-Firmware` com **motor
  segmentado** (várias ligações em paralelo): velocidade em MB/s em direto, retoma de
  onde parou, **vários downloads ao mesmo tempo** e verificação SHA-256 no fim.
- **Importar IPSW** — usa um ficheiro já descarregado; **Verificar** calcula o SHA-256
  e compara com o do catálogo (autenticidade).
- **Restaurar firmware** — restaura a versão assinada; antes de começar, a app **recusa
  firmware de outro modelo** (compara o nome do IPSW com o aparelho ligado) e o progresso
  aparece em direto na página **Restauro**.

> **Aviso:** o restauro **apaga todo o conteúdo** e repõe os dados de fábrica. Só funciona
> com firmware que a Apple ainda assina e **não** remove o bloqueio de ativação (iCloud) —
> para isso é preciso a conta original.

## Página Restauro

Mostra o restauro a acontecer, em duas barras:

1. **Preparação e extração do IPSW** — extrair e verificar os componentes do firmware;
2. **Gravação no iPhone** — enviar o sistema de ficheiros, verificar, gravar
   bootloader/firmware e modem.

Por baixo, a fase atual em português e a saída completa do motor (`idevicerestore`). No
fim, o iPhone reinicia no ecrã de configuração. Não desligue o cabo durante o processo.

## Página Reparo

Ações locais (sem rede): **Verificar modo**, **Entrar em modo recovery**
(`ideviceenterrecovery`), **Sair do modo recovery** (`irecovery -n`) e **Reiniciar**
(`idevicediagnostics restart`).

- **Estado de ativação** — consulta o estado (ativado / não ativado) sem tocar em nada do
  aparelho. A app **nunca** pede Apple ID nem palavra-passe e **não** contorna o bloqueio
  de ativação.
- **Gestão da empresa (MDM)** — mostra se o USB expõe supervisão, perfis ou MDM. É apenas
  deteção; a app não remove nem contorna gestão empresarial — a remoção passa pelo
  administrador do aparelho.
- **Guia de DFU** — o DFU só se entra com os botões físicos; a página explica a sequência
  para iPhone 8 e posteriores.

## Página Diagnóstico (logs e emparelhamento)

- **Registo do iOS em direto** — `idevicesyslog`: começar/parar, filtrar por texto,
  limpar e guardar em `.txt` na pasta `~/Documents/FDR-SYSTEM/Relatorios`. Se o iPhone
  desligar, o registo termina sozinho e a app diz porquê.
- **Relatórios de falhas** — `idevicecrashreport -e -k`: copia crash logs e panics para
  `~/Documents/FDR-SYSTEM/Relatorios/falhas-…` e gera um ZIP. Os relatórios **não** são
  apagados do iPhone.
- **Emparelhamento** — `idevicepair validate` / `pair`: valida a confiança entre este PC e
  o iPhone ou pede um novo emparelhamento. Desbloqueie o iPhone e aceite **Confiar neste
  computador**. A app **nunca** desemparelha sozinha.
- **Perfis de aprovisionamento** — `ideviceprovision list`: lista os perfis instalados
  (só leitura); não instala nem remove perfis.

## Página Firmware (todos os iPhones)

Catálogo completo, sem precisar de aparelho ligado:

- Escolha o modelo na lista (filtro por nome ou identificador, ex.: «12» ou «iPhone13,2»).
- A tabela mostra **todas** as versões — **Assinado** (a Apple ainda assina) ou
  **Expirado** — com data, tamanho e botão **Baixar** em cada linha.
- Versões expiradas podem ser descarregadas na mesma (útil para arquivo), com **aviso
  claro**: o restauro vai falhar, porque a Apple só permite instalar versões assinadas.
- O download entra na mesma lista da página Online (velocidade, pausa, eliminação). O
  catálogo fica em cache durante 12 horas.

## Página Acessibilidade (fotos e ficheiros)

- **Ficheiros** — navegue na área de ficheiros do iPhone (AFC): duplo clique abre pastas,
  **←** sobe, a barra de endereço aceita qualquer caminho.
- **Importar** — envia ficheiros do PC para o iPhone (por omissão para `/Importados`).
- **Exportar / Eliminar / Nova pasta** — com progresso, cancelamento e confirmação antes
  de apagar.
- **Exportar todas as fotos** — copia o DCIM inteiro (100APPLE, 101APPLE…) para o PC
  mantendo as pastas.
- **Captura do ecrã do iPhone** — guarda um PNG do ecrã (noutras versões do iOS pode pedir
  a imagem de programador; a app explica se falhar).

## Página Driver (acesso USB no Linux)

Não há drivers Apple para instalar: no Linux, o `usbmuxd` e as regras udev fazem esse
papel. A página mostra, só de leitura:

- se o socket do `usbmuxd` existe e se o serviço está ativo/ativado;
- o caminho das regras udev e os grupos do utilizador (por exemplo, `plugdev`);
- os dispositivos Apple vistos no `sysfs`, com o modo (normal, recovery ou DFU);
- os comandos de correção para copiar, sem os executar.

## Página Instalação (dependências)

- Deteta a distribuição, o gestor de pacotes (`apt`, `dnf` ou `pacman`) e o estado de cada
  dependência.
- **Instalar dependências** pede elevação por `pkexec` (polkit) e corre um script gerado
  pela app; o script pode ser copiado e revisto antes.
- A app **não corre `sudo`**, **não lê nem guarda a palavra-passe** e não instala nada
  sem uma ação explícita.
- No fim, verifica se as ferramentas essenciais ficaram disponíveis.

## Página Jailbreak (opcional)

- **Deteta** `palera1n` e `checkra1n`: caminho, versão (só do palera1n) e comandos
  `sudo palera1n -l`, `sudo palera1n -c -f` e `sudo checkra1n` para copiar.
- **Compatibilidade:** iPhones do 5s ao X (chips A7 a A11), com iOS 12 a 17. Em modelos
  mais recentes não funciona — nada é alterado no aparelho se só abrires a página.
- Usa apenas em aparelhos teus ou com autorização do dono. A app **não executa** as
  ferramentas, **não corre `sudo`** e não serve para contornar iCloud, Activation Lock
  ou MDM.

## Privacidade

- O email de contacto é opcional, digitado manualmente; a app **não** procura email da
  Apple Account.
- Chaves criptográficas e credenciais não são consultadas.
- A página Online contacta apenas o catálogo público de firmware; nada do aparelho é
  enviado.
- As páginas Offline, Restauro, Reparo, Diagnóstico, Firmware (lista já em cache),
  Acessibilidade, Driver e Jailbreak não enviam dados pela rede.
- Serial, ECID, UDID, IMEI, MACs e hashes ficam **mascarados** por omissão.

## Logs e diagnóstico

A app grava erros e eventos em
`~/.local/share/FDR-SYSTEM/logs/app-AAAAMMDD.log` (um ficheiro por dia, só nesta
máquina) e remove automaticamente logs com mais de 14 dias. O botão **Exportar logs** no
topo gera um ZIP com um resumo do sistema (versão, SO, ferramentas encontradas) mais os
logs, pronto a enviar para diagnóstico. Nada é enviado automaticamente.

Os dados ficam em:

| O quê | Onde |
|---|---|
| Definições | `~/.local/share/FDR-SYSTEM/settings.json` |
| Logs | `~/.local/share/FDR-SYSTEM/logs/` |
| Cache do catálogo | `~/.local/share/FDR-SYSTEM/cache/` |
| Relatórios | `~/Documents/FDR-SYSTEM/Relatorios/` |
| Firmware (IPSW) | `~/Documents/iDevice-Firmware/` |

Quem vem da 1.6.0 tem as pastas antigas `FDR_SYSTEM` migradas automaticamente na primeira
execução.

## Desenvolvimento

Requer o .NET 8 SDK e uma sessão gráfica X11 ou Wayland (não há modo headless).

```bash
# compilar
dotnet build FDR-SYSTEM.sln -c Release

# testes (166 testes dos parsers, caminhos e motores puros)
dotnet test tests/FdrSystem.Core.Tests/FdrSystem.Core.Tests.csproj -c Release

# correr
dotnet run --project src/FdrSystem.App/FdrSystem.App.csproj

# publicar + .deb
bash packaging/build-linux.sh

# AppImage (no host da mesma arquitectura)
bash packaging/build-appimage.sh
```

A CI em `.github/workflows/linux.yml` corre os testes e compila os dois RIDs; o workflow
`.github/workflows/release.yml` corre em tags `v*` (ou manualmente) e anexa os `.deb`, a
AppImage e o código-fonte à release.

## Versionamento

Cada função ou correção gera uma versão nova (série `1.6.x` em diante). O número vive
num único sítio, `Directory.Build.props`, e sobe com `packaging/bump-version.sh`. Uma
release publica:

- `fdr-system-<versão>-amd64.deb` e `fdr-system-<versão>-arm64.deb`;
- `FDR-SYSTEM-<versão>-x86_64.AppImage`;
- `FDR-SYSTEM-<versão>-source.tar.gz` e os arquivos de código-fonte do GitHub;
- checksums SHA-256 no corpo da release.

## Referências

- Conjunto de campos inspirado nos relatórios do 3uTools:
  [informações do dispositivo](https://www.3u.com/tutorial/details/133/how-to-view-iphones-information-using-3utools).
- iPhone 12 = Apple A14 Bionic:
  [especificações técnicas](https://support.apple.com/pt-pt/111876).
- `libimobiledevice`, `usbmuxd` e `idevicerestore`:
  [libimobiledevice.org](https://libimobiledevice.org/).
