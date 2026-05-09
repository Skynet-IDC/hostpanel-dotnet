using Microsoft.Extensions.Logging;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;

namespace HostPanel.Modules.Web;

public enum AppType { DotNet, NodeJs, Python }

public record HostedApp(int Id, string Name, AppType Type, string EntryPoint, int Port, bool IsRunning);

/// <summary>
/// Manages .NET, Node.js, and Python applications via systemd services.
/// </summary>
public class AppHostingService
{
    private readonly IServerAgent _agent;
    private readonly ILogger<AppHostingService> _logger;

    public AppHostingService(IServerAgent agent, ILogger<AppHostingService> logger)
    {
        _agent = agent;
        _logger = logger;
    }

    public string GenerateSystemdUnit(string appName, string username, string workDir, AppType type, string entryPoint, int port)
    {
        var execStart = type switch
        {
            AppType.DotNet => $"/usr/bin/dotnet {entryPoint}",
            AppType.NodeJs => $"/usr/bin/node {entryPoint}",
            AppType.Python => $"/usr/bin/python3 {entryPoint}",
            _ => throw new ArgumentException($"Unknown app type: {type}")
        };

        return $$"""
            [Unit]
            Description=HostPanel App: {{appName}}
            After=network.target

            [Service]
            Type=simple
            User={{username}}
            WorkingDirectory={{workDir}}
            ExecStart={{execStart}}
            Restart=always
            RestartSec=10
            Environment=ASPNETCORE_URLS=http://localhost:{{port}}
            Environment=PORT={{port}}
            Environment=NODE_ENV=production
            Environment=DOTNET_ENVIRONMENT=Production

            # Security
            NoNewPrivileges=true
            ProtectSystem=strict
            ProtectHome=true
            ReadWritePaths={{workDir}}

            [Install]
            WantedBy=multi-user.target
            """;
    }

    public string GenerateNginxReverseProxy(string domain, int port)
    {
        return $$"""
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
            """;
    }
}
