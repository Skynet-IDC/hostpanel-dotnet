using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class NginxCreateVhostHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.NginxCreateVhost;
    private readonly ILogger<NginxCreateVhostHandler> _logger;

    public NginxCreateVhostHandler(ILogger<NginxCreateVhostHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var domain = GetString(parameters, "domain");
        var docRoot = GetString(parameters, "docRoot");
        var phpVersion = GetString(parameters, "phpVersion") ?? "8.3";

        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(docRoot))
            return new AgentResult { Success = false, Data = new() { ["error"] = "domain and docRoot are required" } };

        // Validate domain format
        if (!System.Text.RegularExpressions.Regex.IsMatch(domain, @"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*$"))
            return new AgentResult { Success = false, Data = new() { ["error"] = "Invalid domain format" } };

        // Validate docRoot is under /var/www/
        if (!docRoot.StartsWith("/var/www/"))
            return new AgentResult { Success = false, Data = new() { ["error"] = "docRoot must be under /var/www/" } };

        var configPath = $"/etc/nginx/sites-available/{domain}.conf";
        var enabledPath = $"/etc/nginx/sites-enabled/{domain}.conf";

        var config = GenerateNginxConfig(domain, docRoot, phpVersion);

        _logger.LogInformation("Creating Nginx vhost for {Domain} at {ConfigPath}", domain, configPath);

        // Create document root
        Directory.CreateDirectory(docRoot);

        // Write Nginx config
        await File.WriteAllTextAsync(configPath, config, cancellationToken);

        // Create symlink to enable
        if (!File.Exists(enabledPath))
            File.CreateSymbolicLink(enabledPath, configPath);

        // Test Nginx config
        var testResult = await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-t"], cancellationToken);
        if (testResult.ExitCode != 0)
        {
            // Rollback
            File.Delete(enabledPath);
            File.Delete(configPath);
            return new AgentResult { Success = false, Data = new() { ["error"] = $"Nginx config test failed: {testResult.StdErr}" } };
        }

        // Reload Nginx
        await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-s", "reload"], cancellationToken);

        return new AgentResult
        {
            Success = true,
            Data = new()
            {
                ["configPath"] = configPath,
                ["docRoot"] = docRoot
            }
        };
    }

    private static string GenerateNginxConfig(string domain, string docRoot, string phpVersion)
    {
        var phpSocket = $"/run/php/php{phpVersion}-fpm.sock";
        return $$"""
            server {
                listen 80;
                listen [::]:80;
                server_name {{domain}} www.{{domain}};
                root {{docRoot}};
                index index.php index.html;

                access_log /var/log/nginx/{{domain}}.access.log;
                error_log /var/log/nginx/{{domain}}.error.log;

                # Security
                disable_symlinks on;

                location / {
                    try_files $uri $uri/ /index.php?$query_string;
                }

                location ~ \.php$ {
                    fastcgi_pass unix:{{phpSocket}};
                    fastcgi_param SCRIPT_FILENAME $realpath_root$fastcgi_script_name;
                    include fastcgi_params;
                }

                location ~ /\.ht {
                    deny all;
                }
            }
            """;
    }

    private static string? GetString(Dictionary<string, object?> p, string key) =>
        p.TryGetValue(key, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class NginxDeleteVhostHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.NginxDeleteVhost;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var domain = parameters.TryGetValue("domain", out var v) ? v?.ToString() : null;
        if (string.IsNullOrEmpty(domain))
            return new AgentResult { Success = false, Data = new() { ["error"] = "domain is required" } };

        var configPath = $"/etc/nginx/sites-available/{domain}.conf";
        var enabledPath = $"/etc/nginx/sites-enabled/{domain}.conf";

        if (File.Exists(enabledPath)) File.Delete(enabledPath);
        if (File.Exists(configPath)) File.Delete(configPath);

        await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-s", "reload"], cancellationToken);

        return new AgentResult { Success = true };
    }
}

public class NginxReloadHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.NginxReload;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-s", "reload"], cancellationToken);
        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new() { ["output"] = result.StdOut, ["error"] = result.StdErr }
        };
    }
}
