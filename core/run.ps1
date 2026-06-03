# Bootstrap script for GFXR Capture Tool
# Builds and launches the tool. No network calls, no auto-update.
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step([string]$msg) { Write-Host "`n>> $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "   OK: $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "   !! $msg" -ForegroundColor Yellow }

# ── 1. Find .NET SDK ──────────────────────────────────────────────────────────

$requiredMajor  = 8
$localDotnetDir = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
$localDotnetExe = Join-Path $localDotnetDir "dotnet.exe"

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
    Write-Warn ".NET $requiredMajor SDK not found. Downloading installer..."
    $installPs1 = Join-Path $env:TEMP "dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installPs1 -UseBasicParsing
    & $installPs1 -Channel "$requiredMajor.0" -InstallDir $localDotnetDir
    $env:PATH = "$localDotnetDir;$env:PATH"
    $dotnet = $localDotnetExe
    Write-OK ".NET $requiredMajor installed."
} else {
    Write-OK "Found .NET $(& $dotnet --version 2>$null) at $dotnet"
}

# ── 2. Clean bin/obj ──────────────────────────────────────────────────────────
# Wipe before every build so stale compiler-generated files never cause errors.

foreach ($proj in @("GFXRTool", "GFXRWatcher")) {
    foreach ($dir in @("bin", "obj")) {
        $p = Join-Path $ScriptDir "$proj\$dir"
        if (Test-Path $p) { Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# ── 3. Restore + Build ────────────────────────────────────────────────────────

$csproj = Join-Path $ScriptDir "GFXRTool\GFXRTool.csproj"
if (-not (Test-Path $csproj)) { Write-Error "Project not found: $csproj"; exit 1 }

Write-Step "Restoring NuGet packages..."
& $dotnet restore $csproj --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Restore failed."; exit 1 }
Write-OK "Packages restored."

Write-Step "Building (Release)..."
& $dotnet build $csproj -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }
Write-OK "Build succeeded."

# ── 4. Launch ─────────────────────────────────────────────────────────────────

$exe = Get-ChildItem -Path (Join-Path $ScriptDir "GFXRTool\bin\Release") `
    -Filter "GFXRTool.exe" -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $exe) { Write-Error "GFXRTool.exe not found — build may have failed."; exit 1 }

Write-Step "Launching GFXR Capture Tool..."
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
Write-OK "Launched."
