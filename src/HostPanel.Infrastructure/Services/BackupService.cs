using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Infrastructure.Services;

public class BackupService : IBackupService
{
    private readonly AppDbContext _db;
    private readonly IServerAgent _agent;
    private readonly string _backupBasePath;
    private readonly ILogger<BackupService> _logger;

    public BackupService(AppDbContext db, IServerAgent agent, IConfiguration config, ILogger<BackupService> logger)
    {
        _db = db;
        _agent = agent;
        _backupBasePath = config["Backup:BasePath"] ?? "/var/backups/hostpanel";
        _logger = logger;
    }

    public async Task<Backup> CreateBackupAsync(int subscriptionId, CancellationToken ct = default)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.Domains)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw new InvalidOperationException("Subscription not found");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupDir = Path.Combine(_backupBasePath, sub.SystemUsername ?? $"sub_{subscriptionId}");
        var backupFile = Path.Combine(backupDir, $"backup_{timestamp}.tar.gz");

        var backup = new Backup
        {
            SubscriptionId = subscriptionId,
            Type = "full",
            FilePath = backupFile,
            IsCompleted = false
        };

        _db.Backups.Add(backup);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Starting backup for subscription {SubId} to {Path}", subscriptionId, backupFile);

        // Execute backup via agent
        var result = await _agent.ExecuteAsync(new Core.DTOs.AgentRequest
        {
            Method = Core.DTOs.AgentMethods.BackupCreate,
            Params = new Dictionary<string, object?>
            {
                ["username"] = sub.SystemUsername,
                ["homeDir"] = $"/var/www/{sub.SystemUsername}",
                ["outputPath"] = backupFile
            }
        }, ct);

        if (result.IsSuccess)
        {
            backup.IsCompleted = true;
            // Get file size
            if (result.Result?.Data?.TryGetValue("sizeBytes", out var size) == true)
            {
                if (size is System.Text.Json.JsonElement je) backup.SizeBytes = je.TryGetInt64(out var n) ? n : 0;
                else backup.SizeBytes = Convert.ToInt64(size);
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Backup {Status} for subscription {SubId}",
            backup.IsCompleted ? "completed" : "failed", subscriptionId);

        return backup;
    }

    public async Task RestoreBackupAsync(int backupId, CancellationToken ct = default)
    {
        var backup = await _db.Backups
            .Include(b => b.Subscription)
            .FirstOrDefaultAsync(b => b.Id == backupId, ct)
            ?? throw new InvalidOperationException("Backup not found");

        var result = await _agent.ExecuteAsync(new Core.DTOs.AgentRequest
        {
            Method = Core.DTOs.AgentMethods.BackupRestore,
            Params = new Dictionary<string, object?>
            {
                ["username"] = backup.Subscription.SystemUsername,
                ["homeDir"] = $"/var/www/{backup.Subscription.SystemUsername}",
                ["backupPath"] = backup.FilePath
            }
        }, ct);

        if (!result.IsSuccess)
            throw new InvalidOperationException($"Restore failed: {result.Error?.Message}");

        _logger.LogInformation("Restored backup {BackupId} for subscription {SubId}", backupId, backup.SubscriptionId);
    }

    public async Task<IEnumerable<Backup>> ListBackupsAsync(int subscriptionId, CancellationToken ct = default)
    {
        return await _db.Backups
            .Where(b => b.SubscriptionId == subscriptionId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);
    }
}
