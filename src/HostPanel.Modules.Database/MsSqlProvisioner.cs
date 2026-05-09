using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;

namespace HostPanel.Modules.Database;

public class MsSqlProvisioner : IDbProvisioningService
{
    public DatabaseType DbType => DatabaseType.MsSql;

    private readonly string _connectionString;
    private readonly ILogger<MsSqlProvisioner> _logger;

    public MsSqlProvisioner(IConfiguration config, ILogger<MsSqlProvisioner> logger)
    {
        _connectionString = config["Databases:MsSql:ConnectionString"]
            ?? "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True;";
        _logger = logger;
    }

    public async Task<DbProvisionResult> CreateDatabaseAsync(string dbName, string username, string password, CancellationToken ct = default)
    {
        ValidateName(dbName);
        ValidateName(username);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Create database
        await ExecuteNonQueryAsync(conn, $"IF DB_ID('{dbName}') IS NULL CREATE DATABASE [{dbName}]", ct);

        // Create login and user
        await ExecuteNonQueryAsync(conn, $"""
            IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = '{username}')
            CREATE LOGIN [{username}] WITH PASSWORD = '{password.Replace("'", "''")}';
            """, ct);

        await ExecuteNonQueryAsync(conn, $"""
            USE [{dbName}];
            IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = '{username}')
            CREATE USER [{username}] FOR LOGIN [{username}];
            ALTER ROLE db_owner ADD MEMBER [{username}];
            """, ct);

        _logger.LogInformation("Created MSSQL database {Db} with user {User}", dbName, username);

        return new DbProvisionResult(dbName, username,
            $"Server=localhost;Database={dbName};User Id={username};Password={password};TrustServerCertificate=True;");
    }

    public async Task DropDatabaseAsync(string dbName, CancellationToken ct = default)
    {
        ValidateName(dbName);
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await ExecuteNonQueryAsync(conn, $"""
            IF DB_ID('{dbName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{dbName}];
            END
            """, ct);
    }

    public async Task DropUserAsync(string username, CancellationToken ct = default)
    {
        ValidateName(username);
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await ExecuteNonQueryAsync(conn, $"IF EXISTS (SELECT * FROM sys.server_principals WHERE name = '{username}') DROP LOGIN [{username}]", ct);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            return true;
        }
        catch { return false; }
    }

    public async Task<List<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<string>();
        while (await reader.ReadAsync(ct)) list.Add(reader.GetString(0));
        return list;
    }

    private static async Task ExecuteNonQueryAsync(SqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void ValidateName(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]{0,63}$"))
            throw new ArgumentException($"Invalid name: {name}");
    }
}
