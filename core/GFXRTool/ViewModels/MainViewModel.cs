using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GFXRTool.Models;
using GFXRTool.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace GFXRTool.ViewModels;

public enum LaunchMode { Standard, System32 }

public partial class MainViewModel : ObservableObject
{
    private readonly GameDiscoveryService _discovery = new();
    private readonly GameLauncherService  _launcher  = new();
    private readonly System32Service      _sys32     = new();
    private readonly LogService           _log       = App.StartupLog;

    public ObservableCollection<Game>    Games  { get; } = new();
    public ObservableCollection<GfxrDll> Dlls   { get; } = new();
    public ReplayViewModel               Replay { get; } = new();

    // ── Launch mode ───────────────────────────────────────────────────────────

    [ObservableProperty]
    private LaunchMode _launchMode = LaunchMode.Standard;

    partial void OnLaunchModeChanged(LaunchMode value)
    {
        OnPropertyChanged(nameof(UseStandard));
        OnPropertyChanged(nameof(UseSystem32));
        if (value == LaunchMode.System32) RefreshSystem32Status();
    }

    public bool UseStandard
    {
        get => LaunchMode == LaunchMode.Standard;
        set { if (value) LaunchMode = LaunchMode.Standard; }
    }
    public bool UseSystem32
    {
        get => LaunchMode == LaunchMode.System32;
        set { if (value) LaunchMode = LaunchMode.System32; }
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCaptureCommand))]
    private Game? _selectedGame;

    // ── System32 status ───────────────────────────────────────────────────────

    [ObservableProperty]
    private string _system32StatusText = "Unknown — click Check Status";

    [ObservableProperty]
    private string _system32StatusColor = "#858585";

    // ── Capture settings ──────────────────────────────────────────────────────

    private const string EnvTrigger  = "GFXRECON_CAPTURE_TRIGGER";
    private const string EnvCapFile  = "GFXRECON_CAPTURE_FILE";  // full path to .gfxr output file

    [ObservableProperty]
    private string _captureOutputDir = string.Empty;

    partial void OnCaptureOutputDirChanged(string value)
    {
        var live = Environment.GetEnvironmentVariable(EnvCapFile, EnvironmentVariableTarget.User);
        if (!string.IsNullOrEmpty(live))
        {
            var liveDir = Path.GetDirectoryName(live) ?? string.Empty;
            if (!string.Equals(liveDir, value, StringComparison.OrdinalIgnoreCase))
                SetStatus("Output directory changed — click Apply and restart for the new location to take effect.");
        }
    }

    [ObservableProperty]
    private string _triggerKey = "F12";

    [ObservableProperty]
    private bool _useTrigger = false;

    // Status of the live User env vars (read at startup and after Apply)
    [ObservableProperty] private string _envStatusText  = string.Empty;
    [ObservableProperty] private string _envStatusColor = "#858585";
    [ObservableProperty] private bool   _envIsReady     = false;

    public static IReadOnlyList<string> AvailableTriggerKeys { get; } =
        Enumerable.Range(1, 12).Select(i => $"F{i}").ToList();

    private void RefreshEnvStatus()
    {
        var trigger = Environment.GetEnvironmentVariable(EnvTrigger,  EnvironmentVariableTarget.User);
        var capFile = Environment.GetEnvironmentVariable(EnvCapFile,  EnvironmentVariableTarget.User);

        if (!string.IsNullOrEmpty(trigger) && !string.IsNullOrEmpty(capFile))
        {
            EnvStatusText  = $"Ready — trigger: {trigger}  |  output: {capFile}";
            EnvStatusColor = "#4EC9B0";
            EnvIsReady     = true;
        }
        else if (!string.IsNullOrEmpty(trigger))
        {
            EnvStatusText  = $"Trigger key set ({trigger}) but capture file path is not configured";
            EnvStatusColor = "#CE9178";
            EnvIsReady     = false;
        }
        else
        {
            EnvStatusText  = "Not configured — set key and output directory, then click Apply";
            EnvStatusColor = "#F44747";
            EnvIsReady     = false;
        }
    }

    [RelayCommand]
    private void ClearGfxrEnvVars()
    {
        Environment.SetEnvironmentVariable(EnvTrigger, null, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(EnvCapFile,  null, EnvironmentVariableTarget.User);
        _log.Log("Env vars cleared: GFXRECON_CAPTURE_TRIGGER, GFXRECON_CAPTURE_FILE");

        RefreshEnvStatus();

        MessageBox.Show(
            "GFXR trigger environment variables have been removed.\n\n" +
            "A system restart is required for games launched via Steam or Epic\n" +
            "to lose the old values. After restarting, GFXR will capture all\n" +
            "frames from the moment the game launches.",
            "Restart Required", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ApplyGfxrEnvVars()
    {
        if (string.IsNullOrWhiteSpace(CaptureOutputDir))
        {
            MessageBox.Show("Please set a Capture Output Directory before applying.",
                "Missing Output Directory", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Directory.CreateDirectory(CaptureOutputDir);
        var capFile = Path.Combine(CaptureOutputDir, "gfxrecon_capture.gfxr");

        Environment.SetEnvironmentVariable(EnvTrigger, TriggerKey, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(EnvCapFile,  capFile,   EnvironmentVariableTarget.User);
        _log.Log($"Env vars written: {EnvTrigger}={TriggerKey}  {EnvCapFile}={capFile}");

        RefreshEnvStatus();

        MessageBox.Show(
            $"GFXR environment variables saved:\n\n" +
            $"  {EnvTrigger} = {TriggerKey}\n" +
            $"  {EnvCapFile} = {capFile}\n\n" +
            "A system restart is required for games launched via Steam or Epic\n" +
            "to inherit these settings.\n\n" +
            "After restarting, launch the game — GFXR will wait for the\n" +
            $"{TriggerKey} key before capturing. Press it in-game (or use\n" +
            "the overlay button) to start and stop the capture.",
            "Restart Required", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void SelectCaptureOutputDir()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Capture Output Directory"
        };
        if (dlg.ShowDialog() == true)
            CaptureOutputDir = dlg.FolderName;
    }

    // ── General state ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _statusMessage = "Select a game, then click Launch Capture.";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    // ── Init ──────────────────────────────────────────────────────────────────

    public MainViewModel()
    {
        Dlls.CollectionChanged += (_, _) => LaunchCaptureCommand.NotifyCanExecuteChanged();
        LoadDefaultDlls();
        RefreshEnvStatus();
    }

    private void LoadDefaultDlls()
    {
        // Pre-populate output dir from env var if already configured, else use default.
        var liveCapFile = Environment.GetEnvironmentVariable(EnvCapFile, EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(liveCapFile))
        {
            CaptureOutputDir = Path.GetDirectoryName(liveCapFile) ?? string.Empty;
        }
        else
        {
            var defaultCaptureDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "gfxr_captures"));
            Directory.CreateDirectory(defaultCaptureDir);
            CaptureOutputDir = defaultCaptureDir;
        }
        _log.Log($"Capture dir: {CaptureOutputDir}");

        var layersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Layers");

        _log.Log($"Tool started. Base: {AppDomain.CurrentDomain.BaseDirectory}");
        _log.Log($"Looking for Layers dir: {layersDir}  exists={Directory.Exists(layersDir)}");

        if (!Directory.Exists(layersDir)) return;

        var files = Directory.GetFiles(layersDir, "*.dll")
            .OrderBy(p => Path.GetFileNameWithoutExtension(p)
                              .Equals("d3d12_capture", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(p => p);

        foreach (var path in files)
        {
            Dlls.Add(new GfxrDll { Name = Path.GetFileNameWithoutExtension(path), Path = path });
            _log.Log($"  Loaded DLL: {path}");
        }

        SetStatus(Dlls.Count > 0
            ? $"Loaded {Dlls.Count} layer DLL(s) from Layers folder. Select a game and click Launch Capture."
            : "No DLLs found in Layers folder. Add DLLs manually.");
    }

    // ── Game library commands ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task ScanGamesAsync()
    {
        IsScanning = true;
        SetStatus("Scanning for installed games...");
        try
        {
            var found = await _discovery.DiscoverAllGamesAsync();
            Games.Clear();
            foreach (var g in found)
            {
                Games.Add(g);
                _log.Log($"  Found: [{g.Source}] {g.Name}  id={g.LauncherId ?? "—"}  exe={g.ExecutablePath}");
            }
            SetStatus(found.Count > 0
                ? $"Found {found.Count} game(s). Select one and click Launch Capture."
                : "No games found. Try adding one manually.");
        }
        catch (Exception ex)
        {
            _log.LogError("ScanGames", ex);
            SetStatus($"Scan error: {ex.Message}");
        }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    private void AddGameManually()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select Game Executable",
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var game = new Game
        {
            Name             = Path.GetFileNameWithoutExtension(dlg.FileName),
            ExecutablePath   = dlg.FileName,
            InstallDirectory = Path.GetDirectoryName(dlg.FileName),
            Source           = "Manual"
        };
        Games.Add(game);
        SelectedGame = game;
        _log.Log($"Manually added: {game.Name}  exe={game.ExecutablePath}");
        SetStatus($"Added: {game.Name}");
    }

    [RelayCommand]
    private void RemoveGame()
    {
        if (SelectedGame == null) return;
        _log.Log($"Removed game: {SelectedGame.Name}");
        Games.Remove(SelectedGame);
        SelectedGame = null;
    }

    // ── System32 commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshSystem32Status()
    {
        try
        {
            var status   = _sys32.GetStatus();
            var writable = _sys32.TestWriteAccess();

            (System32StatusText, System32StatusColor) = status switch
            {
                System32Service.Sys32Status.Original        => ("Original DLLs present", "#4EC9B0"),
                System32Service.Sys32Status.CustomInstalled => ("GFXR layers installed", "#007ACC"),
                System32Service.Sys32Status.BackupMissing   => ("Custom DLLs present — no backup found!", "#F44747"),
                _                                           => ("Mixed state — check manually", "#CE9178")
            };

            if (!writable)
                System32StatusText += "  |  Write access: DENIED";

            _log.Log($"System32 status: {System32StatusText}  writable={writable}");
        }
        catch (Exception ex)
        {
            System32StatusText  = $"Status check failed: {ex.Message}";
            System32StatusColor = "#F44747";
            _log.LogError("RefreshSystem32Status", ex);
        }
    }

    [RelayCommand]
    private async Task InstallToSystem32Async()
    {
        var confirm = MessageBox.Show(
            "This will replace d3d11.dll, d3d12.dll and dxgi.dll in System32 with GFXR capture layers.\n\n" +
            "Original files will be backed up as *_ms.dll.\n\n" +
            "If the device shuts off during capture, it may need to be reflashed.\n\n" +
            "Continue?",
            "Install to System32", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        _log.Log("Installing to System32...");
        try
        {
            var layersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Layers");
            await _sys32.InstallAsync(layersDir, new Progress<string>(SetStatus));
            RefreshSystem32Status();
        }
        catch (Exception ex)
        {
            _log.LogError("InstallToSystem32", ex);
            SetStatus($"Install failed: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RestoreSystem32Async()
    {
        IsBusy = true;
        _log.Log("Restoring System32 originals...");
        try
        {
            await _sys32.RestoreAsync(new Progress<string>(SetStatus));
            RefreshSystem32Status();
        }
        catch (Exception ex)
        {
            _log.LogError("RestoreSystem32", ex);
            SetStatus($"Restore failed: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ForceRestoreSystem32Async()
    {
        var confirm = MessageBox.Show(
            "Force Restore runs SFC /scannow and DISM /RestoreHealth to repair system files.\n\n" +
            "This can take 10–30 minutes. Continue?",
            "Force Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        _log.Log("Force restore: running SFC + DISM...");
        try
        {
            await _sys32.ForceRestoreAsync(new Progress<string>(SetStatus));
            RefreshSystem32Status();
        }
        catch (Exception ex)
        {
            _log.LogError("ForceRestoreSystem32", ex);
            SetStatus($"Force restore failed: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    // ── Launch ────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchCaptureAsync()
    {
        var game = SelectedGame!;
        var dlls = Dlls.ToList();

        _log.Log($"--- Launch ---  game={game.Name}  mode={LaunchMode}  id={game.LauncherId ?? "—"}");
        _log.Log($"  exe={game.ExecutablePath}");
        _log.Log($"  dlls=[{string.Join(", ", dlls.Select(d => d.Name))}]");
        _log.Log($"  defer=true  triggerKey={TriggerKey}  outputDir={CaptureOutputDir}");

        SetStatus($"Launching {game.Name}...");

        System.Diagnostics.Process? process = null;
        try
        {
            switch (LaunchMode)
            {
                case LaunchMode.Standard:
                {
                    var deployDir = Path.GetDirectoryName(game.ExecutablePath)!;
                    SetStatus($"Deploying {dlls.Count} layer(s) to: {deployDir}");
                    var (proc, copied) = await _launcher.LaunchWithSideloadAsync(
                        game, dlls, CaptureOutputDir, deferCapture: true, TriggerKey);
                    _log.Log($"  PID {proc.Id} started");
                    foreach (var (dest, bak) in copied)
                        _log.Log($"  staged: {dest}  backup={bak ?? "none"}");
                    SetStatus($"{game.Name} launched — {copied.Count} DLL(s) deployed to {deployDir}");
                    process = proc;
                    _activeCopied = copied;
                    StagedInDir   = deployDir;
                    RemoveStagedDllsCommand.NotifyCanExecuteChanged();
                    MonitorGame(proc, () =>
                    {
                        CleanupDlls(copied);
                        _activeCopied = null;
                        StagedInDir   = null;
                        RemoveStagedDllsCommand.NotifyCanExecuteChanged();
                    });
                    break;
                }

                case LaunchMode.System32:
                    process = await LaunchSystem32ModeAsync(game);
                    break;
            }

            if (process != null && UseTrigger)
                SpawnOverlay(game.Name, process, TriggerKey);
        }
        catch (Exception ex)
        {
            _log.LogError("LaunchCapture", ex);
            SetStatus($"Launch failed: {ex.Message}");
        }
    }

    private static void CleanupDlls(IReadOnlyList<(string Dest, string? Backup)> copied) =>
        GameLauncherService.CleanupStagedDlls(copied);

    private void SpawnOverlay(string gameName, System.Diagnostics.Process process, string triggerKey)
    {
        var vm      = new CaptureViewModel(gameName, CaptureOutputDir, triggerKey, process);
        var overlay = new OverlayWindow(vm);
        overlay.Show();
        vm.RequestClose = () => overlay.Close();
    }

    private static void MonitorGame(System.Diagnostics.Process process, Action? onExit)
    {
        if (onExit == null) return;
        _ = process.WaitForExitAsync().ContinueWith(_ =>
            Application.Current.Dispatcher.InvokeAsync(() => onExit()));
    }

    private async Task<System.Diagnostics.Process?> LaunchSystem32ModeAsync(Game game)
    {
        var status = _sys32.GetStatus();
        if (status != System32Service.Sys32Status.CustomInstalled)
        {
            SetStatus("Installing GFXR layers to System32...");
            var layersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Layers");
            await _sys32.InstallAsync(layersDir, new Progress<string>(SetStatus));
            RefreshSystem32Status();
        }

        System.Diagnostics.Process process;

        if (!string.IsNullOrEmpty(game.LauncherId))
        {
            // BattleEye / EAC require the process to be spawned by the official launcher.
            // Fire the Steam/Epic URL so the trust chain is intact, then poll for the process.
            var protocolUrl = GameLauncherService.BuildProtocolUrl(game);
            SetStatus($"Firing launcher: {protocolUrl}");
            _log.Log($"  System32 mode: firing launcher URL {protocolUrl}");
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(protocolUrl) { UseShellExecute = true });

            var gameDir      = Path.GetDirectoryName(game.ExecutablePath)!;
            var launcherName = Path.GetFileNameWithoutExtension(game.ExecutablePath);
            SetStatus("Waiting for game process...");
            process = await GameLauncherService.WaitForGameProcessInDirAsync(
                gameDir, launcherName, timeoutMs: 120_000,
                CancellationToken.None, new Progress<string>(SetStatus));
        }
        else
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName         = game.ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(game.ExecutablePath),
                UseShellExecute  = true
            };
            process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start game process.");
        }

        _log.Log($"  PID {process.Id} started in System32 mode");
        SetStatus($"{game.Name} launched in System32 mode — originals will auto-restore on exit.");

        _ = process.WaitForExitAsync().ContinueWith(async _ =>
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                SetStatus("Game exited — restoring original System32 DLLs...");
                try
                {
                    await _sys32.RestoreAsync(new Progress<string>(SetStatus));
                    RefreshSystem32Status();
                }
                catch (Exception ex)
                {
                    _log.LogError("System32 auto-restore", ex);
                    SetStatus($"Auto-restore failed: {ex.Message}  — use Restore or Force Restore manually.");
                }
            });
        });

        return process;
    }

    private bool CanLaunch() => SelectedGame != null && Dlls.Count > 0;

    // ── Manual DLL cleanup ────────────────────────────────────────────────────
    // Tracks the last set of staged DLLs so they can be removed manually if the
    // game crashes before the normal on-exit cleanup fires.

    private IReadOnlyList<(string Dest, string? Backup)>? _activeCopied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStagedDlls))]
    private string? _stagedInDir;

    public bool HasStagedDlls => StagedInDir != null;

    [RelayCommand(CanExecute = nameof(HasStagedDlls))]
    private void RemoveStagedDlls()
    {
        var dir = StagedInDir;
        _log.Log($"Manual DLL removal requested in: {dir}");

        if (_activeCopied != null)
        {
            GameLauncherService.CleanupStagedDlls(_activeCopied);
            _log.Log($"  Removed {_activeCopied.Count} staged DLL(s) and restored backups.");
        }

        // Also sweep for any orphaned .gfxr_bak files left by a previous crashed session.
        if (dir != null && Directory.Exists(dir))
        {
            foreach (var bak in Directory.GetFiles(dir, "*.gfxr_bak"))
            {
                var original = bak[..^".gfxr_bak".Length]; // strip the suffix
                try
                {
                    if (!File.Exists(original))
                        File.Move(bak, original);
                    else
                        File.Delete(bak);
                    _log.Log($"  Cleaned up orphan: {bak}");
                }
                catch (Exception ex) { _log.LogError("RemoveStagedDlls orphan", ex); }
            }
        }

        _activeCopied = null;
        StagedInDir   = null;
        SetStatus("GFXR DLLs removed from game folder.");
        RemoveStagedDllsCommand.NotifyCanExecuteChanged();
    }

    // ── Log ───────────────────────────────────────────────────────────────────

    private void SetStatus(string msg)
    {
        StatusMessage = msg;
        _log.Log(msg);
    }

    [RelayCommand]
    private void CopyLog()
    {
        _log.CopyToClipboard();
        StatusMessage = "Log copied to clipboard — paste it wherever needed.";
    }
}
