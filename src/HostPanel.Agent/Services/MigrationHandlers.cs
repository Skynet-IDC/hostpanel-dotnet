using System.Text.Json;
using System.Text.RegularExpressions;
using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

/// <summary>
/// Plesk → HostPanel migration handlers. Run from VM 280 (HostPanel host).
/// Each handler shells out to ssh/rsync/mysql/imapsync with strict argv arrays.
/// SSH key path is supplied per-call so future customers don't share the key.
/// </summary>
internal static class MigParam
{
    public static string Str(Dictionary<string, object?> p, string key, string fallback = "")
    {
        if (!p.TryGetValue(key, out var raw) || raw is null) return fallback;
        if (raw is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? fallback;
        return raw.ToString() ?? fallback;
    }

    public static int Int(Dictionary<string, object?> p, string key, int fallback)
    {
        if (!p.TryGetValue(key, out var raw) || raw is null) return fallback;
        if (raw is JsonElement je && je.TryGetInt32(out var n)) return n;
        return int.TryParse(raw.ToString(), out var parsed) ? parsed : fallback;
    }

    public static string[] SshBaseArgs(Dictionary<string, object?> p) => new[]
    {
        "-i", Str(p, "sshKeyPath", "/etc/hostpanel/plesk-migration-key"),
        "-p", Int(p, "sshPort", 22).ToString(),
        "-o", "BatchMode=yes",
        "-o", "StrictHostKeyChecking=accept-new",
        "-o", "ConnectTimeout=10",
        $"{Str(p, "sshUser", "root")}@{Str(p, "sshHost")}"
    };
}

/// <summary>Rsync remote /var/www/vhosts/&lt;domain&gt;/httpdocs/ → local target path.</summary>
public class MigrationRsyncFromPleskHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.MigrationRsyncFromPlesk;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> p, CancellationToken ct)
    {
        var sshHost = MigParam.Str(p, "sshHost");
        var sshUser = MigParam.Str(p, "sshUser", "root");
        var sshKey = MigParam.Str(p, "sshKeyPath", "/etc/hostpanel/plesk-migration-key");
        var sshPort = MigParam.Int(p, "sshPort", 22);
        var remotePath = MigParam.Str(p, "remotePath");
        var localPath = MigParam.Str(p, "localPath");
        var chownUser = MigParam.Str(p, "chownUser");

        if (string.IsNullOrEmpty(sshHost) || string.IsNullOrEmpty(remotePath) || string.IsNullOrEmpty(localPath))
            return new AgentResult { Success = false, Data = new() { ["error"] = "sshHost, remotePath, localPath required" } };

        Directory.CreateDirectory(localPath);

        var rsyncArgs = new[]
        {
            "-a", "--delete", "--numeric-ids",
            "-e", $"ssh -i {sshKey} -p {sshPort} -o BatchMode=yes -o StrictHostKeyChecking=accept-new",
            "--rsync-path=sudo rsync",
            $"{sshUser}@{sshHost}:{remotePath}",
            localPath
        };

        var rsync = await ProcessRunner.RunAsync("rsync", rsyncArgs, ct, timeoutMs: 30 * 60 * 1000);
        var data = new Dictionary<string, object?>
        {
            ["exitCode"] = rsync.ExitCode,
            ["stdoutTail"] = Tail(rsync.StdOut, 2000),
            ["stderrTail"] = Tail(rsync.StdErr, 2000)
        };

        if (rsync.ExitCode != 0)
        {
            data["error"] = $"rsync exit {rsync.ExitCode}";
            return new AgentResult { Success = false, Data = data };
        }

        if (!string.IsNullOrEmpty(chownUser) && IsSafeUsername(chownUser))
        {
            var chown = await ProcessRunner.RunAsync("chown",
                new[] { "-R", $"{chownUser}:{chownUser}", localPath },
                ct, timeoutMs: 5 * 60 * 1000);
            data["chownExit"] = chown.ExitCode;
            if (chown.ExitCode != 0) data["chownStderr"] = Tail(chown.StdErr, 500);
        }

        return new AgentResult { Success = true, Data = data };
    }

    private static bool IsSafeUsername(string s) => Regex.IsMatch(s, "^[a-z0-9_-]+$");
    private static string Tail(string s, int n) => s.Length <= n ? s : s[^n..];
}

/// <summary>Remote mysqldump piped to local mysql restore. Creates target DB + user if missing.</summary>
public class MigrationMysqldumpRemoteHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.MigrationMysqldumpRemote;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> p, CancellationToken ct)
    {
        var sshArgs = MigParam.SshBaseArgs(p);
        var srcDbName = MigParam.Str(p, "sourceDbName");
        var srcDbUser = MigParam.Str(p, "sourceDbUser");
        var srcDbPass = MigParam.Str(p, "sourceDbPass");
        var tgtDbName = MigParam.Str(p, "targetDbName");
        var tgtDbUser = MigParam.Str(p, "targetDbUser");

        if (string.IsNullOrEmpty(srcDbName) || string.IsNullOrEmpty(tgtDbName))
            return Fail("sourceDbName and targetDbName required");

        // Step 1: dump remote → temp file (use unique name to avoid TOCTOU)
        var dumpFile = $"/tmp/hp-migration-{Guid.NewGuid():N}.sql";

        // Build ssh + mysqldump command argv. Single-shot exec.
        var dumpCmd = $"mysqldump --no-tablespaces --single-transaction -u {Esc(srcDbUser)} -p{Esc(srcDbPass)} {Esc(srcDbName)}";
        var sshDumpArgs = sshArgs.Concat(new[] { dumpCmd }).ToArray();

        try
        {
            // Redirect stdout to dumpFile via bash
            var pipe = await RunWithStdoutToFileAsync("ssh", sshDumpArgs, dumpFile, ct, 60 * 60 * 1000);
            if (pipe.ExitCode != 0)
            {
                File.Delete(dumpFile);
                return Fail($"mysqldump failed: {pipe.StdErr[^Math.Min(500, pipe.StdErr.Length)..]}");
            }

            var sz = new FileInfo(dumpFile).Length;
            if (sz < 100) return Fail($"Dump file suspiciously small ({sz} bytes)");

            // Step 2: create local target DB + user
            var randomPass = Guid.NewGuid().ToString("N");
            var setupSql = $@"CREATE DATABASE IF NOT EXISTS `{tgtDbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '{tgtDbUser}'@'localhost' IDENTIFIED BY '{randomPass}';
ALTER USER '{tgtDbUser}'@'localhost' IDENTIFIED BY '{randomPass}';
GRANT ALL ON `{tgtDbName}`.* TO '{tgtDbUser}'@'localhost';
FLUSH PRIVILEGES;";

            var setup = await ProcessRunner.RunAsync("mysql",
                new[] { "--protocol=socket", "-e", setupSql },
                ct, timeoutMs: 30_000);
            if (setup.ExitCode != 0) return Fail($"Target DB setup failed: {setup.StdErr}");

            // Step 3: restore
            var restore = await RunWithStdinFromFileAsync("mysql",
                new[] { tgtDbName }, dumpFile, ct, 60 * 60 * 1000);
            if (restore.ExitCode != 0) return Fail($"Restore failed: {restore.StdErr}");

            return new AgentResult
            {
                Success = true,
                Data = new()
                {
                    ["dumpBytes"] = sz,
                    ["targetDbName"] = tgtDbName,
                    ["targetDbUser"] = tgtDbUser,
                    ["targetDbPass"] = randomPass
                }
            };
        }
        finally
        {
            try { File.Delete(dumpFile); } catch { }
        }
    }

    private static string Esc(string s) =>
        Regex.IsMatch(s, "^[A-Za-z0-9_./@-]+$") ? s : "'" + s.Replace("'", "'\"'\"'") + "'";

    private static AgentResult Fail(string msg) => new()
    { Success = false, Data = new() { ["error"] = msg } };

    private static async Task<ProcessResult> RunWithStdoutToFileAsync(
        string fileName, string[] args, string outPath, CancellationToken ct, int timeoutMs)
    {
        using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var a in args) process.StartInfo.ArgumentList.Add(a);
        process.Start();

        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(fs, ct);
        var errTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct);
            await copyTask;
        }
        catch (TimeoutException)
        {
            try { process.Kill(true); } catch { }
            return new ProcessResult(-1, "", $"timed out after {timeoutMs}ms");
        }
        return new ProcessResult(process.ExitCode, "", await errTask);
    }

    private static async Task<ProcessResult> RunWithStdinFromFileAsync(
        string fileName, string[] args, string inPath, CancellationToken ct, int timeoutMs)
    {
        using var fs = new FileStream(inPath, FileMode.Open, FileAccess.Read);
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var a in args) process.StartInfo.ArgumentList.Add(a);
        process.Start();

        var feedTask = Task.Run(async () =>
        {
            await fs.CopyToAsync(process.StandardInput.BaseStream, ct);
            process.StandardInput.Close();
        }, ct);

        var outTask = process.StandardOutput.ReadToEndAsync(ct);
        var errTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct);
            await feedTask;
        }
        catch (TimeoutException)
        {
            try { process.Kill(true); } catch { }
            return new ProcessResult(-1, "", $"timed out after {timeoutMs}ms");
        }
        return new ProcessResult(process.ExitCode, await outTask, await errTask);
    }
}

/// <summary>imapsync from source IMAP to local Dovecot. Requires post-reset passwords on both ends.</summary>
public class MigrationImapsyncHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.MigrationImapsync;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> p, CancellationToken ct)
    {
        var srcHost = MigParam.Str(p, "sourceHost");
        var srcUser = MigParam.Str(p, "sourceUser");
        var srcPass = MigParam.Str(p, "sourcePassword");
        var dstUser = MigParam.Str(p, "targetUser");
        var dstPass = MigParam.Str(p, "targetPassword");
        var dstHost = MigParam.Str(p, "targetHost", "localhost");

        if (string.IsNullOrEmpty(srcHost) || string.IsNullOrEmpty(srcUser) ||
            string.IsNullOrEmpty(dstUser))
            return new AgentResult { Success = false, Data = new() { ["error"] = "source/target host+user required" } };

        var args = new[]
        {
            "--host1", srcHost,
            "--user1", srcUser,
            "--password1", srcPass,
            "--ssl1",
            "--host2", dstHost,
            "--user2", dstUser,
            "--password2", dstPass,
            "--automap",
            "--no-modulesversion",
            "--nofoldersizes",
            "--syncacls"
        };

        var r = await ProcessRunner.RunAsync("imapsync", args, ct, timeoutMs: 60 * 60 * 1000);
        return new AgentResult
        {
            Success = r.ExitCode == 0,
            Data = new()
            {
                ["exitCode"] = r.ExitCode,
                ["stdoutTail"] = r.StdOut.Length > 2000 ? r.StdOut[^2000..] : r.StdOut,
                ["stderrTail"] = r.StdErr.Length > 2000 ? r.StdErr[^2000..] : r.StdErr
            }
        };
    }
}

/// <summary>Detect WordPress under a local docroot and parse DB credentials from wp-config.php.</summary>
public class MigrationDetectWordPressHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.MigrationDetectWordPress;

    public Task<AgentResult> HandleAsync(Dictionary<string, object?> p, CancellationToken ct)
    {
        var docroot = MigParam.Str(p, "docroot");
        if (string.IsNullOrEmpty(docroot) || !Directory.Exists(docroot))
            return Task.FromResult(new AgentResult { Success = false, Data = new() { ["error"] = "docroot missing" } });

        var configPath = Path.Combine(docroot, "wp-config.php");
        if (!File.Exists(configPath))
            return Task.FromResult(new AgentResult { Success = true, Data = new() { ["isWordPress"] = false } });

        string content;
        try { content = File.ReadAllText(configPath); }
        catch (Exception ex)
        {
            return Task.FromResult(new AgentResult { Success = false, Data = new() { ["error"] = $"read failed: {ex.Message}" } });
        }

        var data = new Dictionary<string, object?>
        {
            ["isWordPress"] = true,
            ["wpConfigPath"] = configPath,
            ["dbName"] = ExtractDefine(content, "DB_NAME"),
            ["dbUser"] = ExtractDefine(content, "DB_USER"),
            ["dbPass"] = ExtractDefine(content, "DB_PASSWORD"),
            ["dbHost"] = ExtractDefine(content, "DB_HOST"),
            ["tablePrefix"] = ExtractTablePrefix(content)
        };
        return Task.FromResult(new AgentResult { Success = true, Data = data });
    }

    private static string ExtractDefine(string content, string name)
    {
        var rx = new Regex($@"define\s*\(\s*['""]\s*{Regex.Escape(name)}\s*['""]\s*,\s*['""]([^'""]*)['""]");
        var m = rx.Match(content);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string ExtractTablePrefix(string content)
    {
        var m = Regex.Match(content, @"\$table_prefix\s*=\s*['""]([^'""]+)['""]");
        return m.Success ? m.Groups[1].Value : "wp_";
    }
}

/// <summary>
/// Rewrite wp-config.php's DB_NAME/USER/PASSWORD/HOST and relax mode to 640
/// so the panel user (in tenant group via usermod -aG) can read it.
/// Runs as root (agent), bypasses the API's www-data perm limit.
/// </summary>
public class MigrationPatchWpConfigHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.MigrationPatchWpConfig;

    public Task<AgentResult> HandleAsync(Dictionary<string, object?> p, CancellationToken ct)
    {
        var docroot = MigParam.Str(p, "docroot");
        var newDb = MigParam.Str(p, "dbName");
        var newUser = MigParam.Str(p, "dbUser");
        var newPass = MigParam.Str(p, "dbPass");
        var newHost = MigParam.Str(p, "dbHost", "localhost");
        var chownUser = MigParam.Str(p, "chownUser");

        var path = Path.Combine(docroot, "wp-config.php");
        if (!File.Exists(path))
            return Task.FromResult(new AgentResult { Success = false, Data = new() { ["error"] = "wp-config.php missing" } });

        try
        {
            var content = File.ReadAllText(path);
            content = ReplaceDefine(content, "DB_NAME", newDb);
            content = ReplaceDefine(content, "DB_USER", newUser);
            content = ReplaceDefine(content, "DB_PASSWORD", newPass);
            content = ReplaceDefine(content, "DB_HOST", newHost);
            File.WriteAllText(path, content);

            // Tighten owner to tenant + relax mode so the API/php-fpm (in tenant group) can read.
            // 640 = owner rw, group r, other none. wp-config.php should NOT be group-writable.
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead);

            if (!string.IsNullOrEmpty(chownUser) && System.Text.RegularExpressions.Regex.IsMatch(chownUser, "^[a-z0-9_-]+$"))
            {
                // Use chown shell-out since .NET doesn't expose chown directly.
                var ps = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "chown",
                        ArgumentList = { $"{chownUser}:{chownUser}", path },
                        UseShellExecute = false,
                        RedirectStandardError = true
                    }
                };
                ps.Start();
                ps.WaitForExit();
            }

            return Task.FromResult(new AgentResult { Success = true, Data = new() { ["path"] = path } });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new AgentResult { Success = false, Data = new() { ["error"] = ex.Message } });
        }
    }

    private static string ReplaceDefine(string content, string name, string value)
    {
        var rx = new System.Text.RegularExpressions.Regex(
            $@"(define\s*\(\s*['""]\s*{System.Text.RegularExpressions.Regex.Escape(name)}\s*['""]\s*,\s*['""])([^'""]*)((['""])\s*\))");
        var escVal = value.Replace("\\", "\\\\").Replace("$", "$$");
        return rx.Replace(content, m => m.Groups[1].Value + escVal + m.Groups[3].Value);
    }
}

/// <summary>Multi-purpose: sshProbe / sshDuVhost / validateCutover.</summary>
public class MigrationVerifyCutoverHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.MigrationVerifyCutover;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> p, CancellationToken ct)
    {
        var action = MigParam.Str(p, "action");

        return action switch
        {
            "sshProbe" => await SshProbe(p, ct),
            "sshDuVhost" => await SshDuVhost(p, ct),
            "validateCutover" => await ValidateCutover(p, ct),
            _ => new AgentResult { Success = false, Data = new() { ["error"] = $"unknown action '{action}'" } }
        };
    }

    private static async Task<AgentResult> SshProbe(Dictionary<string, object?> p, CancellationToken ct)
    {
        var cmd = MigParam.Str(p, "command", "echo ok");
        var args = MigParam.SshBaseArgs(p).Concat(new[] { cmd }).ToArray();
        var r = await ProcessRunner.RunAsync("ssh", args, ct, 15_000);
        return new AgentResult
        {
            Success = r.ExitCode == 0,
            Data = new() { ["exitCode"] = r.ExitCode, ["output"] = r.StdOut.Trim(), ["stderr"] = r.StdErr.Trim() }
        };
    }

    private static async Task<AgentResult> SshDuVhost(Dictionary<string, object?> p, CancellationToken ct)
    {
        var domain = MigParam.Str(p, "domain");
        if (string.IsNullOrEmpty(domain) || domain.Contains("..") || domain.Contains('/'))
            return new AgentResult { Success = false, Data = new() { ["error"] = "invalid domain" } };

        var cmd = $"du -sm /var/www/vhosts/{domain} 2>/dev/null | awk '{{print $1}}'";
        var args = MigParam.SshBaseArgs(p).Concat(new[] { cmd }).ToArray();
        var r = await ProcessRunner.RunAsync("ssh", args, ct, 60_000);
        var sizeMb = long.TryParse(r.StdOut.Trim(), out var n) ? n : 0L;
        return new AgentResult
        {
            Success = r.ExitCode == 0,
            Data = new() { ["sizeMb"] = sizeMb, ["raw"] = r.StdOut.Trim() }
        };
    }

    private static async Task<AgentResult> ValidateCutover(Dictionary<string, object?> p, CancellationToken ct)
    {
        var domain = MigParam.Str(p, "domain");
        if (string.IsNullOrEmpty(domain))
            return new AgentResult { Success = false, Data = new() { ["error"] = "domain required" } };

        var data = new Dictionary<string, object?>();

        // HTTP probe (allow self-signed in case cert isn't issued yet)
        try
        {
            using var http = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                AllowAutoRedirect = true
            }) { Timeout = TimeSpan.FromSeconds(15) };

            var resp = await http.GetAsync($"https://{domain}/", ct);
            data["httpsStatus"] = (int)resp.StatusCode;
            data["httpsOk"] = resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { data["httpsError"] = ex.Message; }

        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(15) };
            var resp = await http.GetAsync($"http://{domain}/", ct);
            data["httpStatus"] = (int)resp.StatusCode;
        }
        catch (Exception ex) { data["httpError"] = ex.Message; }

        var ok = (data.GetValueOrDefault("httpsOk") as bool? == true) ||
                 (data.GetValueOrDefault("httpStatus") is int s && s is >= 200 and < 400);
        return new AgentResult { Success = ok, Data = data };
    }
}
