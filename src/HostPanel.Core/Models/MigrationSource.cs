namespace HostPanel.Core.Models;

public class MigrationSource : BaseEntity
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int PleskApiPort { get; set; } = 8443;
    public string SshUser { get; set; } = "root";
    public int SshPort { get; set; } = 22;
    public string SshKeyPath { get; set; } = "/etc/hostpanel/plesk-migration-key";
    public string PleskApiKey { get; set; } = "";
    public string DnsProvider { get; set; } = "cloudflare";
    public bool IsActive { get; set; } = true;
}
