#!/usr/bin/env bash
# Instala as dependências do FDR SYSTEM numa distribuição Debian/Ubuntu/Kali.
#
#   sudo bash packaging/install-linux-deps.sh
#
# O script não instala drivers da Apple, UsbDk, libwdi, WinRa1n nem
# certificados self-signed. No Linux o acesso USB é feito pelo usbmuxd
# oficial e pelas ferramentas do libimobiledevice.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ "${EUID:-$(id -u)}" -ne 0 ]]; then
  echo "ERRO: executa com sudo:" >&2
  echo "  sudo bash packaging/install-linux-deps.sh" >&2
  exit 1
fi

if ! command -v apt-get >/dev/null 2>&1; then
  echo "ERRO: este script é para Debian/Ubuntu/Kali (apt)." >&2
  echo "Em Fedora/Arch vê a secção de dependências em docs/LINUX-FULL-GUIDE.md." >&2
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive

echo "==> atualizar índices"
apt-get update

echo "==> dependências base, USB e GUI"
apt-get install -y --no-install-recommends \
  ca-certificates curl git usbutils \
  libimobiledevice-utils libimobiledevice-1.0-6 usbmuxd \
  dpkg-deb fakeroot xdg-utils \
  libx11-6 libice6 libsm6 libfontconfig1 libfreetype6 \
  libxrandr2 libxi6 libxcursor1 libxinerama1 libgl1 libegl1 \
  fonts-dejavu-core

# Opcionais: não impedem o build se o repositório da distribuição não os tiver.
install_optional() {
  local pkg="$1"
  if apt-get install -y --no-install-recommends "$pkg"; then
    echo "    $pkg: instalado"
  else
    echo "    $pkg: indisponível no repositório (opcional)" >&2
  fi
}

echo "==> motores opcionais"
install_optional idevicerestore
install_optional libirecovery-utils

echo "==> .NET 8 SDK"
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -qE '^8\.'; then
  echo "    SDK 8 já instalado: $(dotnet --version)"
else
  if ! apt-get install -y dotnet-sdk-8.0; then
    cat >&2 <<'EOF'
ERRO: o pacote dotnet-sdk-8.0 não está disponível neste repositório.

Instala o SDK 8 pela página oficial da Microsoft para Linux:
  https://learn.microsoft.com/dotnet/core/install/linux
Depois confirma:
  dotnet --list-sdks

O projeto fixa net8.0. Não uses .NET 9 sem migrar o código.
EOF
    exit 1
  fi
fi

echo "==> serviço usbmuxd e regras udev"
if command -v systemctl >/dev/null 2>&1; then
  systemctl enable --now usbmuxd 2>/dev/null || true
  systemctl status usbmuxd --no-pager 2>/dev/null || true
else
  echo "    systemctl indisponível; num container usa o socket /run/usbmuxd." >&2
fi

if command -v udevadm >/dev/null 2>&1; then
  udevadm control --reload-rules 2>/dev/null || true
  udevadm trigger 2>/dev/null || true
fi

if [[ -n "${SUDO_USER:-}" && "${SUDO_USER}" != "root" ]]; then
  if getent group plugdev >/dev/null 2>&1; then
    usermod -aG plugdev "$SUDO_USER" 2>/dev/null || true
    echo "==> $SUDO_USER adicionado ao grupo plugdev"
    echo "    Termina a sessão e volta a entrar para a permissão passar a valer."
  fi
fi

echo
echo "==> ferramentas encontradas"
missing=0
for tool in \
  idevice_id ideviceinfo idevicepair idevicesyslog \
  idevicecrashreport ideviceprovision idevicescreenshot
do
  if command -v "$tool" >/dev/null 2>&1; then
    printf '    %-22s %s\n' "$tool" "$(command -v "$tool")"
  else
    printf '    %-22s EM FALTA\n' "$tool"
    missing=1
  fi
done

for tool in ideviceenterrecovery irecovery idevicerestore ideviceactivation; do
  if command -v "$tool" >/dev/null 2>&1; then
    printf '    %-22s %s (opcional)\n' "$tool" "$(command -v "$tool")"
  else
    printf '    %-22s não instalado (opcional)\n' "$tool"
  fi
done

echo
if [[ "$missing" -ne 0 ]]; then
  echo "AVISO: faltam ferramentas essenciais. Confirma os pacotes acima." >&2
  exit 1
fi

echo "Dependências essenciais instaladas."
echo "Próximo passo: bash packaging/check-linux-env.sh"
