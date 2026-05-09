using HostPanel.Core.Models;

namespace HostPanel.Core.Interfaces;

public interface IBackupService
{
    Task<Backup> CreateBackupAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(int backupId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Backup>> ListBackupsAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
