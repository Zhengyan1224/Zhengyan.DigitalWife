param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [switch]$ListOnly,
    [switch]$SkipBuildServerShutdown
)

$ErrorActionPreference = "Stop"

$buildDirectoryNames = @("obj", "bin")
$projectExtensions = @(".csproj", ".fsproj", ".vbproj")

function Test-PathUnderRoot {
    param(
        [string]$Path,
        [string]$RootPath
    )

    $trimChars = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $rootFullPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd($trimChars)
    $pathFullPath = [System.IO.Path]::GetFullPath($Path)
    $rootWithSeparator = $rootFullPath + [System.IO.Path]::DirectorySeparatorChar

    return $pathFullPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-ProjectFile {
    param([System.IO.FileInfo]$File)

    return $projectExtensions -contains $File.Extension
}

function Test-ProjectDirectory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return $false
    }

    foreach ($file in Get-ChildItem -LiteralPath $Path -File -Force) {
        if (Test-ProjectFile -File $file) {
            return $true
        }
    }

    return $false
}

function Test-SafeBuildDirectory {
    param(
        [string]$Path,
        [string]$RootPath
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return $false
    }

    $item = Get-Item -LiteralPath $Path -Force
    $fullPath = $item.FullName
    if (-not (Test-PathUnderRoot -Path $fullPath -RootPath $RootPath)) {
        return $false
    }

    $leaf = Split-Path -Leaf $fullPath
    if ($buildDirectoryNames -notcontains $leaf) {
        return $false
    }

    $parent = Split-Path -Parent $fullPath
    return Test-ProjectDirectory -Path $parent
}

function Test-IgnoredProjectPath {
    param(
        [string]$Path,
        [string]$RootPath
    )

    $trimChars = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $rootFullPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd($trimChars)
    $pathFullPath = [System.IO.Path]::GetFullPath($Path)
    $rootWithSeparator = $rootFullPath + [System.IO.Path]::DirectorySeparatorChar

    if (-not $pathFullPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $relativePath = $pathFullPath.Substring($rootWithSeparator.Length)
    $segments = $relativePath.Split(
        [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar),
        [System.StringSplitOptions]::RemoveEmptyEntries)

    foreach ($segment in $segments) {
        if ($segment -in @(".git", ".build-trash", ".obj-trash", "obj", "bin")) {
            return $true
        }
    }

    return $false
}

function Get-ProjectBuildDirectories {
    param([string]$RootPath)

    $directories = [System.Collections.Generic.List[System.IO.DirectoryInfo]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    $projectFiles = Get-ChildItem -LiteralPath $RootPath -Recurse -Force -File |
        Where-Object { (Test-ProjectFile -File $_) -and -not (Test-IgnoredProjectPath -Path $_.FullName -RootPath $RootPath) }

    foreach ($projectFile in $projectFiles) {
        $projectDirectory = $projectFile.DirectoryName
        foreach ($name in $buildDirectoryNames) {
            $candidate = Join-Path $projectDirectory $name
            if (-not (Test-SafeBuildDirectory -Path $candidate -RootPath $RootPath)) {
                continue
            }

            $directory = Get-Item -LiteralPath $candidate -Force
            if ($seen.Add($directory.FullName)) {
                $directories.Add($directory)
            }
        }
    }

    return $directories | Sort-Object FullName
}

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
            return "removed"
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
        $projectLeaf = Split-Path -Leaf (Split-Path -Parent $Path)
        $buildLeaf = Split-Path -Leaf $Path
        $trashPath = Join-Path $TrashRoot "$projectLeaf-$buildLeaf-$([Guid]::NewGuid().ToString('N'))"
        Move-Item -LiteralPath $Path -Destination $trashPath -Force
        return "moved"
    }

    throw "Failed to remove '$Path' after $MaxRetries attempts. $($lastError.Exception.Message)"
}

$resolvedRoot = (Resolve-Path $Root).Path
$solutionPath = Join-Path $resolvedRoot "Zhengyan.DigitalWife.sln"
$solutionxPath = Join-Path $resolvedRoot "Zhengyan.DigitalWife.slnx"

if (-not (Test-Path -LiteralPath $solutionPath) -and -not (Test-Path -LiteralPath $solutionxPath)) {
    throw "Repository root not recognized: $resolvedRoot"
}

$buildDirectories = @(Get-ProjectBuildDirectories -RootPath $resolvedRoot)

if ($buildDirectories.Count -eq 0) {
    Write-Host "No project bin/obj directories found under $resolvedRoot"
    exit 0
}

foreach ($directory in $buildDirectories) {
    Write-Host $directory.FullName
}

if ($ListOnly) {
    Write-Host "Listed $($buildDirectories.Count) project bin/obj directories."
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
$trashRoot = Join-Path $resolvedRoot ".build-trash"

foreach ($directory in $buildDirectories) {
    try {
        if (-not (Test-SafeBuildDirectory -Path $directory.FullName -RootPath $resolvedRoot)) {
            throw "Refusing to remove non-project build directory."
        }

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

Write-Host "Removed $removedCount project bin/obj directories under $resolvedRoot"
if ($movedCount -gt 0) {
    Write-Host "Moved $movedCount locked or inconsistent project bin/obj directories to $trashRoot"
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Failed directories:"
    foreach ($failure in $failures) {
        Write-Host $failure
    }

    exit 1
}
