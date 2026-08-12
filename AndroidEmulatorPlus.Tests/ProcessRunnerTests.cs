using AndroidEmulatorPlus.Helpers;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_captures_stdout_and_stderr()
    {
        var stdout = new List<string>();
        var stderr = new List<string>();

        var result = await ProcessRunner.RunAsync(
            "cmd.exe",
            new[] { "/c", "echo output & echo error 1>&2" },
            onStdOut: stdout.Add,
            onStdErr: stderr.Add,
            timeout: TimeSpan.FromSeconds(10));

        Assert.True(result.Success);
        Assert.Contains("output", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("error", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("output", stdout.Single().Trim());
        Assert.Equal("error", stderr.Single().Trim());
        Assert.True(result.CleanupCompleted);
        Assert.False(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task RunWithStdin_starts_readers_before_writing()
    {
        var result = await ProcessRunner.RunWithStdinAsync(
            "cmd.exe",
            new[] { "/c", "more" },
            new[] { "alpha", "beta" },
            timeout: TimeSpan.FromSeconds(10));

        Assert.True(result.Success);
        Assert.Contains("alpha", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beta", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeout_contains_output_and_cleanup_state()
    {
        var exception = await Assert.ThrowsAsync<ProcessTimeoutException>(() =>
            ProcessRunner.RunAsync(
                "cmd.exe",
                new[] { "/c", "ping", "127.0.0.1", "-n", "10", "-w", "1000" },
                timeout: TimeSpan.FromMilliseconds(100)));

        Assert.True(exception.Result.TimedOut);
        Assert.False(exception.Result.Cancelled);
        Assert.True(exception.Result.CleanupCompleted);
        Assert.Contains("timeout", exception.Result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_contains_output_and_cleanup_state()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAsync<ProcessCancelledException>(() =>
            ProcessRunner.RunAsync(
                "cmd.exe",
                new[] { "/c", "ping", "127.0.0.1", "-n", "10", "-w", "1000" },
                ct: cts.Token));

        Assert.False(exception.Result.TimedOut);
        Assert.True(exception.Result.Cancelled);
        Assert.True(exception.Result.CleanupCompleted);
        Assert.Contains("cancelled", exception.Result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }
}
