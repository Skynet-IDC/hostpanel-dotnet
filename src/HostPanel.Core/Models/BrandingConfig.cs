namespace HostPanel.Core.Models;

public class BrandingConfig : BaseEntity
{
    public string PanelName { get; set; } = "HostPanel";
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string PrimaryColor { get; set; } = "#2563eb";
    public string SidebarColor { get; set; } = "#111827";
    public string? AccentColor { get; set; }
    public string? LoginBackgroundUrl { get; set; }
    public string? LoginMessage { get; set; }
    public string? FooterText { get; set; }
    public string? SupportUrl { get; set; }
    public string? SupportEmail { get; set; }
    public string? CustomCss { get; set; }
    public bool HidePoweredBy { get; set; }
    public int? ResellerId { get; set; }  // null = global, set = per-reseller branding

    /// <summary>True once an admin completes the first-launch SetupWizard.
    /// Used by GET /branding to tell the frontend whether to render the wizard.</summary>
    public bool Configured { get; set; }
}
