namespace HostPanel.Installer.Services;

public enum CliMode { Wizard, Headless, DryRun, Uninstall, Reconfigure, Help }

/// <summary>
/// Parses installer command-line flags. Five modes besides the default wizard:
///   --uninstall                Nuke everything (hold-to-confirm).
///   --config FILE              Load wizard answers from a YAML file.
///   --headless                 No web UI — run orchestrator from --config and exit.
///   --dry-run                  Walk pipeline, log what would happen, mutate nothing.
///   --reconfigure              Run wizard against an existing install (rotate creds, etc).
///
/// Flag combinations:
///   --headless requires --config.
///   --dry-run can pair with --headless OR be a flag on a wizard-mode run.
///   --uninstall ignores all other flags except --yes (skip hold-to-confirm).
/// </summary>
public class CliOptions
{
    public CliMode Mode { get; set; } = CliMode.Wizard;
    public string? ConfigPath { get; set; }
    public bool DryRun { get; set; }
    public bool SkipConfirm { get; set; }     // --yes for --uninstall
    public bool Help { get; set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--uninstall": o.Mode = CliMode.Uninstall; break;
                case "--reconfigure": o.Mode = CliMode.Reconfigure; break;
                case "--headless": o.Mode = CliMode.Headless; break;
                case "--dry-run": o.DryRun = true; if (o.Mode == CliMode.Wizard) o.Mode = CliMode.DryRun; break;
                case "--config":
                    if (i + 1 >= args.Length) throw new ArgumentException("--config requires a path");
                    o.ConfigPath = args[++i]; break;
                case "--yes" or "-y": o.SkipConfirm = true; break;
                case "--help" or "-h": o.Help = true; o.Mode = CliMode.Help; break;
                default:
                    throw new ArgumentException($"Unknown flag: {args[i]}. Try --help.");
            }
        }

        if (o.Mode == CliMode.Headless && string.IsNullOrEmpty(o.ConfigPath))
            throw new ArgumentException("--headless requires --config <path-to-yaml>");

        return o;
    }

    public static string HelpText() => """
HostPanel installer

USAGE:
  hostpanel-installer                  Boot wizard on http://0.0.0.0:8080.
  hostpanel-installer --dry-run        Wizard, but every step prints what it would
                                       do and mutates nothing. For previews.
  hostpanel-installer --config PATH --headless
                                       No wizard. Loads YAML config and runs
                                       orchestrator non-interactively. Suitable
                                       for Ansible/Terraform/CI.
  hostpanel-installer --reconfigure    Wizard against an existing install. Lets
                                       you rotate SA password, swap LE/self-signed,
                                       change hostname, etc.
  hostpanel-installer --uninstall      Removes /opt/hostpanel, systemd units,
                                       mssql databases, nginx vhost. Asks for
                                       confirmation unless --yes is also passed.

EXAMPLES:
  curl https://install.hostpanel.tld | sudo bash       # production install
  sudo hostpanel-installer --config /etc/hp.yaml --headless  # CI / IaC
  sudo hostpanel-installer --dry-run                   # preview without changes
  sudo hostpanel-installer --uninstall --yes           # nuke for test cycles
""";
}
