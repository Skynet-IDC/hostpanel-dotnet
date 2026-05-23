using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class PhpCreatePoolHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.PhpCreatePool;
    private readonly ILogger<PhpCreatePoolHandler> _logger;
    public PhpCreatePoolHandler(ILogger<PhpCreatePoolHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var poolName = GetStr(parameters, "poolName");
        var username = GetStr(parameters, "username");
        var phpVersion = GetStr(parameters, "phpVersion") ?? "8.3";

        if (string.IsNullOrEmpty(poolName) || string.IsNullOrEmpty(username))
            return Fail("poolName and username required");

        if (!System.Text.RegularExpressions.Regex.IsMatch(poolName, @"^[a-zA-Z_][a-zA-Z0-9_]{0,31}$"))
            return Fail("Invalid pool name");

        var configPath = $"/etc/php/{phpVersion}/fpm/pool.d/{poolName}.conf";
        var config = $$"""
            [{{poolName}}]
            user = {{username}}
            group = {{username}}
            listen = /run/php/php{{phpVersion}}-fpm-{{poolName}}.sock
            listen.owner = www-data
            listen.group = www-data
            pm = dynamic
            pm.max_children = 10
            pm.start_servers = 2
            pm.min_spare_servers = 1
            pm.max_spare_servers = 3
            pm.max_requests = 500
            security.limit_extensions = .php
            php_admin_value[open_basedir] = /var/www/{{username}}:/tmp
            php_admin_value[disable_functions] = exec,passthru,shell_exec,system,proc_open,popen
            """;

        await File.WriteAllTextAsync(configPath, config, ct);

        var result = await ProcessRunner.RunAsync("/usr/sbin/service",
            [$"php{phpVersion}-fpm", "reload"], ct);

        _logger.LogInformation("Created PHP-FPM pool {Pool} for {User}", poolName, username);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new() { ["configPath"] = configPath }
        };
    }

    private static AgentResult Fail(string msg) => new() { Success = false, Data = new() { ["error"] = msg } };
    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class PhpDeletePoolHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.PhpDeletePool;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var poolName = parameters.TryGetValue("poolName", out var p) ? p?.ToString() : null;
        var phpVersion = parameters.TryGetValue("phpVersion", out var v) ? v?.ToString() ?? "8.3" : "8.3";

        if (string.IsNullOrEmpty(poolName))
            return new AgentResult { Success = false, Data = new() { ["error"] = "poolName required" } };

        var configPath = $"/etc/php/{phpVersion}/fpm/pool.d/{poolName}.conf";
        if (File.Exists(configPath)) File.Delete(configPath);

        await ProcessRunner.RunAsync("/usr/sbin/service", [$"php{phpVersion}-fpm", "reload"], ct);
        return new AgentResult { Success = true };
    }
}

/// <summary>
/// Phase C: regenerate a per-domain FPM pool config with a new PHP version,
/// extension toggles, and php.ini overrides. Replaces the existing pool file
/// in place. If `oldPhpVersion` differs from `phpVersion`, the old pool file
/// is removed from the old version's pool.d directory (otherwise abandoned
/// files accumulate as you flip versions).
/// </summary>
public class PhpUpdatePoolHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.PhpUpdatePool;
    private readonly ILogger<PhpUpdatePoolHandler> _logger;
    public PhpUpdatePoolHandler(ILogger<PhpUpdatePoolHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var poolName = GetStr(parameters, "poolName");
        var username = GetStr(parameters, "username");
        var phpVersion = GetStr(parameters, "phpVersion") ?? "8.3";
        var oldPhpVersion = GetStr(parameters, "oldPhpVersion");
        var extensionsJson = GetStr(parameters, "extensionsJson") ?? "[]";
        var iniJson = GetStr(parameters, "iniJson") ?? "{}";

        if (string.IsNullOrEmpty(poolName) || string.IsNullOrEmpty(username))
            return Fail("poolName and username required");

        if (!System.Text.RegularExpressions.Regex.IsMatch(poolName, @"^[a-zA-Z_][a-zA-Z0-9_]{0,31}$"))
            return Fail("Invalid pool name");

        var extensions = ParseExtensionList(extensionsJson);
        var iniOverrides = ParseIniDict(iniJson);

        // Build php_admin_value lines for every operator override. Validate keys
        // against an allowlist to refuse setting nonsense like `open_basedir`
        // (already managed) or `disable_functions` (already locked down).
        var iniLines = new List<string>();
        foreach (var (key, val) in iniOverrides)
        {
            if (!IsAllowedIniKey(key)) continue;
            // Quote the value in case it contains spaces (rare, but memory_limit can have units).
            iniLines.Add($"php_admin_value[{key}] = {val}");
        }

        var configPath = $"/etc/php/{phpVersion}/fpm/pool.d/{poolName}.conf";
        var config = $$"""
            [{{poolName}}]
            user = {{username}}
            group = {{username}}
            listen = /run/php/php{{phpVersion}}-fpm-{{poolName}}.sock
            listen.owner = www-data
            listen.group = www-data
            pm = dynamic
            pm.max_children = 10
            pm.start_servers = 2
            pm.min_spare_servers = 1
            pm.max_spare_servers = 3
            pm.max_requests = 500
            security.limit_extensions = .php
            php_admin_value[open_basedir] = /var/www/{{username}}:/tmp
            php_admin_value[disable_functions] = exec,passthru,shell_exec,system,proc_open,popen
            {{string.Join("\n", iniLines)}}
            """;

        // Ensure the target FPM pool dir exists (handles brand-new PHP version installs).
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, config, ct);

        var versionChanged = !string.IsNullOrEmpty(oldPhpVersion) && oldPhpVersion != phpVersion;
        var oldPath = versionChanged ? $"/etc/php/{oldPhpVersion}/fpm/pool.d/{poolName}.conf" : null;
        if (oldPath is not null && File.Exists(oldPath))
        {
            File.Delete(oldPath);
            _logger.LogInformation("Removed stale {OldPool} after version flip {Old}->{New}", oldPath, oldPhpVersion, phpVersion);
            await ProcessRunner.RunAsync("/usr/sbin/service", [$"php{oldPhpVersion}-fpm", "reload"], ct);
        }

        // Reload the NEW version's FPM so the new pool starts answering.
        var reload = await ProcessRunner.RunAsync("/usr/sbin/service", [$"php{phpVersion}-fpm", "reload"], ct);

        _logger.LogInformation("Updated PHP pool {Pool} version={Ver} extensions=[{Exts}] ini-overrides={IniCount}",
            poolName, phpVersion, string.Join(",", extensions), iniLines.Count);

        return new AgentResult
        {
            Success = reload.ExitCode == 0,
            Data = new()
            {
                ["configPath"] = configPath,
                ["versionChanged"] = versionChanged,
                ["socketPath"] = $"/run/php/php{phpVersion}-fpm-{poolName}.sock",
                ["enabledExtensions"] = extensions,
                ["iniOverridesApplied"] = iniLines.Count,
                ["reloadStderr"] = reload.StdErr
            }
        };
    }

    private static List<string> ParseExtensionList(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.ValueKind != JsonValueKind.Array) return [];
            return doc.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? "")
                .Where(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"^[a-z][a-z0-9-]{0,31}$"))
                .ToList();
        }
        catch { return []; }
    }

    private static Dictionary<string, string> ParseIniDict(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.ValueKind != JsonValueKind.Object) return new();
            return doc.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String || p.Value.ValueKind == JsonValueKind.Number)
                .ToDictionary(p => p.Name, p => p.Value.ToString());
        }
        catch { return new(); }
    }

    // Allowlist of php.ini keys the operator is allowed to set per-pool.
    // Keeps customers from disabling our security hardening (open_basedir,
    // disable_functions) or breaking the FPM pool with kernel-only tunables.
    private static readonly HashSet<string> AllowedIniKeys = new(StringComparer.Ordinal)
    {
        "memory_limit", "upload_max_filesize", "post_max_size", "max_execution_time",
        "max_input_time", "max_input_vars", "default_socket_timeout",
        "display_errors", "display_startup_errors", "error_reporting", "log_errors",
        "date.timezone", "session.gc_maxlifetime", "session.cookie_secure",
        "session.cookie_httponly", "session.cookie_samesite",
        "opcache.enable", "opcache.memory_consumption", "opcache.max_accelerated_files",
        "opcache.revalidate_freq", "opcache.validate_timestamps", "opcache.jit", "opcache.jit_buffer_size",
        "realpath_cache_size", "realpath_cache_ttl",
        "expose_php", "allow_url_fopen"
    };

    private static bool IsAllowedIniKey(string key) => AllowedIniKeys.Contains(key);

    private static AgentResult Fail(string msg) => new() { Success = false, Data = new() { ["error"] = msg } };
    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

/// <summary>
/// Phase C: list PHP versions installed on the box. Reads /etc/php/* directly
/// (one subdir per installed version per Debian convention) so we don't have
/// to shell out to dpkg + parse.
/// </summary>
public class PhpListVersionsHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.PhpListVersions;

    public Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        // A version is only "usable" if its fpm dir exists AND the fpm binary is on disk.
        // /etc/php/X.Y can be created by a partial install (e.g. apt pulling in php8.4-common
        // as a transitive dep) without -fpm — picking that version in the panel would 502.
        // Require both the pool.d directory and the FPM binary so the picker only shows
        // versions we can actually run.
        var versions = new List<string>();
        if (Directory.Exists("/etc/php"))
        {
            foreach (var dir in Directory.GetDirectories("/etc/php"))
            {
                var name = Path.GetFileName(dir);
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d+\.\d+$")) continue;
                if (!Directory.Exists($"/etc/php/{name}/fpm/pool.d")) continue;
                if (!File.Exists($"/usr/sbin/php-fpm{name}")) continue;
                versions.Add(name);
            }
        }
        versions.Sort();
        return Task.FromResult(new AgentResult
        {
            Success = true,
            Data = new() { ["versions"] = versions }
        });
    }
}

/// <summary>
/// Phase C: list available + enabled extensions for a given PHP version.
/// Reads /etc/php/{ver}/mods-available/*.ini for the catalog and
/// /etc/php/{ver}/fpm/conf.d/*.ini for the active set.
/// </summary>
public class PhpListExtensionsHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.PhpListExtensions;

    public Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var phpVersion = parameters.TryGetValue("phpVersion", out var v) ? v?.ToString() ?? "8.3" : "8.3";
        var availableDir = $"/etc/php/{phpVersion}/mods-available";
        var enabledDir = $"/etc/php/{phpVersion}/fpm/conf.d";
        var available = new List<string>();
        var enabled = new List<string>();

        if (Directory.Exists(availableDir))
        {
            foreach (var f in Directory.GetFiles(availableDir, "*.ini"))
                available.Add(Path.GetFileNameWithoutExtension(f));
            available.Sort();
        }
        if (Directory.Exists(enabledDir))
        {
            foreach (var f in Directory.GetFiles(enabledDir, "*.ini"))
            {
                // Format: NN-name.ini — strip the priority prefix.
                var name = Path.GetFileNameWithoutExtension(f);
                var dash = name.IndexOf('-');
                if (dash > 0 && dash < name.Length - 1) name = name[(dash + 1)..];
                enabled.Add(name);
            }
            enabled.Sort();
        }

        return Task.FromResult(new AgentResult
        {
            Success = true,
            Data = new()
            {
                ["phpVersion"] = phpVersion,
                ["available"] = available,
                ["enabled"] = enabled
            }
        });
    }
}
