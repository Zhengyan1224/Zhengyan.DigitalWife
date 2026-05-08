#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODELS_ROOT="$ROOT/models"
DOWNLOADS_ROOT="$ROOT/artifacts/downloads"

mkdir -p "$MODELS_ROOT" "$DOWNLOADS_ROOT"

DOTNET_CLI_HOME="$ROOT/.codex-dotnet-home"
export DOTNET_CLI_HOME DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$DOTNET_CLI_HOME"

dotnet run --project "$ROOT/tools/Zhengyan.DigitalWife.Tools.ModelInstaller/Zhengyan.DigitalWife.Tools.ModelInstaller.csproj" -- \
  download-and-extract-tarbz2 \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30.tar.bz2" \
  "$MODELS_ROOT/asr" \
  "$DOWNLOADS_ROOT"

dotnet run --project "$ROOT/tools/Zhengyan.DigitalWife.Tools.ModelInstaller/Zhengyan.DigitalWife.Tools.ModelInstaller.csproj" -- \
  download-and-extract-tarbz2 \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/kws-models/sherpa-onnx-kws-zipformer-wenetspeech-3.3M-2024-01-01.tar.bz2" \
  "$MODELS_ROOT/wake" \
  "$DOWNLOADS_ROOT"

dotnet run --project "$ROOT/tools/Zhengyan.DigitalWife.Tools.ModelInstaller/Zhengyan.DigitalWife.Tools.ModelInstaller.csproj" -- \
  download-and-extract-tarbz2 \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/matcha-icefall-zh-en.tar.bz2" \
  "$MODELS_ROOT/tts" \
  "$DOWNLOADS_ROOT"

mkdir -p "$MODELS_ROOT/tts/matcha-icefall-zh-en"
dotnet run --project "$ROOT/tools/Zhengyan.DigitalWife.Tools.ModelInstaller/Zhengyan.DigitalWife.Tools.ModelInstaller.csproj" -- \
  download-file \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/vocoder-models/vocos-16khz-univ.onnx" \
  "$MODELS_ROOT/tts/matcha-icefall-zh-en/vocos-16khz-univ.onnx"

mkdir -p "$MODELS_ROOT/whisper"
dotnet run --project "$ROOT/tools/Zhengyan.DigitalWife.Tools.ModelInstaller/Zhengyan.DigitalWife.Tools.ModelInstaller.csproj" -- \
  download-file \
  "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin?download=true" \
  "$MODELS_ROOT/whisper/ggml-base.bin"

echo "Model download complete."

