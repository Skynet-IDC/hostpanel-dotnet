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

            // Step 2: Git pull
            _logger.LogInformation("[UPDATE] Step 2/7: Pulling latest code");
            log.Add("=== Step 2: Git Pull ===");
            if (Directory.Exists(Path.Combine(sourceDir, ".git")))
            {
                await RunStep("/usr/bin/git", ["-C", sourceDir, "fetch", "origin"], "Git fetch", log, ct);
                await RunStep("/usr/bin/git", ["-C", sourceDir, "reset", "--hard", $"origin/{branch}"], "Git reset", log, ct);
            }
            else
            {
                log.Add("SKIP: No .git directory in source dir");
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

            // Step 4: Build frontend
            _logger.LogInformation("[UPDATE] Step 3/7: Building frontend");
            log.Add("=== Step 3: Frontend Build ===");
            var uiDir = Path.Combine(sourceDir, "src", "hostpanel-ui");
            if (Directory.Exists(uiDir))
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

            // Step 5: Build API
            _logger.LogInformation("[UPDATE] Step 4/7: Building API");
            log.Add("=== Step 4: API Build ===");
            await RunStep("/usr/bin/dotnet",
                ["publish", $"{sourceDir}/src/HostPanel.Api/HostPanel.Api.csproj",
                 "-c", "Release", "-o", installDir, "--nologo", "-v", "quiet"],
                "API build", log, ct, timeoutMs: 300_000);

            // Step 6: Build Agent
            _logger.LogInformation("[UPDATE] Step 5/7: Building Agent");
            log.Add("=== Step 5: Agent Build ===");
            await RunStep("/usr/bin/dotnet",
                ["publish", $"{sourceDir}/src/HostPanel.Agent/HostPanel.Agent.csproj",
                 "-c", "Release", "-o", $"{installDir}/agent", "--nologo", "-v", "quiet"],
                "Agent build", log, ct, timeoutMs: 300_000);

            // Step 7: Fix ownership
            log.Add("=== Step 6: Permissions ===");
            await RunStep("/bin/chown", ["-R", "www-data:www-data", installDir], "API ownership", log, ct);
            await RunStep("/bin/chown", ["-R", "root:root", $"{installDir}/agent"], "Agent ownership", log, ct);

            // Step 8: Restart services
            _logger.LogInformation("[UPDATE] Step 6/7: Restarting services");
            log.Add("=== Step 7: Restart ===");
            await RunStep("/usr/bin/systemctl", ["restart", "hostpanel-agent"], "Restart agent", log, ct);
            await Task.Delay(2000, ct);
            await RunStep("/usr/bin/systemctl", ["restart", "hostpanel-api"], "Restart API", log, ct);
            await Task.Delay(5000, ct);

            // Step 9: Health check
            _logger.LogInformation("[UPDATE] Step 7/7: Health check");
            log.Add("=== Health Check ===");
            var healthOk = false;
            for (var i = 0; i < 5; i++)
            {
                var hc = await ProcessRunner.RunAsync("/usr/bin/curl",
                    ["-sk", "--max-time", "5", "https://localhost:8443/api/v1/health"], ct);
                if (hc.StdOut.Contains("healthy"))
                {
                    healthOk = true;
                    log.Add("Health check PASSED");
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
