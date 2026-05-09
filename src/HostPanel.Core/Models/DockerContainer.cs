namespace HostPanel.Core.Models;

public class DockerContainer : BaseEntity
{
    public int SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;
    public string ContainerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string Status { get; set; } = "created";
    public string? Ports { get; set; }  // JSON: {"80":"8080","443":"8443"}
    public string? EnvVars { get; set; } // JSON
    public string? Volumes { get; set; } // JSON
    public int? ProxyPort { get; set; }  // Nginx reverse proxy to this port
}
