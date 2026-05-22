using HostPanel.Installer.Services;
using static HostPanel.Installer.Pages.FormHelpers;

namespace HostPanel.Installer.Pages;

/// <summary>Wizard page 7 — admin + branding (primary only).</summary>
public static class AdminPage
{
    public static string Render(WizardState s) =>
        HtmlLayout.RenderPage(
            title: "Admin",
            currentStep: 7,
            totalSteps: 9,
            bodyContent: $"""
<h1>Admin account & branding</h1>
<p class='sub'>Set up the first admin user. Optional branding so the panel
isn't generic on first login.</p>

{Form("/wizard/admin",
    $"""
<div class='card'>
  <p style="margin-bottom:.75rem; font-weight:500">Admin account</p>
  {Text("adminEmail", "Admin email", s.AdminEmail, "you@example.com", required: true)}
  {Text("adminPassword", "Admin password", s.AdminPassword, "min 12 chars", inputType: "password", required: true)}
  {Text("recoveryContactEmail", "Recovery contact email (optional)", s.RecoveryContactEmail, "you+backup@example.com")}
</div>

<div class='card'>
  <p style="margin-bottom:.75rem; font-weight:500">Brand the panel</p>
  {Check("uploadBrandLogo", "Use a custom logo + brand color on the panel masthead", s.UploadBrandLogo,
    "Off by default. You can change this later in Settings → Branding.")}
  {Text("brandLogoUrl", "Logo URL (only if branding is ticked)", s.BrandLogoUrl,
    "https://example.com/logo.svg")}
  {Text("brandColor", "Brand color hex (e.g. #2563eb)", s.BrandColor, "#2563eb")}
</div>

<div class='card'>
  <p style="margin-bottom:.75rem; font-weight:500">Get started faster (optional)</p>
  {Check("createDemoCustomer", "Create a demo customer + sample WordPress site after install", s.CreateDemoCustomer,
    "Gives you a non-empty panel to click around. Easy to delete later.")}
  {Check("prefetchAppCatalog", "Pre-fetch the app catalog (WordPress, Ghost, etc.)", s.PrefetchAppCatalog,
    "Downloads ~100 MB of template tarballs so first customer installs are instant.")}
</div>
""", backHref: "/wizard/integrations")}
""");

    public static void ApplyForm(WizardState s, IFormCollection f)
    {
        s.AdminEmail = Str(f, "adminEmail");
        s.AdminPassword = Str(f, "adminPassword");
        s.RecoveryContactEmail = Str(f, "recoveryContactEmail");
        s.UploadBrandLogo = Bool(f, "uploadBrandLogo");
        s.BrandLogoUrl = Str(f, "brandLogoUrl");
        s.BrandColor = Str(f, "brandColor");
        s.CreateDemoCustomer = Bool(f, "createDemoCustomer");
        s.PrefetchAppCatalog = Bool(f, "prefetchAppCatalog");
        s.LastUpdatedAt = DateTime.UtcNow;
    }
}
