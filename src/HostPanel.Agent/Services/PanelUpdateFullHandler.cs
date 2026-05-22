using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

/// <summary>
/// Full update pipeline: backup → git pull → npm build → dotnet publish → migrate → restart → health check → rollback if failed.
/// Replaces the simpler PanelUpdateHandler/PanelRollbackHandler.
/// </summary>
public class PanelFullUpdateHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.PanelUpdate;
    private readonly ILogger<PanelFullUpdateHandler> _logger;
    public PanelFullUpdateHandler(ILogger<PanelFullUpdateHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var sourceDir = GetStr(parameters, "sourceDir") ?? "/opt/hostpanel-src";
        var installDir = GetStr(parameters, "installDir") ?? "/opt/hostpanel";
        var branch = GetStr(parameters, "branch") ?? "main";
        var backupDb = parameters.TryGetValue("backupDb", out var bdb) && (bdb is true || bdb?.ToString() == "True");
        var checkOnly = parameters.TryGetValue("checkOnly", out var co)
            && (co is true
                || (co is string cs && bool.TryParse(cs, out var csb) && csb)
                || (co is JsonElement coj
                    && (coj.ValueKind == JsonValueKind.True
                        || (coj.ValueKind == JsonValueKind.String && bool.TryParse(coj.GetString(), out var jcsb) && jcsb))));

        // Short-circuit for check-only requests — compare local vs remote version.json, no file/service mutation
        if (checkOnly)
        {
            var localVerFile = Path.Combine(installDir, "version.json");
            var remoteVerFile = Path.Combine(sourceDir, "version.json");
            var localVer = "0.0.0";
            var remoteVer = "0.0.0";
            try
            {
                if (File.Exists(localVerFile))
                {
                    var j = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(localVerFile, ct));
                    if (j.TryGetProperty("version", out var lv))
                        localVer = lv.GetString() ?? localVer;
                }
            }
            catch { }
            try
            {
                if (File.Exists(remoteVerFile))
                {
                    var j = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(remoteVerFile, ct));
                    if (j.TryGetProperty("version", out var rv))
                        remoteVer = rv.GetString() ?? remoteVer;
                }
            }
            catch { }

            bool updateAvailable;
            try { updateAvailable = remoteVer != "0.0.0" && new Version(remoteVer) > new Version(localVer); }
            catch { updateAvailable = remoteVer != localVer && remoteVer != "0.0.0"; }

            return new AgentResult
            {
                Success = true,
                Data = new()
                {
                    ["currentVersion"] = localVer,
                    ["latestVersion"] = remoteVer,
                    ["updateAvailable"] = updateAvailable,
                    ["sourceExists"] = Directory.Exists(sourceDir),
                    ["checkOnly"] = true
                }
            };
        }

        var log = new List<string>();
        var backupDir = $"{installDir}_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

        try
        {
            // Step 1: Backup current installation
            _logger.LogInformation("[UPDATE] Step 1/7: Backing up current installation");
            log.Add("=== Step 1: Backup ===");
            await RunStep("/bin/cp", ["-a", installDir, backupDir], "Backup files", log, ct);

            // Step 1b: Backup database if requested
            if (backupDb)
            {
                log.Add("Backing up database...");
                var dbDump = $"{backupDir}/hostpanel_db.sql";
                await RunStep("/usr/bin/mysqldump", ["--all-databases", "--result-file", dbDump], "DB backup", log, ct, optional: true);
            }

            // Step 2: Git pull. Capture HEAD before+after for log context, but per-subsystem
            // sentinels (below) are what actually drive the selective rebuild — they survive
            // out-of-band git pulls that would otherwise fool a pre-pull-HEAD comparison.
            _logger.LogInformation("[UPDATE] Step 2/7: Pulling latest code");
            log.Add("=== Step 2: Git Pull ===");
            string oldRev = "", newRev = "";
            var frontendChanged = true;
            var apiChanged = true;
            var agentChanged = true;
            // Sentinel paths: each holds the git SHA that subsystem's deployed binary was built FROM.
            // Comparing newRev against the sentinel (not against pre-pull HEAD) is what makes
            // selective rebuild correct when the working tree was pre-advanced by cron/manual pull.
            var apiSentinelPath = Path.Combine(installDir, ".api-build-sha");
            var agentSentinelPath = Path.Combine(installDir, "agent", ".agent-build-sha");
            var frontendSentinelPath = Path.Combine(installDir, "wwwroot", ".frontend-build-sha");

            if (Directory.Exists(Path.Combine(sourceDir, ".git")))
            {
                var oldRevRes = await ProcessRunner.RunAsync("/usr/bin/git", ["-C", sourceDir, "rev-parse", "HEAD"], ct);
                oldRev = oldRevRes.StdOut.Trim();
                await RunStep("/usr/bin/git", ["-C", sourceDir, "fetch", "origin"], "Git fetch", log, ct);
                await RunStep("/usr/bin/git", ["-C", sourceDir, "reset", "--hard", $"origin/{branch}"], "Git reset", log, ct);
                var newRevRes = await ProcessRunner.RunAsync("/usr/bin/git", ["-C", sourceDir, "rev-parse", "HEAD"], ct);
                newRev = newRevRes.StdOut.Trim();

                // Read per-subsystem build-SHA sentinels (missing = "deployed SHA unknown — rebuild").
                string? apiSentinel = await ReadSentinelAsync(apiSentinelPath, ct);
                string? agentSentinel = await ReadSentinelAsync(agentSentinelPath, ct);
                string? frontendSentinel = await ReadSentinelAsync(frontendSentinelPath, ct);

                log.Add(oldRev == newRev
                    ? $"Working tree at {newRev[..7]} (no commits pulled — sentinels drive rebuild decision)"
                    : $"Pulled {oldRev[..7]}..{newRev[..7]}");

                frontendChanged = await SubsystemNeedsBuildAsync(sourceDir, frontendSentinel, newRev,
                    ["src/hostpanel-ui/"], log, "frontend", ct);
                apiChanged = await SubsystemNeedsBuildAsync(sourceDir, apiSentinel, newRev,
                    ["src/HostPanel.Api/", "src/HostPanel.Core/", "src/HostPanel.Infrastructure/",
                     "src/HostPanel.Modules.", "src/HostPanel.Shared/"], log, "api", ct);
                agentChanged = await SubsystemNeedsBuildAsync(sourceDir, agentSentinel, newRev,
                    ["src/HostPanel.Agent/", "src/HostPanel.Core/", "src/HostPanel.Shared/"], log, "agent", ct);

                if (!frontendChanged && !apiChanged && !agentChanged)
                    log.Add($"All subsystems already at {newRev[..7]} — restart only.");
            }
            else
            {
                log.Add("SKIP: No .git directory in source dir — rebuilding everything to be safe");
            }

            // Step 3: Read new version
            var versionFile = Path.Combine(sourceDir, "version.json");
            var newVersion = "unknown";
            if (File.Exists(versionFile))
            {
                var vj = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(versionFile, ct));
                newVersion = vj.TryGetProperty("version", out var v) ? v.GetString() ?? "unknown" : "unknown";
                log.Add($"New version: {newVersion}");
            }

            // Step 4: Build frontend (only if any src/hostpanel-ui/** changed in this pull)
            _logger.LogInformation("[UPDATE] Step 3/7: Building frontend");
            log.Add("=== Step 3: Frontend Build ===");
            var uiDir = Path.Combine(sourceDir, "src", "hostpanel-ui");
            if (!frontendChanged)
            {
                log.Add("SKIP: no frontend changes since last build");
            }
            else if (Directory.Exists(uiDir))
            {
                await RunStep("/usr/bin/npm", ["ci", "--prefix", uiDir], "npm ci", log, ct, timeoutMs: 300_000);
                await RunStep("/usr/bin/npm", ["run", "build", "--prefix", uiDir], "npm build", log, ct, timeoutMs: 120_000);

                // Deploy frontend
                var distDir = Path.Combine(uiDir, "dist");
                var wwwroot = Path.Combine(installDir, "wwwroot");
                if (Directory.Exists(distDir))
                {
                    if (Directory.Exists(wwwroot)) Directory.Delete(wwwroot, true);
                    await RunStep("/bin/cp", ["-a", distDir, wwwroot], "Deploy frontend", log, ct);
                    await WriteSentinelAsync(frontendSentinelPath, newRev, ct);
                }
            }
            else
            {
                log.Add("SKIP: No hostpanel-ui directory");
            }

            // Step 5: Build API (only if any backend .cs changed)
            _logger.LogInformation("[UPDATE] Step 4/7: Building API");
            log.Add("=== Step 4: API Build ===");
            if (!apiChanged)
            {
                log.Add("SKIP: no API/Core/Infrastructure/Modules changes since last build");
            }
            else
            {
                // -p:DebugType=embedded keeps PDB symbols inside the DLL so we don't
                // emit a .pdb file alongside. The previous behavior emitted .pdb files
                // that the running API process held open; the next publish then failed
                // with MSB3027/MSB3021 ("could not copy because in use by another
                // process") and -v quiet swallowed the error so the orchestrator saw
                // "API build failed:" with empty stderr and rolled back. Hit deploying
                // Phase A backend 2026-05-22.
                await RunStep("/usr/bin/dotnet",
                    ["publish", $"{sourceDir}/src/HostPanel.Api/HostPanel.Api.csproj",
                     "-c", "Release", "-o", installDir, "--nologo", "-v", "quiet",
                     "-p:DebugType=embedded", "-p:DebugSymbols=true"],
                    "API build", log, ct, timeoutMs: 300_000);
                // Best-effort cleanup of orphaned .pdb files left behind from pre-fix
                // builds. New publishes no longer create them; this just tidies disk.
                foreach (var orphan in Directory.GetFiles(installDir, "*.pdb"))
                {
                    try { File.Delete(orphan); } catch { /* may be locked — fine to leave */ }
                }
                await WriteSentinelAsync(apiSentinelPath, newRev, ct);
            }

            // Step 6: Build Agent (only if HostPanel.Agent or shared deps changed)
            _logger.LogInformation("[UPDATE] Step 5/7: Building Agent");
            log.Add("=== Step 5: Agent Build ===");
            if (!agentChanged)
            {
                log.Add("SKIP: no Agent/Core/Shared changes since last build");
            }
            else
            {
                // Same DebugType=embedded fix as API — the agent self-restart path
                // sometimes leaves a brief window where the old .pdb is still mapped.
                await RunStep("/usr/bin/dotnet",
                    ["publish", $"{sourceDir}/src/HostPanel.Agent/HostPanel.Agent.csproj",
                     "-c", "Release", "-o", $"{installDir}/agent", "--nologo", "-v", "quiet",
                     "-p:DebugType=embedded", "-p:DebugSymbols=true"],
                    "Agent build", log, ct, timeoutMs: 300_000);
                var agentDir = $"{installDir}/agent";
                if (Directory.Exists(agentDir))
                {
                    foreach (var orphan in Directory.GetFiles(agentDir, "*.pdb"))
                    {
                        try { File.Delete(orphan); } catch { /* may be locked — fine */ }
                    }
                }
                await WriteSentinelAsync(agentSentinelPath, newRev, ct);
            }

            // Step 7: Fix ownership
            log.Add("=== Step 6: Permissions ===");
            await RunStep("/bin/chown", ["-R", "www-data:www-data", installDir], "API ownership", log, ct);
            await RunStep("/bin/chown", ["-R", "root:root", $"{installDir}/agent"], "Agent ownership", log, ct);

            // Step 8: Restart services (only the ones whose code we actually rebuilt).
            // Restart API first (safe — doesn't affect the agent process).
            // For the agent itself, spawn a detached shell that waits 3s then restarts —
            // the agent can't await its own systemctl restart (SIGTERM kills the waiter).
            // systemd's Restart=always will bring the new binary up automatically.
            _logger.LogInformation("[UPDATE] Step 6/7: Restarting services");
            log.Add("=== Step 7: Restart ===");
            if (apiChanged || string.IsNullOrEmpty(oldRev))
            {
                await RunStep("/usr/bin/systemctl", ["restart", "hostpanel-api"], "Restart API", log, ct);
                await Task.Delay(3000, ct);
            }
            else
            {
                log.Add("SKIP: API not rebuilt — leaving running instance alone");
            }
            if (agentChanged || string.IsNullOrEmpty(oldRev))
            {
                // Schedule the agent self-restart via systemd-run so the restart
                // command escapes the agent's cgroup. The old approach (`bash -c
                // "sleep 3 && systemctl restart"` as a child of the agent) got
                // killed when systemd terminated the agent's cgroup during restart,
                // before the bash sleep finished — so the restart never fired and
                // the agent kept running the old binary. Symptom: "Agent self-restart
                // scheduled" logged, but `ls /proc/$(pgrep agent)/exe` pointed at the
                // pre-update binary. Hit 2026-05-22 deploying the F5 fix.
                //
                // `systemd-run --no-block --collect --unit <name>` creates a
                // transient .service unit owned by systemd PID 1, not the agent's
                // cgroup. The unit survives the agent's death, fires the restart
                // after 3s, then is garbage-collected.
                var unitName = $"hostpanel-agent-restart-{Guid.NewGuid():N}";
                using var p = new System.Diagnostics.Process();
                p.StartInfo = new System.Diagnostics.ProcessStartInfo("/usr/bin/systemd-run")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                p.StartInfo.ArgumentList.Add("--no-block");
                p.StartInfo.ArgumentList.Add("--collect");
                p.StartInfo.ArgumentList.Add("--unit");
                p.StartInfo.ArgumentList.Add(unitName);
                p.StartInfo.ArgumentList.Add("/bin/bash");
                p.StartInfo.ArgumentList.Add("-c");
                p.StartInfo.ArgumentList.Add("sleep 3 && systemctl restart hostpanel-agent");
                p.Start();
                log.Add($"Agent self-restart scheduled via systemd-run (transient unit: {unitName})");
                await Task.Delay(1000, ct);
            }
            else
            {
                log.Add("SKIP: Agent not rebuilt — leaving running instance alone");
            }

            // Step 9: Health check. 30s window per operator spec — long enough for systemd
            // restart + Kestrel cold-start + EF migration replay in a worst-case migration.
            _logger.LogInformation("[UPDATE] Step 7/7: Health check");
            log.Add("=== Health Check ===");
            var healthOk = false;
            for (var i = 0; i < 10; i++)
            {
                var hc = await ProcessRunner.RunAsync("/usr/bin/curl",
                    ["-sk", "--max-time", "5", "https://localhost:8443/api/v1/health"], ct);
                if (hc.StdOut.Contains("healthy"))
                {
                    healthOk = true;
                    log.Add($"Health check PASSED (attempt {i + 1})");
                    break;
                }
                await Task.Delay(3000, ct);
            }

            if (!healthOk)
            {
                log.Add("Health check FAILED — initiating rollback");
                throw new InvalidOperationException("Health check failed after update");
            }

            // Write version to installed location
            if (File.Exists(versionFile))
                File.Copy(versionFile, Path.Combine(installDir, "version.json"), overwrite: true);

            _logger.LogInformation("[UPDATE] Complete. Version: {Version}", newVersion);
            log.Add($"=== UPDATE COMPLETE — v{newVersion} ===");

            return new AgentResult
            {
                Success = true,
                Data = new()
                {
                    ["version"] = newVersion,
                    ["backup"] = backupDir,
                    ["log"] = string.Join("\n", log)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UPDATE] Failed — rolling back");
            log.Add($"ERROR: {ex.Message}");
            log.Add("=== ROLLBACK ===");

            // Rollback
            try
            {
                if (Directory.Exists(backupDir))
                {
                    var failedDir = $"{installDir}_failed_{DateTime.UtcNow:HHmmss}";
                    Directory.Move(installDir, failedDir);
                    Directory.Move(backupDir, installDir);
                    await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "hostpanel-agent"], ct);
                    await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "hostpanel-api"], ct);
                    log.Add("Rollback complete — previous version restored");
                    Directory.Delete(failedDir, true);
                }
            }
            catch (Exception rbEx)
            {
                log.Add($"ROLLBACK FAILED: {rbEx.Message}");
            }

            return new AgentResult
            {
                Success = false,
                Data = new()
                {
                    ["error"] = ex.Message,
                    ["log"] = string.Join("\n", log)
                }
            };
        }
    }

    private static async Task RunStep(string cmd, string[] args, string label, List<string> log, CancellationToken ct, int timeoutMs = 60_000, bool optional = false)
    {
        var result = await ProcessRunner.RunAsync(cmd, args, ct, timeoutMs);
        if (result.ExitCode != 0 && !optional)
        {
            log.Add($"FAIL [{label}]: {result.StdErr}");
            throw new InvalidOperationException($"{label} failed: {result.StdErr}");
        }
        log.Add($"OK [{label}]");
    }

    /// <summary>
    /// Returns true if a subsystem must be rebuilt. Decision is based on per-subsystem
    /// build-SHA sentinels (the SHA the deployed binary was built FROM), not pre-pull HEAD.
    /// Forces rebuild on: missing sentinel (first run after fix, or fresh install),
    /// unreachable sentinel (history rewrite / force-push), or any path in the diff
    /// sentinelSha..newRev matching pathPrefixes.
    /// </summary>
    private static async Task<bool> SubsystemNeedsBuildAsync(
        string sourceDir, string? sentinelSha, string newRev,
        string[] pathPrefixes, List<string> log, string subsystem, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sentinelSha))
        {
            log.Add($"  [{subsystem}] no build-sha sentinel — forcing rebuild");
            return true;
        }
        if (sentinelSha == newRev)
        {
            log.Add($"  [{subsystem}] sentinel matches HEAD ({newRev[..7]}) — already up to date");
            return false;
        }
        var verify = await ProcessRunner.RunAsync("/usr/bin/git",
            ["-C", sourceDir, "cat-file", "-e", $"{sentinelSha}^{{commit}}"], ct);
        if (verify.ExitCode != 0)
        {
            log.Add($"  [{subsystem}] sentinel SHA {sentinelSha[..7]} unreachable (force-push?) — forcing rebuild");
            return true;
        }
        var diffRes = await ProcessRunner.RunAsync("/usr/bin/git",
            ["-C", sourceDir, "diff", "--name-only", $"{sentinelSha}..{newRev}"], ct);
        if (diffRes.ExitCode != 0)
        {
            log.Add($"  [{subsystem}] diff failed — forcing rebuild");
            return true;
        }
        var changed = diffRes.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var hit = changed.Any(p => pathPrefixes.Any(pre => p.StartsWith(pre, StringComparison.Ordinal)));
        log.Add($"  [{subsystem}] {(hit ? "REBUILD" : "skip")} ({changed.Length} files {sentinelSha[..7]}..{newRev[..7]})");
        return hit;
    }

    private static async Task<string?> ReadSentinelAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        try { return (await File.ReadAllTextAsync(path, ct)).Trim(); }
        catch { return null; }
    }

    private static async Task WriteSentinelAsync(string path, string sha, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sha)) return; // no .git path / no rev to stamp
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, sha, ct);
        }
        catch { /* best effort — sentinel will be regenerated on next successful build */ }
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class PanelFullRollbackHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.PanelRollback;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var installDir = "/opt/hostpanel";
        var parentDir = Path.GetDirectoryName(installDir) ?? "/opt";

        var backups = Directory.GetDirectories(parentDir, "hostpanel_backup_*")
            .Concat(Directory.GetDirectories(parentDir, "hostpanel-backup_*"))
            .OrderByDescending(d => d)
            .ToList();

        if (backups.Count == 0)
            return new AgentResult { Success = false, Data = new() { ["error"] = "No backup found" } };

        var latest = backups[0];

        var tmpDir = $"{installDir}_rollback_{DateTime.UtcNow:HHmmss}";
        Directory.Move(installDir, tmpDir);
        Directory.Move(latest, installDir);

        await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "hostpanel-agent"], ct);
        await Task.Delay(2000, ct);
        await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "hostpanel-api"], ct);

        // Verify health
        await Task.Delay(5000, ct);
        var hc = await ProcessRunner.RunAsync("/usr/bin/curl",
            ["-sk", "--max-time", "5", "https://localhost:8443/api/v1/health"], ct);

        Directory.Delete(tmpDir, true);

        return new AgentResult
        {
            Success = hc.StdOut.Contains("healthy"),
            Data = new()
            {
                ["rolledBackTo"] = latest,
                ["healthy"] = hc.StdOut.Contains("healthy")
            }
        };
    }
}
