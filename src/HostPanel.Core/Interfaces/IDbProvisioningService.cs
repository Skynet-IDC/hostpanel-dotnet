using HostPanel.Core.Enums;

namespace HostPanel.Core.Interfaces;

public interface IDbProvisioningService
{
    DatabaseType DbType { get; }
    Task<DbProvisionResult> CreateDatabaseAsync(string dbName, string username, string password, CancellationToken ct = default);
    Task DropDatabaseAsync(string dbName, CancellationToken ct = default);
    Task DropUserAsync(string username, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
    Task<List<string>> ListDatabasesAsync(CancellationToken ct = default);
}

public record DbProvisionResult(string DatabaseName, string Username, string ConnectionString);
