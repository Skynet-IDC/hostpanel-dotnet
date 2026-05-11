using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class WordPressInstallHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.WordPressInstall;
    private readonly ILogger<WordPressInstallHandler> _logger;
    public WordPressInstallHandler(ILogger<WordPressInstallHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var docRoot = GetStr(parameters, "docRoot");
        var username = GetStr(parameters, "username");
        var dbName = GetStr(parameters, "dbName");
        var dbUser = GetStr(parameters, "dbUser");
        var dbPass = GetStr(parameters, "dbPass");
        var siteUrl = GetStr(parameters, "siteUrl");
        var siteTitle = GetStr(parameters, "siteTitle") ?? "My Site";
        var adminUser = GetStr(parameters, "adminUser") ?? "admin";
        var adminPassword = GetStr(parameters, "adminPassword");
        var adminEmail = GetStr(parameters, "adminEmail");

        if (string.IsNullOrEmpty(docRoot) || string.IsNullOrEmpty(username))
            return Fail("docRoot and username required");

        _logger.LogInformation("Installing WordPress at {DocRoot}", docRoot);
        Directory.CreateDirectory(docRoot);

        // Validate names to prevent SQL injection (only alphanumeric + underscore)
        if (!System.Text.RegularExpressions.Regex.IsMatch(dbName!, @"^[a-zA-Z_][a-zA-Z0-9_]{0,63}$"))
            return Fail("Invalid database name");
        if (!System.Text.RegularExpressions.Regex.IsMatch(dbUser!, @"^[a-zA-Z_][a-zA-Z0-9_]{0,63}$"))
            return Fail("Invalid database user");

        // Create MySQL database - each statement as separate -e call to avoid shell issues
        await ProcessRunner.RunAsync("/usr/bin/mysql", ["-e", $"CREATE DATABASE IF NOT EXISTS `{dbName}`"], ct);
        await ProcessRunner.RunAsync("/usr/bin/mysql", ["-e", $"CREATE USER IF NOT EXISTS '{dbUser}'@'localhost' IDENTIFIED BY '{dbPass!.Replace("'", "''")}'"], ct);
        await ProcessRunner.RunAsync("/usr/bin/mysql", ["-e", $"GRANT ALL ON `{dbName}`.* TO '{dbUser}'@'localhost'"], ct);
        await ProcessRunner.RunAsync("/usr/bin/mysql", ["-e", "FLUSH PRIVILEGES"], ct);

        // Download WordPress
        await ProcessRunner.RunAsync("/usr/bin/wget", ["-q", "https://wordpress.org/latest.tar.gz", "-O", "/tmp/wordpress.tar.gz"], ct);
        await ProcessRunner.RunAsync("/bin/tar", ["xzf", "/tmp/wordpress.tar.gz", "-C", docRoot, "--strip-components=1"], ct);

        // Create wp-config.php
        var wpConfig = $"""
            <?php
            define('DB_NAME', '{dbName}');
            define('DB_USER', '{dbUser}');
            define('DB_PASSWORD', '{dbPass}');
            define('DB_HOST', 'localhost');
            define('DB_CHARSET', 'utf8mb4');
            define('DB_COLLATE', '');
            define('WP_HOME', '{siteUrl}');
            define('WP_SITEURL', '{siteUrl}');
            $table_prefix = 'wp_';
            define('WP_DEBUG', false);
            if ( !defined('ABSPATH') ) define('ABSPATH', __DIR__ . '/');
            require_once ABSPATH . 'wp-settings.php';
            """;
        await File.WriteAllTextAsync(Path.Combine(docRoot, "wp-config.php"), wpConfig, ct);

        // Set ownership BEFORE running wp-cli so it can write wp-content/uploads etc. as the tenant user.
        await ProcessRunner.RunAsync("/bin/chown", ["-R", $"{username}:{username}", docRoot], ct);

        // Install via WP-CLI. Two important details:
        //  - Invoke /usr/bin/php8.3 explicitly as the interpreter. wp-cli's shebang is
        //    #!/usr/bin/env php which resolves to /etc/alternatives/php — that points at
        //    the *highest* installed version (php8.4 on Ubuntu 24.04 with multi-PHP), and
        //    php8.4 may not have php-mysql installed. WP_CLI_PHP env var is NOT respected
        //    by older wp-cli versions, so we bypass the shebang by passing /usr/local/bin/wp
        //    as a script arg to php8.3 directly.
        //  - --skip-email avoids a 30s SMTP attempt + timeout on the welcome email.
        var wpCli = await ProcessRunner.RunAsync("/usr/bin/php8.3",
            ["/usr/local/bin/wp", "core", "install",
             $"--url={siteUrl}", $"--title={siteTitle}",
             $"--admin_user={adminUser}", $"--admin_password={adminPassword}",
             $"--admin_email={adminEmail}",
             $"--path={docRoot}", "--skip-email", "--allow-root"], ct, timeoutMs: 60_000);

        if (wpCli.ExitCode != 0)
        {
            _logger.LogError("wp-cli core install failed for {DocRoot}: exit={Exit} stderr={StdErr}",
                docRoot, wpCli.ExitCode, wpCli.StdErr);
            return new AgentResult
            {
                Success = false,
                Data = new()
                {
                    ["error"] = $"wp-cli core install failed (exit {wpCli.ExitCode})",
                    ["stderr"] = wpCli.StdErr,
                    ["stdout"] = wpCli.StdOut
                }
            };
        }

        // Re-chown — wp-cli might have created new files as root via --allow-root
        await ProcessRunner.RunAsync("/bin/chown", ["-R", $"{username}:{username}", docRoot], ct);

        _logger.LogInformation("WordPress installed at {DocRoot}, site={SiteUrl}", docRoot, siteUrl);

        return new AgentResult
        {
            Success = true,
            Data = new() { ["path"] = docRoot, ["siteUrl"] = siteUrl ?? "", ["wpCliOutput"] = wpCli.StdOut }
        };
    }

    private static AgentResult Fail(string msg) => new() { Success = false, Data = new() { ["error"] = msg } };
    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class WordPressCloneHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.WordPressClone;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var sourceDocRoot = GetStr(parameters, "sourceDocRoot");
        var destDocRoot = GetStr(parameters, "destDocRoot");
        var sourceDb = GetStr(parameters, "sourceDb");
        var destDb = GetStr(parameters, "destDb");
        var username = GetStr(parameters, "username");
        var oldUrl = GetStr(parameters, "oldUrl");
        var newUrl = GetStr(parameters, "newUrl");

        // Copy files
        await ProcessRunner.RunAsync("/bin/cp", ["-a", sourceDocRoot!, destDocRoot!], ct, timeoutMs: 120_000);

        // Clone database via temp file (no shell pipe)
        var dumpFile = Path.GetTempFileName();
        try
        {
            await ProcessRunner.RunAsync("/usr/bin/mysqldump",
                ["--result-file", dumpFile, sourceDb!], ct, timeoutMs: 120_000);
            await ProcessRunner.RunAsync("/usr/bin/mysql", ["-e", $"CREATE DATABASE IF NOT EXISTS `{destDb}`"], ct);
            await ProcessRunner.RunAsync("/usr/bin/mysql", [destDb!, "-e", $"SOURCE {dumpFile}"], ct, timeoutMs: 120_000);
        }
        finally { try { File.Delete(dumpFile); } catch { } }

        // Search-replace URLs via WP-CLI
        if (!string.IsNullOrEmpty(oldUrl) && !string.IsNullOrEmpty(newUrl))
        {
            await ProcessRunner.RunAsync("/usr/local/bin/wp",
                ["search-replace", oldUrl, newUrl, $"--path={destDocRoot}", "--all-tables", "--allow-root"], ct, timeoutMs: 60_000);
        }

        await ProcessRunner.RunAsync("/bin/chown", ["-R", $"{username}:{username}", destDocRoot!], ct);

        return new AgentResult { Success = true, Data = new() { ["destDocRoot"] = destDocRoot } };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class WordPressPushStagingHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.WordPressPushStaging;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var stagingDocRoot = GetStr(parameters, "stagingDocRoot");
        var productionDocRoot = GetStr(parameters, "productionDocRoot");
        var stagingDb = GetStr(parameters, "stagingDb");
        var productionDb = GetStr(parameters, "productionDb");
        var username = GetStr(parameters, "username");
        var oldUrl = GetStr(parameters, "oldUrl");
        var newUrl = GetStr(parameters, "newUrl");

        // Backup production first
        var backupDir = $"{productionDocRoot}_pre_push_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        await ProcessRunner.RunAsync("/bin/cp", ["-a", productionDocRoot!, backupDir], ct, timeoutMs: 120_000);

        // Sync files (excluding wp-config.php)
        await ProcessRunner.RunAsync("/usr/bin/rsync", ["-a", "--delete",
            "--exclude=wp-config.php", $"{stagingDocRoot}/", $"{productionDocRoot}/"], ct, timeoutMs: 120_000);

        // Clone staging DB to production via temp file (no shell pipe)
        var dumpFile = Path.GetTempFileName();
        try
        {
            await ProcessRunner.RunAsync("/usr/bin/mysqldump",
                ["--result-file", dumpFile, stagingDb!], ct, timeoutMs: 120_000);
            await ProcessRunner.RunAsync("/usr/bin/mysql", [productionDb!, "-e", $"SOURCE {dumpFile}"], ct, timeoutMs: 120_000);
        }
        finally { try { File.Delete(dumpFile); } catch { } }

        // Search-replace URLs
        if (!string.IsNullOrEmpty(oldUrl) && !string.IsNullOrEmpty(newUrl))
        {
            await ProcessRunner.RunAsync("/usr/local/bin/wp",
                ["search-replace", oldUrl, newUrl, $"--path={productionDocRoot}", "--all-tables", "--allow-root"], ct);
        }

        await ProcessRunner.RunAsync("/bin/chown", ["-R", $"{username}:{username}", productionDocRoot!], ct);

        return new AgentResult { Success = true, Data = new() { ["backup"] = backupDir } };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class WordPressUpdateHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.WordPressUpdate;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var docRoot = GetStr(parameters, "docRoot");
        var username = GetStr(parameters, "username");

        var results = new Dictionary<string, object?>();

        // Update core
        var core = await ProcessRunner.RunAsync("/usr/local/bin/wp",
            ["core", "update", $"--path={docRoot}", "--allow-root"], ct, timeoutMs: 120_000);
        results["core"] = core.StdOut;

        // Update plugins
        var plugins = await ProcessRunner.RunAsync("/usr/local/bin/wp",
            ["plugin", "update", "--all", $"--path={docRoot}", "--allow-root"], ct, timeoutMs: 120_000);
        results["plugins"] = plugins.StdOut;

        // Update themes
        var themes = await ProcessRunner.RunAsync("/usr/local/bin/wp",
            ["theme", "update", "--all", $"--path={docRoot}", "--allow-root"], ct, timeoutMs: 120_000);
        results["themes"] = themes.StdOut;

        await ProcessRunner.RunAsync("/bin/chown", ["-R", $"{username}:{username}", docRoot!], ct);

        return new AgentResult { Success = true, Data = results };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class WordPressSecurityScanHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.WordPressSecurityScan;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var docRoot = GetStr(parameters, "docRoot");
        var issues = new List<string>();
        var status = "good";

        // Check file permissions
        var wpConfig = Path.Combine(docRoot!, "wp-config.php");
        if (File.Exists(wpConfig))
        {
            var info = new FileInfo(wpConfig);
            if ((info.UnixFileMode & UnixFileMode.OtherRead) != 0)
                issues.Add("wp-config.php is world-readable");
        }

        // Check for debug mode
        if (File.Exists(wpConfig))
        {
            var content = await File.ReadAllTextAsync(wpConfig, ct);
            if (content.Contains("WP_DEBUG', true") || content.Contains("WP_DEBUG',true"))
                issues.Add("WP_DEBUG is enabled in production");
        }

        // Check for default admin user
        var adminCheck = await ProcessRunner.RunAsync("/usr/local/bin/wp",
            ["user", "list", "--role=administrator", "--field=user_login", $"--path={docRoot}", "--allow-root"], ct);
        if (adminCheck.StdOut.Contains("admin\n"))
            issues.Add("Default 'admin' username in use");

        // Check WordPress version
        var versionCheck = await ProcessRunner.RunAsync("/usr/local/bin/wp",
            ["core", "check-update", $"--path={docRoot}", "--allow-root"], ct);
        if (!string.IsNullOrWhiteSpace(versionCheck.StdOut) && !versionCheck.StdOut.Contains("Success"))
            issues.Add("WordPress core update available");

        // Check plugin updates
        var pluginCheck = await ProcessRunner.RunAsync("/usr/local/bin/wp",
            ["plugin", "list", "--update=available", "--format=count", $"--path={docRoot}", "--allow-root"], ct);
        if (int.TryParse(pluginCheck.StdOut.Trim(), out var pluginCount) && pluginCount > 0)
            issues.Add($"{pluginCount} plugin update(s) available");

        if (issues.Count > 3) status = "critical";
        else if (issues.Count > 0) status = "warnings";

        return new AgentResult
        {
            Success = true,
            Data = new()
            {
                ["status"] = status,
                ["issues"] = System.Text.Json.JsonSerializer.Serialize(issues),
                ["issueCount"] = issues.Count
            }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}
