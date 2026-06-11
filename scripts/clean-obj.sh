#!/usr/bin/env bash
set -euo pipefail

resolve_script_path() {
  local source="${BASH_SOURCE[0]}"

  if [[ "$source" != */* ]]; then
    source="$(command -v -- "$source" || printf '%s' "$source")"
  fi

  while [[ -L "$source" ]]; do
    local dir
    dir="$(cd -P -- "$(dirname -- "$source")" >/dev/null 2>&1 && pwd)"
    source="$(readlink -- "$source")"
    [[ "$source" != /* ]] && source="$dir/$source"
  done

  local dir
  dir="$(cd -P -- "$(dirname -- "$source")" >/dev/null 2>&1 && pwd)"
  printf '%s/%s\n' "$dir" "$(basename -- "$source")"
}

SCRIPT_PATH="$(resolve_script_path)"
SCRIPT_DIR="$(cd -P -- "$(dirname -- "$SCRIPT_PATH")" && pwd)"
REPO_ROOT="$(cd -P -- "$SCRIPT_DIR/.." && pwd)"
ROOT="$REPO_ROOT"
LIST_ONLY=0
SKIP_BUILD_SERVER_SHUTDOWN=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --root)
      [[ $# -ge 2 ]] || { echo "Missing value for --root" >&2; exit 1; }
      ROOT="$2"
      shift 2
      ;;
    --list)
      LIST_ONLY=1
      shift
      ;;
    --skip-build-server-shutdown)
      SKIP_BUILD_SERVER_SHUTDOWN=1
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      echo "Usage: clean-obj.sh [--root <path>] [--list] [--skip-build-server-shutdown]" >&2
      exit 1
      ;;
  esac
done

ROOT="$(cd -P -- "$ROOT" && pwd)"

if [[ ! -f "$ROOT/Zhengyan.DigitalWife.sln" ]]; then
  echo "Repository root not recognized: $ROOT" >&2
  exit 1
fi

mapfile -t OBJ_DIRS < <(find "$ROOT" -type d -name obj | sort)

if [[ ${#OBJ_DIRS[@]} -eq 0 ]]; then
  echo "No obj directories found under $ROOT"
  exit 0
fi

for dir in "${OBJ_DIRS[@]}"; do
  echo "$dir"
done

if [[ $LIST_ONLY -eq 1 ]]; then
  echo "Listed ${#OBJ_DIRS[@]} obj directories."
  exit 0
fi

if [[ $SKIP_BUILD_SERVER_SHUTDOWN -eq 0 ]] && command -v dotnet >/dev/null 2>&1; then
  echo "Shutting down dotnet build servers..."
  dotnet build-server shutdown
  sleep 1
fi

for dir in "${OBJ_DIRS[@]}"; do
  rm -rf -- "$dir"
done

echo "Removed ${#OBJ_DIRS[@]} obj directories under $ROOT"
