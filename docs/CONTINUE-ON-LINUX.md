# Continuar a build no Linux

> **Handoff:** 28/10/2026 · branch `main` · confirma o commit com `git log -1 --oneline`
> Repositório privado: `https://github.com/anymousdark/FDR_SYSTEM-Linux`
>
> Este documento é o resumo rápido. O guia completo, com dependências por página,
> diagnóstico de “só aparece Offline” e plano de implementação, está em
> [`docs/LINUX-FULL-GUIDE.md`](LINUX-FULL-GUIDE.md).
> Lê também `README.md`, `AGENTS.md` e `docs/PORTING-MAP.md`.

---

## 1. O que já está feito

| Área | Estado |
|---|---|
| Shell Avalonia, temas, navegação e página Offline | funcional |
| Diagnóstico | implementado; testado com iPhone real no Windows |
| Acessibilidade (AFC) | implementado; testado com iPhone real no Windows |
| Driver | implementado; testado no Windows; falta validar no Linux |
| Online, Firmware, Restauro, Reparo e Jailbreak | ainda placeholders |
| Publicação `linux-x64` self-contained | funcional |
| `.deb` básico | funcional; AppImage ainda falta |

Testes já feitos no Windows de desenvolvimento, com um iPhone 13 real:

- deteção USB e leitura de dados;
- AFC: listar `/`, `/DCIM`, entrar em `100APPLE` e descarregar um ficheiro pequeno;
- Diagnóstico: `idevicepair validate`, `ideviceprovision list` e syslog a iniciar/parar;
- Driver: deteção do iPhone, atualização do estado e cópia dos comandos.

**O que falta mesmo é validar tudo numa instalação Linux real.**

---

## 2. Instalar o Linux

O alvo documentado é Debian/Ubuntu/Kali. Noutra distribuição, adapta os nomes dos pacotes.

### 2.1 Pacotes base

```bash
sudo apt update
sudo apt install -y git curl \
  libimobiledevice-utils libimobiledevice-1.0-6 usbmuxd \
  dpkg-deb fakeroot
```

Opcional, para restauro:

```bash
sudo apt install -y idevicerestore || true
```

### 2.2 .NET 8 SDK

Tenta primeiro o pacote da distribuição:

```bash
sudo apt install -y dotnet-sdk-8.0
dotnet --info
```

Se o pacote não existir, usa o instalador oficial da Microsoft:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 8.0
export PATH="$HOME/.dotnet:$PATH"
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
dotnet --info
```

O projeto fixa **.NET 8 / `net8.0`**. Não mudar para .NET 9 sem migrar o código.

### 2.3 Permissões USB

```bash
sudo usermod -aG plugdev "$USER"
sudo systemctl enable --now usbmuxd
```

Depois de alterar os grupos, **termina a sessão e entra novamente** para as permissões
passarem a valer.

### 2.4 Confirmar que o Linux vê o iPhone

Liga o iPhone por USB, desbloqueia-o e aceita **«Confiar neste computador»**.

```bash
systemctl status usbmuxd --no-pager
idevice_id -l
ideviceinfo -k ProductType
idevicepair validate
```

Se `idevice_id -l` não devolver nada, o problema é do sistema (`usbmuxd`, cabo, pareamento
ou permissões), não da GUI.

---

## 3. Obter o código

Repositório privado. Usa a conta GitHub que já tem acesso.

```bash
git clone https://github.com/anymousdark/FDR_SYSTEM-Linux.git
cd FDR_SYSTEM-Linux
git switch main
git pull
git log -1 --oneline
```

Se o clone por HTTPS pedir autenticação, usa um token pessoal ou configura SSH:

```bash
git clone git@github.com:anymousdark/FDR_SYSTEM-Linux.git
```

Não colar tokens em issues, logs ou capturas de ecrã.

---

## 4. Build e execução

```bash
dotnet restore FDR_SYSTEM.sln
dotnet build src/FdrSystem.App/FdrSystem.App.csproj -c Debug
```

Resultado esperado: **0 erros, 0 avisos**.

Correr a aplicação:

```bash
dotnet run --project src/FdrSystem.App/FdrSystem.App.csproj
```

Precisa de sessão gráfica X11 ou Wayland. Não há modo headless.

---

## 5. Teste de aceitação com iPhone

### Offline

- Abrir a app com o iPhone ligado e desbloqueado.
- Confirmar modelo, iOS, bateria e armazenamento.
- Confirmar que UDID, série e IMEI aparecem mascarados por omissão.

### Diagnóstico

- Confirmar `Ferramentas libimobiledevice: OK`.
- **Validar** o emparelhamento.
- **Listar perfis** (só leitura).
- Iniciar e parar o syslog.
- Opcional: recolher crash logs para ZIP; os relatórios são copiados, não apagados.

### Acessibilidade (AFC)

- Confirmar que a lista de `/` aparece.
- Duplo clique em `DCIM` e depois em `100APPLE`.
- Exportar **um ficheiro pequeno** para `/tmp` e confirmar o tamanho.
- Não testar apagar/importar no iPhone sem confirmação explícita.

### Driver

A página deve mostrar, numa máquina Linux:

- ambiente `Linux — sysfs USB + usbmuxd ativo`;
- socket `presente (/run/usbmuxd)`;
- serviço `active` e regras udev encontradas;
- grupos do utilizador e indicação de `plugdev`;
- o iPhone na tabela, em modo `Normal` ou `Recovery`/`DFU`;
- ferramentas essenciais `OK`.

Se aparecer `socket ausente`:

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

A app **não executa `sudo`**. Os comandos são copiados para o terminal pelo utilizador.

---

## 6. Publicar e instalar

### Binário self-contained

```bash
dotnet publish src/FdrSystem.App/FdrSystem.App.csproj \
  -c Release -r linux-x64 --self-contained true \
  -o publish/linux-x64

./publish/linux-x64/FDR_SYSTEM
```

### Script de empacotamento

```bash
bash packaging/build-linux.sh
```

O script gera o publish e, se `dpkg-deb` estiver instalado, um `.deb` simples.

```bash
sudo apt install ./publish/fdr-system_1.5.9_amd64.deb
fdr-system
```

O `.deb` atual é **amd64**. Para ARM64 é preciso ajustar a arquitetura no `control`.

---

## 7. Próximos passos de desenvolvimento

Ordem recomendada:

1. **Online / Firmware / Restauro** — portar `DownloadManager.cs` (download segmentado e
   SHA-256) e executar `idevicerestore` com progresso por `stdout`.
2. **Reparo** — recovery/restart/ativação; MDM apenas deteção.
3. **Jailbreak** — detetar `palera1n`/`checkra1n` instalados e abrir a ferramenta do sistema.
4. **Empacotamento** — AppImage e `.deb` mais completo.
5. **CI** — build e testes num runner Linux.

Para cada página:

- criar `Views/Pages/<Pagina>View.axaml(.cs)` em code-behind;
- ligar em `MainWindow.ShowPage(string tag)`;
- remover a entrada correspondente de `PlaceholderView.cs`;
- atualizar `README.md`, `AGENTS.md` e `docs/PORTING-MAP.md`;
- compilar, testar, fazer commit e push.

### Regras que não mudam

- Avalonia **11.3.22**; DataGrid **11.3.13**; .NET **8**.
- XAML em **`.axaml`**; bindings compilados continuam desligados.
- UI em code-behind, não converter para MVVM a meio do port.
- Português de Portugal nas strings.
- Mascarar identificadores por omissão.
- Não mexer na app Windows `OfflineDeviceDiagnostics`.
- Não implementar contorno de iCloud/Activation Lock/MDM.
- Não instalar libwdi nem confiar em certificados self-signed; usar `usbmuxd` e
  `libimobiledevice` oficiais.

---

## 8. Problemas comuns

### `dotnet publish` falha com `ResponseEnded`

Repetir o comando. O runtime pack fica em cache e a segunda tentativa costuma passar.

### `AVLN2000: Unable to resolve XAML resource`

O ficheiro tem de ser `.axaml`; um `.xaml` não é compilado e o erro engana.

### `libimobiledevice-1.0.so` em falta

```bash
sudo apt install libimobiledevice-1.0-6
```

### `idevice_id -l` vazio

```bash
systemctl status usbmuxd --no-pager
idevicepair validate
groups
```

Confirmar cabo de dados, iPhone desbloqueado, «Confiar neste computador» e sessão
reiniciada depois do `usermod`.

### A janela não abre

Confirmar que estás numa sessão X11/Wayland e que `DISPLAY` ou `WAYLAND_DISPLAY` estão
definidos.

### A página Driver mostra Windows

É normal se estiveres a correr o build de desenvolvimento no Windows. A página só lê
`usbmuxd`/udev quando corre em Linux.

---

## 9. Segurança e limites

- A app não pede nem guarda Apple ID, palavra-passe ou código de dois fatores.
- A app não contorna iCloud, Activation Lock ou MDM.
- A app não envia dados do dispositivo para servidores próprios.
- Nada é apagado do iPhone sem confirmação explícita.
- UDID, série, IMEI e outros identificadores ficam mascarados por omissão.
- O repositório é privado; não o tornar público sem autorização.

---

## 10. Comandos rápidos

```bash
# Atualizar
git pull

# Compilar
dotnet build src/FdrSystem.App/FdrSystem.App.csproj -c Debug

# Correr
dotnet run --project src/FdrSystem.App/FdrSystem.App.csproj

# Publicar
dotnet publish src/FdrSystem.App/FdrSystem.App.csproj \
  -c Release -r linux-x64 --self-contained true -o publish/linux-x64

# Estado USB
systemctl status usbmuxd --no-pager
idevice_id -l
ideviceinfo -k ProductType

# Commit
git status
git add .
git commit -m "feat: ..."
git push origin main
```
