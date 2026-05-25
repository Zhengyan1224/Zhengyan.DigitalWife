param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [switch]$SkipAsr,
    [switch]$SkipWake,
    [switch]$SkipTts,
    [switch]$SkipWhisper
)

$ErrorActionPreference = "Stop"

$modelsRoot = Join-Path $Root "models"
$downloadsRoot = Join-Path $Root "artifacts/downloads"

function Invoke-ModelInstaller {
    param(
        [string[]]$InstallerArgs
    )

    $env:DOTNET_CLI_HOME = Join-Path $Root ".codex-dotnet-home"
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

    & dotnet run --project (Join-Path $Root "tools/Zhengyan.DigitalWife.Tools.ModelInstaller/Zhengyan.DigitalWife.Tools.ModelInstaller.csproj") -- @InstallerArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Zhengyan.DigitalWife.Tools.ModelInstaller failed with exit code $LASTEXITCODE"
    }
}

New-Item -ItemType Directory -Force -Path $modelsRoot, $downloadsRoot | Out-Null

if (-not $SkipAsr) {
    Invoke-ModelInstaller @(
        "download-and-extract-tarbz2",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30.tar.bz2",
        (Join-Path $modelsRoot "asr"),
        $downloadsRoot
    )
}

if (-not $SkipWake) {
    Invoke-ModelInstaller @(
        "download-and-extract-tarbz2",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/kws-models/sherpa-onnx-kws-zipformer-wenetspeech-3.3M-2024-01-01.tar.bz2",
        (Join-Path $modelsRoot "wake"),
        $downloadsRoot
    )
}

if (-not $SkipTts) {
    Invoke-ModelInstaller @(
        "download-and-extract-tarbz2",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/matcha-icefall-zh-en.tar.bz2",
        (Join-Path $modelsRoot "tts"),
        $downloadsRoot
    )

    Invoke-ModelInstaller @(
        "download-and-extract-tarbz2",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-zh-hf-fanchen-C.tar.bz2",
        (Join-Path $modelsRoot "tts"),
        $downloadsRoot
    )

    $matchaDir = Join-Path $modelsRoot "tts\matcha-icefall-zh-en"
    New-Item -ItemType Directory -Force -Path $matchaDir | Out-Null
    Invoke-ModelInstaller @(
        "download-file",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/vocoder-models/vocos-16khz-univ.onnx",
        (Join-Path $matchaDir "vocos-16khz-univ.onnx")
    )
}

if (-not $SkipWhisper) {
    $whisperDir = Join-Path $modelsRoot "whisper"
    New-Item -ItemType Directory -Force -Path $whisperDir | Out-Null
    Invoke-ModelInstaller @(
        "download-file",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin?download=true",
        (Join-Path $whisperDir "ggml-base.bin")
    )
}

Write-Host "Model download complete."
