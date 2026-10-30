#!/usr/bin/env bash
# Gera uma AppImage self-contained para a arquitectura do host.
#   bash packaging/build-appimage.sh
#   RID=linux-arm64 bash packaging/build-appimage.sh   # só num host aarch64
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID="${RID:-linux-x64}"
PROPS="$ROOT/Directory.Build.props"
VERSION="${VERSION:-$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$PROPS" 2>/dev/null | head -1)}"
if [[ -z "$VERSION" ]]; then
  echo "ERRO: versão não encontrada em Directory.Build.props." >&2
  exit 1
fi

case "$RID" in
  linux-x64)   APPIMAGE_ARCH="x86_64" ;;
  linux-arm64) APPIMAGE_ARCH="aarch64" ;;
  *)
    echo "ERRO: RID '$RID' sem arquitectura AppImage conhecida (usa linux-x64 ou linux-arm64)." >&2
    exit 1
    ;;
esac

HOST_ARCH="$(uname -m)"
if [[ "$HOST_ARCH" != "$APPIMAGE_ARCH" ]]; then
  echo "ERRO: a AppImage $APPIMAGE_ARCH tem de ser gerada num host $APPIMAGE_ARCH (este é $HOST_ARCH)." >&2
  echo "      Usa um runner/VM dessa arquitectura; o .deb funciona em cross-build." >&2
  exit 1
fi

OUT_BIN="$ROOT/publish/$RID/FDR_SYSTEM"
if [ ! -x "$OUT_BIN" ]; then
  echo "==> publish em falta; a compilar ($RID)"
  RID="$RID" bash "$ROOT/packaging/build-linux.sh"
fi

TOOLS="$ROOT/publish/tools"
TOOL="$TOOLS/appimagetool-$APPIMAGE_ARCH.AppImage"
mkdir -p "$TOOLS"
if [ ! -x "$TOOL" ]; then
  echo "==> a descarregar appimagetool ($APPIMAGE_ARCH)"
  curl -fL --retry 3 --max-time 180 \
    -o "$TOOL" \
    "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-${APPIMAGE_ARCH}.AppImage"
  chmod +x "$TOOL"
fi

APPDIR="$ROOT/publish/AppDir-$APPIMAGE_ARCH"
ICONS="$ROOT/src/FdrSystem.App/Assets"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/lib/fdr-system" \
         "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/256x256/apps"

cp -r "$ROOT/publish/$RID/." "$APPDIR/usr/lib/fdr-system/"

install -m644 "$ICONS/fdr-system-256.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/fdr-system.png"
install -m644 "$ICONS/fdr-system-256.png" "$APPDIR/fdr-system.png"
ln -sf fdr-system.png "$APPDIR/.DirIcon"

cat > "$APPDIR/fdr-system.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=FDR_SYSTEM
Comment=Diagnostico e recuperacao de iPhones
Exec=fdr-system
Icon=fdr-system
Terminal=false
Categories=Utility;
StartupWMClass=FDR_SYSTEM
Keywords=iphone;ipsw;firmware;restore;diagnostico;usb;
X-AppImage-Version=$VERSION
EOF
cp "$APPDIR/fdr-system.desktop" "$APPDIR/usr/share/applications/fdr-system.desktop"

cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/fdr-system/FDR_SYSTEM" "$@"
EOF
chmod 755 "$APPDIR/AppRun"

# O appimagetool exige leitura para todos e execução onde já existe.
chmod -R a+rX "$APPDIR"

APPIMAGE="$ROOT/publish/FDR_SYSTEM-${VERSION}-${APPIMAGE_ARCH}.AppImage"
echo "==> a gerar AppImage"
ARCH="$APPIMAGE_ARCH" APPIMAGE_EXTRACT_AND_RUN=1 \
  "$TOOL" --no-appstream "$APPDIR" "$APPIMAGE"
chmod +x "$APPIMAGE"
echo "==> $APPIMAGE"
