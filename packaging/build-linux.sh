#!/usr/bin/env bash
# Publica a app para linux-x64 e, se existir dpkg-deb, gera um .deb simples.
#   bash packaging/build-linux.sh            # x64, self-contained
#   RID=linux-arm64 bash packaging/build-linux.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID="${RID:-linux-x64}"
VERSION="1.5.9"
OUT="$ROOT/publish/$RID"

echo "==> publish ($RID)"
dotnet publish "$ROOT/src/FdrSystem.App/FdrSystem.App.csproj" \
  -c Release -r "$RID" --self-contained true \
  -o "$OUT"

if [ ! -x "$OUT/FDR_SYSTEM" ]; then
  echo "ERRO: binário não gerado em $OUT/FDR_SYSTEM" >&2
  exit 1
fi
chmod +x "$OUT/FDR_SYSTEM"

echo "==> ok: $OUT/FDR_SYSTEM ($(du -sh "$OUT" | cut -f1))"

if ! command -v dpkg-deb >/dev/null 2>&1; then
  echo "==> dpkg-deb não encontrado; a saltar o .deb (AppImage/Flatpak ficam por fazer)"
  exit 0
fi

echo "==> a gerar .deb"
PKGROOT="$ROOT/publish/deb-root"
rm -rf "$PKGROOT"
mkdir -p "$PKGROOT/DEBIAN" "$PKGROOT/opt/fdr-system" "$PKGROOT/usr/bin" "$PKGROOT/usr/share/applications"

cp -r "$OUT/." "$PKGROOT/opt/fdr-system/"
ln -sf /opt/fdr-system/FDR_SYSTEM "$PKGROOT/usr/bin/fdr-system"

cat > "$PKGROOT/DEBIAN/control" <<EOF
Package: fdr-system
Version: $VERSION
Section: utils
Priority: optional
Architecture: amd64
Depends: libimobiledevice-utils, libimobiledevice-1.0-6, usbmuxd
Maintainer: anymousdark
Description: FDR_SYSTEM — leitura, recuperacao e restauro de iPhones (Linux)
 Diagnostico offline, firmwares, restauro e ficheiros de iPhones por USB.
EOF

cat > "$PKGROOT/usr/share/applications/fdr-system.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=FDR_SYSTEM
Comment=Diagnostico e recuperacao de iPhones
Exec=/opt/fdr-system/FDR_SYSTEM
Terminal=false
Categories=Utility;
EOF

dpkg-deb --build --root-owner-group "$PKGROOT" "$ROOT/publish/fdr-system_${VERSION}_amd64.deb" >/dev/null
echo "==> $ROOT/publish/fdr-system_${VERSION}_amd64.deb"
