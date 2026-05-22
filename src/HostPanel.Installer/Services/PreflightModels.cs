namespace HostPanel.Installer.Services;

/// <summary>Three-state classification for any preflight finding.</summary>
public enum PreflightStatus
{
    /// <summary>Meets the recommended target — green.</summary>
    Pass,
    /// <summary>Below recommended but installer will continue — amber, info only.</summary>
    Warn,
    /// <summary>Hard blocker — install cannot proceed until operator resolves.</summary>
    Block
}

public record PreflightCheck(
    string Id,
    string Label,
    PreflightStatus Status,
    string? Detail = null,
    string? Suggestion = null);

public record OsInfo(
    string PrettyName,
    string Id,
    string VersionId);

public record MemoryInfo(
    long TotalKb,
    long AvailableKb,
    long SwapTotalKb);

public record DiskInfo(
    string Mount,
    long TotalGb,
    long FreeGb);

public record NetInterface(
    string Name,
    List<string> Ipv4,
    string? MacAddress,
    bool IsLoopback);

public record ListeningPort(
    int Port,
    string Address,
    string? ProcessName,
    int? Pid,
    string? User);

public record ExistingInstall(
    bool InstallDirExists,
    bool ApiServiceActive,
    bool AgentServiceActive,
    string? DeployedSha);

/// <summary>Full preflight snapshot returned by /api/preflight.</summary>
public record PreflightReport(
    OsInfo Os,
    MemoryInfo Memory,
    List<DiskInfo> Disks,
    List<NetInterface> Interfaces,
    List<ListeningPort> Ports,
    ExistingInstall Existing,
    List<PreflightCheck> Checks,
    bool HasAnyBlockers,
    bool HasAnyWarnings,
    DateTime GatheredAt);
