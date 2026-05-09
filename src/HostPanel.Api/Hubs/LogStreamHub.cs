using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;

namespace HostPanel.Api.Hubs;

[Authorize]
public class LogStreamHub : Hub
{
    private readonly IServerAgent _agent;

    public LogStreamHub(IServerAgent agent) => _agent = agent;

    public async IAsyncEnumerable<string> StreamLog(string logPath, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Validate path
        var allowedPrefixes = new[] { "/var/log/nginx/", "/var/log/mail", "/var/log/hostpanel/" };
        if (!allowedPrefixes.Any(p => logPath.StartsWith(p)))
        {
            yield return "ERROR: Path not allowed";
            yield break;
        }

        var lastLineCount = 0;

        while (!ct.IsCancellationRequested)
        {
            var result = await _agent.ExecuteAsync(new AgentRequest
            {
                Method = AgentMethods.SystemReadLog,
                Params = new Dictionary<string, object?> { ["path"] = logPath, ["lines"] = 50 }
            }, ct);

            if (result.Result?.Data?.TryGetValue("lines", out var linesObj) == true)
            {
                var lines = System.Text.Json.JsonSerializer.Deserialize<List<string>>(linesObj?.ToString() ?? "[]") ?? [];

                // Only send new lines
                if (lines.Count > lastLineCount)
                {
                    foreach (var line in lines.Skip(lastLineCount))
                        yield return line;
                    lastLineCount = lines.Count;
                }
            }

            await Task.Delay(2000, ct);
        }
    }
}
