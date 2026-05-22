using System.Text.Json;
using System.Text.Json.Serialization;

namespace HostPanel.Installer.Services;

/// <summary>
/// Persists install state to disk so a crashed/rebooted installer can resume
/// from the last successful step (per Decision 4: persist + resume, no rollback).
/// File lives in /var/lib/hostpanel-installer/.install-state.json — the dir is
/// owned by root since the installer runs as root.
/// </summary>
public class InstallStateStore
{
    private readonly ILogger<InstallStateStore> _logger;
    private const string StateDir = "/var/lib/hostpanel-installer";
    private const string StatePath = "/var/lib/hostpanel-installer/.install-state.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public InstallStateStore(ILogger<InstallStateStore> logger) => _logger = logger;

    public async Task SaveAsync(PersistedInstallState state, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(StateDir);
            var tmp = StatePath + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(state, JsonOpts), ct);
            File.Move(tmp, StatePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist install state (continuing without resume support)");
        }
    }

    public async Task<PersistedInstallState?> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(StatePath)) return null;
        try
        {
            var txt = await File.ReadAllTextAsync(StatePath, ct);
            return JsonSerializer.Deserialize<PersistedInstallState>(txt, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read install state file (treating as no prior run)");
            return null;
        }
    }

    public void Clear()
    {
        try { if (File.Exists(StatePath)) File.Delete(StatePath); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete install state file"); }
    }
}
