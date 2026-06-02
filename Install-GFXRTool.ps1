#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads and installs the latest GFXRTool release from GitHub.

.DESCRIPTION
    Fetches the latest release from the GitHub repository, downloads the zip,
    and extracts it over the current installation directory.
    The Layers folder (your GFXR proxy DLLs) is preserved across updates.

.PARAMETER Repo
    GitHub repository in "owner/repo" format.
    Default: update this to match your actual repo, e.g. "yourname/GFXRTool"

.PARAMETER InstallDir
    Where to extract the tool.
    Default: the directory this script lives in.

.EXAMPLE
    # Run from anywhere — installs next to the script
    pwsh -ExecutionPolicy Bypass -File Install-GFXRTool.ps1

.EXAMPLE
    # Point at a different install location
    pwsh -ExecutionPolicy Bypass -File Install-GFXRTool.ps1 -InstallDir "D:\Tools\GFXRTool"
#>
param(
    [string] $Repo       = "rpanttaja/GFXR-GUI-Tool",
    [string] $InstallDir = $PSScriptRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Helpers ──────────────────────────────────────────────────────────────────

function Write-Step([string]$msg) { Write-Host "  $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "  $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  WARNING: $msg" -ForegroundColor Yellow }

# ── Fetch latest release metadata ────────────────────────────────────────────

Write-Host ""
Write-Host "GFXRTool Updater" -ForegroundColor White
Write-Host "  Repo:       $Repo"
Write-Host "  InstallDir: $InstallDir"
Write-Host ""

Write-Step "Fetching latest release from GitHub..."

$apiUrl  = "https://api.github.com/repos/$Repo/releases/latest"
$headers = @{ "User-Agent" = "GFXRTool-Updater" }

try {
    $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers
} catch {
    Write-Host ""
    Write-Host "ERROR: Could not fetch release info." -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Check that the repo '$Repo' is correct and has at least one release." -ForegroundColor Yellow
    exit 1
}

$tag     = $release.tag_name
$zipAsset = $release.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1

if (-not $zipAsset) {
    Write-Host "ERROR: No zip asset found in release '$tag'." -ForegroundColor Red
    exit 1
}

Write-Ok "Latest release: $tag  ($($zipAsset.name))"

# ── Check if already up to date ──────────────────────────────────────────────

$versionFile = Join-Path $InstallDir "version.txt"
if (Test-Path $versionFile) {
    $current = (Get-Content $versionFile -Raw).Trim()
    if ($current -eq $tag) {
        Write-Ok "Already up to date ($tag). Nothing to do."
        Write-Host ""
        exit 0
    }
    Write-Step "Updating $current -> $tag"
} else {
    Write-Step "No version file found — performing fresh install."
}

# ── Download ──────────────────────────────────────────────────────────────────

$tempZip = Join-Path $env:TEMP "GFXRTool-$tag.zip"
$tempDir = Join-Path $env:TEMP "GFXRTool-extract-$tag"

Write-Step "Downloading $($zipAsset.browser_download_url)..."
Invoke-WebRequest -Uri $zipAsset.browser_download_url -OutFile $tempZip -UseBasicParsing
Write-Ok "Download complete."

# ── Extract ───────────────────────────────────────────────────────────────────

Write-Step "Extracting..."
if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
Expand-Archive -Path $tempZip -DestinationPath $tempDir

# The zip may contain a single top-level folder — find the actual content root.
$extractedItems = Get-ChildItem $tempDir
$contentRoot = if ($extractedItems.Count -eq 1 -and $extractedItems[0].PSIsContainer) {
    $extractedItems[0].FullName
} else {
    $tempDir
}

# ── Preserve the Layers folder ────────────────────────────────────────────────
# The Layers folder holds your GFXR proxy DLLs which are NOT part of the tool
# binary — they live alongside it and must survive an update.

$layersBackup = $null
$layersDst    = Join-Path $InstallDir "Layers"

if (Test-Path $layersDst) {
    $layersBackup = Join-Path $env:TEMP "GFXRTool-Layers-backup"
    Write-Step "Preserving Layers folder..."
    if (Test-Path $layersBackup) { Remove-Item $layersBackup -Recurse -Force }
    Copy-Item $layersDst $layersBackup -Recurse
}

# ── Install ───────────────────────────────────────────────────────────────────

Write-Step "Installing to $InstallDir..."
New-Item -ItemType Directory -Force $InstallDir | Out-Null

# Copy new files over, skipping the Layers folder from the zip
# (the repo ships example/empty Layers; we don't want to wipe real DLLs)
Get-ChildItem $contentRoot | Where-Object { $_.Name -ne "Layers" } | ForEach-Object {
    $dst = Join-Path $InstallDir $_.Name
    if ($_.PSIsContainer) {
        Copy-Item $_.FullName $dst -Recurse -Force
    } else {
        Copy-Item $_.FullName $dst -Force
    }
}

# Restore preserved Layers
if ($layersBackup) {
    Write-Step "Restoring Layers folder..."
    Copy-Item $layersBackup $layersDst -Recurse -Force
}

# ── Write version stamp ───────────────────────────────────────────────────────

$tag | Set-Content (Join-Path $InstallDir "version.txt")

# ── Cleanup ───────────────────────────────────────────────────────────────────

Remove-Item $tempZip  -Force -ErrorAction SilentlyContinue
Remove-Item $tempDir  -Recurse -Force -ErrorAction SilentlyContinue
if ($layersBackup) { Remove-Item $layersBackup -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host ""
Write-Ok "GFXRTool $tag installed to: $InstallDir"
Write-Host ""
