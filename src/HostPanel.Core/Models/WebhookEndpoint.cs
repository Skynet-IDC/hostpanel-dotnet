namespace HostPanel.Core.Models;

public class WebhookEndpoint : BaseEntity
{
    public string Url { get; set; } = string.Empty;
    public string? Secret { get; set; }
    public string Events { get; set; } = "*";  // comma-separated or "*" for all
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public int FailureCount { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
}
