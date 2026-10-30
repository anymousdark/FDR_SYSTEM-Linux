#!/usr/bin/env bash
# Sobe a versão do projeto num único sítio: Directory.Build.props.
#
#   bash packaging/bump-version.sh patch          # 1.6.0 -> 1.6.1
#   bash packaging/bump-version.sh minor          # 1.6.0 -> 1.7.0
#   bash packaging/bump-version.sh major          # 1.6.0 -> 2.0.0
#   bash packaging/bump-version.sh 1.6.2          # versão explícita
#
# Opções:
#   --commit    faz commit da alteração ("chore: versão X")
#   --tag       cria a tag anotada vX (implica --commit)
#   --push      envia o commit e a tag para origin (implica --tag)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROPS="$ROOT/Directory.Build.props"

[[ -f "$PROPS" ]] || { echo "ERRO: $PROPS não existe." >&2; exit 1; }

part="${1:-patch}"
shift || true

commit=0
tag=0
push=0
for arg in "$@"; do
  case "$arg" in
    --commit) commit=1 ;;
    --tag)    tag=1; commit=1 ;;
    --push)   push=1; tag=1; commit=1 ;;
    *) echo "ERRO: opção desconhecida '$arg'." >&2; exit 1 ;;
  esac
done

current="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$PROPS" | head -1)"
[[ -n "$current" ]] || { echo "ERRO: não encontrei <Version> em $PROPS." >&2; exit 1; }

if [[ "$part" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  new="$part"
else
  IFS=. read -r major minor patch <<<"$current"
  case "$part" in
    major) major=$((major + 1)); minor=0; patch=0 ;;
    minor) minor=$((minor + 1)); patch=0 ;;
    patch) patch=$((patch + 1)) ;;
    *) echo "ERRO: usa major, minor, patch ou uma versão X.Y.Z." >&2; exit 1 ;;
  esac
  new="$major.$minor.$patch"
fi

if [[ "$new" == "$current" ]]; then
  echo "Versão já é $current; nada a fazer."
  exit 0
fi

sed -i "s:<Version>$current</Version>:<Version>$new</Version>:" "$PROPS"
echo "versão: $current -> $new"

cd "$ROOT"
if [[ "$commit" -eq 1 ]]; then
  git add Directory.Build.props
  git commit -m "chore: versão $new" >/dev/null
  echo "commit criado: chore: versão $new"
fi

if [[ "$tag" -eq 1 ]]; then
  git tag -a "v$new" -m "FDR_SYSTEM Linux $new"
  echo "tag criada: v$new"
fi

if [[ "$push" -eq 1 ]]; then
  branch="$(git rev-parse --abbrev-ref HEAD)"
  git push origin "$branch"
  git push origin "v$new"
  echo "commit e tag enviados para origin ($branch)."
fi

echo "Próximo passo: bash packaging/build-linux.sh"
