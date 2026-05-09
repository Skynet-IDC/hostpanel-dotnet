using System.Diagnostics;

namespace HostPanel.Agent.Services;

/// <summary>
/// Safe process runner that uses argv arrays only (no shell pipelines).
/// Kills child process on timeout or cancellation.
/// </summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string[] arguments,
        CancellationToken cancellationToken,
        int timeoutMs = 30_000)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var arg in arguments)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken)
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);
        }
        catch (TimeoutException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(-1, await stdOutTask, $"Process timed out after {timeoutMs}ms and was killed");
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await stdOutTask,
            await stdErrTask
        );
    }
}

public record ProcessResult(int ExitCode, string StdOut, string StdErr);
