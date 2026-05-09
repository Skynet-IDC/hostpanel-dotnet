namespace HostPanel.Core.Models;

public class TotpToken : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;
    public string SecretKey { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public string? RecoveryCodes { get; set; } // JSON array
}
