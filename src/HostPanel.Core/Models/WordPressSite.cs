namespace HostPanel.Core.Models;

public class WordPressSite : BaseEntity
{
    public int DomainId { get; set; }
    public Domain Domain { get; set; } = null!;
    public string Path { get; set; } = "/";
    public string Version { get; set; } = string.Empty;
    public string SiteUrl { get; set; } = string.Empty;
    public string AdminUser { get; set; } = "admin";
    public bool AutoUpdate { get; set; } = true;
    public bool MaintenanceMode { get; set; }
    public bool IsStaging { get; set; }
    public int? ProductionSiteId { get; set; }
    public WordPressSite? ProductionSite { get; set; }
    public string? DbName { get; set; }
    public string? DbUser { get; set; }
    public DateTime? LastScannedAt { get; set; }
    public string? SecurityStatus { get; set; } // "good", "warnings", "critical"
}
