namespace HostPanel.Core.Models;

public class MigrationJob : BaseEntity
{
    public int MigrationSourceId { get; set; }
    public MigrationSource? MigrationSource { get; set; }

    public string SourceDomain { get; set; } = "";

    public int? TargetSubscriptionId { get; set; }
    public string? TargetUserId { get; set; }
    public int? ServicePlanId { get; set; }

    // discovered | preflight-pass | preflight-fail | staging | staged
    // | cutover-pending | cutover | cutover-failed | rolled-back | failed
    public string State { get; set; } = "discovered";

    public string DiscoveryJson { get; set; } = "{}";
    public string PreflightReport { get; set; } = "{}";
    public string MigrationLog { get; set; } = "";
    public string? ErrorMessage { get; set; }

    public DateTime? StagedAt { get; set; }
    public DateTime? CutoverAt { get; set; }
    public DateTime? RolledBackAt { get; set; }
}
