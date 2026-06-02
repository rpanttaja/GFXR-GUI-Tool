namespace GFXRTool.Models;

public class Game
{
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string? InstallDirectory { get; set; }
    public string? Source { get; set; }

    // Steam: numeric AppId ("1172470").  Epic: CatalogItemId or AppName ("fortnite").
    // Null for manually-added games or launchers that don't expose a protocol URL.
    public string? LauncherId { get; set; }
}
