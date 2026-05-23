using System.Diagnostics;
using System.Reflection;

namespace HostPanel.Installer.Services;

/// <summary>
/// Runs the install pipeline. Each step is independently skippable based on
/// wizard prompts (per Decision 3: prompts-everywhere; only ticked actions fire).
/// Emits a steady stream of progress events to the SSE broker so the
/// progress page can render a tailing log + checklist.
///
/// On step failure: persists state, bundles diagnostics into a tarball
/// (Decision 4 + Decision 7), surfaces the failure to the broker, exits the
/// pipeline. No rollback — operator runs again to resume from the failed step.
/// </summary>
public class InstallOrchestrator
{
    private readonly ILogger<InstallOrchestrator> _logger;
    private readonly SseProgressBroker _broker;
    private readonly InstallStateStore _stateStore;
    private readonly WizardState _wizard;

    private List<InstallStep> _steps = new();
    private string? _activeRunId;
    private Task? _activeRun;

    public bool IsRunning => _activeRun is { IsCompleted: false };
    public string? ActiveRunId => _activeRunId;
    public IReadOnlyList<InstallStep> Steps => _steps;

    public InstallOrchestrator(
        ILogger<InstallOrchestrator> logger,
        SseProgressBroker broker,
        InstallStateStore stateStore,
        WizardState wizard)
    {
        _logger = logger;
        _broker = broker;
        _stateStore = stateStore;
        _wizard = wizard;
    }

    /// <summary>Builds the pipeline. Stage 4 layer 1 is stub steps for framework demo.</summary>
    public List<InstallStep> BuildPipeline()
    {
        var s = _wizard;
        var steps = new List<InstallStep>
        {
            new("validate", "Validate wizard answers", Required: true, ValidateInputsAsync),
            new("write-state-dir", "Create /var/lib/hostpanel-installer", Required: true, EnsureStateDirAsync),
            // Always drain dpkg before any apt-using step. Ubuntu cloud images run
            // unattended-upgrades on first boot — apt timers fire just as the operator
            // hits "Install", leaving dpkg in a half-configured state. The bash
            // installer already handles this internally, but optional steps (chrony,
            // fail2ban, tailscale) run BEFORE bash-install and hit the lock first.
            new("dpkg-drain", "Stop unattended-upgrades + wait for dpkg lock", Required: true, DrainDpkgAsync),
        };

        // Optional hygiene steps fire only when ticked.
        if (s.SetSystemHostname && !string.IsNullOrWhiteSpace(s.Hostname))
            steps.Add(new("hostname", $"Set system hostname to {s.Hostname}", Required: false, SetHostnameAsync));
        if (!string.IsNullOrWhiteSpace(s.Timezone))
            steps.Add(new("timezone", $"Set timezone to {s.Timezone}", Required: false, SetTimezoneAsync));
        if (s.InstallChrony)
            steps.Add(new("chrony", "Install + enable chrony", Required: false, InstallChronyAsync));
        if (s.CreateSwapfile)
            steps.Add(new("swap", "Create 4 GB swapfile", Required: false, CreateSwapfileAsync));

        // Core install path — extracts + execs the embedded bash installer with
        // wizard-state-derived flags. Real work happens here (apt installs, EF
        // migrations, systemd unit writes, nginx config, etc).
        steps.Add(new("bash-install", "Run bundled hostpanel-install.sh",
            Required: true, RunBashInstallerAsync));

        // Optional integrations.
        if (s.ConfigureUfw)
            steps.Add(new("ufw", "Configure UFW firewall rules", Required: false, ConfigureUfwAsync));
        if (s.InstallFail2ban)
            steps.Add(new("fail2ban", "Install fail2ban + HostPanel jail", Required: false, InstallFail2banAsync));
        if (!string.IsNullOrEmpty(s.AdminSshPublicKey))
            steps.Add(new("ssh-key", "Import admin SSH public key", Required: false, ImportSshKeyAsync));
        if (s.JoinTailscale && !string.IsNullOrEmpty(s.TailscaleAuthKey))
            steps.Add(new("tailscale", "Join Tailscale tailnet", Required: false, JoinTailscaleAsync));
        if (s.Role == InstallRole.Standby && !string.IsNullOrEmpty(s.PairingToken))
            steps.Add(new("ha-register", "Register with primary (HA pairing)", Required: true, RegisterStandbyAsync));

        steps.Add(new("health-check", "Final health check", Required: true, HealthCheckRealAsync));

        _steps = steps;
        return steps;
    }

    public bool TryStart(bool dryRun = false)
    {
        if (IsRunning) return false;
        _activeRunId = Guid.NewGuid().ToString("N")[..12];
        _activeRun = Task.Run(() => RunPipelineAsync(dryRun));
        return true;
    }

    /// <summary>Await the currently-running pipeline. Used by the headless runner.</summary>
    public Task? ActiveRunTask => _activeRun;

    private async Task RunPipelineAsync(bool dryRun)
    {
        var runId = _activeRunId!;
        var ctx = new InstallContext
        {
            State = _wizard,
            Progress = _broker,
            Logger = _logger,
            DryRun = dryRun
        };
        if (dryRun)
        {
            _broker.Broadcast(new InstallProgressEvent("step_log", null, null, null,
                "── DRY RUN: no mutations will occur. Logging what would happen. ──", DateTime.UtcNow));
        }
        var startedAt = DateTime.UtcNow;

        BuildPipeline();

        try
        {
            foreach (var step in _steps)
            {
                step.Status = InstallStepStatus.Running;
                step.StartedAt = DateTime.UtcNow;
                _broker.Broadcast(new InstallProgressEvent("step_started", step.Id, step.Label, step.Status, null, DateTime.UtcNow));
                ctx.Log($"▶ {step.Label}");

                try
                {
                    await step.Action(ctx, CancellationToken.None);
                    step.Status = InstallStepStatus.Completed;
                    step.FinishedAt = DateTime.UtcNow;
                    ctx.Log($"✓ {step.Label}");
                    _broker.Broadcast(new InstallProgressEvent("step_completed", step.Id, step.Label, step.Status, null, DateTime.UtcNow));
                }
                catch (Exception ex)
                {
                    step.Status = InstallStepStatus.Failed;
                    step.FinishedAt = DateTime.UtcNow;
                    step.FailureMessage = ex.Message;
                    ctx.Log($"✗ {step.Label} FAILED: {ex.Message}");
                    _broker.Broadcast(new InstallProgressEvent("step_failed", step.Id, step.Label, step.Status, ex.Message, DateTime.UtcNow));

                    await PersistStateAsync(runId, startedAt, step.Id);
                    await BundleDiagnosticsAsync(runId, ctx);
                    _broker.Broadcast(new InstallProgressEvent("run_failed", null, null, null,
                        $"Install halted at step '{step.Id}'. Diagnostic bundle written to /tmp/hostpanel-install-failure-{runId}.tar.gz", DateTime.UtcNow));
                    return;
                }

                await PersistStateAsync(runId, startedAt, step.Id);
            }

            _broker.Broadcast(new InstallProgressEvent("run_completed", null, null, null, "Install complete.", DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orchestrator failed outside step boundary");
            _broker.Broadcast(new InstallProgressEvent("run_failed", null, null, null, $"Orchestrator error: {ex.Message}", DateTime.UtcNow));
        }
    }

    private async Task PersistStateAsync(string runId, DateTime startedAt, string currentStepId)
    {
        var snapshot = new PersistedInstallState(
            RunId: runId,
            StartedAt: startedAt,
            LastUpdatedAt: DateTime.UtcNow,
            CurrentStepId: currentStepId,
            StepStatuses: _steps.ToDictionary(s => s.Id, s => s.Status),
            WizardSnapshot: _wizard);
        await _stateStore.SaveAsync(snapshot, CancellationToken.None);
    }

    private async Task BundleDiagnosticsAsync(string runId, InstallContext ctx)
    {
        try
        {
            var bundlePath = $"/tmp/hostpanel-install-failure-{runId}.tar.gz";
            var workDir = $"/tmp/hostpanel-install-failure-{runId}";
            Directory.CreateDirectory(workDir);

            await File.WriteAllTextAsync($"{workDir}/install-log.txt", string.Join("\n", ctx.RunLog));
            await File.WriteAllTextAsync($"{workDir}/wizard-state.json",
                System.Text.Json.JsonSerializer.Serialize(_wizard, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            // Best-effort copy a few system diag files
            var copies = new[]
            {
                "/var/log/nginx/error.log",
                "/var/log/mssql/errorlog",
                "/var/lib/hostpanel-installer/.install-state.json"
            };
            foreach (var src in copies)
            {
                try { if (File.Exists(src)) File.Copy(src, $"{workDir}/{Path.GetFileName(src)}", overwrite: true); }
                catch { /* best effort */ }
            }

            await Run("/usr/bin/tar", ["-czf", bundlePath, "-C", "/tmp", $"hostpanel-install-failure-{runId}"]);
            ctx.Log($"📦 Diagnostic bundle: {bundlePath}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bundle diagnostics");
        }
    }

    // === Step implementations (Stage 4 layer 1 — most are stubs proving the pipeline) ===

    private Task ValidateInputsAsync(InstallContext ctx, CancellationToken ct)
    {
        var s = ctx.State;
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(s.Hostname)) problems.Add("hostname is required");
        if (s.Role == InstallRole.Primary)
        {
            if (string.IsNullOrWhiteSpace(s.AdminEmail)) problems.Add("admin email is required");
            if (string.IsNullOrWhiteSpace(s.AdminPassword)) problems.Add("admin password is required");
            if (s.InstallMssql && string.IsNullOrWhiteSpace(s.SaPassword)) problems.Add("SA password is required when installing MSSQL");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(s.PrimaryUrl)) problems.Add("primary URL is required (standby)");
            if (string.IsNullOrWhiteSpace(s.PairingToken)) problems.Add("pairing token is required (standby)");
        }
        if (problems.Count > 0) throw new InvalidOperationException("Validation failed: " + string.Join("; ", problems));
        ctx.Log($"  inputs OK (role={s.Role}, hostname={s.Hostname})");
        return Task.CompletedTask;
    }

    private Task EnsureStateDirAsync(InstallContext ctx, CancellationToken ct)
    {
        Directory.CreateDirectory("/var/lib/hostpanel-installer");
        ctx.Log("  /var/lib/hostpanel-installer created");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop Ubuntu's unattended-upgrades + apt-daily timers, then wait up to
    /// 10 minutes for /var/lib/dpkg/lock-frontend to be released, then run
    /// `dpkg --configure -a` to finalize any half-configured state. Without
    /// this, optional apt steps (chrony, fail2ban) hit "dpkg was interrupted"
    /// on freshly-booted Ubuntu cloud images because cloud-init's first-boot
    /// unattended-upgrades is still in progress. Hit on VM 282 Stage 8 dogfood.
    /// </summary>
    private async Task DrainDpkgAsync(InstallContext ctx, CancellationToken ct)
    {
        if (ctx.DryRun)
        {
            ctx.Log("  [dry-run] would stop apt timers + wait for dpkg lock + dpkg --configure -a");
            return;
        }

        var unitsToStop = new[]
        {
            "unattended-upgrades.service",
            "apt-daily.service", "apt-daily.timer",
            "apt-daily-upgrade.service", "apt-daily-upgrade.timer"
        };
        foreach (var unit in unitsToStop)
        {
            // Best-effort; some units may not exist or already be stopped.
            await Run("/usr/bin/systemctl", ["stop", unit], ct);
            await Run("/usr/bin/systemctl", ["mask", unit], ct);
        }
        ctx.Log("  apt timers stopped + masked");

        // Wait up to 10 minutes for any in-flight apt to finish.
        var deadline = DateTime.UtcNow.AddMinutes(10);
        var waited = 0;
        while (DateTime.UtcNow < deadline)
        {
            var (exit, _, _) = await Run("/usr/bin/fuser", ["/var/lib/dpkg/lock-frontend"], ct);
            if (exit != 0) break; // exit 1 = lock not held → we're clear
            await Task.Delay(5000, ct);
            waited += 5;
            if (waited % 30 == 0) ctx.Log($"  waited {waited}s for dpkg lock to release…");
        }

        if (DateTime.UtcNow >= deadline)
            ctx.Log("  ⚠ dpkg lock still held after 10min — continuing anyway; configure-a will likely retry");

        // Finalize any half-configured packages from a prior interrupted run.
        var (cfgExit, _, cfgErr) = await Run("/usr/bin/dpkg", ["--configure", "-a"], ct);
        if (cfgExit != 0)
            throw new InvalidOperationException($"dpkg --configure -a failed: {cfgErr}");
        ctx.Log("  dpkg --configure -a OK — apt is ready for installs");
    }

    private async Task SetHostnameAsync(InstallContext ctx, CancellationToken ct)
    {
        var (exit, _, err) = await Run("/usr/bin/hostnamectl", ["set-hostname", ctx.State.Hostname!], ct);
        if (exit != 0) throw new InvalidOperationException($"hostnamectl failed: {err}");
    }

    private async Task SetTimezoneAsync(InstallContext ctx, CancellationToken ct)
    {
        var (exit, _, err) = await Run("/usr/bin/timedatectl", ["set-timezone", ctx.State.Timezone!], ct);
        if (exit != 0) throw new InvalidOperationException($"timedatectl failed: {err}");
    }

    private async Task InstallChronyAsync(InstallContext ctx, CancellationToken ct)
    {
        await AptInstall(ctx, ct, "chrony");
        await Run("/usr/bin/systemctl", ["enable", "--now", "chrony"], ct);
    }

    private async Task CreateSwapfileAsync(InstallContext ctx, CancellationToken ct)
    {
        const string swap = "/swapfile";
        if (File.Exists(swap)) { ctx.Log("  /swapfile already exists — skipping"); return; }
        await Run("/usr/bin/fallocate", ["-l", "4G", swap], ct);
        File.SetUnixFileMode(swap, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await Run("/sbin/mkswap", [swap], ct);
        await Run("/sbin/swapon", [swap], ct);
        await File.AppendAllTextAsync("/etc/fstab", $"{swap} none swap sw 0 0\n", ct);
    }

    /// <summary>
    /// Stage 4 layer 2 — extract the embedded hostpanel-install.sh and run it
    /// with CLI flags derived from wizard state. stdout/stderr lines stream to
    /// the SSE broker as step_log events so the operator sees real-time progress.
    /// </summary>
    private async Task RunBashInstallerAsync(InstallContext ctx, CancellationToken ct)
    {
        var scriptPath = ExtractEmbeddedScript("hostpanel-install.sh");
        ctx.Log($"  extracted bundled installer to {scriptPath}");
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var args = new List<string> { scriptPath };
        var s = ctx.State;

        // Always pass --git-repo + --git-ref so the bash installer clones the panel
        // source itself (the installer binary doesn't ship the panel source).
        args.AddRange(["--git-repo", "https://github.com/mgardner323/hostpanel.git", "--git-ref", "main"]);

        if (s.InstallMssql && !string.IsNullOrEmpty(s.SaPassword))
            args.AddRange(["--sql-password", s.SaPassword]);
        else if (!s.InstallMssql)
            args.Add("--skip-mssql");

        if (!string.IsNullOrEmpty(s.AdminEmail))
            args.AddRange(["--admin-email", s.AdminEmail]);
        if (!string.IsNullOrEmpty(s.AdminPassword))
            args.AddRange(["--admin-password", s.AdminPassword]);

        // Sentry DSNs are configured later via panel Settings — installer-side opt-in
        // only flips the install-failure-reporting switch, not the panel's runtime DSNs.

        ctx.Log($"  invoking: bash {scriptPath} (with {(args.Count - 1)/2} arg pairs)");
        await StreamCommandAsync("/bin/bash", args.ToArray(), ctx, ct);
    }

    /// <summary>
    /// Stage 4 layer 2 — real health check. Curls https://localhost:8443/api/v1/health
    /// and accepts 200 or 503 (503 from /health = nginx up, agent fronted but unhealthy
    /// — we'll see that and the operator can address it post-install).
    /// </summary>
    private async Task HealthCheckRealAsync(InstallContext ctx, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            var (exit, stdout, _) = await Run("/usr/bin/curl",
                ["-sk", "-o", "/dev/null", "-w", "%{http_code}", "--max-time", "5",
                 "https://localhost:8443/api/v1/health"], ct);
            if (exit == 0 && (stdout.Trim() == "200" || stdout.Trim() == "503"))
            {
                ctx.Log($"  ✓ panel health check returned {stdout.Trim()} on attempt {attempt}");
                return;
            }
            ctx.Log($"  attempt {attempt}/12: health returned exit={exit} body={stdout.Trim()} (waiting 5s)");
            await Task.Delay(5000, ct);
        }
        throw new InvalidOperationException("Panel health check never returned 200/503 after 60s — install may be incomplete");
    }

    /// <summary>Extract a file embedded via &lt;EmbeddedResource&gt; in the csproj to /tmp.</summary>
    private static string ExtractEmbeddedScript(string logicalName)
    {
        var asm = typeof(InstallOrchestrator).Assembly;
        using var stream = asm.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded resource '{logicalName}' not found. Manifest: {string.Join(", ", asm.GetManifestResourceNames())}");
        var dest = Path.Combine(Path.GetTempPath(), logicalName);
        using var file = File.Create(dest);
        stream.CopyTo(file);
        return dest;
    }

    /// <summary>
    /// Run a command, streaming stdout AND stderr line-by-line to the install
    /// context's log (which fans out to the SSE broker). Throws on non-zero exit.
    /// In DryRun mode: logs the command line and returns without executing.
    /// </summary>
    private static async Task StreamCommandAsync(string cmd, string[] args, InstallContext ctx, CancellationToken ct)
    {
        if (ctx.DryRun)
        {
            ctx.Log($"  [dry-run] would exec: {cmd} {string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");
            await Task.CompletedTask;
            return;
        }

        var psi = new ProcessStartInfo(cmd)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;

        async Task DrainAsync(StreamReader reader, string prefix)
        {
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                // Strip ANSI color codes so the SSE log pane stays clean.
                var clean = System.Text.RegularExpressions.Regex.Replace(line, @"\x1b\[[0-9;]*m", "");
                ctx.Log($"  {prefix}{clean}");
            }
        }

        var stdoutTask = DrainAsync(p.StandardOutput, "");
        var stderrTask = DrainAsync(p.StandardError, "[err] ");

        await p.WaitForExitAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(cmd)} exited with {p.ExitCode}");
    }

    private async Task ConfigureUfwAsync(InstallContext ctx, CancellationToken ct)
    {
        var rules = new[] { "22", "80", "443", "8443" };
        foreach (var r in rules) await Run("/usr/sbin/ufw", ["allow", r], ct);
        await Run("/usr/sbin/ufw", ["--force", "enable"], ct);
    }

    private async Task InstallFail2banAsync(InstallContext ctx, CancellationToken ct)
    {
        await AptInstall(ctx, ct, "fail2ban");
        await Run("/usr/bin/systemctl", ["enable", "--now", "fail2ban"], ct);
    }

    private async Task ImportSshKeyAsync(InstallContext ctx, CancellationToken ct)
    {
        const string path = "/root/.ssh/authorized_keys";
        Directory.CreateDirectory("/root/.ssh");
        await File.AppendAllTextAsync(path, "\n" + ctx.State.AdminSshPublicKey + "\n", ct);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private async Task JoinTailscaleAsync(InstallContext ctx, CancellationToken ct)
    {
        var (exit, _, err) = await Run("/usr/bin/tailscale", ["up", "--auth-key", ctx.State.TailscaleAuthKey!], ct);
        if (exit != 0) throw new InvalidOperationException($"tailscale up failed: {err}");
    }

    /// <summary>
    /// Stage 5 — real standby registration flow.
    /// 1. Read this box's freshly-written agent token from
    ///    /opt/hostpanel/agent/appsettings.Production.json (created by bash installer).
    /// 2. POST {primary}/api/v1/ha/register-standby with pairing token + agent token
    ///    + this box's hostname + reachable IP.
    /// 3. Primary persists the Server + HaSyncState rows and (best-effort) calls
    ///    this box's agent to wire up pull-sync.
    /// 4. Poll {primary}/api/v1/ha/sync-state until LastSyncAt is non-null
    ///    (initial replication complete) OR timeout (operator can retry).
    /// </summary>
    private async Task RegisterStandbyAsync(InstallContext ctx, CancellationToken ct)
    {
        var s = ctx.State;
        var primary = s.PrimaryUrl?.TrimEnd('/')
            ?? throw new InvalidOperationException("PrimaryUrl missing on standby");
        var token = s.PairingToken
            ?? throw new InvalidOperationException("PairingToken missing on standby");
        var reachable = s.ThisBoxReachableAddress
            ?? throw new InvalidOperationException("ThisBoxReachableAddress missing on standby");

        var agentToken = await ReadAgentTokenAsync(ctx, ct);
        ctx.Log($"  read agent token from /opt/hostpanel/agent/appsettings.Production.json ({agentToken.Length} chars)");

        using var http = new HttpClient(new HttpClientHandler
        {
            // The primary likely uses a self-signed or LE cert that the standby
            // may not trust during initial install (CA bundles not yet refreshed).
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });
        http.Timeout = TimeSpan.FromSeconds(30);

        var body = new
        {
            token,
            hostname = s.Hostname,
            ipAddress = reachable,
            agentToken,
            syncDomains = true,
            syncDatabases = true,
            syncEmail = true,
            syncIntervalMinutes = 15
        };

        ctx.Log($"  POST {primary}/api/v1/ha/register-standby (token={token[..8]}…, hostname={s.Hostname}, ip={reachable})");
        var resp = await http.PostAsJsonAsync($"{primary}/api/v1/ha/register-standby", body, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Primary rejected registration: HTTP {(int)resp.StatusCode} — {respBody}");

        ctx.Log($"  ✓ primary registered this standby — response: {respBody}");

        // Poll sync-state until the primary reports a LastSyncAt for our server.
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(5000, ct);
            try
            {
                var syncResp = await http.GetAsync($"{primary}/api/v1/ha/sync-state", ct);
                if (!syncResp.IsSuccessStatusCode)
                {
                    ctx.Log($"  sync-state poll: HTTP {(int)syncResp.StatusCode} — waiting…");
                    continue;
                }
                var syncJson = await syncResp.Content.ReadAsStringAsync(ct);
                if (syncJson.Contains("\"lastSyncAt\"") && !syncJson.Contains("\"lastSyncAt\":null"))
                {
                    ctx.Log("  ✓ initial sync completed — this standby is now in the cluster");
                    return;
                }
                ctx.Log("  sync-state: LastSyncAt still null, waiting 5s…");
            }
            catch (Exception ex)
            {
                ctx.Log($"  sync-state poll error: {ex.Message} (continuing)");
            }
        }

        // Don't hard-fail — the operator can re-run /ha/configure-failover from the
        // primary's UI if the standby agent isn't fully up yet. Surfaces as a warning.
        ctx.Log("  ⚠ initial sync didn't complete within 5min — operator can re-trigger from primary's HA dashboard");
    }

    private static async Task<string> ReadAgentTokenAsync(InstallContext ctx, CancellationToken ct)
    {
        const string path = "/opt/hostpanel/agent/appsettings.Production.json";
        if (!File.Exists(path))
            throw new InvalidOperationException($"{path} not found — bash installer must run successfully first");

        var json = await File.ReadAllTextAsync(path, ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("Agent", out var agent)
            && agent.TryGetProperty("Token", out var token))
        {
            return token.GetString() ?? throw new InvalidOperationException("Agent.Token in appsettings is null");
        }
        throw new InvalidOperationException("Agent.Token not found in agent appsettings.Production.json");
    }

    private async Task AptInstall(InstallContext ctx, CancellationToken ct, params string[] packages)
    {
        if (ctx.DryRun)
        {
            ctx.Log($"  [dry-run] would apt-get install -y {string.Join(" ", packages)}");
            return;
        }
        var args = new List<string> { "-y", "install" };
        args.AddRange(packages);
        var (exit, _, err) = await Run("/usr/bin/apt-get", args.ToArray(), ct);
        if (exit != 0) throw new InvalidOperationException($"apt-get install {string.Join(" ", packages)}: {err}");
    }

    private static async Task<(int exit, string stdout, string stderr)> Run(
        string cmd, string[] args, CancellationToken ct = default)
    {
        if (!File.Exists(cmd)) return (-1, "", $"not found: {cmd}");
        var psi = new ProcessStartInfo(cmd)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var outT = p.StandardOutput.ReadToEndAsync(ct);
            var errT = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return (p.ExitCode, await outT, await errT);
        }
        catch (Exception ex) { return (-1, "", ex.Message); }
    }
}
