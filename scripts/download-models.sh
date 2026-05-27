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
MODELS_ROOT="$REPO_ROOT/models"
DOWNLOADS_ROOT="$REPO_ROOT/artifacts/downloads"
INSTALLER_PROJECT="$REPO_ROOT/tools/Zhengyan.DigitalWife.Tools.ModelInstaller/Zhengyan.DigitalWife.Tools.ModelInstaller.csproj"

if [[ ! -f "$INSTALLER_PROJECT" ]]; then
  echo "Model installer project not found: $INSTALLER_PROJECT" >&2
  echo "Resolved script path: $SCRIPT_PATH" >&2
  echo "Resolved repository root: $REPO_ROOT" >&2
  exit 1
fi

mkdir -p "$MODELS_ROOT" "$DOWNLOADS_ROOT"

run_installer() {
  dotnet run --project "$INSTALLER_PROJECT" -- "$@"
}

run_installer \
  download-and-extract-tarbz2 \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30.tar.bz2" \
  "$MODELS_ROOT/asr" \
  "$DOWNLOADS_ROOT"

run_installer \
  download-and-extract-tarbz2 \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/kws-models/sherpa-onnx-kws-zipformer-wenetspeech-3.3M-2024-01-01.tar.bz2" \
  "$MODELS_ROOT/wake" \
  "$DOWNLOADS_ROOT"

run_installer \
  download-and-extract-tarbz2 \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/matcha-icefall-zh-en.tar.bz2" \
  "$MODELS_ROOT/tts" \
  "$DOWNLOADS_ROOT"

run_installer \
  download-and-extract-tarbz2 \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-zh-hf-fanchen-C.tar.bz2" \
  "$MODELS_ROOT/tts" \
  "$DOWNLOADS_ROOT"

mkdir -p "$MODELS_ROOT/tts/matcha-icefall-zh-en"
run_installer \
  download-file \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/vocoder-models/vocos-16khz-univ.onnx" \
  "$MODELS_ROOT/tts/matcha-icefall-zh-en/vocos-16khz-univ.onnx"

mkdir -p "$MODELS_ROOT/whisper"
run_installer \
  download-file \
  "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin?download=true" \
  "$MODELS_ROOT/whisper/ggml-base.bin"

echo "Model download complete."
