param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [switch]$ListOnly,
    [switch]$SkipBuildServerShutdown
)

$ErrorActionPreference = "Stop"

function Clear-ReadOnlyAttributes {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $readOnly = [System.IO.FileAttributes]::ReadOnly
    Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if (($_.Attributes -band $readOnly) -ne 0) {
            $_.Attributes = $_.Attributes -band (-bnot $readOnly)
        }
    }

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -ne $item -and ($item.Attributes -band $readOnly) -ne 0) {
        $item.Attributes = $item.Attributes -band (-bnot $readOnly)
    }
}

function Remove-DirectoryRobust {
    param(
        [string]$Path,
        [int]$MaxRetries = 5,
        [string]$TrashRoot = ""
    )

    $lastError = $null

    for ($attempt = 1; $attempt -le $MaxRetries; $attempt++) {
        if (-not (Test-Path -LiteralPath $Path)) {
            return
        }

        try {
            Clear-ReadOnlyAttributes -Path $Path
            [System.IO.Directory]::Delete($Path, $true)
        }
        catch {
            $lastError = $_
            Start-Sleep -Milliseconds (200 * $attempt)
        }
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        return "removed"
    }

    if (-not [string]::IsNullOrWhiteSpace($TrashRoot)) {
        New-Item -ItemType Directory -Force -Path $TrashRoot | Out-Null
        $leaf = Split-Path -Leaf (Split-Path -Parent $Path)
        $trashPath = Join-Path $TrashRoot "$leaf-obj-$([Guid]::NewGuid().ToString('N'))"
        Move-Item -LiteralPath $Path -Destination $trashPath -Force
        return "moved"
    }

    throw "Failed to remove '$Path' after $MaxRetries attempts. $($lastError.Exception.Message)"
}

$resolvedRoot = (Resolve-Path $Root).Path
$solutionPath = Join-Path $resolvedRoot "Zhengyan.DigitalWife.sln"

if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw "Repository root not recognized: $resolvedRoot"
}

$objDirectories = Get-ChildItem -LiteralPath $resolvedRoot -Directory -Recurse -Force -Filter obj |
    Sort-Object FullName

if ($objDirectories.Count -eq 0) {
    Write-Host "No obj directories found under $resolvedRoot"
    exit 0
}

foreach ($directory in $objDirectories) {
    Write-Host $directory.FullName
}

if ($ListOnly) {
    Write-Host "Listed $($objDirectories.Count) obj directories."
    exit 0
}

if (-not $SkipBuildServerShutdown) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnet) {
        Write-Host "Shutting down dotnet build servers..."
        & $dotnet.Source build-server shutdown | Out-Host
        Start-Sleep -Milliseconds 500
    }
}

$removedCount = 0
$movedCount = 0
$failures = [System.Collections.Generic.List[string]]::new()
$trashRoot = Join-Path $resolvedRoot ".obj-trash"

foreach ($directory in $objDirectories) {
    try {
        $result = Remove-DirectoryRobust -Path $directory.FullName -TrashRoot $trashRoot
        if ($result -eq "moved") {
            $movedCount++
        }
        else {
            $removedCount++
        }
    }
    catch {
        $failures.Add("$($directory.FullName) :: $($_.Exception.Message)")
    }
}

Write-Host "Removed $removedCount obj directories under $resolvedRoot"
if ($movedCount -gt 0) {
    Write-Host "Moved $movedCount locked or inconsistent obj directories to $trashRoot"
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Failed directories:"
    foreach ($failure in $failures) {
        Write-Host $failure
    }

    exit 1
}
