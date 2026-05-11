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
        var sslCertPath = GetString(parameters, "sslCertPath");
        var sslKeyPath = GetString(parameters, "sslKeyPath");

        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(docRoot))
            return new AgentResult { Success = false, Data = new() { ["error"] = "domain and docRoot are required" } };

        // Validate domain format
        if (!System.Text.RegularExpressions.Regex.IsMatch(domain, @"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*$"))
            return new AgentResult { Success = false, Data = new() { ["error"] = "Invalid domain format" } };

        // Validate docRoot is under /var/www/
        if (!docRoot.StartsWith("/var/www/"))
            return new AgentResult { Success = false, Data = new() { ["error"] = "docRoot must be under /var/www/" } };

        // SSL params: if one is provided both must be, and the files must exist + be readable by nginx
        var sslEnabled = !string.IsNullOrEmpty(sslCertPath) && !string.IsNullOrEmpty(sslKeyPath);
        if (sslEnabled && (!File.Exists(sslCertPath) || !File.Exists(sslKeyPath)))
            return new AgentResult { Success = false, Data = new() { ["error"] = $"SSL cert/key files not found: {sslCertPath} / {sslKeyPath}" } };

        var configPath = $"/etc/nginx/sites-available/{domain}.conf";
        var enabledPath = $"/etc/nginx/sites-enabled/{domain}.conf";

        var config = GenerateNginxConfig(domain, docRoot, phpVersion, sslCertPath, sslKeyPath);

        _logger.LogInformation("Creating Nginx vhost for {Domain} at {ConfigPath} (ssl={Ssl})", domain, configPath, sslEnabled);

        // Create document root
        Directory.CreateDirectory(docRoot);

        // Back up the existing config so we can roll back on `nginx -t` failure without losing
        // a previously-working vhost. (Without this, rewriting a working :80 vhost with a broken
        // SSL block would delete BOTH files in the catch path and take the site offline.)
        string? backupPath = null;
        if (File.Exists(configPath))
        {
            backupPath = $"{configPath}.bak.{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(configPath, backupPath, overwrite: true);
        }

        // Write Nginx config
        await File.WriteAllTextAsync(configPath, config, cancellationToken);

        // Create symlink to enable (idempotent — re-creates only if missing)
        if (!File.Exists(enabledPath))
            File.CreateSymbolicLink(enabledPath, configPath);

        // Test Nginx config
        var testResult = await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-t"], cancellationToken);
        if (testResult.ExitCode != 0)
        {
            // Rollback: restore previous config if we had one, otherwise remove both files
            if (backupPath is not null && File.Exists(backupPath))
            {
                File.Copy(backupPath, configPath, overwrite: true);
                File.Delete(backupPath);
            }
            else
            {
                if (File.Exists(enabledPath)) File.Delete(enabledPath);
                if (File.Exists(configPath)) File.Delete(configPath);
            }
            return new AgentResult { Success = false, Data = new() { ["error"] = $"Nginx config test failed: {testResult.StdErr}" } };
        }

        // Config valid — drop the backup
        if (backupPath is not null && File.Exists(backupPath))
            File.Delete(backupPath);

        // Reload Nginx
        await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-s", "reload"], cancellationToken);

        return new AgentResult
        {
            Success = true,
            Data = new()
            {
                ["configPath"] = configPath,
                ["docRoot"] = docRoot,
                ["sslEnabled"] = sslEnabled
            }
        };
    }

    private static string GenerateNginxConfig(string domain, string docRoot, string phpVersion, string? sslCertPath, string? sslKeyPath)
    {
        var phpSocket = $"/run/php/php{phpVersion}-fpm.sock";
        var sslEnabled = !string.IsNullOrEmpty(sslCertPath) && !string.IsNullOrEmpty(sslKeyPath);
        var sb = new System.Text.StringBuilder();

        // HTTP block — always present. When SSL is enabled it just handles
        // ACME challenges + redirects everything else to HTTPS.
        sb.AppendLine("server {");
        sb.AppendLine("    listen 80;");
        sb.AppendLine("    listen [::]:80;");
        sb.AppendLine($"    server_name {domain} www.{domain};");
        sb.AppendLine();
        sb.AppendLine("    # ACME HTTP-01 challenge — must stay on HTTP so LE renewals continue to work");
        sb.AppendLine("    location ^~ /.well-known/acme-challenge/ {");
        sb.AppendLine($"        root {docRoot};");
        sb.AppendLine("        default_type \"text/plain\";");
        sb.AppendLine("    }");
        sb.AppendLine();

        if (sslEnabled)
        {
            sb.AppendLine("    # All other traffic -> HTTPS");
            sb.AppendLine("    location / {");
            sb.AppendLine("        return 301 https://$host$request_uri;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();

            // HTTPS block. Use the legacy combined "listen ... ssl http2" form so this works
            // on nginx 1.18-1.24 (Ubuntu 22.04/24.04 default). nginx 1.25+ accepts it too but
            // logs a deprecation warning — the cleaner `http2 on;` syntax is 1.25-only.
            sb.AppendLine("server {");
            sb.AppendLine("    listen 443 ssl http2;");
            sb.AppendLine("    listen [::]:443 ssl http2;");
            sb.AppendLine($"    server_name {domain} www.{domain};");
            sb.AppendLine($"    root {docRoot};");
            sb.AppendLine("    index index.php index.html;");
            sb.AppendLine();
            sb.AppendLine($"    ssl_certificate {sslCertPath};");
            sb.AppendLine($"    ssl_certificate_key {sslKeyPath};");
            sb.AppendLine("    ssl_protocols TLSv1.2 TLSv1.3;");
            sb.AppendLine("    ssl_prefer_server_ciphers off;");
            sb.AppendLine();
            sb.AppendLine($"    access_log /var/log/nginx/{domain}.access.log;");
            sb.AppendLine($"    error_log /var/log/nginx/{domain}.error.log;");
            sb.AppendLine();
            sb.AppendLine("    # Security");
            sb.AppendLine("    disable_symlinks on;");
            sb.AppendLine();
            sb.AppendLine("    location / {");
            sb.AppendLine("        try_files $uri $uri/ /index.php?$query_string;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    location ~ \\.php$ {");
            sb.AppendLine($"        fastcgi_pass unix:{phpSocket};");
            sb.AppendLine("        fastcgi_param SCRIPT_FILENAME $realpath_root$fastcgi_script_name;");
            sb.AppendLine("        include fastcgi_params;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    location ~ /\\.ht {");
            sb.AppendLine("        deny all;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }
        else
        {
            // Plain HTTP serving (pre-SSL state)
            sb.AppendLine($"    root {docRoot};");
            sb.AppendLine("    index index.php index.html;");
            sb.AppendLine();
            sb.AppendLine($"    access_log /var/log/nginx/{domain}.access.log;");
            sb.AppendLine($"    error_log /var/log/nginx/{domain}.error.log;");
            sb.AppendLine();
            sb.AppendLine("    # Security");
            sb.AppendLine("    disable_symlinks on;");
            sb.AppendLine();
            sb.AppendLine("    location / {");
            sb.AppendLine("        try_files $uri $uri/ /index.php?$query_string;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    location ~ \\.php$ {");
            sb.AppendLine($"        fastcgi_pass unix:{phpSocket};");
            sb.AppendLine("        fastcgi_param SCRIPT_FILENAME $realpath_root$fastcgi_script_name;");
            sb.AppendLine("        include fastcgi_params;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    location ~ /\\.ht {");
            sb.AppendLine("        deny all;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }

        return sb.ToString();
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
