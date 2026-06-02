# Bootstrap script for GFXR Capture Tool
# Installs .NET 8 SDK if missing, restores NuGet packages, builds, and launches.
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step([string]$msg) { Write-Host "`n>> $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "   OK: $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "   !! $msg" -ForegroundColor Yellow }

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
        } catch { }  # broken or wrong-version dotnet - skip to next candidate
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

    # Add to PATH for this session
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
