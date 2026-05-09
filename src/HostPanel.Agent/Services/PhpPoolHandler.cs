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
