# Bootstrap script for GFXR Capture Tool
# Auto-updates source from GitHub, installs .NET 8 SDK if missing, builds, and launches.
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir   # one level up from core/

function Write-Step([string]$msg) { Write-Host "`n>> $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "   OK: $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "   !! $msg" -ForegroundColor Yellow }

# ── 0. Auto-update from GitHub ────────────────────────────────────────────────

# ── 0. Auto-update from GitHub ────────────────────────────────────────────────
# Always pull the latest main branch source before building.
# Uses a simple ETag-based check so it skips the download when nothing changed.

$Repo        = "rpanttaja/GFXR-GUI-Tool"
$SourceZipUrl = "https://github.com/$Repo/archive/refs/heads/main.zip"
$ETagFile    = Join-Path $RepoRoot ".github_etag"

Write-Step "Checking for updates..."

try {
    $etagHeader = @{ "User-Agent" = "GFXRTool-Bootstrap" }
    if (Test-Path $ETagFile) {
        $etagHeader["If-None-Match"] = (Get-Content $ETagFile -Raw).Trim()
    }

    $response = Invoke-WebRequest -Uri $SourceZipUrl -Headers $etagHeader `
                                  -UseBasicParsing -ErrorAction Stop

    if ($response.StatusCode -eq 200) {
        # Save ETag for next run
        $etag = $response.Headers["ETag"]
        if ($etag) { $etag | Set-Content $ETagFile }

        $sourceZip = Join-Path $env:TEMP "GFXR-source-latest.zip"
        [System.IO.File]::WriteAllBytes($sourceZip, $response.Content)

        $sourceDir = Join-Path $env:TEMP "GFXR-source-latest"
        if (Test-Path $sourceDir) { Remove-Item $sourceDir -Recurse -Force }
        Expand-Archive -Path $sourceZip -DestinationPath $sourceDir

        $inner = Get-ChildItem $sourceDir | Where-Object { $_.PSIsContainer } | Select-Object -First 1
        $src   = if ($inner) { $inner.FullName } else { $sourceDir }

        # Preserve Layers
        $layersSrc = Join-Path $RepoRoot "core\Layers"
        $layersBak = Join-Path $env:TEMP "GFXR-Layers-bak"
        if (Test-Path $layersSrc) {
            if (Test-Path $layersBak) { Remove-Item $layersBak -Recurse -Force }
            Copy-Item $layersSrc $layersBak -Recurse -Force
        }

        # Copy new source, skip Layers and build output from zip
        $skipDirs = @("Layers", "bin", "obj")
        Get-ChildItem $src | ForEach-Object {
            $target = Join-Path $RepoRoot $_.Name
            if ($_.PSIsContainer) {
                if ($_.Name -eq "core") {
                    Get-ChildItem $_.FullName | Where-Object { $skipDirs -notcontains $_.Name } | ForEach-Object {
                        $t = Join-Path $target $_.Name
                        if ($_.PSIsContainer) { Copy-Item $_.FullName $t -Recurse -Force }
                        else { Copy-Item $_.FullName $t -Force }
                    }
                } else {
                    Copy-Item $_.FullName $target -Recurse -Force
                }
            } else {
                Copy-Item $_.FullName $target -Force
            }
        }

        # Clean stale build output so compiler doesn't see duplicate generated files
        foreach ($proj in @("GFXRTool", "GFXRWatcher")) {
            foreach ($dir in @("bin", "obj")) {
                $p = Join-Path $RepoRoot "core\$proj\$dir"
                if (Test-Path $p) { Remove-Item $p -Recurse -Force }
            }
        }

        if (Test-Path $layersBak) {
            Copy-Item $layersBak $layersSrc -Recurse -Force
            Remove-Item $layersBak -Recurse -Force -ErrorAction SilentlyContinue
        }

        Remove-Item $sourceZip -Force -ErrorAction SilentlyContinue
        Remove-Item $sourceDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-OK "Source updated."
    } elseif ($response.StatusCode -eq 304) {
        Write-OK "Already up to date (no changes on GitHub)."
    }
} catch {
    Write-Warn "Could not check for updates (offline?). Continuing with current source..."
}

# ── 1. Find or install .NET 8 SDK ────────────────────────────────────────────

$requiredMajor  = 8
$localDotnetDir = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
$localDotnetExe = Join-Path $localDotnetDir   "dotnet.exe"

function Get-DotnetExe {
    $candidates = @(
        (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        "$env:ProgramFiles\dotnet\dotnet.exe",
        "${env:ProgramFiles(x86)}\dotnet\dotnet.exe",
        $localDotnetExe
    )
    foreach ($c in $candidates) {
        if (-not $c -or -not (Test-Path $c -ErrorAction SilentlyContinue)) { continue }
        try {
            $rawVer = (& $c --version 2>$null) -replace "-.*", ""
            if ([version]$rawVer -ge [version]"$requiredMajor.0") { return $c }
        } catch { }
    }
    return $null
}

Write-Step "Checking for .NET $requiredMajor SDK..."
$dotnet = Get-DotnetExe

if (-not $dotnet) {
    Write-Warn ".NET $requiredMajor SDK not found. Downloading Microsoft install script..."

    $installPs1 = Join-Path $env:TEMP "dotnet-install.ps1"
    try {
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" `
                          -OutFile $installPs1 -UseBasicParsing
    } catch {
        Write-Error "Could not download dotnet-install.ps1. Check your internet connection.`n$_"
        exit 1
    }

    Write-Warn "Installing .NET $requiredMajor SDK to $localDotnetDir ..."
    & $installPs1 -Channel "$requiredMajor.0" -InstallDir $localDotnetDir

    $env:PATH = "$localDotnetDir;$env:PATH"
    $dotnet = $localDotnetExe
    Write-OK ".NET $requiredMajor SDK installed at $localDotnetDir"
} else {
    $ver = & $dotnet --version 2>$null
    Write-OK "Found .NET $ver at $dotnet"
}

# ── 2. Restore NuGet packages ─────────────────────────────────────────────────

$csproj = Join-Path $ScriptDir "GFXRTool\GFXRTool.csproj"
if (-not (Test-Path $csproj)) {
    Write-Error "Project file not found: $csproj"
    exit 1
}

Write-Step "Restoring NuGet packages..."
& $dotnet restore $csproj --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "NuGet restore failed."; exit 1 }
Write-OK "Packages restored."

# ── 3. Build ──────────────────────────────────────────────────────────────────

Write-Step "Building (Release)..."
& $dotnet build $csproj -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }
Write-OK "Build succeeded."

# ── 4. Launch ─────────────────────────────────────────────────────────────────

$exe = Get-ChildItem -Path (Join-Path $ScriptDir "GFXRTool\bin\Release") `
    -Filter "GFXRTool.exe" -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $exe) {
    Write-Error "Executable not found under GFXRTool\bin\Release - build may have failed."
    exit 1
}

Write-Step "Launching GFXR Capture Tool..."
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
Write-OK "Launched successfully."
