using System.Diagnostics;

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

        // Core install path — Stage 4 layer 2 will replace with embedded bash invoker.
        steps.Add(new("bash-install", "Run bundled hostpanel-install.sh (Stage 4 layer 2 — placeholder)",
            Required: true, PlaceholderBashStepAsync));

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

        steps.Add(new("health-check", "Final health check", Required: true, HealthCheckAsync));

        _steps = steps;
        return steps;
    }

    public bool TryStart()
    {
        if (IsRunning) return false;
        _activeRunId = Guid.NewGuid().ToString("N")[..12];
        _activeRun = Task.Run(RunPipelineAsync);
        return true;
    }

    private async Task RunPipelineAsync()
    {
        var runId = _activeRunId!;
        var ctx = new InstallContext
        {
            State = _wizard,
            Progress = _broker,
            Logger = _logger
        };
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

    private Task PlaceholderBashStepAsync(InstallContext ctx, CancellationToken ct)
    {
        ctx.Log("  [layer 2] would invoke embedded hostpanel-install.sh with wizard env vars here");
        ctx.Log("  [layer 2] for now this step succeeds as a no-op so we can validate the framework");
        return Task.CompletedTask;
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

    private async Task RegisterStandbyAsync(InstallContext ctx, CancellationToken ct)
    {
        // Stage 5 will flesh this out with the real /ha/register-standby POST.
        ctx.Log($"  [Stage 5] would POST {ctx.State.PrimaryUrl}/api/v1/ha/register-standby with token {ctx.State.PairingToken?[..8]}…");
        await Task.CompletedTask;
    }

    private async Task HealthCheckAsync(InstallContext ctx, CancellationToken ct)
    {
        ctx.Log("  [Stage 4 layer 2] would curl https://localhost:8443/api/v1/health and verify 200");
        await Task.CompletedTask;
    }

    private async Task AptInstall(InstallContext ctx, CancellationToken ct, params string[] packages)
    {
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
