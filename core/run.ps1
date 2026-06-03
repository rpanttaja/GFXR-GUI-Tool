# Bootstrap script for GFXR Capture Tool
# Downloads the latest pre-built release from GitHub and launches it.
# No local compilation — avoids all build-related errors.
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir

function Write-Step([string]$msg) { Write-Host "`n>> $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "   OK: $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "   !! $msg" -ForegroundColor Yellow }

$Repo        = "rpanttaja/GFXR-GUI-Tool"
$ApiUrl      = "https://api.github.com/repos/$Repo/releases/latest"
$InstallDir  = Join-Path $RepoRoot "app"
$VersionFile = Join-Path $InstallDir "version.txt"
$ExePath     = Join-Path $InstallDir "GFXRTool.exe"

# ── 1. Check for updates ──────────────────────────────────────────────────────

Write-Step "Checking for latest release..."

$release  = $null
$latest   = $null
$zipAsset = $null

try {
    $headers = @{ "User-Agent" = "GFXRTool-Bootstrap" }
    $release  = Invoke-RestMethod -Uri $ApiUrl -Headers $headers -ErrorAction Stop
    $latest   = $release.tag_name
    $zipAsset = $release.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1
    Write-OK "Latest release: $latest"
} catch {
    Write-Warn "Could not reach GitHub ($($_.Exception.Message))."
}

$current = if (Test-Path $VersionFile) { (Get-Content $VersionFile -Raw).Trim() } else { "" }

$needsUpdate = $zipAsset -and ($current -ne $latest -or -not (Test-Path $ExePath))

# ── 2. Download and install if needed ─────────────────────────────────────────

if ($needsUpdate) {
    Write-Step "$(if ($current) { "Updating $current -> $latest" } else { "Installing $latest" })..."

    $tempZip = Join-Path $env:TEMP "GFXRTool-$latest.zip"
    $tempDir = Join-Path $env:TEMP "GFXRTool-extract-$latest"

    try {
        Write-OK "Downloading..."
        Invoke-WebRequest -Uri $zipAsset.browser_download_url `
                          -OutFile $tempZip -UseBasicParsing

        if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
        Expand-Archive -Path $tempZip -DestinationPath $tempDir

        # Unwrap single top-level folder if present
        $items = Get-ChildItem $tempDir
        $src   = if ($items.Count -eq 1 -and $items[0].PSIsContainer) { $items[0].FullName } else { $tempDir }

        # Preserve Layers across updates
        $layersDst = Join-Path $InstallDir "Layers"
        $layersBak = Join-Path $env:TEMP "GFXRTool-Layers-bak"
        if (Test-Path $layersDst) {
            if (Test-Path $layersBak) { Remove-Item $layersBak -Recurse -Force }
            Copy-Item $layersDst $layersBak -Recurse -Force
        }

        # Install — replace everything
        if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
        New-Item -ItemType Directory -Force $InstallDir | Out-Null
        Copy-Item "$src\*" $InstallDir -Recurse -Force

        # Restore Layers
        if (Test-Path $layersBak) {
            Copy-Item $layersBak $layersDst -Recurse -Force
            Remove-Item $layersBak -Recurse -Force -ErrorAction SilentlyContinue
        }

        $latest | Set-Content $VersionFile
        Write-OK "Installed to: $InstallDir"
    } catch {
        Write-Warn "Update failed: $($_.Exception.Message)"
        Write-Warn "Will try to launch existing version if available."
    } finally {
        Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
} elseif (-not $needsUpdate -and $latest) {
    Write-OK "Already up to date ($latest)."
} else {
    Write-Warn "Skipping update check (offline or no release found)."
}

# ── 3. Launch ─────────────────────────────────────────────────────────────────

Write-Step "Launching GFXR Capture Tool..."

if (-not (Test-Path $ExePath)) {
    Write-Host ""
    Write-Host "ERROR: GFXRTool.exe not found at: $ExePath" -ForegroundColor Red
    Write-Host "No release has been downloaded yet and no cached version exists." -ForegroundColor Yellow
    Write-Host "Check your internet connection and try again." -ForegroundColor Yellow
    exit 1
}

Start-Process -FilePath $ExePath -WorkingDirectory $InstallDir
Write-OK "Launched."
