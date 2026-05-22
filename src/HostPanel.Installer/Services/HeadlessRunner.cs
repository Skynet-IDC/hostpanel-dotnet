using Microsoft.Extensions.Logging.Abstractions;

namespace HostPanel.Installer.Services;

/// <summary>
/// Runs the install orchestrator without spinning up the web UI. Used by:
///   --config FILE --headless    full install from YAML
///   --dry-run --config FILE     dry-run from YAML
///   --dry-run (no config)       boot wizard, but dry-run when Install is clicked
///
/// SSE events are printed to stdout (one line per event) so the operator can
/// follow along on the terminal or pipe to a log file.
/// </summary>
public static class HeadlessRunner
{
    public static async Task<int> RunAsync(string configPath, bool dryRun, CancellationToken ct)
    {
        WizardState state;
        try
        {
            state = ConfigYaml.Load(configPath);
            Console.WriteLine($"Loaded config from {configPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not load config: {ex.Message}");
            return 2;
        }

        var stateStore = new InstallStateStore(NullLogger<InstallStateStore>.Instance);
        var broker = new SseProgressBroker();
        var orchestrator = new InstallOrchestrator(NullLogger<InstallOrchestrator>.Instance, broker, stateStore, state);

        var doneTcs = new TaskCompletionSource<int>();
        using var sub = broker.Subscribe(ev =>
        {
            // Single-line console formatting — operator-friendly stream.
            var line = ev.Phase switch
            {
                "step_started" => $"[{ev.At:HH:mm:ss}] ▶ {ev.StepLabel}",
                "step_completed" => $"[{ev.At:HH:mm:ss}] ✓ {ev.StepLabel}",
                "step_failed" => $"[{ev.At:HH:mm:ss}] ✗ {ev.StepLabel}: {ev.Message}",
                "step_log" => $"[{ev.At:HH:mm:ss}]   {ev.Message}",
                "run_completed" => $"[{ev.At:HH:mm:ss}] === COMPLETE ===",
                "run_failed" => $"[{ev.At:HH:mm:ss}] === FAILED: {ev.Message} ===",
                _ => $"[{ev.At:HH:mm:ss}] {ev.Phase} {ev.Message}"
            };
            Console.WriteLine(line);

            if (ev.Phase == "run_completed") doneTcs.TrySetResult(0);
            if (ev.Phase == "run_failed") doneTcs.TrySetResult(3);
            return Task.CompletedTask;
        }, out _);

        if (!orchestrator.TryStart(dryRun))
        {
            Console.Error.WriteLine("Orchestrator refused to start (another run in progress?)");
            return 4;
        }

        // Wait for run_completed/run_failed OR the orchestrator task to finish (covers
        // a crash inside the orchestrator that never broadcasts a terminal event).
        var exit = await await Task.WhenAny(
            doneTcs.Task,
            (orchestrator.ActiveRunTask ?? Task.CompletedTask).ContinueWith(_ => 5, ct));

        return exit;
    }
}
