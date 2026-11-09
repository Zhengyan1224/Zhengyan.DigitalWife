param(
    [ValidateRange(1, 65535)]
    [int] $Port = 5052,
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $repositoryDirectory 'src\Zhengyan.DigitalWife.GamePlayer.Web\Zhengyan.DigitalWife.GamePlayer.Web.csproj'
$publishDirectory = Join-Path $repositoryDirectory 'artifacts\web-player'
$webDirectory = Join-Path $publishDirectory 'wwwroot'

if (-not $SkipPublish) {
    & dotnet publish $projectFile -c Release -o $publishDirectory -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Web AOT publication failed.' }
}

if (-not (Test-Path -LiteralPath (Join-Path $webDirectory 'index.html'))) {
    throw 'The published Web Player is missing. Run this script without -SkipPublish first.'
}
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    throw 'Install Python 3 to use this local preview server, or serve artifacts\web-player\wwwroot with your static web server.'
}

Write-Host "Web Player: http://127.0.0.1:$Port/"
Write-Host 'Press Ctrl+C to stop the local server.'
& python (Join-Path $PSScriptRoot 'web-preview-server.py') --port $Port --bind 127.0.0.1 --directory $webDirectory
if ($LASTEXITCODE -ne 0) { throw 'The Web preview server stopped with an error.' }
