using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;

namespace HostPanel.Modules.Database;

/// <summary>
/// MySQL/MariaDB database provisioner. Uses mysql command-line client via ProcessRunner-style execution.
/// In production, use MySqlConnector NuGet package for direct connections.
/// </summary>
public class MySqlProvisioner : IDbProvisioningService
{
    public DatabaseType DbType => DatabaseType.MySql;

    private readonly string _host;
    private readonly string _rootUser;
    private readonly string _rootPassword;
    private readonly ILogger<MySqlProvisioner> _logger;

    public MySqlProvisioner(IConfiguration config, ILogger<MySqlProvisioner> logger)
    {
        _host = config["Databases:MySql:Host"] ?? "localhost";
        _rootUser = config["Databases:MySql:RootUser"] ?? "root";
        _rootPassword = config["Databases:MySql:RootPassword"] ?? "";
        _logger = logger;
    }

    public async Task<DbProvisionResult> CreateDatabaseAsync(string dbName, string username, string password, CancellationToken ct = default)
    {
        ValidateName(dbName);
        ValidateName(username);

        var sql = $"""
            CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
            CREATE USER IF NOT EXISTS '{username}'@'localhost' IDENTIFIED BY '{password.Replace("'", "\\'")}';
            GRANT ALL PRIVILEGES ON `{dbName}`.* TO '{username}'@'localhost';
            FLUSH PRIVILEGES;
            """;

        await ExecuteMySqlAsync(sql, ct);

        _logger.LogInformation("Created MySQL database {Db} with user {User}", dbName, username);

        return new DbProvisionResult(dbName, username,
            $"Server={_host};Database={dbName};User={username};Password={password};");
    }

    public async Task DropDatabaseAsync(string dbName, CancellationToken ct = default)
    {
        ValidateName(dbName);
        await ExecuteMySqlAsync($"DROP DATABASE IF EXISTS `{dbName}`;", ct);
    }

    public async Task DropUserAsync(string username, CancellationToken ct = default)
    {
        ValidateName(username);
        await ExecuteMySqlAsync($"DROP USER IF EXISTS '{username}'@'localhost';", ct);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await ExecuteMySqlAsync("SELECT 1;", ct);
            return true;
        }
        catch { return false; }
    }

    public async Task<List<string>> ListDatabasesAsync(CancellationToken ct = default)
    {
        // Would use MySqlConnector in production; stub for now
        await Task.CompletedTask;
        return [];
    }

    private async Task ExecuteMySqlAsync(string sql, CancellationToken ct)
    {
        var args = new List<string> { $"-h{_host}", $"-u{_rootUser}" };
        if (!string.IsNullOrEmpty(_rootPassword))
            args.Add($"-p{_rootPassword}");
        args.Add("-e");
        args.Add(sql);

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/mysql",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"MySQL command failed: {err}");
        }
    }

    private static void ValidateName(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]{0,63}$"))
            throw new ArgumentException($"Invalid name: {name}");
    }
}

/// <summary>
/// MariaDB provisioner - same protocol as MySQL, different branding.
/// </summary>
public class MariaDbProvisioner : MySqlProvisioner
{
    public new DatabaseType DbType => DatabaseType.MariaDb;

    public MariaDbProvisioner(IConfiguration config, ILogger<MySqlProvisioner> logger) : base(config, logger) { }
}
