using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class SslIssueCertHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SslIssueCertificate;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var domain = GetStr(parameters, "domain");
        var webRoot = GetStr(parameters, "webRoot");
        var certDir = GetStr(parameters, "certDir") ?? "/etc/hostpanel/ssl";

        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(webRoot))
            return Fail("domain and webRoot required");

        Directory.CreateDirectory(Path.Combine(certDir, domain));

        // Use certbot for ACME (if installed), else the API's Certes library handles it
        var result = await ProcessRunner.RunAsync("/usr/bin/certbot",
            ["certonly", "--webroot", "-w", webRoot, "-d", domain,
             "--cert-path", $"{certDir}/{domain}/fullchain.pem",
             "--key-path", $"{certDir}/{domain}/privkey.pem",
             "--non-interactive", "--agree-tos", "--register-unsafely-without-email"],
            ct, timeoutMs: 120_000);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new()
            {
                ["certPath"] = $"{certDir}/{domain}/fullchain.pem",
                ["keyPath"] = $"{certDir}/{domain}/privkey.pem",
                ["output"] = result.StdOut,
                ["error"] = result.StdErr
            }
        };
    }

    private static AgentResult Fail(string msg) => new() { Success = false, Data = new() { ["error"] = msg } };
    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class SslRenewCertHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SslRenewCertificate;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("/usr/bin/certbot",
            ["renew", "--non-interactive"], ct, timeoutMs: 300_000);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new() { ["output"] = result.StdOut, ["error"] = result.StdErr }
        };
    }
}

public class SslRevokeCertHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SslRevokeCertificate;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var certPath = parameters.TryGetValue("certPath", out var c) ? c?.ToString() : null;
        if (string.IsNullOrEmpty(certPath) || !certPath.StartsWith("/etc/hostpanel/ssl/"))
            return new AgentResult { Success = false, Data = new() { ["error"] = "Invalid certPath" } };

        var result = await ProcessRunner.RunAsync("/usr/bin/certbot",
            ["revoke", "--cert-path", certPath, "--non-interactive"], ct);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new() { ["output"] = result.StdOut }
        };
    }
}
