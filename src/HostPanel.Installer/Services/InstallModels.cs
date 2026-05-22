namespace HostPanel.Installer.Services;

public enum InstallStepStatus
{
    Pending,
    Running,
    Completed,
    Skipped,
    Failed
}

public record InstallStep(
    string Id,
    string Label,
    bool Required,
    Func<InstallContext, CancellationToken, Task> Action)
{
    public InstallStepStatus Status { get; set; } = InstallStepStatus.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? FailureMessage { get; set; }
}

/// <summary>Live state of an in-flight install run, persisted as JSON for resume.</summary>
public record InstallProgressEvent(
    string Phase,           // step_started | step_log | step_completed | step_failed | run_completed | run_failed
    string? StepId,
    string? StepLabel,
    InstallStepStatus? Status,
    string? Message,
    DateTime At);

/// <summary>Per-run shared mutable context — handed to each step's action.</summary>
public class InstallContext
{
    public required WizardState State { get; init; }
    public required SseProgressBroker Progress { get; init; }
    public required ILogger Logger { get; init; }
    public bool DryRun { get; init; }
    public List<string> RunLog { get; } = [];

    public void Log(string line)
    {
        RunLog.Add($"{DateTime.UtcNow:HH:mm:ss} {line}");
        Logger.LogInformation("[install] {Line}", line);
        Progress.Broadcast(new InstallProgressEvent("step_log", null, null, null, line, DateTime.UtcNow));
    }
}

/// <summary>
/// What gets persisted to /var/lib/hostpanel-installer/.install-state.json after
/// each successful step. On rerun (per Decision 4: resume on rerun), the
/// orchestrator skips steps already marked Completed.
/// </summary>
public record PersistedInstallState(
    string RunId,
    DateTime StartedAt,
    DateTime LastUpdatedAt,
    string? CurrentStepId,
    Dictionary<string, InstallStepStatus> StepStatuses,
    WizardState WizardSnapshot);
