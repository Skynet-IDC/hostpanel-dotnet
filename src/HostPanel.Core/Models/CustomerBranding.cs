namespace HostPanel.Core.Models;

/// <summary>
/// Per-customer branding override. Layered on top of the global / reseller BrandingConfig
/// when GET /branding is called with the customer's id, email, or once the customer is signed in.
/// Either the customer themselves OR the panel admin can set this on the customer's behalf.
/// </summary>
public class CustomerBranding : BaseEntity
{
    public string UserId { get; set; } = "";
    public AppUser? User { get; set; }

    public string? PanelName { get; set; }
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? LoginMessage { get; set; }
    public string? LoginBackgroundUrl { get; set; }
}
