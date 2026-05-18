using System.IO.Compression;
using System.Text.Json;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

/// <summary>
/// Scans nginx access logs for the given hostnames within a date window and sums bytes_sent.
/// Requires the nginx log_format to include `host=$host` (installer ships that for new installs;
/// older installs will return 0 — the API surface reports `bandwidthTracked: false` in that case).
/// Reads `/var/log/nginx/access.log` plus any rotated `.1`/`.2.gz` siblings touched within the window.
/// </summary>
public class SystemGetBandwidthHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.SystemGetBandwidth;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var hostnames = ExtractHostnames(parameters);
        var since = ExtractDate(parameters, "since") ?? DateTime.UtcNow.AddMonths(-1);

        if (hostnames.Count == 0)
            return new AgentResult { Success = true, Data = new Dictionary<string, object?> { ["bytes"] = 0L } };

        var bytes = 0L;
        try
        {
            // Prefer per-vhost log files (`/var/log/nginx/<host>.access.log*`) — set by the
            // panel's nginx vhost template. Falls back to scanning the global access.log if a
            // per-vhost file isn't present for a hostname (e.g. installs prior to log_format ship).
            foreach (var host in hostnames)
            {
                ct.ThrowIfCancellationRequested();
                var perHost = EnumeratePerHostLogs(host, since).ToList();
                if (perHost.Count > 0)
                {
                    foreach (var p in perHost)
                        bytes += await SumBytesPerHostLogAsync(p, ct);
                }
            }
            // Whatever is left, try to find in the global log filtered by host= field.
            foreach (var path in EnumerateLogFiles(since))
            {
                ct.ThrowIfCancellationRequested();
                bytes += await SumBytesAsync(path, hostnames, since, ct);
            }
            return new AgentResult { Success = true, Data = new Dictionary<string, object?> { ["bytes"] = bytes } };
        }
        catch (Exception ex)
        {
            return new AgentResult
            {
                Success = false,
                Data = new Dictionary<string, object?>
                {
                    ["bytes"] = bytes,
                    ["error"] = ex.Message,
                }
            };
        }
    }

    private static HashSet<string> ExtractHostnames(Dictionary<string, object?> p)
    {
        if (!p.TryGetValue("hostnames", out var raw) || raw is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (raw is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in je.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s)
                    set.Add(s);
            }
            return set;
        }

        if (raw is IEnumerable<object?> objs)
        {
            return new HashSet<string>(objs.OfType<string>(), StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static DateTime? ExtractDate(Dictionary<string, object?> p, string key)
    {
        if (!p.TryGetValue(key, out var raw) || raw is null) return null;
        var s = raw is JsonElement je ? je.GetString() : raw.ToString();
        return DateTime.TryParse(s, out var d) ? d.ToUniversalTime() : null;
    }

    private static IEnumerable<string> EnumeratePerHostLogs(string host, DateTime since)
    {
        const string dir = "/var/log/nginx";
        var primary = Path.Combine(dir, $"{host}.access.log");
        if (File.Exists(primary)) yield return primary;
        var candidates = new List<string>();
        try { candidates.AddRange(Directory.EnumerateFiles(dir, $"{host}.access.log.*")); } catch { /* unreadable */ }
        foreach (var f in candidates)
        {
            DateTime? when = null;
            try { when = new FileInfo(f).LastWriteTimeUtc; } catch { /* skip */ }
            if (when is not null && when >= since.AddDays(-1)) yield return f;
        }
    }

    private static async Task<long> SumBytesPerHostLogAsync(string path, CancellationToken ct)
    {
        Stream raw;
        try { raw = File.OpenRead(path); } catch (UnauthorizedAccessException) { return 0; }
        Stream stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(raw, CompressionMode.Decompress)
            : raw;
        long total = 0;
        try
        {
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
                total += ExtractBytesSent(line);
        }
        finally { stream.Dispose(); }
        return total;
    }

    private static IEnumerable<string> EnumerateLogFiles(DateTime since)
    {
        const string dir = "/var/log/nginx";
        if (!Directory.Exists(dir)) yield break;

        // Plain access.log (current — always include)
        var current = Path.Combine(dir, "access.log");
        if (File.Exists(current)) yield return current;

        // Rotated: access.log.1, access.log.2.gz, ... (filter inside, yield outside the try)
        var candidates = new List<string>();
        try { candidates.AddRange(Directory.EnumerateFiles(dir, "access.log.*")); } catch { /* unreadable dir */ }
        foreach (var f in candidates)
        {
            DateTime? when = null;
            try { when = new FileInfo(f).LastWriteTimeUtc; } catch { /* skip */ }
            if (when is not null && when >= since.AddDays(-1)) yield return f;
        }
    }

    private static async Task<long> SumBytesAsync(string path, HashSet<string> hostnames, DateTime since, CancellationToken ct)
    {
        Stream raw;
        try
        {
            raw = File.OpenRead(path);
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        Stream stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(raw, CompressionMode.Decompress)
            : raw;

        long total = 0;
        try
        {
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                // Custom log_format extension: lines end with `host=<hostname>` (no quoting).
                var hostIdx = line.LastIndexOf("host=", StringComparison.Ordinal);
                if (hostIdx < 0) continue;
                var host = line[(hostIdx + 5)..].Trim();
                if (!hostnames.Contains(host)) continue;

                // Combined log columns; bytes_sent is the 10th whitespace-delimited token,
                // but quoted strings complicate that. Fall back to: find `" 200 1234 "` pattern
                // — between `status` and `bytes_sent`. Walk backwards from the second `"` after
                // the closing of `"$request"` to locate the size token.
                var size = ExtractBytesSent(line);
                if (size > 0) total += size;
            }
        }
        finally
        {
            stream.Dispose();
        }
        return total;
    }

    /// <summary>
    /// Extract bytes_sent from a combined-log-format line. After the closing quote of the request
    /// field, the next two tokens are `status` and `bytes_sent`.
    /// </summary>
    private static long ExtractBytesSent(string line)
    {
        // Find the request quote close: "GET /foo HTTP/1.1" — locate index of the matching close.
        var firstQuote = line.IndexOf('"');
        if (firstQuote < 0) return 0;
        var closeQuote = line.IndexOf('"', firstQuote + 1);
        if (closeQuote < 0 || closeQuote + 2 >= line.Length) return 0;

        var rest = line[(closeQuote + 2)..];
        var sp = rest.IndexOf(' ');
        if (sp < 0) return 0;
        // rest[0..sp] = status
        var afterStatus = rest[(sp + 1)..];
        var sp2 = afterStatus.IndexOf(' ');
        var tok = sp2 < 0 ? afterStatus : afterStatus[..sp2];
        return long.TryParse(tok, out var n) ? n : 0;
    }
}
