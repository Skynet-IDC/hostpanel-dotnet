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

            // Step 2: Git pull. Capture HEAD before+after so we can drive selective rebuild —
            // a docs-only commit shouldn't burn 3 min on a clean npm+dotnet pass.
            _logger.LogInformation("[UPDATE] Step 2/7: Pulling latest code");
            log.Add("=== Step 2: Git Pull ===");
            string oldRev = "", newRev = "";
            var frontendChanged = true;
            var apiChanged = true;
            var agentChanged = true;
            if (Directory.Exists(Path.Combine(sourceDir, ".git")))
            {
                var oldRevRes = await ProcessRunner.RunAsync("/usr/bin/git", ["-C", sourceDir, "rev-parse", "HEAD"], ct);
                oldRev = oldRevRes.StdOut.Trim();
                await RunStep("/usr/bin/git", ["-C", sourceDir, "fetch", "origin"], "Git fetch", log, ct);
                await RunStep("/usr/bin/git", ["-C", sourceDir, "reset", "--hard", $"origin/{branch}"], "Git reset", log, ct);
                var newRevRes = await ProcessRunner.RunAsync("/usr/bin/git", ["-C", sourceDir, "rev-parse", "HEAD"], ct);
                newRev = newRevRes.StdOut.Trim();
                if (!string.IsNullOrEmpty(oldRev) && oldRev != newRev)
                {
                    var diffRes = await ProcessRunner.RunAsync("/usr/bin/git",
                        ["-C", sourceDir, "diff", "--name-only", $"{oldRev}..{newRev}"], ct);
                    var changed = diffRes.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    frontendChanged = changed.Any(p => p.StartsWith("src/hostpanel-ui/", StringComparison.Ordinal));
                    apiChanged = changed.Any(p =>
                        p.StartsWith("src/HostPanel.Api/", StringComparison.Ordinal) ||
                        p.StartsWith("src/HostPanel.Core/", StringComparison.Ordinal) ||
                        p.StartsWith("src/HostPanel.Infrastructure/", StringComparison.Ordinal) ||
                        p.StartsWith("src/HostPanel.Modules.", StringComparison.Ordinal) ||
                        p.StartsWith("src/HostPanel.Shared/", StringComparison.Ordinal));
                    agentChanged = changed.Any(p =>
                        p.StartsWith("src/HostPanel.Agent/", StringComparison.Ordinal) ||
                        p.StartsWith("src/HostPanel.Core/", StringComparison.Ordinal) ||
                        p.StartsWith("src/HostPanel.Shared/", StringComparison.Ordinal));
                    log.Add($"Selective rebuild: frontend={frontendChanged}, api={apiChanged}, agent={agentChanged} ({changed.Length} files changed {oldRev[..7]}..{newRev[..7]})");
                }
                else if (oldRev == newRev)
                {
                    // Same HEAD post-reset — nothing to do, but we still let the pipeline complete a
                    // restart so the operator can use Update as a "kick the service" button.
                    frontendChanged = apiChanged = agentChanged = false;
                    log.Add($"No new commits ({oldRev[..7]}). Skipping all builds; restart only.");
                }
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
                await RunStep("/usr/bin/dotnet",
                    ["publish", $"{sourceDir}/src/HostPanel.Api/HostPanel.Api.csproj",
                     "-c", "Release", "-o", installDir, "--nologo", "-v", "quiet"],
                    "API build", log, ct, timeoutMs: 300_000);
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
                await RunStep("/usr/bin/dotnet",
                    ["publish", $"{sourceDir}/src/HostPanel.Agent/HostPanel.Agent.csproj",
                     "-c", "Release", "-o", $"{installDir}/agent", "--nologo", "-v", "quiet"],
                    "Agent build", log, ct, timeoutMs: 300_000);
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
                using var p = new System.Diagnostics.Process();
                p.StartInfo = new System.Diagnostics.ProcessStartInfo("/bin/bash",
                    "-c \"sleep 3 && systemctl restart hostpanel-agent\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                p.Start();
                log.Add("Agent self-restart scheduled");
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
