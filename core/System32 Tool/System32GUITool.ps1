# --- Force Admin Elevation ---
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell "-ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}
# ------------------------------

# --- Ensure is writable ---
try {
    takeown /f C:\Windows\System32\ | Out-Null
    icacls C:\Windows\System32\ /grant Everyone:F | Out-Null
    Write-Host "Permissions updated."
} catch {
    Write-Host "Failed to update permissions"
}
# ------------------------------------------------------------

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$Form = New-Object System.Windows.Forms.Form
$Form.Text = "DirectX DLL Manager"
$Form.Size = New-Object System.Drawing.Size(420,380)
$Form.StartPosition = "CenterScreen"

$OutputBox = New-Object System.Windows.Forms.RichTextBox
$OutputBox.ReadOnly = $true
$OutputBox.BackColor = "Black"
$OutputBox.ForeColor = "White"
$OutputBox.Font = New-Object System.Drawing.Font("Consolas",10)
$OutputBox.Size = New-Object System.Drawing.Size(380,170)
$OutputBox.Location = New-Object System.Drawing.Point(10,170)
$OutputBox.HideSelection = $false
$Form.Controls.Add($OutputBox)

function Log($msg) {
    $OutputBox.AppendText("$msg`r`n")
}

function Color-Text($text, $color) {
    $OutputBox.SelectionStart = $OutputBox.TextLength
    $OutputBox.SelectionLength = 0
    $OutputBox.SelectionColor = $color
    $OutputBox.AppendText("$text`r`n")
    $OutputBox.SelectionColor = $OutputBox.ForeColor
}

# --- Require Admin ---
function Require-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)

    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This action requires administrator privileges."
    }
}

$DLLs   = @("d3d11","d3d12","dxgi")
$SysPath = "C:\Windows\System32"

# -------------------------------------------------------
# INSTALL FUNCTION
# -------------------------------------------------------
function Install-DLLs {
    Require-Admin

    Log "=== Installing Custom DirectX DLLs ==="

    foreach ($dll in $DLLs) {
        Log "`nProcessing $dll.dll..."

        $src    = ".\$dll.dll"
        $dst    = "$SysPath\$dll.dll"
        $backup = "$SysPath\${dll}_ms.dll"

        if (!(Test-Path $src)) {
            Color-Text "ERROR: Missing $src" 'Red'
            continue
        }

        if (!(Test-Path $backup)) {
            Log "Creating backup..."
            takeown /F $dst /A | Out-Null
            icacls $dst /grant administrators:F | Out-Null

            try {
                Rename-Item $dst $backup -ErrorAction Stop
                Log "Backup created."
            } catch {
                Color-Text "ERROR: Could not rename $dll.dll (file in use)" 'Red'
                continue
            }
        } else {
            Log "Backup already exists."
        }

        try {
            Copy-Item $src $dst -Force
            Color-Text "SUCCESS: Installed new $dll.dll" 'Green'
        } catch {
            Color-Text "ERROR: Failed to copy new $dll.dll" 'Red'
        }
    }

    # Install d3d12_capture.dll
    Log "`nInstalling d3d12_capture.dll..."
    $capSrc = ".\d3d12_capture.dll"
    $capDst = "$SysPath\d3d12_capture.dll"

    if (Test-Path $capSrc) {
            try {
                Copy-Item $capSrc $capDst -Force
                Color-Text "SUCCESS: Installed d3d12_capture.dll" 'Green'
            } catch {
                Color-Text "ERROR: Failed to install d3d12_capture.dll" 'Red'
            }
    } else {
        Color-Text "ERROR: Missing d3d12_capture.dll in script folder" 'Red'
    }
}

# -------------------------------------------------------
# RESTORE FUNCTION
# -------------------------------------------------------
function Restore-DLLs {
    Require-Admin

    Log "=== Restoring Original DirectX DLLs ==="

    foreach ($dll in $DLLs) {
        Log "`nRestoring $dll.dll..."

        $dst    = "$SysPath\$dll.dll"
        $backup = "$SysPath\${dll}_ms.dll"

        if (!(Test-Path $backup)) {
            Color-Text "ERROR: No backup found for $dll.dll" 'Yellow'
            continue
        }

        takeown /F $dst /A | Out-Null
        icacls $dst /grant administrators:F | Out-Null

        try {
            Remove-Item $dst -Force
        } catch {
            Color-Text "ERROR: Could not delete modified $dll.dll (file in use)" 'Red'
            continue
        }

        try {
            Rename-Item $backup $dst
            Color-Text "SUCCESS: Restored original $dll.dll" 'Green'
        } catch {
            Color-Text "ERROR: Failed to restore $dll.dll" 'Red'
        }
    }

    # Remove d3d12_capture.dll
    Log "`nRemoving d3d12_capture.dll..."
    $capDst = "$SysPath\d3d12_capture.dll"

    if (Test-Path $capDst) {
            try {
                Remove-Item $capDst -Force
                Color-Text "SUCCESS: Removed d3d12_capture.dll" 'Green'
            } catch {
                Color-Text "ERROR: Could not remove d3d12_capture.dll" 'Red'
            }
    } else {
        Color-Text "d3d12_capture.dll not found; nothing to remove" 'Yellow'
    }
}

# -------------------------------------------------------
# FORCE RESTORE (SFC + DISM)
# -------------------------------------------------------
function Force-Restore {
    Require-Admin
    Log "=== FORCE RESTORE ==="
    Log "Running SFC and DISM to repair system files..."
    Log "This may take several minutes."

    Start-Process "sfc.exe" "/scannow" -Wait -NoNewWindow
    Log "SFC completed."

    Start-Process "DISM.exe" "/Online /Cleanup-Image /RestoreHealth" -Wait -NoNewWindow
    Log "DISM completed."

    Log "Force Restore finished."
}

# -------------------------------------------------------
# STATUS CHECK (COLOR-CODED)
# -------------------------------------------------------
function Check-Status {
    Require-Admin
    Log "=== DLL Status ==="

    foreach ($dll in $DLLs) {
        $dst    = "$SysPath\$dll.dll"
        $backup = "$SysPath\${dll}_ms.dll"

        if (Test-Path $dst) {
                Color-Text "$dll.dll: OK" 'Green'
        } else {
            Color-Text "$dll.dll: MISSING" 'Red'
        }

        if (Test-Path $backup) {
            Color-Text "Backup: Present" 'Green'
        } else {
            Color-Text "Backup: Missing" 'Yellow'
        }

        Log ""
    }

    $cap = "$SysPath\d3d12_capture.dll"
    if (Test-Path $cap) {
            Color-Text "d3d12_capture.dll: OK" 'Green'
    } else {
        Color-Text "d3d12_capture.dll: MISSING" 'Yellow'
    }
}

# -------------------------------------------------------
# TEST WRITE ACCESS
# -------------------------------------------------------
function Test-WriteAccess {
    Require-Admin
    Log "=== Testing Write Access to System32 ==="

    $testFile = "$SysPath\dx_write_test.tmp"

    try {
        "test" | Out-File -FilePath $testFile -Force
        Remove-Item $testFile -Force
        Color-Text "Write Access: OK" 'Green'
    } catch {
        Color-Text "Write Access: FAILED" 'Red'
        Log "If this fails, ensure script is elevated and consider Safe Mode."
    }
}

# -------------------------------------------------------
# GUI BUTTONS
# -------------------------------------------------------
$InstallBtn = New-Object System.Windows.Forms.Button
$InstallBtn.Text = "Install Custom DLLs"
$InstallBtn.Size = New-Object System.Drawing.Size(180,30)
$InstallBtn.Location = New-Object System.Drawing.Point(10,10)
$InstallBtn.Add_Click({ Install-DLLs })
$Form.Controls.Add($InstallBtn)

$RestoreBtn = New-Object System.Windows.Forms.Button
$RestoreBtn.Text = "Restore Original DLLs"
$RestoreBtn.Size = New-Object System.Drawing.Size(180,30)
$RestoreBtn.Location = New-Object System.Drawing.Point(200,10)
$RestoreBtn.Add_Click({ Restore-DLLs })
$Form.Controls.Add($RestoreBtn)

$ForceBtn = New-Object System.Windows.Forms.Button
$ForceBtn.Text = "Force Restore (SFC/DISM)"
$ForceBtn.Size = New-Object System.Drawing.Size(180,30)
$ForceBtn.Location = New-Object System.Drawing.Point(10,50)
$ForceBtn.Add_Click({ Force-Restore })
$Form.Controls.Add($ForceBtn)

$StatusBtn = New-Object System.Windows.Forms.Button
$StatusBtn.Text = "Check Status"
$StatusBtn.Size = New-Object System.Drawing.Size(180,30)
$StatusBtn.Location = New-Object System.Drawing.Point(200,50)
$StatusBtn.Add_Click({ Check-Status })
$Form.Controls.Add($StatusBtn)

$TestBtn = New-Object System.Windows.Forms.Button
$TestBtn.Text = "Test Write Access"
$TestBtn.Size = New-Object System.Drawing.Size(180,30)
$TestBtn.Location = New-Object System.Drawing.Point(110,90)
$TestBtn.Add_Click({ Test-WriteAccess })
$Form.Controls.Add($TestBtn)

$ExitBtn = New-Object System.Windows.Forms.Button
$ExitBtn.Text = "Exit"
$ExitBtn.Size = New-Object System.Drawing.Size(180,30)
$ExitBtn.Location = New-Object System.Drawing.Point(110,130)
$ExitBtn.Add_Click({ $Form.Close() })
$Form.Controls.Add($ExitBtn)

[void]$Form.ShowDialog()