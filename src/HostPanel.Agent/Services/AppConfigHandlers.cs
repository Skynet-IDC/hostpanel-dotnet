using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class AppConfigureHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.AppConfigure;
    private readonly ILogger<AppConfigureHandler> _logger;
    public AppConfigureHandler(ILogger<AppConfigureHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var domain = GetStr(parameters, "domain");
        var docRoot = GetStr(parameters, "docRoot");
        var username = GetStr(parameters, "username");
        var runtime = GetStr(parameters, "runtime") ?? "nodejs";
        var entryPoint = GetStr(parameters, "entryPoint") ?? "server.js";
        var port = int.TryParse(GetStr(parameters, "port"), out var p) ? p : 3000;
        var restartMethod = GetStr(parameters, "restartMethod") ?? "systemd";
        var startCommand = GetStr(parameters, "startCommand");
        var envVars = GetStr(parameters, "envVars");
        var workDir = GetStr(parameters, "workingDirectory") ?? docRoot;

        _logger.LogInformation("Configuring {Runtime} app for {Domain} on port {Port}, restart={Method}",
            runtime, domain, port, restartMethod);

        var serviceName = $"hostpanel-app-{domain!.Replace(".", "-")}";

        if (restartMethod == "systemd")
        {
            var execStart = runtime switch
            {
                "nodejs" => startCommand ?? $"/usr/bin/node {entryPoint}",
                "dotnet" => startCommand ?? $"/usr/bin/dotnet {entryPoint}",
                "python" => startCommand ?? $"/usr/bin/python3 {entryPoint}",
                _ => startCommand ?? $"/usr/bin/node {entryPoint}"
            };

            // Write .env file if provided
            if (!string.IsNullOrEmpty(envVars))
                await WriteEnvFile(workDir!, envVars);

            // Build environment lines for systemd
            var envLines = "";
            if (!string.IsNullOrEmpty(envVars))
            {
                try
                {
                    var envDict = JsonSerializer.Deserialize<Dictionary<string, string>>(envVars);
                    if (envDict is not null)
                        envLines = string.Join("\n", envDict.Select(kv => $"Environment={kv.Key}={kv.Value}"));
                }
                catch { }
            }

            var unit = $"""
                [Unit]
                Description=HostPanel App: {domain}
                After=network.target

                [Service]
                Type=simple
                User={username}
                WorkingDirectory={workDir}
                ExecStart={execStart}
                Restart=always
                RestartSec=10
                Environment=PORT={port}
                Environment=NODE_ENV=production
                {envLines}

                [Install]
                WantedBy=multi-user.target
                """;

            await File.WriteAllTextAsync($"/etc/systemd/system/{serviceName}.service", unit, ct);
            await ProcessRunner.RunAsync("/usr/bin/systemctl", ["daemon-reload"], ct);
            await ProcessRunner.RunAsync("/usr/bin/systemctl", ["enable", serviceName], ct);
            await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", serviceName], ct);
        }
        else if (restartMethod == "passenger")
        {
            // Phusion Passenger: just needs tmp/restart.txt
            Directory.CreateDirectory(Path.Combine(workDir!, "tmp"));
            // Write .env if provided
            if (!string.IsNullOrEmpty(envVars))
                await WriteEnvFile(workDir!, envVars);
        }
        else if (restartMethod == "pm2")
        {
            // PM2: use ecosystem config or direct start
            if (!string.IsNullOrEmpty(envVars))
                await WriteEnvFile(workDir!, envVars);

            await ProcessRunner.RunAsync("/usr/bin/pm2",
                ["start", entryPoint!, "--name", domain!, "--cwd", workDir!,
                 "-i", "max", "--update-env"], ct);
            await ProcessRunner.RunAsync("/usr/bin/pm2", ["save"], ct);
        }

        // Configure Nginx reverse proxy
        var nginxConf = $$"""
            server {
                listen 80;
                listen [::]:80;
                server_name {{domain}};
                root {{docRoot}};

                location / {
                    proxy_pass http://127.0.0.1:{{port}};
                    proxy_http_version 1.1;
                    proxy_set_header Upgrade $http_upgrade;
                    proxy_set_header Connection "upgrade";
                    proxy_set_header Host $host;
                    proxy_set_header X-Real-IP $remote_addr;
                    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                    proxy_set_header X-Forwarded-Proto $scheme;
                    proxy_cache_bypass $http_upgrade;
                }

                location /_next/static {
                    alias {{docRoot}}/.next/static;
                    expires 365d;
                    access_log off;
                }

                location /public {
                    alias {{docRoot}}/public;
                    expires 30d;
                }
            }
            """;

        await File.WriteAllTextAsync($"/etc/nginx/sites-available/{domain}.conf", nginxConf, ct);
        if (!File.Exists($"/etc/nginx/sites-enabled/{domain}.conf"))
            File.CreateSymbolicLink($"/etc/nginx/sites-enabled/{domain}.conf", $"/etc/nginx/sites-available/{domain}.conf");

        var nginxTest = await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-t"], ct);
        if (nginxTest.ExitCode == 0)
            await ProcessRunner.RunAsync("/usr/sbin/nginx", ["-s", "reload"], ct);

        return new AgentResult
        {
            Success = true,
            Data = new() { ["serviceName"] = serviceName, ["port"] = port, ["restartMethod"] = restartMethod }
        };
    }

    private static async Task WriteEnvFile(string workDir, string envVarsJson)
    {
        try
        {
            var envDict = JsonSerializer.Deserialize<Dictionary<string, string>>(envVarsJson);
            if (envDict is not null)
            {
                var envContent = string.Join("\n", envDict.Select(kv => $"{kv.Key}={kv.Value}"));
                await File.WriteAllTextAsync(Path.Combine(workDir, ".env"), envContent);
            }
        }
        catch { }
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class AppBuildHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.AppBuild;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var workDir = GetStr(parameters, "workingDirectory");
        var buildCommand = GetStr(parameters, "buildCommand");
        var username = GetStr(parameters, "username");
        var envVars = GetStr(parameters, "envVars");

        if (string.IsNullOrEmpty(workDir) || string.IsNullOrEmpty(buildCommand))
            return new AgentResult { Success = false, Data = new() { ["error"] = "workingDirectory and buildCommand required" } };

        // Write .env before build
        if (!string.IsNullOrEmpty(envVars))
        {
            try
            {
                var envDict = JsonSerializer.Deserialize<Dictionary<string, string>>(envVars);
                if (envDict is not null)
                {
                    var envContent = string.Join("\n", envDict.Select(kv => $"{kv.Key}={kv.Value}"));
                    await File.WriteAllTextAsync(Path.Combine(workDir, ".env"), envContent, ct);
                }
            }
            catch { }
        }

        // Install dependencies first
        if (File.Exists(Path.Combine(workDir, "package.json")))
        {
            var install = await ProcessRunner.RunAsync("/usr/bin/npm",
                ["ci", "--prefix", workDir], ct, timeoutMs: 300_000);
            if (install.ExitCode != 0)
                return new AgentResult { Success = false, Data = new() { ["error"] = $"npm ci failed: {install.StdErr}" } };
        }

        // Run build command (could be multi-step: "npx prisma migrate deploy && npm run build")
        var commands = buildCommand.Split("&&").Select(c => c.Trim()).ToList();
        var allOutput = new List<string>();

        foreach (var cmd in commands)
        {
            var parts = cmd.Split(' ', 2);
            var binary = parts[0];
            var args = parts.Length > 1 ? parts[1] : "";

            // Resolve common commands
            var fullBinary = binary switch
            {
                "npm" => "/usr/bin/npm",
                "npx" => "/usr/bin/npx",
                "node" => "/usr/bin/node",
                "prisma" => "/usr/bin/npx",
                _ => $"/usr/bin/{binary}"
            };

            if (binary == "prisma")
                args = $"prisma {args}";

            var result = await ProcessRunner.RunAsync(fullBinary,
                args.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                ct, timeoutMs: 600_000);

            allOutput.Add($"$ {cmd}\n{result.StdOut}{result.StdErr}");

            if (result.ExitCode != 0)
                return new AgentResult
                {
                    Success = false,
                    Data = new() { ["error"] = $"Command failed: {cmd}", ["output"] = string.Join("\n---\n", allOutput) }
                };
        }

        // Fix ownership
        if (!string.IsNullOrEmpty(username))
            await ProcessRunner.RunAsync("/bin/chown", ["-R", $"{username}:{username}", workDir], ct);

        return new AgentResult
        {
            Success = true,
            Data = new() { ["output"] = string.Join("\n---\n", allOutput) }
        };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class AppRestartHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.AppRestart;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var domain = GetStr(parameters, "domain");
        var restartMethod = GetStr(parameters, "restartMethod") ?? "systemd";
        var workDir = GetStr(parameters, "workingDirectory");

        ProcessResult result;

        switch (restartMethod)
        {
            case "passenger":
                // Phusion Passenger restart
                var tmpDir = Path.Combine(workDir!, "tmp");
                Directory.CreateDirectory(tmpDir);
                await File.WriteAllTextAsync(Path.Combine(tmpDir, "restart.txt"), "", ct);
                return new AgentResult { Success = true, Data = new() { ["method"] = "passenger", ["action"] = "touch tmp/restart.txt" } };

            case "pm2":
                result = await ProcessRunner.RunAsync("/usr/bin/pm2", ["restart", domain!], ct);
                return new AgentResult { Success = result.ExitCode == 0, Data = new() { ["method"] = "pm2", ["output"] = result.StdOut } };

            case "systemd":
            default:
                var serviceName = $"hostpanel-app-{domain!.Replace(".", "-")}";
                result = await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", serviceName], ct);
                return new AgentResult { Success = result.ExitCode == 0, Data = new() { ["method"] = "systemd", ["output"] = result.StdOut } };
        }
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class AppWriteEnvHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.AppWriteEnv;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var workDir = GetStr(parameters, "workingDirectory");
        var envVars = GetStr(parameters, "envVars");

        if (string.IsNullOrEmpty(workDir))
            return new AgentResult { Success = false, Data = new() { ["error"] = "workingDirectory required" } };

        try
        {
            var envDict = JsonSerializer.Deserialize<Dictionary<string, string>>(envVars ?? "{}");
            if (envDict is not null)
            {
                var content = string.Join("\n", envDict.Select(kv => $"{kv.Key}={kv.Value}"));
                await File.WriteAllTextAsync(Path.Combine(workDir, ".env"), content, ct);
            }
        }
        catch (Exception ex)
        {
            return new AgentResult { Success = false, Data = new() { ["error"] = ex.Message } };
        }

        return new AgentResult { Success = true };
    }

    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}
