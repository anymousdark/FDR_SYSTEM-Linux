#!/usr/bin/env bash
# Diagnóstico do ambiente Linux do FDR_SYSTEM.
# Não instala nada e não corre sudo.
#
#   bash packaging/check-linux-env.sh
#
# Mostra o commit, as ferramentas, o usbmuxd, o iPhone, as páginas ligadas
# e compara o binário do repositório com um eventual FDR_SYSTEM instalado.
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT" || exit 1

failures=0
warnings=0

ok()   { printf '[ OK ]    %s\n' "$*"; }
warn() { printf '[AVISO]   %s\n' "$*"; warnings=$((warnings + 1)); }
fail() { printf '[FALHA]   %s\n' "$*"; failures=$((failures + 1)); }
info() { printf '          %s\n' "$*"; }

echo "== FDR_SYSTEM — verificação Linux =="
echo "Repositório: $ROOT"
echo

echo "-- sistema"
if [[ "$(uname -s 2>/dev/null)" == "Linux" ]]; then
  ok "Linux: $(uname -sr)"
else
  warn "não estás em Linux ($(uname -s 2>/dev/null)); a app só lê usbmuxd/udev em Linux."
fi

if [[ -d .git ]] && command -v git >/dev/null 2>&1; then
  branch="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
  commit="$(git log -1 --oneline 2>/dev/null || echo '?')"
  ok "git: $branch — $commit"
  dirty="$(git status --porcelain 2>/dev/null || true)"
  if [[ -n "$dirty" ]]; then
    warn "há alterações locais por commit; não faças reset sem as guardar:"
    printf '%s\n' "$dirty" | sed 's/^/          /'
  else
    ok "árvore de trabalho limpa"
  fi
else
  warn "não foi possível ler o estado do git"
fi

echo
echo "-- .NET"
if command -v dotnet >/dev/null 2>&1; then
  dotnet_version="$(dotnet --version 2>/dev/null || echo '?')"
  if [[ "$dotnet_version" == 8.* ]]; then
    ok ".NET SDK: $dotnet_version"
  else
    warn ".NET ativo é $dotnet_version; o projeto fixa net8.0."
  fi
  if dotnet --list-sdks 2>/dev/null | grep -qE '^8\.'; then
    ok "SDK 8 presente"
  else
    fail "SDK 8 não encontrado (dotnet --list-sdks)"
  fi
else
  fail "dotnet não encontrado no PATH"
fi

echo
echo "-- ferramentas libimobiledevice"
for tool in idevice_id ideviceinfo; do
  if command -v "$tool" >/dev/null 2>&1; then
    ok "$tool: $(command -v "$tool")"
  else
    fail "$tool em falta (instala libimobiledevice-utils)"
  fi
done

for tool in idevicepair idevicesyslog idevicecrashreport ideviceprovision idevicescreenshot; do
  if command -v "$tool" >/dev/null 2>&1; then
    ok "$tool: $(command -v "$tool")"
  else
    warn "$tool em falta (necessário para Diagnóstico/Acessibilidade)"
  fi
done

for tool in ideviceenterrecovery irecovery idevicerestore ideviceactivation; do
  if command -v "$tool" >/dev/null 2>&1; then
    info "$tool: $(command -v "$tool") (opcional)"
  else
    info "$tool: não instalado (opcional para Reparo/Restauro)"
  fi
done

echo
echo "-- biblioteca AFC"
if command -v ldconfig >/dev/null 2>&1 && ldconfig -p 2>/dev/null | grep -q 'libimobiledevice-1\.0\.so'; then
  ok "libimobiledevice-1.0.so encontrada"
else
  warn "libimobiledevice-1.0.so não encontrada; Acessibilidade/AFC pode falhar"
  info "sudo apt install libimobiledevice-1.0-6"
fi

echo
echo "-- usbmuxd / udev"
if command -v systemctl >/dev/null 2>&1; then
  service_state="$(systemctl is-active usbmuxd 2>/dev/null || true)"
  if [[ "$service_state" == "active" ]]; then
    ok "serviço usbmuxd: active"
  else
    warn "serviço usbmuxd: ${service_state:-desconhecido}"
    info "sudo systemctl enable --now usbmuxd"
  fi
else
  warn "systemctl indisponível (container ou sistema sem systemd)"
fi

if [[ -S /run/usbmuxd || -S /var/run/usbmuxd ]]; then
  ok "socket usbmuxd presente"
else
  warn "socket /run/usbmuxd ausente"
fi

udev_path=""
for candidate in \
  /etc/udev/rules.d/39-usbmuxd.rules \
  /usr/lib/udev/rules.d/39-usbmuxd.rules \
  /lib/udev/rules.d/39-usbmuxd.rules
do
  if [[ -f "$candidate" ]]; then udev_path="$candidate"; break; fi
done
if [[ -z "$udev_path" ]] && command -v dpkg >/dev/null 2>&1; then
  udev_path="$(dpkg -L usbmuxd 2>/dev/null | grep -E '\.rules$' | head -n 1 || true)"
fi
if [[ -n "$udev_path" ]]; then
  ok "regras udev: $udev_path"
else
  warn "regras udev do usbmuxd não encontradas"
fi

if command -v id >/dev/null 2>&1; then
  groups="$(id -nG 2>/dev/null || true)"
  if printf '%s\n' "$groups" | tr ' ' '\n' | grep -qx plugdev; then
    ok "utilizador em plugdev"
  else
    warn "utilizador não está em plugdev: $groups"
    info "sudo usermod -aG plugdev \"\$USER\"  (depois termina sessão)"
  fi
fi

echo
echo "-- iPhone"
if command -v idevice_id >/dev/null 2>&1; then
  if command -v timeout >/dev/null 2>&1; then
    ids="$(timeout 8 idevice_id -l 2>/dev/null || true)"
  else
    ids="$(idevice_id -l 2>/dev/null || true)"
  fi
  if [[ -n "$ids" ]]; then
    ok "iPhone(s) detetado(s):"
    printf '%s\n' "$ids" | sed 's/^/          /'
  else
    warn "idevice_id -l não devolveu nenhum iPhone"
    info "desbloqueia o iPhone, aceita «Confiar neste computador» e confirma o cabo."
  fi
else
  warn "não foi possível testar o iPhone (idevice_id em falta)"
fi

if command -v lsusb >/dev/null 2>&1; then
  if lsusb 2>/dev/null | grep -qi 'Apple'; then
    ok "dispositivo Apple visível no lsusb"
  else
    warn "nenhum dispositivo Apple no lsusb"
  fi
fi

echo
echo "-- páginas ligadas no código"
for tag in offline diagnostico acessibilidade driver; do
  if grep -q "\"$tag\" =>" src/FdrSystem.App/Views/MainWindow.axaml.cs 2>/dev/null; then
    ok "rota $tag presente"
  else
    fail "rota $tag ausente (código antigo ou ficheiro errado)"
  fi
done

echo
echo "-- placeholders esperados"
for tag in online restauro reparo firmware jailbreak; do
  if grep -q "\[\"$tag\"\]" src/FdrSystem.App/Views/Pages/PlaceholderView.cs 2>/dev/null; then
    info "$tag: ainda placeholder (por implementar)"
  else
    info "$tag: já não está em PlaceholderView"
  fi
done

echo
echo "-- binários"
repo_bin="$ROOT/publish/linux-x64/FDR_SYSTEM"
if [[ -x "$repo_bin" ]]; then
  ok "build do repositório: $repo_bin"
  info "$(stat -c '%y' "$repo_bin" 2>/dev/null || true)"
else
  warn "ainda não existe $repo_bin"
  info "bash packaging/build-linux.sh"
fi

installed_bin="$(command -v fdr-system 2>/dev/null || true)"
if [[ -n "$installed_bin" ]]; then
  resolved="$(readlink -f "$installed_bin" 2>/dev/null || echo "$installed_bin")"
  info "app instalada: $installed_bin -> $resolved"
  info "$(stat -c '%y' "$resolved" 2>/dev/null || true)"
  if [[ -x "$repo_bin" && "$resolved" != "$repo_bin" ]]; then
    warn "a app instalada é outro ficheiro; se vês páginas antigas, reinstala:"
    info "sudo apt install --reinstall ./publish/fdr-system_1.5.9_amd64.deb"
  fi
else
  info "fdr-system não está no PATH; podes correr ./publish/linux-x64/FDR_SYSTEM"
fi

echo
echo "-- logs"
if [[ -d "$HOME/.local/share/FDR_SYSTEM/logs" ]]; then
  info "$HOME/.local/share/FDR_SYSTEM/logs"
else
  info "a pasta de logs aparece depois da primeira execução."
fi

echo
if [[ "$failures" -gt 0 ]]; then
  echo "Resultado: $failures falha(s), $warnings aviso(s)."
  exit 1
fi

echo "Resultado: sem falhas essenciais; $warnings aviso(s)."
