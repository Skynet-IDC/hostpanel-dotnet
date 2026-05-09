using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;

namespace HostPanel.Modules.Database;

public class PostgreSqlProvisioner : IDbProvisioningService
{
    public DatabaseType DbType => DatabaseType.PostgreSql;

    private readonly string _host;
    private readonly ILogger<PostgreSqlProvisioner> _logger;

    public PostgreSqlProvisioner(IConfiguration config, ILogger<PostgreSqlProvisioner> logger)
    {
        _host = config["Databases:PostgreSql:Host"] ?? "localhost";
        _logger = logger;
    }

    public async Task<DbProvisionResult> CreateDatabaseAsync(string dbName, string username, string password, CancellationToken ct = default)
    {
        ValidateName(dbName);
        ValidateName(username);

        // Create role (idempotent)
        await RunPsqlAsync($"DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{username}') THEN CREATE ROLE \"{username}\" LOGIN PASSWORD '{password.Replace("'", "''")}'; END IF; END $$;", ct);

        // Create database (use createdb binary to avoid \gexec issues in non-interactive psql)
        await RunCreateDbAsync(dbName, username, ct);

        // Grant privileges
        await RunPsqlAsync($"GRANT ALL PRIVILEGES ON DATABASE \"{dbName}\" TO \"{username}\";", ct);

        _logger.LogInformation("Created PostgreSQL database {Db} with user {User}", dbName, username);

        return new DbProvisionResult(dbName, username,
            $"Host={_host};Database={dbName};Username={username};Password={password};");
    }

    public async Task DropDatabaseAsync(string dbName, CancellationToken ct = default)
    {
        ValidateName(dbName);
        await RunPsqlAsync($"DROP DATABASE IF EXISTS \"{dbName}\";", ct);
    }

    public async Task DropUserAsync(string username, CancellationToken ct = default)
    {
        ValidateName(username);
        await RunPsqlAsync($"DROP ROLE IF EXISTS \"{username}\";", ct);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await RunPsqlAsync("SELECT 1;", ct);
            return true;
        }
        catch { return false; }
    }

    public async Task<List<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask;
        return [];
    }

    private async Task RunPsqlAsync(string sql, CancellationToken ct)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/psql",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("-h");
        process.StartInfo.ArgumentList.Add(_host);
        process.StartInfo.ArgumentList.Add("-U");
        process.StartInfo.ArgumentList.Add("postgres");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(sql);

        process.Start();
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"PostgreSQL command failed: {err}");
        }
    }

    private async Task RunCreateDbAsync(string dbName, string owner, CancellationToken ct)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/createdb",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("-h");
        process.StartInfo.ArgumentList.Add(_host);
        process.StartInfo.ArgumentList.Add("-U");
        process.StartInfo.ArgumentList.Add("postgres");
        process.StartInfo.ArgumentList.Add("-O");
        process.StartInfo.ArgumentList.Add(owner);
        process.StartInfo.ArgumentList.Add(dbName);

        process.Start();
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            // Ignore "already exists" errors for idempotency
            if (!err.Contains("already exists"))
                throw new InvalidOperationException($"createdb failed: {err}");
        }
    }

    private static void ValidateName(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]{0,63}$"))
            throw new ArgumentException($"Invalid name: {name}");
    }
}
