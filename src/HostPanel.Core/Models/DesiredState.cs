using HostPanel.Core.Enums;

namespace HostPanel.Core.Models;

public class DesiredState : BaseEntity
{
    public string ResourceType { get; set; } = string.Empty;  // "subscription", "domain", "mailbox", etc.
    public int ResourceId { get; set; }
    public ProvisioningState State { get; set; } = ProvisioningState.Pending;
    public string DesiredConfig { get; set; } = "{}";  // JSON of what we want
    public string? ActualConfig { get; set; }           // JSON of what the server actually has
    public string? LastError { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 5;
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public string? TraceId { get; set; }
}
