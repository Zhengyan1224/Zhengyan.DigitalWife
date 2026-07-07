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

run_find() {
  if [[ -x /usr/bin/find ]]; then
    /usr/bin/find "$@"
  else
    command find "$@"
  fi
}

run_sort() {
  if [[ -x /usr/bin/sort ]]; then
    /usr/bin/sort "$@"
  else
    command sort "$@"
  fi
}

has_project_file() {
  local dir="$1"
  local candidate

  for candidate in "$dir"/*.csproj "$dir"/*.fsproj "$dir"/*.vbproj; do
    [[ -f "$candidate" ]] && return 0
  done

  return 1
}

is_ignored_project_path() {
  local path="$1"

  case "$path" in
    "$ROOT"/.git/*|"$ROOT"/.build-trash/*|"$ROOT"/.obj-trash/*|*/obj/*|*/bin/*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

is_safe_build_dir() {
  local dir="$1"
  local real_dir
  local leaf
  local parent

  [[ -d "$dir" ]] || return 1
  real_dir="$(cd -P -- "$dir" && pwd)" || return 1

  case "$real_dir" in
    "$ROOT"/*)
      ;;
    *)
      return 1
      ;;
  esac

  leaf="$(basename -- "$real_dir")"
  [[ "$leaf" == "obj" || "$leaf" == "bin" ]] || return 1

  parent="$(cd -P -- "$(dirname -- "$real_dir")" && pwd)" || return 1
  has_project_file "$parent"
}

contains_build_dir() {
  local needle="$1"
  local item

  for item in "${BUILD_DIRS[@]}"; do
    [[ "$item" == "$needle" ]] && return 0
  done

  return 1
}

add_build_dir() {
  local dir="$1"
  local real_dir

  is_safe_build_dir "$dir" || return 0
  real_dir="$(cd -P -- "$dir" && pwd)"
  contains_build_dir "$real_dir" && return 0
  BUILD_DIRS+=("$real_dir")
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
      echo "Usage: clean-build.sh [--root <path>] [--list] [--skip-build-server-shutdown]" >&2
      exit 1
      ;;
  esac
done

ROOT="$(cd -P -- "$ROOT" && pwd)"

if [[ ! -f "$ROOT/Zhengyan.DigitalWife.sln" && ! -f "$ROOT/Zhengyan.DigitalWife.slnx" ]]; then
  echo "Repository root not recognized: $ROOT" >&2
  exit 1
fi

PROJECT_FILES=()
while IFS= read -r -d '' project_file; do
  is_ignored_project_path "$project_file" && continue
  PROJECT_FILES+=("$project_file")
done < <(run_find "$ROOT" -type f \( -name '*.csproj' -o -name '*.fsproj' -o -name '*.vbproj' \) -print0)

BUILD_DIRS=()
for project_file in "${PROJECT_FILES[@]}"; do
  project_dir="$(cd -P -- "$(dirname -- "$project_file")" && pwd)"
  add_build_dir "$project_dir/obj"
  add_build_dir "$project_dir/bin"
done

if [[ ${#BUILD_DIRS[@]} -gt 0 ]]; then
  SORTED_BUILD_DIRS=()
  while IFS= read -r build_dir; do
    SORTED_BUILD_DIRS+=("$build_dir")
  done < <(printf '%s\n' "${BUILD_DIRS[@]}" | run_sort)
  BUILD_DIRS=("${SORTED_BUILD_DIRS[@]}")
fi

if [[ ${#BUILD_DIRS[@]} -eq 0 ]]; then
  echo "No project bin/obj directories found under $ROOT"
  exit 0
fi

for dir in "${BUILD_DIRS[@]}"; do
  echo "$dir"
done

if [[ $LIST_ONLY -eq 1 ]]; then
  echo "Listed ${#BUILD_DIRS[@]} project bin/obj directories."
  exit 0
fi

if [[ $SKIP_BUILD_SERVER_SHUTDOWN -eq 0 ]] && command -v dotnet >/dev/null 2>&1; then
  echo "Shutting down dotnet build servers..."
  dotnet build-server shutdown
  sleep 1
fi

REMOVED_COUNT=0
FAILURES=()
for dir in "${BUILD_DIRS[@]}"; do
  if ! is_safe_build_dir "$dir"; then
    FAILURES+=("$dir :: refusing to remove non-project build directory")
    continue
  fi

  chmod -R u+w -- "$dir" 2>/dev/null || true
  if rm -rf -- "$dir"; then
    REMOVED_COUNT=$((REMOVED_COUNT + 1))
  else
    FAILURES+=("$dir :: failed to remove")
  fi
done

echo "Removed $REMOVED_COUNT project bin/obj directories under $ROOT"

if [[ ${#FAILURES[@]} -gt 0 ]]; then
  echo
  echo "Failed directories:"
  for failure in "${FAILURES[@]}"; do
    echo "$failure"
  done

  exit 1
fi
