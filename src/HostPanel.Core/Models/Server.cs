namespace HostPanel.Core.Models;

public class Server : BaseEntity
{
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int AgentPort { get; set; } = 50051;
    public string? OsInfo { get; set; }
    public bool IsLocal { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string? TlsCertificatePath { get; set; }
    public string? TlsKeyPath { get; set; }
    public string? AgentToken { get; set; }
    public DateTime? LastHealthCheck { get; set; }
    public bool IsHealthy { get; set; }
    public string? ServerGroup { get; set; }
}
