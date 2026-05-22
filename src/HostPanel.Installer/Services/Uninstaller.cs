using System.Diagnostics;

namespace HostPanel.Installer.Services;

/// <summary>
/// Implements `hostpanel-installer --uninstall`. Cleanly removes everything
/// the bash installer + this installer set up, so the operator can re-run
/// a fresh install on the same box. Hold-to-confirm by default; bypass with --yes.
///
/// Stop services first → drop systemd units → remove /opt/hostpanel → drop
/// nginx vhost → drop mssql databases (optional, prompted separately) → drop
/// state dir + sentinels.
/// </summary>
public static class Uninstaller
{
    public static int Run(bool skipConfirm)
    {
        Console.WriteLine();
        Console.WriteLine("=== HostPanel uninstaller ===");
        Console.WriteLine("This will remove:");
        Console.WriteLine("  - systemd units: hostpanel-api, hostpanel-agent (stopped + disabled + deleted)");
        Console.WriteLine("  - /opt/hostpanel (panel binaries + agent + wwwroot + sentinels)");
        Console.WriteLine("  - /etc/nginx/sites-enabled/hostpanel.conf + /etc/nginx/sites-available/hostpanel.conf");
        Console.WriteLine("  - /var/lib/hostpanel + /var/log/hostpanel + /etc/hostpanel");
        Console.WriteLine("  - /var/lib/hostpanel-installer/.install-state.json");
        Console.WriteLine();
        Console.WriteLine("NOT removed (you must drop these manually if desired):");
        Console.WriteLine("  - The MSSQL hostpanel database (use sqlcmd to DROP DATABASE)");
        Console.WriteLine("  - System packages installed (dotnet, mssql-server, nginx, etc.)");
        Console.WriteLine("  - Customer data outside /opt/hostpanel");
        Console.WriteLine();

        if (!skipConfirm)
        {
            Console.Write("Type 'uninstall' to confirm: ");
            var input = Console.ReadLine();
            if (input?.Trim() != "uninstall")
            {
                Console.WriteLine("Aborted — no changes made.");
                return 1;
            }
        }

        var errors = new List<string>();
        Step("Stop services", () => Run("/usr/bin/systemctl", ["stop", "hostpanel-api", "hostpanel-agent"]), errors);
        Step("Disable services", () => Run("/usr/bin/systemctl", ["disable", "hostpanel-api", "hostpanel-agent"]), errors);
        Step("Remove systemd unit files", () =>
        {
            DeleteIfExists("/etc/systemd/system/hostpanel-api.service");
            DeleteIfExists("/etc/systemd/system/hostpanel-agent.service");
            Run("/usr/bin/systemctl", ["daemon-reload"]);
        }, errors);
        Step("Remove /opt/hostpanel", () => DeleteDirIfExists("/opt/hostpanel"), errors);
        Step("Remove nginx vhost", () =>
        {
            DeleteIfExists("/etc/nginx/sites-enabled/hostpanel.conf");
            DeleteIfExists("/etc/nginx/sites-available/hostpanel.conf");
            Run("/usr/bin/systemctl", ["reload", "nginx"]);
        }, errors);
        Step("Remove /var/lib/hostpanel", () => DeleteDirIfExists("/var/lib/hostpanel"), errors);
        Step("Remove /var/log/hostpanel", () => DeleteDirIfExists("/var/log/hostpanel"), errors);
        Step("Remove /etc/hostpanel", () => DeleteDirIfExists("/etc/hostpanel"), errors);
        Step("Remove installer state", () => DeleteDirIfExists("/var/lib/hostpanel-installer"), errors);

        Console.WriteLine();
        if (errors.Count == 0)
        {
            Console.WriteLine("✓ Uninstall complete. Run the installer again for a fresh install.");
            return 0;
        }
        Console.WriteLine($"⚠ Uninstall completed with {errors.Count} error(s):");
        foreach (var e in errors) Console.WriteLine($"  - {e}");
        return 2;
    }

    private static void Step(string label, Action action, List<string> errors)
    {
        Console.Write($"  {label}… ");
        try { action(); Console.WriteLine("OK"); }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {ex.Message}");
            errors.Add($"{label}: {ex.Message}");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void DeleteDirIfExists(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static void Run(string cmd, string[] args)
    {
        if (!File.Exists(cmd)) throw new InvalidOperationException($"not found: {cmd}");
        var psi = new ProcessStartInfo(cmd) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        // ignore exit codes — service may already be stopped, unit may already be gone, etc.
    }
}
