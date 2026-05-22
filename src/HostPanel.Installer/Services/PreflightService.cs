using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HostPanel.Installer.Services;

/// <summary>
/// Reads everything the wizard needs to know about the target box.
/// Pure read — never mutates anything. Mutations (free port 80, install chrony,
/// create swap) are surfaced as prompts in the wizard pages and acted on in
/// the install orchestrator (Stage 4) once the operator opts in.
/// Linux-only; runs as root in production.
/// </summary>
public class PreflightService
{
    private readonly ILogger<PreflightService> _logger;

    // Ports the installer needs to know about — order matches what the operator
    // sees in the preflight page.
    public static readonly int[] WatchedPorts = [22, 80, 443, 8080, 8443, 50051, 1433];

    public PreflightService(ILogger<PreflightService> logger) => _logger = logger;

    public async Task<PreflightReport> GatherAsync(CancellationToken ct = default)
    {
        var os = ReadOsRelease();
        var memory = ReadMemInfo();
        var disks = ReadDiskUsage();
        var interfaces = await ReadNetworkInterfacesAsync(ct);
        var ports = await ReadListeningPortsAsync(ct);
        var existing = await ReadExistingInstallAsync(ct);

        var checks = BuildChecks(os, memory, disks, interfaces, ports, existing);

        return new PreflightReport(
            Os: os,
            Memory: memory,
            Disks: disks,
            Interfaces: interfaces,
            Ports: ports,
            Existing: existing,
            Checks: checks,
            HasAnyBlockers: checks.Any(c => c.Status == PreflightStatus.Block),
            HasAnyWarnings: checks.Any(c => c.Status == PreflightStatus.Warn),
            GatheredAt: DateTime.UtcNow);
    }

    private OsInfo ReadOsRelease()
    {
        const string path = "/etc/os-release";
        if (!File.Exists(path))
            return new OsInfo("unknown", "unknown", "");

        var lines = File.ReadAllLines(path);
        var dict = lines
            .Where(l => l.Contains('='))
            .Select(l => l.Split('=', 2))
            .ToDictionary(p => p[0], p => p[1].Trim('"'));

        return new OsInfo(
            PrettyName: dict.GetValueOrDefault("PRETTY_NAME", "unknown"),
            Id: dict.GetValueOrDefault("ID", "unknown"),
            VersionId: dict.GetValueOrDefault("VERSION_ID", ""));
    }

    private MemoryInfo ReadMemInfo()
    {
        const string path = "/proc/meminfo";
        if (!File.Exists(path))
            return new MemoryInfo(0, 0, 0);

        long total = 0, avail = 0, swap = 0;
        foreach (var line in File.ReadAllLines(path))
        {
            var m = Regex.Match(line, @"^(\w+):\s+(\d+)\s+kB");
            if (!m.Success) continue;
            var kb = long.Parse(m.Groups[2].Value);
            switch (m.Groups[1].Value)
            {
                case "MemTotal": total = kb; break;
                case "MemAvailable": avail = kb; break;
                case "SwapTotal": swap = kb; break;
            }
        }
        return new MemoryInfo(total, avail, swap);
    }

    private List<DiskInfo> ReadDiskUsage()
    {
        // Only report mounts we care about: root, /opt, /var. DriveInfo on Linux maps
        // each mounted filesystem to a DriveInfo.
        var interesting = new[] { "/", "/opt", "/var" };
        var results = new List<DiskInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            if (!interesting.Contains(drive.Name)) continue;
            try
            {
                results.Add(new DiskInfo(
                    Mount: drive.Name,
                    TotalGb: drive.TotalSize / (1024L * 1024 * 1024),
                    FreeGb: drive.AvailableFreeSpace / (1024L * 1024 * 1024)));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not stat drive {Mount}", drive.Name);
            }
        }
        return results;
    }

    private async Task<List<NetInterface>> ReadNetworkInterfacesAsync(CancellationToken ct)
    {
        // `ip -j addr` gives us JSON with everything we need (iface name, IPs, mac).
        // Falls back to NetworkInterface.GetAllNetworkInterfaces() if iproute2 missing.
        var (exit, stdout, _) = await RunAsync("/usr/sbin/ip", ["-j", "addr"], ct, fallback: "/sbin/ip");
        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
            return FallbackInterfacesFromDotNet();

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var list = new List<NetInterface>();
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                var name = elem.GetProperty("ifname").GetString() ?? "?";
                var isLoopback = elem.TryGetProperty("link_type", out var lt) && lt.GetString() == "loopback";
                var mac = elem.TryGetProperty("address", out var addr) ? addr.GetString() : null;
                var ipv4 = new List<string>();
                if (elem.TryGetProperty("addr_info", out var ai))
                {
                    foreach (var a in ai.EnumerateArray())
                    {
                        if (a.TryGetProperty("family", out var fam) && fam.GetString() == "inet"
                            && a.TryGetProperty("local", out var local))
                        {
                            ipv4.Add(local.GetString() ?? "");
                        }
                    }
                }
                list.Add(new NetInterface(name, ipv4, mac, isLoopback));
            }
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse ip -j addr — falling back to .NET");
            return FallbackInterfacesFromDotNet();
        }
    }

    private static List<NetInterface> FallbackInterfacesFromDotNet()
    {
        var results = new List<NetInterface>();
        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            var ipv4 = ni.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .ToList();
            results.Add(new NetInterface(
                Name: ni.Name,
                Ipv4: ipv4,
                MacAddress: ni.GetPhysicalAddress().ToString(),
                IsLoopback: ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback));
        }
        return results;
    }

    private async Task<List<ListeningPort>> ReadListeningPortsAsync(CancellationToken ct)
    {
        // `ss -tlnpH` — H suppresses header, p shows owning process (needs root for full info)
        var (exit, stdout, _) = await RunAsync("/usr/bin/ss", ["-tlnpH"], ct, fallback: "/sbin/ss");
        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
            return [];

        var results = new List<ListeningPort>();
        foreach (var raw in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Format: State Recv-Q Send-Q LocalAddr:Port PeerAddr:Port users:(("name",pid=1234,fd=3))
            var fields = Regex.Split(raw.Trim(), @"\s+");
            if (fields.Length < 4) continue;

            var localAddr = fields[3];
            var lastColon = localAddr.LastIndexOf(':');
            if (lastColon < 0 || !int.TryParse(localAddr[(lastColon + 1)..], out var port))
                continue;

            if (!WatchedPorts.Contains(port)) continue;

            var address = localAddr[..lastColon];
            string? procName = null;
            int? pid = null;

            var userMatch = Regex.Match(raw, @"users:\(\(""([^""]+)"",pid=(\d+)");
            if (userMatch.Success)
            {
                procName = userMatch.Groups[1].Value;
                pid = int.Parse(userMatch.Groups[2].Value);
            }

            string? user = null;
            if (pid.HasValue)
            {
                try
                {
                    var statusPath = $"/proc/{pid}/status";
                    if (File.Exists(statusPath))
                    {
                        var uidLine = File.ReadAllLines(statusPath).FirstOrDefault(l => l.StartsWith("Uid:"));
                        if (uidLine is not null)
                        {
                            var parts = uidLine.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2) user = $"uid={parts[1]}";
                        }
                    }
                }
                catch { /* best effort */ }
            }

            results.Add(new ListeningPort(port, address, procName, pid, user));
        }
        return results.OrderBy(p => p.Port).ToList();
    }

    private async Task<ExistingInstall> ReadExistingInstallAsync(CancellationToken ct)
    {
        var installDirExists = Directory.Exists("/opt/hostpanel");
        var apiActive = await IsSystemdUnitActiveAsync("hostpanel-api", ct);
        var agentActive = await IsSystemdUnitActiveAsync("hostpanel-agent", ct);

        string? deployedSha = null;
        var apiSentinel = "/opt/hostpanel/.api-build-sha";
        if (File.Exists(apiSentinel))
        {
            try { deployedSha = (await File.ReadAllTextAsync(apiSentinel, ct)).Trim(); }
            catch { /* unreadable */ }
        }

        return new ExistingInstall(installDirExists, apiActive, agentActive, deployedSha);
    }

    private async Task<bool> IsSystemdUnitActiveAsync(string unit, CancellationToken ct)
    {
        var (exit, stdout, _) = await RunAsync("/usr/bin/systemctl", ["is-active", unit], ct);
        return exit == 0 && stdout.Trim() == "active";
    }

    private List<PreflightCheck> BuildChecks(
        OsInfo os, MemoryInfo mem, List<DiskInfo> disks,
        List<NetInterface> ifs, List<ListeningPort> ports, ExistingInstall existing)
    {
        var checks = new List<PreflightCheck>();

        // OS — Ubuntu 24.04 is the tested target.
        checks.Add(os.Id switch
        {
            "ubuntu" when os.VersionId == "24.04" =>
                new PreflightCheck("os", "Operating system", PreflightStatus.Pass, os.PrettyName),
            "ubuntu" =>
                new PreflightCheck("os", "Operating system", PreflightStatus.Warn,
                    $"{os.PrettyName} (tested target is Ubuntu 24.04)",
                    "Install may work but is not in the supported matrix."),
            "debian" =>
                new PreflightCheck("os", "Operating system", PreflightStatus.Warn,
                    $"{os.PrettyName} (tested target is Ubuntu 24.04)",
                    "Debian-family install path. MSSQL package availability not guaranteed."),
            _ =>
                new PreflightCheck("os", "Operating system", PreflightStatus.Block,
                    $"{os.PrettyName} is not supported",
                    "HostPanel installer requires Ubuntu 24.04 (or compatible Debian-family).")
        });

        // RAM — 8GB recommended, 4GB minimum.
        var totalGb = mem.TotalKb / 1024 / 1024;
        checks.Add(totalGb switch
        {
            >= 8 => new PreflightCheck("ram", "Memory", PreflightStatus.Pass, $"{totalGb} GB"),
            >= 4 => new PreflightCheck("ram", "Memory", PreflightStatus.Warn,
                $"{totalGb} GB (recommended ≥ 8 GB for 10+ customers)",
                "Will install. MSSQL memory cap will be sized accordingly."),
            _    => new PreflightCheck("ram", "Memory", PreflightStatus.Block,
                $"{totalGb} GB (minimum 4 GB)",
                "MSSQL + dotnet API need at least 4 GB to start.")
        });

        // Swap — only flagged if RAM < 8GB and no swap configured.
        if (mem.SwapTotalKb == 0 && totalGb < 8)
        {
            checks.Add(new PreflightCheck("swap", "Swap", PreflightStatus.Warn,
                "No swap configured",
                $"On a {totalGb} GB box, a swapfile would help under load. The wizard will offer to create one."));
        }
        else
        {
            var swapMb = mem.SwapTotalKb / 1024;
            checks.Add(new PreflightCheck("swap", "Swap", PreflightStatus.Pass, $"{swapMb} MB"));
        }

        // Disk — root mount needs at least 20 GB free, recommend 50 GB.
        var root = disks.FirstOrDefault(d => d.Mount == "/");
        if (root is not null)
        {
            checks.Add(root.FreeGb switch
            {
                >= 50 => new PreflightCheck("disk", "Free disk on /", PreflightStatus.Pass, $"{root.FreeGb} GB free of {root.TotalGb} GB"),
                >= 20 => new PreflightCheck("disk", "Free disk on /", PreflightStatus.Warn,
                    $"{root.FreeGb} GB free of {root.TotalGb} GB (recommended ≥ 50 GB)",
                    "OK for a small panel. Tight if you plan to host customer site backups locally."),
                _     => new PreflightCheck("disk", "Free disk on /", PreflightStatus.Block,
                    $"{root.FreeGb} GB free of {root.TotalGb} GB (need ≥ 20 GB)",
                    "MSSQL + dotnet + customer-site working space won't fit.")
            });
        }
        else
        {
            checks.Add(new PreflightCheck("disk", "Free disk on /", PreflightStatus.Warn,
                "Could not stat root filesystem", null));
        }

        // Network — need at least one non-loopback IPv4.
        var publicIfs = ifs.Where(i => !i.IsLoopback && i.Ipv4.Count > 0).ToList();
        if (publicIfs.Count == 0)
            checks.Add(new PreflightCheck("network", "Network", PreflightStatus.Block,
                "No non-loopback IPv4 address found",
                "Box needs at least one network interface with an IPv4 address."));
        else
            checks.Add(new PreflightCheck("network", "Network", PreflightStatus.Pass,
                $"{publicIfs.Count} interface{(publicIfs.Count == 1 ? "" : "s")} with IPv4: {string.Join(", ", publicIfs.Select(i => $"{i.Name}={string.Join(",", i.Ipv4)}"))}"));

        // Ports — 8443 + 50051 must be free; others are advisory.
        foreach (var port in WatchedPorts)
        {
            var hit = ports.FirstOrDefault(p => p.Port == port);
            if (hit is null)
            {
                // 22 should typically be up so the operator doesn't lock themselves out post-install.
                if (port == 22)
                    checks.Add(new PreflightCheck($"port-22", "SSH (port 22)", PreflightStatus.Warn,
                        "No SSH daemon listening",
                        "Without SSH you'll lose remote access after this terminal closes. Consider sudo systemctl enable --now ssh before proceeding."));
                continue;
            }

            var status = port switch
            {
                8443 => PreflightStatus.Block, // panel binds here
                50051 => PreflightStatus.Block, // agent binds here
                _ => PreflightStatus.Warn
            };
            var who = hit.ProcessName is null ? "unknown process" : $"{hit.ProcessName} (pid {hit.Pid})";
            var suggestion = port switch
            {
                8443 => "Stop the process holding 8443 (likely a prior hostpanel install) before continuing.",
                50051 => "Stop the process holding 50051 (likely a prior hostpanel-agent) before continuing.",
                80 => "certbot needs port 80 free during cert issuance. Wizard will prompt to stop the holder.",
                443 => "nginx will bind 443 — wizard will prompt to stop the holder.",
                1433 => "Existing MSSQL instance detected. Wizard will reuse it instead of dropping a new server.",
                _ => "Wizard will prompt to free this port if needed."
            };
            checks.Add(new PreflightCheck($"port-{port}", $"Port {port}", status,
                $"Listening: {hit.Address}:{port} ({who})",
                suggestion));
        }

        // Existing install — refuses, per Decision 3 ("re-entrant install: refuse").
        if (existing.InstallDirExists || existing.ApiServiceActive || existing.AgentServiceActive)
        {
            var detail = new List<string>();
            if (existing.InstallDirExists) detail.Add("/opt/hostpanel exists");
            if (existing.ApiServiceActive) detail.Add("hostpanel-api is running");
            if (existing.AgentServiceActive) detail.Add("hostpanel-agent is running");
            if (existing.DeployedSha is not null) detail.Add($"deployed SHA: {existing.DeployedSha[..7]}");

            checks.Add(new PreflightCheck("existing", "Existing HostPanel install", PreflightStatus.Block,
                string.Join("; ", detail),
                "Use `hostpanel update` from the panel UI to upgrade, or `hostpanel-installer --uninstall` to start clean."));
        }
        else
        {
            checks.Add(new PreflightCheck("existing", "Existing HostPanel install", PreflightStatus.Pass, "Clean target"));
        }

        return checks;
    }

    /// <summary>
    /// Runs a process, returns (exitCode, stdout, stderr). Empty stdout if exe missing.
    /// Optional fallback path lets us try /sbin/ss when /usr/bin/ss isn't present.
    /// </summary>
    private static async Task<(int exit, string stdout, string stderr)> RunAsync(
        string cmd, string[] args, CancellationToken ct, string? fallback = null)
    {
        if (!File.Exists(cmd))
        {
            if (fallback is not null && File.Exists(fallback))
                cmd = fallback;
            else
                return (-1, "", $"executable not found: {cmd}");
        }

        var psi = new ProcessStartInfo(cmd)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi)!;
            var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return (p.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
