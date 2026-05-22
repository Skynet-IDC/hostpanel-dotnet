using HostPanel.Installer.Services;
using static HostPanel.Installer.Pages.FormHelpers;

namespace HostPanel.Installer.Pages;

/// <summary>Wizard page 3 — database (primary only).</summary>
public static class DatabasePage
{
    public static string Render(WizardState s)
    {
        var ramGb = s.CachedPreflight?.Memory.TotalKb / 1024 / 1024 ?? 0;
        var autoCap = Math.Min(2048, (int)(ramGb * 1024 * 0.4));
        var memDefault = s.MssqlMemoryMb ?? autoCap;
        var capSub = ramGb > 0
            ? $"Detected RAM: {ramGb} GB. Auto-recommended cap: {autoCap} MB."
            : "Set so MSSQL doesn't grab 75% of the box's RAM by default.";

        return HtmlLayout.RenderPage(
            title: "Database",
            currentStep: 3,
            totalSteps: 9,
            bodyContent: $"""
<h1>Database</h1>
<p class='sub'>HostPanel uses MSSQL. The installer will either drop a local
mssql-server (recommended) or wire up an external connection string.</p>

{Form("/wizard/database",
    $"""
<div class='card'>
  {Check("installMssql", "Install MSSQL Server on this box", s.InstallMssql,
    "Default. Installs Microsoft's mssql-server package from packages.microsoft.com.")}
  {Text("saPassword", "SA password (used during install + stored in appsettings)", s.SaPassword,
    "min 8 chars, must contain upper, lower, digit, symbol", inputType: "password", required: true)}
</div>

<div class='card'>
  <p style="margin-bottom:.75rem; font-weight:500">Memory cap</p>
  {Check("capMssqlMemory", $"Cap MSSQL memory at the value below", s.CapMssqlMemory, capSub)}
  {Text("mssqlMemoryMb", "Memory cap (MB) — blank for auto", memDefault > 0 ? memDefault.ToString() : null, "auto")}
</div>

<div class='card'>
  <p style="margin-bottom:.75rem; font-weight:500">Or use an external database</p>
  {Text("externalDbConnectionString", "External connection string (overrides local install if set)",
    s.ExternalDbConnectionString,
    "Server=db.example.com;Database=hostpanel;User Id=hp;Password=...")}
  <div class='check-detail'>Leave blank to use the local mssql-server. If set, the local install is skipped.</div>
</div>
""", backHref: "/preflight")}
""");
    }

    public static void ApplyForm(WizardState s, IFormCollection f)
    {
        s.InstallMssql = Bool(f, "installMssql");
        s.SaPassword = Str(f, "saPassword");
        s.CapMssqlMemory = Bool(f, "capMssqlMemory");
        s.MssqlMemoryMb = Int(f, "mssqlMemoryMb");
        s.ExternalDbConnectionString = Str(f, "externalDbConnectionString");
        s.LastUpdatedAt = DateTime.UtcNow;
    }
}
