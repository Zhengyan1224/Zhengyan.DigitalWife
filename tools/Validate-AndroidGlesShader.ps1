param(
    [Parameter(Mandatory = $true)][string]$Vertex,
    [Parameter(Mandatory = $true)][string]$Fragment
)

$ErrorActionPreference = 'Stop'
foreach ($path in @($Vertex, $Fragment)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Shader file not found: $path" }
    $source = Get-Content -LiteralPath $path -Raw
    if ($source -notmatch '(?m)^\s*#version\s+300\s+es\b') { throw "${path}: first directive must be #version 300 es" }
    if ($source -notmatch 'void\s+main\s*\(') { throw "${path}: void main() is missing" }
    if ($source -match 'layout\s*\(\s*binding\s*=') { throw "${path}: layout(binding=...) is not part of the Android contract" }
}
Write-Output "Android GLES shader contract passed: $Vertex / $Fragment"
