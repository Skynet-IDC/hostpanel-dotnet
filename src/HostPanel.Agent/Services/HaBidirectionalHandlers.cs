using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

/// <summary>Sync files FROM another server (pull). Used during failback.</summary>
public class HaSyncFromHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.HaSyncFrom;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var sourceIp = GetStr(parameters, "sourceIp");
        var syncExcludes = GetStr(parameters, "syncExcludes");

        if (string.IsNullOrEmpty(sourceIp))
            return Fail("sourceIp required");

        var args = new List<string> { "-az", "--delete", "-e", "ssh -o StrictHostKeyChecking=no" };

        // Add excludes
        if (!string.IsNullOrEmpty(syncExcludes))
        {
            foreach (var exclude in syncExcludes.Split(','))
                args.AddRange(["--exclude", exclude.Trim()]);
        }

        // Always exclude system files
        args.AddRange(["--exclude", ".git", "--exclude", "tmp/", "--exclude", "logs/"]);

        args.Add($"{sourceIp}:/var/www/");
        args.Add("/var/www/");

        var result = await ProcessRunner.RunAsync("/usr/bin/rsync", args.ToArray(), ct, timeoutMs: 600_000);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new()
            {
                ["output"] = result.StdOut,
                ["error"] = result.StdErr,
                ["syncDirection"] = "pull",
                ["source"] = sourceIp
            }
        };
    }

    private static AgentResult Fail(string msg) => new() { Success = false, Data = new() { ["error"] = msg } };
    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

/// <summary>Sync MySQL databases from another server. Dumps on source, transfers, imports locally.</summary>
public class HaSyncDatabasesHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.HaSyncDatabases;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var sourceIp = GetStr(parameters, "sourceIp");

        if (string.IsNullOrEmpty(sourceIp))
            return new AgentResult { Success = false, Data = new() { ["error"] = "sourceIp required" } };

        // Get list of HostPanel-managed databases (prefixed with hp_ or wp_)
        var listResult = await ProcessRunner.RunAsync("/usr/bin/mysql",
            ["-N", "-e", "SHOW DATABASES LIKE 'hp\\_%'; SHOW DATABASES LIKE 'wp\\_%';"], ct);

        var databases = listResult.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(db => !string.IsNullOrWhiteSpace(db))
            .ToList();

        var syncedCount = 0;
        var errors = new List<string>();

        foreach (var db in databases)
        {
            var dbName = db.Trim();
            var dumpFile = $"/tmp/ha_sync_{dbName}.sql";

            try
            {
                // Dump from remote via SSH
                var dumpResult = await ProcessRunner.RunAsync("/usr/bin/ssh",
                    ["-o", "StrictHostKeyChecking=no", sourceIp,
                     $"mysqldump --single-transaction --routines --triggers {dbName}"],
                    ct, timeoutMs: 300_000);

                if (dumpResult.ExitCode != 0)
                {
                    errors.Add($"{dbName}: dump failed - {dumpResult.StdErr}");
                    continue;
                }

                await File.WriteAllTextAsync(dumpFile, dumpResult.StdOut, ct);

                // Create database if not exists
                await ProcessRunner.RunAsync("/usr/bin/mysql",
                    ["-e", $"CREATE DATABASE IF NOT EXISTS `{dbName}`"], ct);

                // Import
                // Using mysql < file approach via shell is needed for large dumps
                var importResult = await ProcessRunner.RunAsync("/usr/bin/mysql",
                    [dbName, "-e", $"SOURCE {dumpFile}"], ct, timeoutMs: 300_000);

                if (importResult.ExitCode == 0)
                    syncedCount++;
                else
                    errors.Add($"{dbName}: import failed - {importResult.StdErr}");
            }
            finally
            {
                try { File.Delete(dumpFile); } catch { }
            }
        }

        return new AgentResult
        {
            Success = errors.Count == 0,
            Data = new()
            {
                ["syncedDatabases"] = syncedCount,
                ["totalDatabases"] = databases.Count,
                ["errors"] = JsonSerializer.Serialize(errors)
            }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

/// <summary>Sync email (Maildir) from another server.</summary>
public class HaSyncEmailHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.HaSyncEmail;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var sourceIp = GetStr(parameters, "sourceIp");

        if (string.IsNullOrEmpty(sourceIp))
            return new AgentResult { Success = false, Data = new() { ["error"] = "sourceIp required" } };

        // Sync virtual mailboxes
        var result = await ProcessRunner.RunAsync("/usr/bin/rsync",
            ["-az", "--delete", "-e", "ssh -o StrictHostKeyChecking=no",
             $"{sourceIp}:/var/vmail/", "/var/vmail/"],
            ct, timeoutMs: 600_000);

        // Fix ownership
        await ProcessRunner.RunAsync("/bin/chown", ["-R", "vmail:vmail", "/var/vmail"], ct);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new() { ["output"] = result.StdOut, ["error"] = result.StdErr }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

/// <summary>Stop all HA sync jobs (remove cron entries).</summary>
public class HaStopSyncHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.HaStopSync;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var marker = "# HOSTPANEL-HA-SYNC";
        var existing = await ProcessRunner.RunAsync("/usr/bin/crontab", ["-l"], ct);

        if (existing.ExitCode == 0)
        {
            var cleaned = existing.StdOut.Split('\n')
                .Where(l => !l.Contains(marker))
                .ToList();
            var tmpFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tmpFile, string.Join("\n", cleaned) + "\n", ct);
            await ProcessRunner.RunAsync("/usr/bin/crontab", [tmpFile], ct);
            File.Delete(tmpFile);
        }

        return new AgentResult { Success = true, Data = new() { ["message"] = "HA sync stopped" } };
    }
}

/// <summary>Demote from primary to standby. Sets up sync pulling from new primary.</summary>
public class HaDemoteToStandbyHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.HaDemoteToStandby;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var primaryIp = GetStr(parameters, "primaryIp");
        var syncInterval = GetStr(parameters, "syncInterval") ?? "15";

        if (string.IsNullOrEmpty(primaryIp))
            return new AgentResult { Success = false, Data = new() { ["error"] = "primaryIp required" } };

        // Set up rsync cron to pull from new primary (append, don't replace)
        var marker = "# HOSTPANEL-HA-SYNC";
        var cronEntry = $"*/{syncInterval} * * * * /usr/bin/rsync -az --delete -e ssh {primaryIp}:/var/www/ /var/www/ 2>/dev/null {marker}";

        var existing = await ProcessRunner.RunAsync("/usr/bin/crontab", ["-l"], ct);
        var lines = existing.ExitCode == 0
            ? existing.StdOut.Split('\n').Where(l => !l.Contains(marker)).ToList()
            : new List<string>();
        lines.Add(cronEntry);

        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, string.Join("\n", lines) + "\n", ct);
        await ProcessRunner.RunAsync("/usr/bin/crontab", [tmpFile], ct);
        File.Delete(tmpFile);

        return new AgentResult
        {
            Success = true,
            Data = new() { ["role"] = "standby", ["primaryIp"] = primaryIp }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}
