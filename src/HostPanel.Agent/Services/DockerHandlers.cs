using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

public class DockerCreateHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.DockerCreate;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var image = GetStr(parameters, "image");
        var name = GetStr(parameters, "name");
        var ports = GetStr(parameters, "ports");
        var envVars = GetStr(parameters, "envVars");
        var volumes = GetStr(parameters, "volumes");

        if (string.IsNullOrEmpty(image) || string.IsNullOrEmpty(name))
            return Fail("image and name required");

        var args = new List<string> { "run", "-d", "--name", name, "--restart", "unless-stopped" };

        if (!string.IsNullOrEmpty(ports))
        {
            foreach (var mapping in ports.Split(','))
                args.AddRange(["-p", mapping.Trim()]);
        }

        if (!string.IsNullOrEmpty(envVars))
        {
            try
            {
                var envDict = JsonSerializer.Deserialize<Dictionary<string, string>>(envVars);
                if (envDict is not null)
                    foreach (var kv in envDict)
                        args.AddRange(["-e", $"{kv.Key}={kv.Value}"]);
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(volumes))
        {
            foreach (var vol in volumes.Split(','))
                args.AddRange(["-v", vol.Trim()]);
        }

        args.Add(image);

        var result = await ProcessRunner.RunAsync("/usr/bin/docker", args.ToArray(), ct, timeoutMs: 120_000);

        return new AgentResult
        {
            Success = result.ExitCode == 0,
            Data = new()
            {
                ["containerId"] = result.StdOut.Trim(),
                ["error"] = result.StdErr
            }
        };
    }

    private static AgentResult Fail(string msg) => new() { Success = false, Data = new() { ["error"] = msg } };
    private static string? GetStr(Dictionary<string, object?> p, string k) =>
        p.TryGetValue(k, out var v) ? v?.ToString() ?? (v is JsonElement je ? je.GetString() : null) : null;
}

public class DockerStartHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.DockerStart;
    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var id = parameters.TryGetValue("containerId", out var v) ? v?.ToString() : null;
        if (string.IsNullOrEmpty(id)) return new AgentResult { Success = false };
        var r = await ProcessRunner.RunAsync("/usr/bin/docker", ["start", id], ct);
        return new AgentResult { Success = r.ExitCode == 0, Data = new() { ["output"] = r.StdOut } };
    }
}

public class DockerStopHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.DockerStop;
    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var id = parameters.TryGetValue("containerId", out var v) ? v?.ToString() : null;
        if (string.IsNullOrEmpty(id)) return new AgentResult { Success = false };
        var r = await ProcessRunner.RunAsync("/usr/bin/docker", ["stop", id], ct);
        return new AgentResult { Success = r.ExitCode == 0, Data = new() { ["output"] = r.StdOut } };
    }
}

public class DockerRestartHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.DockerRestart;
    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var id = parameters.TryGetValue("containerId", out var v) ? v?.ToString() : null;
        if (string.IsNullOrEmpty(id)) return new AgentResult { Success = false };
        var r = await ProcessRunner.RunAsync("/usr/bin/docker", ["restart", id], ct);
        return new AgentResult { Success = r.ExitCode == 0, Data = new() { ["output"] = r.StdOut } };
    }
}

public class DockerRemoveHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.DockerRemove;
    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var id = parameters.TryGetValue("containerId", out var v) ? v?.ToString() : null;
        if (string.IsNullOrEmpty(id)) return new AgentResult { Success = false };
        await ProcessRunner.RunAsync("/usr/bin/docker", ["stop", id], ct);
        var r = await ProcessRunner.RunAsync("/usr/bin/docker", ["rm", id], ct);
        return new AgentResult { Success = r.ExitCode == 0, Data = new() { ["output"] = r.StdOut } };
    }
}
