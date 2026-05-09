namespace HostPanel.Core.Models;

public class AppConfig : BaseEntity
{
    public int DomainId { get; set; }
    public Domain Domain { get; set; } = null!;
    public string Runtime { get; set; } = "nodejs";  // nodejs, dotnet, python, php, static
    public string? EntryPoint { get; set; }           // server.js, app.py, etc.
    public int Port { get; set; } = 3000;
    public string? NodeVersion { get; set; }          // 18, 20, 22
    public string RestartMethod { get; set; } = "systemd"; // systemd, passenger, pm2
    public string? BuildCommand { get; set; }         // npm run build, npx prisma migrate deploy && npm run build
    public string? StartCommand { get; set; }         // npm start, node server.js
    public string? EnvironmentVariables { get; set; } // JSON: {"NODE_ENV":"production","DATABASE_URL":"..."}
    public string? WorkingDirectory { get; set; }
    public bool AutoDeploy { get; set; }
    public string? StagingDomainId { get; set; }      // Linked staging domain
}
