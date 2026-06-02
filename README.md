# GFXRTool

A WPF desktop utility for launching games with [GFXR](https://github.com/google/gfxreconstruct) (GFXReconstruct) capture layers on Windows. Handles game discovery, DLL deployment, and post-capture cleanup automatically.

---

## Requirements

- Windows 10 / 11 (x64)
- .NET 8 runtime
- Administrator privileges (required for Suspended and System32 modes)
- GFXR proxy DLLs placed in a `Layers\` folder next to the executable

### Expected `Layers\` contents

| File | Purpose |
|---|---|
| `d3d12.dll` | GFXR D3D12 proxy |
| `d3d11.dll` | GFXR D3D11 proxy |
| `dxgi.dll` | GFXR DXGI proxy |
| `d3d12_capture.dll` | Core capture logic (no original to replace) |

---

## Game Library

### Scan Games
Scans the local machine for installed games from both Steam and the Epic Games Launcher.

- **Steam** — reads registry for the Steam install path, parses `libraryfolders.vdf` to find all library locations, then reads each `appmanifest_*.acf` file to get the game name and install directory. Picks the most likely executable by matching the filename against the game name, falling back to the largest `.exe` in the install tree. Utility executables (`setup`, `uninstall`, `crashhandler`, etc.) are filtered out.
- **Epic** — reads `.item` manifest files from `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests`, extracts `DisplayName`, `InstallLocation`, and `LaunchExecutable`.

Both scans run in parallel. Results are sorted alphabetically.

### Add Manually
Opens a file picker to select any `.exe`. The game is added to the list with source tagged as `Manual`.

### Remove
Removes the currently selected game from the list.

---

## Deployment Modes

Select a mode before clicking **Launch Capture**. The mode determines how the GFXR proxy DLLs are made visible to the game process.

### Standard
Copies every DLL from the `Layers\` folder into the game's install directory before launch. When the game process exits, all copied files are deleted automatically.

- **Use when**: works for the majority of games — Windows DLL search order finds the local copies before System32.
- **Risk**: none. No system files are modified.
- **Limitation**: fails if the game loads D3D DLLs by full path, or if the game's integrity check rejects unexpected files in its folder.

### Suspended Launch
Starts the game process in a suspended state, injects a remote thread into it that calls `SetDllDirectoryW` with the path to the `Layers\` folder, then resumes the process. The game's DLL loader then finds the GFXR proxies via the updated search path without any files being copied into the game directory.

- **Use when**: Standard mode fails and you want to avoid touching System32. Handles cases where DLLs would otherwise be resolved from `KnownDLLs` before the game folder is checked.
- **Risk**: low. No files are written anywhere. Requires the process to accept a remote thread (i.e. no kernel-level anti-tamper).
- **Limitation**: does not help if the game loads DLLs by full path (`LoadLibrary("C:\\Windows\\System32\\d3d12.dll")`) or uses COM factory paths.

### System32
Replaces `d3d11.dll`, `d3d12.dll`, and `dxgi.dll` in `C:\Windows\System32` with the GFXR proxies, then launches the game normally. When the game exits, the originals are restored automatically. `d3d12_capture.dll` is also deployed and removed on exit.

- **Use when**: Standard and Suspended both fail — covers full-path loads, COM factory paths, and any other mechanism the game uses to find D3D DLLs.
- **Risk**: high. If the device loses power or crashes during a capture session before auto-restore runs, the system may be left with broken D3D DLLs and require reflashing.
- **Original files** are backed up as `*_ms.dll` (e.g. `d3d12_ms.dll`) before being replaced.

#### System32 management buttons

| Button | Action |
|---|---|
| **Check Status** | Queries System32 for backup files to determine whether originals or GFXR layers are currently installed. Also tests write access. |
| **Install DLLs** | Manually installs GFXR layers to System32 (same as what happens automatically on launch in this mode). Shows a confirmation dialog first. |
| **Restore Originals** | Moves the `*_ms.dll` backups back to their original names and deletes `d3d12_capture.dll`. |
| **Force Restore (SFC/DISM)** | Last-resort recovery. Runs `sfc /scannow` followed by `DISM /Online /Cleanup-Image /RestoreHealth` to repair any corrupted system files. Takes 10–30 minutes. |

---

## Launch Capture

The **Launch Capture** button is enabled when a game is selected and at least one DLL is present in the `Layers\` folder. It executes the selected deployment mode and monitors the game process for exit to trigger cleanup.

---

## Status Bar

Displays real-time progress messages: scan results, deployment steps, launch confirmation, restore progress, and error details.

---

## Architecture

```
GFXRTool/
├── Models/
│   ├── Game.cs            # Name, ExecutablePath, InstallDirectory, Source
│   └── GfxrDll.cs         # Name, Path
├── ViewModels/
│   └── MainViewModel.cs   # MVVM: game list, launch mode, all commands
├── Services/
│   ├── GameDiscoveryService.cs   # Steam + Epic scanning
│   ├── GameLauncherService.cs    # Standard, Suspended, Injection launch paths
│   ├── System32Service.cs        # Install, restore, force-restore, status
│   └── NativeMethods.cs          # Win32 P/Invoke (CreateProcess, VirtualAllocEx, etc.)
├── MainWindow.xaml         # UI layout
├── App.xaml                # Dark theme resource dictionary
└── app.manifest            # requireAdministrator elevation
```

### DLL deployment escalation path

```
Standard  →  works for most games, zero risk
    ↓ fails (game checks its own directory)
Suspended →  no files written, handles KnownDLLs
    ↓ fails (full-path LoadLibrary or COM)
System32  →  unconditional, highest risk
```
