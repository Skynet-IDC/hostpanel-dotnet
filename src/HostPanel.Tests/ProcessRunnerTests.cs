using HostPanel.Agent.Services;

namespace HostPanel.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsOutput()
    {
        // Use a cross-platform command
        var isWindows = OperatingSystem.IsWindows();
        var cmd = isWindows ? "cmd.exe" : "/bin/echo";
        var args = isWindows ? new[] { "/c", "echo hello" } : new[] { "hello" };

        var result = await ProcessRunner.RunAsync(cmd, args, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StdOut);
    }

    [Fact]
    public async Task RunAsync_TimesOutAndKills()
    {
        var isWindows = OperatingSystem.IsWindows();
        var cmd = isWindows ? "cmd.exe" : "/bin/sleep";
        var args = isWindows ? new[] { "/c", "ping -n 30 127.0.0.1 > nul" } : new[] { "30" };

        var result = await ProcessRunner.RunAsync(cmd, args, CancellationToken.None, timeoutMs: 500);

        // On timeout, ProcessRunner returns -1 and kills the process
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("timed out", result.StdErr);
    }
}
