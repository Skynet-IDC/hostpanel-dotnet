using System.ComponentModel.DataAnnotations;

namespace HostPanel.Core.Models;

/// <summary>
/// Generic key/value store for panel-wide settings that don't justify their own table.
/// Keys are dotted namespaces, e.g. "setup.dns.resolver" or "setup.cloudflare.apiToken".
/// Values are plaintext today; encrypt-at-rest is a follow-up.
/// </summary>
public class SystemSetting : BaseEntity
{
    [MaxLength(200)]
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    [MaxLength(100)]
    public string? Category { get; set; }
    /// <summary>Soft flag — if true, never expose this value in plaintext via list/read endpoints.</summary>
    public bool IsSensitive { get; set; }
}
