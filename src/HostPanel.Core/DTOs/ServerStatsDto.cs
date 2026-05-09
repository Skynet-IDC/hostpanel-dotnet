namespace HostPanel.Core.DTOs;

public record ServerStatsDto(
    double CpuUsagePercent,
    long TotalMemoryMb,
    long UsedMemoryMb,
    long TotalDiskMb,
    long UsedDiskMb,
    double UptimeHours,
    string Hostname,
    string OsVersion,
    int TotalDomains,
    int TotalSubscriptions,
    int ActiveSubscriptions
);
