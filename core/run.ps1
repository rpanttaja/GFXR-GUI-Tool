# Bootstrap script for GFXR Capture Tool
# Auto-updates source from GitHub, installs .NET 8 SDK if missing, builds, and launches.
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir   # one level up from core/

function Write-Step([string]$msg) { Write-Host "`n>> $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "   OK: $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "   !! $msg" -ForegroundColor Yellow }

# ── 0. Auto-update from GitHub ────────────────────────────────────────────────

$Repo           = "rpanttaja/GFXR-GUI-Tool"
$VersionFile    = Join-Path $RepoRoot "version.txt"
$CurrentVersion = if (Test-Path $VersionFile) { (Get-Content $VersionFile -Raw).Trim() } else { "" }

Write-Step "Checking for updates (current: $(if ($CurrentVersion) { $CurrentVersion } else { 'none' }))..."

try {
    $headers  = @{ "User-Agent" = "GFXRTool-Bootstrap" }
    $release  = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers
    $latest   = $release.tag_name

    if ($latest -and $latest -ne $CurrentVersion) {
        Write-Warn "New version available: $latest — downloading..."

        # Download the release source zip
        $sourceZip = Join-Path $env:TEMP "GFXR-source-$latest.zip"
        $sourceDir = Join-Path $env:TEMP "GFXR-source-$latest"

        $zipUrl = "https://github.com/$Repo/archive/refs/heads/main.zip"
        Invoke-WebRequest -Uri $zipUrl -OutFile $sourceZip -UseBasicParsing

        if (Test-Path $sourceDir) { Remove-Item $sourceDir -Recurse -Force }
        Expand-Archive -Path $sourceZip -DestinationPath $sourceDir

        # GitHub archive has one top-level folder — unwrap it
        $inner = Get-ChildItem $sourceDir | Where-Object { $_.PSIsContainer } | Select-Object -First 1
        $src   = if ($inner) { $inner.FullName } else { $sourceDir }

        # Preserve Layers
        $layersSrc = Join-Path $RepoRoot "core\Layers"
        $layersBak = Join-Path $env:TEMP "GFXR-Layers-bak"
        if (Test-Path $layersSrc) {
            if (Test-Path $layersBak) { Remove-Item $layersBak -Recurse -Force }
            Copy-Item $layersSrc $layersBak -Recurse -Force
        }

        # Copy new source over repo root (skip Layers subfolder from zip)
        Get-ChildItem $src | ForEach-Object {
            $target = Join-Path $RepoRoot $_.Name
            if ($_.PSIsContainer) {
                if ($_.Name -eq "core") {
                    # Copy core but skip Layers inside it
                    Get-ChildItem $_.FullName | Where-Object { $_.Name -ne "Layers" } | ForEach-Object {
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

        # Restore Layers
        if (Test-Path $layersBak) {
            Copy-Item $layersBak $layersSrc -Recurse -Force
            Remove-Item $layersBak -Recurse -Force -ErrorAction SilentlyContinue
        }

        # Stamp version
        $latest | Set-Content $VersionFile

        # Cleanup
        Remove-Item $sourceZip -Force -ErrorAction SilentlyContinue
        Remove-Item $sourceDir -Recurse -Force -ErrorAction SilentlyContinue

        Write-OK "Updated to $latest."
    } else {
        Write-OK "Already up to date ($latest)."
    }
} catch {
    Write-Warn "Could not check for updates (offline?): $($_.Exception.Message)"
    Write-Warn "Continuing with current source..."
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
