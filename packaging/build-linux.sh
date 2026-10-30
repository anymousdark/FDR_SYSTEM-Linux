#!/usr/bin/env bash
# Publica a app para linux-x64 e, se existir dpkg-deb, gera um .deb simples.
#   bash packaging/build-linux.sh                 # x64, self-contained
#   RID=linux-arm64 bash packaging/build-linux.sh
#   bash packaging/build-linux.sh --bump patch    # sobe a versão antes de compilar
#   bash packaging/build-linux.sh --bump minor
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

BUMP=""
if [[ "${1:-}" == "--bump" ]]; then
  BUMP="${2:-patch}"
fi

if [[ -n "$BUMP" ]]; then
  bash "$ROOT/packaging/bump-version.sh" "$BUMP"
fi

RID="${RID:-linux-x64}"
PROPS="$ROOT/Directory.Build.props"
VERSION="${VERSION:-$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$PROPS" 2>/dev/null | head -1)}"
if [[ -z "$VERSION" ]]; then
  echo "ERRO: versão não encontrada em Directory.Build.props." >&2
  exit 1
fi
OUT="$ROOT/publish/$RID"

case "$RID" in
  linux-x64)   DEB_ARCH="amd64" ;;
  linux-arm64) DEB_ARCH="arm64" ;;
  *)
    echo "ERRO: RID '$RID' sem arquitectura .deb conhecida (usa linux-x64 ou linux-arm64)." >&2
    exit 1
    ;;
esac

echo "==> publish ($RID)"
dotnet publish "$ROOT/src/FdrSystem.App/FdrSystem.App.csproj" \
  -c Release -r "$RID" --self-contained true \
  -o "$OUT"

if [ ! -x "$OUT/FDR-SYSTEM" ]; then
  echo "ERRO: binário não gerado em $OUT/FDR-SYSTEM" >&2
  exit 1
fi
chmod +x "$OUT/FDR-SYSTEM"

echo "==> ok: $OUT/FDR-SYSTEM ($(du -sh "$OUT" | cut -f1))"

if ! command -v dpkg-deb >/dev/null 2>&1; then
  echo "==> dpkg-deb não encontrado; a saltar o .deb (AppImage/Flatpak ficam por fazer)"
  exit 0
fi

echo "==> a gerar .deb"
PKGROOT="$ROOT/publish/deb-root"
rm -rf "$PKGROOT"
mkdir -p "$PKGROOT/DEBIAN" "$PKGROOT/opt/fdr-system" "$PKGROOT/usr/bin" "$PKGROOT/usr/share/applications"

ICONS="$ROOT/src/FdrSystem.App/Assets"
if [ ! -f "$ICONS/fdr-system-256.png" ]; then
  echo "ERRO: ícone não encontrado em $ICONS." >&2
  exit 1
fi

cp -r "$OUT/." "$PKGROOT/opt/fdr-system/"
ln -sf /opt/fdr-system/FDR-SYSTEM "$PKGROOT/usr/bin/fdr-system"

cat > "$PKGROOT/DEBIAN/control" <<EOF
Package: fdr-system
Version: $VERSION
Section: utils
Priority: optional
Architecture: $DEB_ARCH
Depends: libimobiledevice-utils, usbmuxd
Recommends: libimobiledevice-1.0-6 | libimobiledevice-1.0-6t64
Maintainer: anymousdark <anymousdark@users.noreply.github.com>
Description: FDR SYSTEM — leitura, recuperacao e restauro de iPhones (Linux)
 Diagnostico offline, firmwares restauro e ficheiros de iPhones por USB.
 A app nao corre sudo e nao contorna iCloud, Activation Lock nem MDM.
EOF

cat > "$PKGROOT/usr/share/applications/fdr-system.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=FDR SYSTEM
Comment=Diagnostico e recuperacao de iPhones
Exec=/opt/fdr-system/FDR-SYSTEM
Icon=fdr-system
Terminal=false
StartupWMClass=FDR-SYSTEM
Categories=Utility;
Keywords=iphone;ipsw;firmware;restore;diagnostico;usb;
EOF
chmod 644 "$PKGROOT/usr/share/applications/fdr-system.desktop"

for size in 16 24 32 48 64 128 256 512; do
  install -Dm644 "$ICONS/fdr-system-${size}.png" \
    "$PKGROOT/usr/share/icons/hicolor/${size}x${size}/apps/fdr-system.png"
done

cat > "$PKGROOT/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true
fi
exit 0
EOF
chmod 755 "$PKGROOT/DEBIAN/postinst"

DEB="$ROOT/publish/fdr-system_${VERSION}_${DEB_ARCH}.deb"
dpkg-deb --build --root-owner-group "$PKGROOT" "$DEB" >/dev/null
echo "==> $DEB"
