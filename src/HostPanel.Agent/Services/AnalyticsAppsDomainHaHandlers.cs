using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

// --- Analytics ---
public class AnalyticsVisitorsHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.AnalyticsVisitors;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var domainName = GetStr(parameters, "domainName");
        var period = GetStr(parameters, "period") ?? "today";

        var logDir = "/var/log/nginx/";
        string[] logFiles;
        if (!string.IsNullOrEmpty(domainName))
        {
            // Filter to specific domain's log file
            var domainLog = Path.Combine(logDir, $"{domainName}.access.log");
            logFiles = File.Exists(domainLog) ? [domainLog] : [];
        }
        else
        {
            logFiles = Directory.Exists(logDir) ? Directory.GetFiles(logDir, "*.access.log") : [];
        }

        var totalHits = 0;
        var uniqueIps = new HashSet<string>();
        var statusCodes = new Dictionary<string, int>();

        foreach (var logFile in logFiles)
        {
            foreach (var line in File.ReadLines(logFile).TakeLast(10000))
            {
                totalHits++;
                var parts = line.Split(' ');
                if (parts.Length > 0) uniqueIps.Add(parts[0]);
                if (parts.Length > 8)
                {
                    var code = parts[8];
                    statusCodes[code] = statusCodes.GetValueOrDefault(code) + 1;
                }
            }
        }

        return new AgentResult
        {
            Success = true,
            Data = new()
            {
                ["totalHits"] = totalHits,
                ["uniqueVisitors"] = uniqueIps.Count,
                ["statusCodes"] = JsonSerializer.Serialize(statusCodes),
                ["period"] = period
            }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class AnalyticsBandwidthHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.AnalyticsBandwidth;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        // Use vnstat or parse logs for bandwidth
        var result = await ProcessRunner.RunAsync("/usr/bin/vnstat", ["--json", "d"], ct);

        if (result.ExitCode != 0)
        {
            // Fallback: calculate from Nginx log sizes
            var logDir = "/var/log/nginx/";
            long totalBytes = 0;
            if (Directory.Exists(logDir))
            {
                foreach (var f in Directory.GetFiles(logDir, "*.access.log"))
                    totalBytes += new FileInfo(f).Length;
            }

            return new AgentResult
            {
                Success = true,
                Data = new() { ["source"] = "logsize", ["totalBytes"] = totalBytes }
            };
        }

        return new AgentResult
        {
            Success = true,
            Data = new() { ["source"] = "vnstat", ["data"] = result.StdOut }
        };
    }
}

public class AnalyticsServerResourcesHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.AnalyticsServerResources;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        // CPU load averages
        var loadAvg = File.Exists("/proc/loadavg")
            ? (await File.ReadAllTextAsync("/proc/loadavg", ct)).Trim()
            : "0 0 0";

        // Memory from /proc/meminfo
        long totalMem = 0, freeMem = 0;
        if (File.Exists("/proc/meminfo"))
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:")) totalMem = ParseKb(line);
                if (line.StartsWith("MemAvailable:")) freeMem = ParseKb(line);
            }
        }

        // Disk usage
        var dfResult = await ProcessRunner.RunAsync("/bin/df", ["-B1", "/"], ct);
        long diskTotal = 0, diskUsed = 0;
        var dfLines = dfResult.StdOut.Split('\n');
        if (dfLines.Length > 1)
        {
            var parts = dfLines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 3)
            {
                long.TryParse(parts[1], out diskTotal);
                long.TryParse(parts[2], out diskUsed);
            }
        }

        return new AgentResult
        {
            Success = true,
            Data = new()
            {
                ["loadAverage"] = loadAvg,
                ["totalMemoryBytes"] = totalMem * 1024,
                ["usedMemoryBytes"] = (totalMem - freeMem) * 1024,
                ["totalDiskBytes"] = diskTotal,
                ["usedDiskBytes"] = diskUsed,
                ["uptimeSeconds"] = Environment.TickCount64 / 1000
            }
        };
    }

    private static long ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 && long.TryParse(parts[1], out var v) ? v : 0;
    }
}

// --- App Installer ---
public class AppInstallHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.AppsInstall;
    private readonly ILogger<AppInstallHandler> _logger;
    public AppInstallHandler(ILogger<AppInstallHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var appId = GetStr(parameters, "appId");
        var domainId = GetStr(parameters, "domainId");

        _logger.LogInformation("Installing app {AppId} for domain {DomainId}", appId, domainId);

        // Each app has its own install script
        // In production, these would download and configure each application
        var result = appId switch
        {
            "wordpress" => await InstallViaWpCli(parameters, ct),
            "nextcloud" => await InstallGenericPhpApp("https://download.nextcloud.com/server/releases/latest.tar.bz2", parameters, ct),
            "ghost" => await InstallNodeApp("ghost", parameters, ct),
            _ => await InstallGenericPhpApp(null, parameters, ct)
        };

        return result;
    }

    private async Task<AgentResult> InstallViaWpCli(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        // Delegate to WordPressInstallHandler logic
        return new AgentResult { Success = true, Data = new() { ["message"] = "Use /api/v1/wordpress/install for WordPress" } };
    }

    private async Task<AgentResult> InstallGenericPhpApp(string? downloadUrl, Dictionary<string, object?> parameters, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(downloadUrl))
            return new AgentResult { Success = false, Data = new() { ["error"] = "Download URL not configured for this app" } };

        return new AgentResult { Success = true, Data = new() { ["message"] = $"Download from {downloadUrl}" } };
    }

    private async Task<AgentResult> InstallNodeApp(string appName, Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("/usr/bin/npm", ["install", "-g", $"ghost-cli"], ct, timeoutMs: 120_000);
        return new AgentResult { Success = result.ExitCode == 0, Data = new() { ["output"] = result.StdOut } };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

// --- Domain Settings (write to Nginx includes directory) ---
public class DomainHotlinkProtectionHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.DomainSetHotlinkProtection;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var domainId = GetStr(parameters, "domainId");
        var enabled = parameters.TryGetValue("enabled", out var e) && (e is true || e?.ToString() == "True");
        var allowedDomains = GetStr(parameters, "allowedDomains") ?? "";
        var extensions = GetStr(parameters, "fileExtensions") ?? "jpg,jpeg,png,gif,webp";

        var includesDir = $"/etc/nginx/hostpanel.d";
        Directory.CreateDirectory(includesDir);
        var snippetPath = $"{includesDir}/hotlink_{domainId}.conf";

        if (enabled)
        {
            var extPattern = string.Join("|", extensions.Split(',').Select(x => x.Trim()));
            var config = $"location ~* \\.({extPattern})$ {{\n  valid_referers none blocked {allowedDomains};\n  if ($invalid_referer) {{ return 403; }}\n}}";
            await File.WriteAllTextAsync(snippetPath, config, ct);
        }
        else
        {
            if (File.Exists(snippetPath)) File.Delete(snippetPath);
        }

        await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-t"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-s", "reload"], ct);

        return new AgentResult { Success = true, Data = new() { ["enabled"] = enabled, ["path"] = snippetPath } };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class DomainIpDenyHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.DomainSetIpDeny;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var domainId = GetStr(parameters, "domainId");
        var deniedIps = GetStr(parameters, "deniedIps") ?? "";

        var includesDir = "/etc/nginx/hostpanel.d";
        Directory.CreateDirectory(includesDir);
        var snippetPath = $"{includesDir}/ipdeny_{domainId}.conf";

        if (string.IsNullOrWhiteSpace(deniedIps))
        {
            if (File.Exists(snippetPath)) File.Delete(snippetPath);
        }
        else
        {
            // Validate IPs before writing
            var lines = deniedIps.Split(',')
                .Select(ip => ip.Trim())
                .Where(ip => System.Text.RegularExpressions.Regex.IsMatch(ip, @"^[\d\.:a-fA-F/]+$"))
                .Select(ip => $"deny {ip};");
            await File.WriteAllTextAsync(snippetPath, string.Join("\n", lines), ct);
        }

        await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-t"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-s", "reload"], ct);

        return new AgentResult { Success = true, Data = new() { ["path"] = snippetPath } };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class DomainPasswordProtectHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.DomainPasswordProtect;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var username = GetStr(parameters, "username");
        var password = GetStr(parameters, "password");
        var directory = GetStr(parameters, "directory") ?? "/";
        var enabled = parameters.TryGetValue("enabled", out var e) && (e is true || e?.ToString() == "True");

        if (!enabled)
            return new AgentResult { Success = true, Data = new() { ["message"] = "Password protection disabled" } };

        // Create htpasswd file via htpasswd binary
        var htpasswdPath = $"/etc/nginx/.htpasswd_{directory.Replace("/", "_")}";
        var result = await ProcessRunner.RunAsync("/usr/bin/htpasswd",
            ["-cb", htpasswdPath, username!, password!], ct);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new()
            {
                ["htpasswdPath"] = htpasswdPath,
                ["nginxConfig"] = $"auth_basic \"Restricted\"; auth_basic_user_file {htpasswdPath};"
            }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

// --- HA ---
public class HaConfigureStandbyHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.HaConfigureStandby;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var primaryIp = GetStr(parameters, "primaryIp");
        var syncInterval = parameters.TryGetValue("syncInterval", out var si) ? si?.ToString() : "15";

        // Append rsync cron job (preserve existing crontab entries)
        var marker = "# HOSTPANEL-HA-SYNC";
        var cronEntry = $"*/{syncInterval} * * * * /usr/bin/rsync -az --delete -e ssh {primaryIp}:/var/www/ /var/www/ 2>/dev/null {marker}";

        // Read existing crontab, strip old HA lines, append new one
        var existing = await ProcessRunner.RunAsync("/usr/bin/crontab", ["-l"], ct);
        var existingLines = existing.ExitCode == 0
            ? existing.StdOut.Split('\n').Where(l => !l.Contains(marker)).ToList()
            : new List<string>();
        existingLines.Add(cronEntry);

        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, string.Join("\n", existingLines) + "\n", ct);
        var result = await ProcessRunner.RunAsync("/usr/bin/crontab", [tmpFile], ct);
        File.Delete(tmpFile);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new() { ["syncInterval"] = syncInterval, ["primaryIp"] = primaryIp }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class HaPromoteToPrimaryHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.HaPromoteToPrimary;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        // Remove only HA sync entries from crontab (preserve other jobs)
        var marker = "# HOSTPANEL-HA-SYNC";
        var existing = await ProcessRunner.RunAsync("/usr/bin/crontab", ["-l"], ct);
        if (existing.ExitCode == 0)
        {
            var cleaned = existing.StdOut.Split('\n').Where(l => !l.Contains(marker)).ToList();
            var tmpFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tmpFile, string.Join("\n", cleaned) + "\n", ct);
            await ProcessRunner.RunAsync("/usr/bin/crontab", [tmpFile], ct);
            File.Delete(tmpFile);
        }

        // Ensure all services are running
        foreach (var svc in new[] { "nginx", "hostpanel-api", "hostpanel-agent", "postfix", "dovecot" })
        {
            await ProcessRunner.RunAsync("/usr/bin/systemctl", ["start", svc], ct);
        }

        return new AgentResult
        {
            Success = true,
            Data = new() { ["message"] = "Promoted to primary. Update DNS to point to this server." }
        };
    }
}

// --- Panel Update ---
public class PanelUpdateHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.PanelUpdate;
    private readonly ILogger<PanelUpdateHandler> _logger;
    public PanelUpdateHandler(ILogger<PanelUpdateHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var sourceDir = GetStr(parameters, "sourceDir") ?? "/opt/hostpanel-src";
        var installDir = GetStr(parameters, "installDir") ?? "/opt/hostpanel";
        var gitPull = parameters.TryGetValue("gitPull", out var gp) && (gp is true || gp?.ToString() == "True");
        var branch = GetStr(parameters, "branch") ?? "main";

        _logger.LogInformation("Updating panel from {Source}", sourceDir);

        // Backup current
        var backupDir = $"{installDir}_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        await ProcessRunner.RunAsync("/bin/cp", ["-a", installDir, backupDir], ct, timeoutMs: 60_000);

        // Pull latest source
        if (gitPull && Directory.Exists(Path.Combine(sourceDir, ".git")))
        {
            await ProcessRunner.RunAsync("/usr/bin/git", ["-C", sourceDir, "pull", "origin", branch], ct, timeoutMs: 60_000);
        }

        // Build
        var buildApi = await ProcessRunner.RunAsync("/usr/bin/dotnet",
            ["publish", $"{sourceDir}/HostPanel.Api/HostPanel.Api.csproj", "-c", "Release", "-o", installDir, "--nologo", "-v", "quiet"],
            ct, timeoutMs: 300_000);

        var buildAgent = await ProcessRunner.RunAsync("/usr/bin/dotnet",
            ["publish", $"{sourceDir}/HostPanel.Agent/HostPanel.Agent.csproj", "-c", "Release", "-o", $"{installDir}/agent", "--nologo", "-v", "quiet"],
            ct, timeoutMs: 300_000);

        // Restart services
        await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "hostpanel-agent"], ct);
        await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "hostpanel-api"], ct);

        return new AgentResult
        {
            Success = buildApi.ExitCode == 0 && buildAgent.ExitCode == 0,
            Data = new()
            {
                ["backup"] = backupDir,
                ["apiBuild"] = buildApi.ExitCode == 0 ? "ok" : buildApi.StdErr,
                ["agentBuild"] = buildAgent.ExitCode == 0 ? "ok" : buildAgent.StdErr
            }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class PanelRollbackHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.PanelRollback;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var installDir = "/opt/hostpanel";
        var backups = Directory.GetDirectories(Path.GetDirectoryName(installDir)!, "hostpanel_backup_*")
            .OrderByDescending(d => d)
            .ToList();

        if (backups.Count == 0)
            return new AgentResult { Success = false, Data = new() { ["error"] = "No backup found to rollback to" } };

        var latestBackup = backups[0];

        // Swap directories
        var tempDir = $"{installDir}_rollback_tmp";
        Directory.Move(installDir, tempDir);
        Directory.Move(latestBackup, installDir);
        Directory.Delete(tempDir, true);

        // Restart services
        await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "hostpanel-agent"], ct);
        await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "hostpanel-api"], ct);

        return new AgentResult
        {
            Success = true,
            Data = new() { ["rolledBackTo"] = latestBackup }
        };
    }
}
