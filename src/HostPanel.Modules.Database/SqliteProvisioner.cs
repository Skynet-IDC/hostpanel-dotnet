using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;

namespace HostPanel.Modules.Database;

public class SqliteProvisioner : IDbProvisioningService
{
    public DatabaseType DbType => DatabaseType.Sqlite;

    private readonly string _basePath;
    private readonly ILogger<SqliteProvisioner> _logger;

    public SqliteProvisioner(IConfiguration config, ILogger<SqliteProvisioner> logger)
    {
        _basePath = config["Databases:Sqlite:BasePath"] ?? "/var/lib/hostpanel/sqlite";
        _logger = logger;
    }

    public Task<DbProvisionResult> CreateDatabaseAsync(string dbName, string username, string password, CancellationToken ct = default)
    {
        ValidateName(dbName);
        Directory.CreateDirectory(_basePath);

        var dbPath = Path.Combine(_basePath, $"{dbName}.db");

        // SQLite creates the file on first access, just ensure path exists
        if (!File.Exists(dbPath))
            File.Create(dbPath).Dispose();

        _logger.LogInformation("Created SQLite database {Db} at {Path}", dbName, dbPath);

        return Task.FromResult(new DbProvisionResult(dbName, username,
            $"Data Source={dbPath};"));
    }

    public Task DropDatabaseAsync(string dbName, CancellationToken ct = default)
    {
        ValidateName(dbName);
        var dbPath = Path.Combine(_basePath, $"{dbName}.db");
        if (File.Exists(dbPath)) File.Delete(dbPath);
        return Task.CompletedTask;
    }

    public Task DropUserAsync(string username, CancellationToken ct = default) =>
        Task.CompletedTask; // SQLite has no users

    public Task<bool> TestConnectionAsync(CancellationToken ct = default) =>
        Task.FromResult(Directory.Exists(_basePath));

    public Task<List<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_basePath)) return Task.FromResult(new List<string>());
        var dbs = Directory.GetFiles(_basePath, "*.db")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();
        return Task.FromResult(dbs);
    }

    private static void ValidateName(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]{0,63}$"))
            throw new ArgumentException($"Invalid name: {name}");
    }
}
