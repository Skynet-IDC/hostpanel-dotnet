namespace HostPanel.Core.Models;

public class GitDeployment : BaseEntity
{
    public int DomainId { get; set; }
    public Domain Domain { get; set; } = null!;
    public string RepoUrl { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public string Provider { get; set; } = "github"; // github, gitlab, bitbucket
    public string? WebhookSecret { get; set; }
    public string DeployPath { get; set; } = string.Empty;
    public bool AutoDeploy { get; set; } = true;
    public string? LastCommitHash { get; set; }
    public DateTime? LastDeployedAt { get; set; }
    public string? LastDeployLog { get; set; }
}
