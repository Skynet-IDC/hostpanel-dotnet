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

/// <summary>
/// Phase 19c: report HA replication lag. Returns per-channel "how stale is the standby?"
/// Doesn't need any params. Cheap — file mtimes + a single ps lookup.
/// </summary>
public class HaReplicationLagHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.HaReplicationLag;

    public Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var data = new Dictionary<string, object?>();
        var now = DateTime.UtcNow;

        // 1. SQL diff backup age (mtime of /var/opt/mssql/data/hostpanel-ha.bak on primary or standby)
        var sqlBak = new FileInfo("/var/opt/mssql/data/hostpanel-ha.bak");
        if (sqlBak.Exists)
        {
            data["sqlBackupPath"] = sqlBak.FullName;
            data["sqlBackupBytes"] = sqlBak.Length;
            data["sqlBackupMtime"] = sqlBak.LastWriteTimeUtc.ToString("o");
            data["sqlBackupAgeSeconds"] = (int)(now - sqlBak.LastWriteTimeUtc).TotalSeconds;
        }
        else
        {
            data["sqlBackupPath"] = null;
            data["sqlBackupAgeSeconds"] = -1;
        }

        // 2. lsyncd: is it running, and how big is its event queue?
        bool lsyncdActive = false;
        int? lsyncdPid = null;
        try
        {
            var ps = ProcessRunner.RunAsync("/bin/systemctl", ["is-active", "lsyncd"], ct).GetAwaiter().GetResult();
            lsyncdActive = ps.ExitCode == 0 && ps.StdOut.Trim() == "active";
            var pidProbe = ProcessRunner.RunAsync("/bin/systemctl", ["show", "-p", "MainPID", "--value", "lsyncd"], ct).GetAwaiter().GetResult();
            if (pidProbe.ExitCode == 0 && int.TryParse(pidProbe.StdOut.Trim(), out var pid) && pid > 0) lsyncdPid = pid;
        }
        catch { /* lsyncd not installed on standby — that's fine */ }
        data["lsyncdActive"] = lsyncdActive;
        data["lsyncdPid"] = lsyncdPid;

        // 3. lsyncd "last sync activity" — informational, NOT a lag metric.
        //    lsyncd is inotify-driven so an idle system shows last event from days ago — that's healthy.
        //    The real "files lag" is the in-flight delay: from inotify event to rsync completion (~1-3s typical).
        //    We expose lastEvent for display, and an `filesInFlight` bool by checking for unfinished "Calling rsync".
        bool filesInFlight = false;
        if (lsyncdActive)
        {
            try
            {
                var jr = ProcessRunner.RunAsync("/bin/journalctl",
                    ["-u", "lsyncd", "-n", "50", "--no-pager", "-o", "short-iso"], ct).GetAwaiter().GetResult();
                if (jr.ExitCode == 0)
                {
                    var lines = jr.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    DateTime? lastEvent = null;
                    int callingCount = 0, finishedCount = 0;
                    foreach (var l in lines)
                    {
                        if (l.Contains("Calling rsync", StringComparison.OrdinalIgnoreCase)) callingCount++;
                        if (l.Contains("Finished", StringComparison.OrdinalIgnoreCase)) finishedCount++;
                        if (l.Contains("Calling rsync", StringComparison.OrdinalIgnoreCase)
                            || l.Contains("Finished", StringComparison.OrdinalIgnoreCase))
                        {
                            var sp = l.IndexOf(' ');
                            if (sp > 0 && DateTime.TryParse(l[..sp], null,
                                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                                out var t))
                            {
                                lastEvent = t;
                            }
                        }
                    }
                    filesInFlight = callingCount > finishedCount;
                    if (lastEvent is DateTime le)
                        data["filesLastSyncMtime"] = le.ToString("o");
                }
            }
            catch { /* journalctl might be restricted */ }
        }
        data["filesInFlight"] = filesInFlight;
        // Kept for back-compat; 0 if syncs are caught up.
        data["filesBacklogSeconds"] = filesInFlight ? 30 : 0;

        // 4. Watchdog state (standby-side check). State file under /var/lib/hostpanel-ha-watchdog/
        var watchdogStateFile = new FileInfo("/var/lib/hostpanel-ha-watchdog/state");
        if (watchdogStateFile.Exists)
        {
            try
            {
                var content = File.ReadAllText(watchdogStateFile.FullName).Trim();
                data["watchdogState"] = content;
                data["watchdogStateMtime"] = watchdogStateFile.LastWriteTimeUtc.ToString("o");
                data["watchdogStaleSeconds"] = (int)(now - watchdogStateFile.LastWriteTimeUtc).TotalSeconds;
            }
            catch { /* maybe permission */ }
        }
        else
        {
            data["watchdogState"] = null;
        }

        // 5. Last failover log line for context
        var failoverLog = new FileInfo("/var/log/hostpanel-ha-failover.log");
        if (failoverLog.Exists)
            data["lastFailoverLogMtime"] = failoverLog.LastWriteTimeUtc.ToString("o");

        return Task.FromResult(new AgentResult { Success = true, Data = data });
    }
}
