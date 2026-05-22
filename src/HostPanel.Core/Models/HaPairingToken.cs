namespace HostPanel.Core.Models;

/// <summary>
/// Short-lived, single-use token issued by a HostPanel primary so a fresh standby
/// installation can authenticate its one-time /ha/register-standby call without
/// needing pre-shared admin credentials.
/// Created via POST /api/v1/ha/pairing-token (admin-only).
/// Consumed via POST /api/v1/ha/register-standby (anonymous — the token IS the auth).
/// TTL is 15 minutes by default; ConsumedAt is set on first successful use.
/// </summary>
public class HaPairingToken : BaseEntity
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string? ConsumedByIp { get; set; }
    public string? Notes { get; set; }
    public string? IssuedByUserId { get; set; }
}
