using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class BackupCreateHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.BackupCreate;
    private readonly ILogger<BackupCreateHandler> _logger;
    public BackupCreateHandler(ILogger<BackupCreateHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var username = GetStr(parameters, "username");
        var homeDir = GetStr(parameters, "homeDir");
        var outputPath = GetStr(parameters, "outputPath");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(homeDir) || string.IsNullOrEmpty(outputPath))
            return Fail("username, homeDir, and outputPath are required");

        if (!outputPath.StartsWith("/var/backups/"))
            return Fail("outputPath must be under /var/backups/");

        var outputDir = Path.GetDirectoryName(outputPath);
        if (outputDir is not null) Directory.CreateDirectory(outputDir);

        _logger.LogInformation("Creating backup of {HomeDir} to {Output}", homeDir, outputPath);

        var result = await ProcessRunner.RunAsync("/bin/tar",
            ["czf", outputPath, "-C", "/var/www", username], ct, timeoutMs: 300_000);

        if (result.ExitCode != 0)
            return Fail($"tar failed: {result.StdErr}");

        var fi = new FileInfo(outputPath);
        return new AgentResult
        {
            Success = true,
            Data = new() { ["sizeBytes"] = fi.Exists ? fi.Length : 0, ["path"] = outputPath }
        };
    }

    private static AgentResult Fail(string msg) => new() { Success = false, Data = new() { ["error"] = msg } };
    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class BackupRestoreHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.BackupRestore;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var username = parameters.TryGetValue("username", out var u) ? u?.ToString() : null;
        var homeDir = parameters.TryGetValue("homeDir", out var h) ? h?.ToString() : null;
        var backupPath = parameters.TryGetValue("backupPath", out var b) ? b?.ToString() : null;

        if (string.IsNullOrEmpty(homeDir) || string.IsNullOrEmpty(backupPath))
            return new AgentResult { Success = false, Data = new() { ["error"] = "homeDir and backupPath required" } };

        if (!backupPath.StartsWith("/var/backups/"))
            return new AgentResult { Success = false, Data = new() { ["error"] = "backupPath must be under /var/backups/" } };

        // Rename existing dir as safety net, then extract clean
        var safetyBackup = $"{homeDir}.pre-restore-{DateTime.UtcNow:yyyyMMddHHmmss}";
        if (Directory.Exists(homeDir))
            Directory.Move(homeDir!, safetyBackup);

        var result = await ProcessRunner.RunAsync("/bin/tar",
            ["xzf", backupPath, "-C", "/var/www"], ct, timeoutMs: 300_000);

        if (result.ExitCode != 0)
        {
            // Rollback: restore the pre-restore copy if extraction failed
            if (!Directory.Exists(homeDir) && Directory.Exists(safetyBackup))
                Directory.Move(safetyBackup, homeDir!);
            return new AgentResult { Success = false, Data = new() { ["error"] = result.StdErr } };
        }

        // Clean up the safety backup on success
        if (Directory.Exists(safetyBackup))
            Directory.Delete(safetyBackup, recursive: true);

        if (!string.IsNullOrEmpty(username))
            await ProcessRunner.RunAsync("/bin/chown", ["-R", $"{username}:{username}", homeDir!], ct);

        return new AgentResult
        {
            Success = true,
            Data = new() { ["output"] = result.StdOut, ["error"] = result.StdErr }
        };
    }
}
