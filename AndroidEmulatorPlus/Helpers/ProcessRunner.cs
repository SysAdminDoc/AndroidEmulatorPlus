using System.Diagnostics;
using System.IO;
using System.Text;

namespace AndroidEmulatorPlus.Helpers;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
    public bool TimedOut { get; init; }
    public bool Cancelled { get; init; }
    public bool CleanupCompleted { get; init; } = true;
    public TimeSpan Duration { get; init; }
    public string? FailureReason { get; init; }
    public string Combined => string.IsNullOrEmpty(StdErr) ? StdOut : $"{StdOut}\n{StdErr}";
}

/// <summary>Provides the captured process result when a tool times out.</summary>
public sealed class ProcessTimeoutException : TimeoutException
{
    public ProcessResult Result { get; }

    public ProcessTimeoutException(string message, ProcessResult result)
        : base(message) => Result = result;
}

/// <summary>Provides the captured process result when a tool is cancelled.</summary>
public sealed class ProcessCancelledException : OperationCanceledException
{
    public ProcessResult Result { get; }

    public ProcessCancelledException(string message, ProcessResult result, CancellationToken cancellationToken)
        : base(message, innerException: null, cancellationToken) => Result = result;
}

public static class ProcessRunner
{
    public static Task<ProcessResult> RunAsync(
        string exe,
        IEnumerable<string> args,
        string? workingDir = null,
        IDictionary<string, string?>? extraEnv = null,
        Action<string>? onStdOut = null,
        Action<string>? onStdErr = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        => RunCoreAsync(exe, args, stdinLines: null, workingDir, extraEnv, onStdOut, onStdErr, timeout, ct);

    /// <summary>
    /// Runs a process with stdin and starts output readers before writing input. This
    /// prevents chatty tools from blocking while their stdout/stderr pipe is full.
    /// </summary>
    public static Task<ProcessResult> RunWithStdinAsync(
        string exe,
        IEnumerable<string> args,
        IEnumerable<string> stdinLines,
        string? workingDir = null,
        IDictionary<string, string?>? extraEnv = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        Action<string>? onStdOut = null,
        Action<string>? onStdErr = null)
        => RunCoreAsync(exe, args, stdinLines, workingDir, extraEnv, onStdOut, onStdErr, timeout, ct);

    /// <summary>Runs a process and streams both output channels to one callback.</summary>
    public static async Task<int> StreamAsync(
        string exe,
        IEnumerable<string> args,
        Action<string> onLine,
        string? workingDir = null,
        IDictionary<string, string?>? extraEnv = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var result = await RunAsync(exe, args, workingDir, extraEnv, onLine, onLine, timeout, ct);
        return result.ExitCode;
    }

    /// <summary>Launches a process detached (not awaited) and returns immediately.</summary>
    public static Process StartDetached(string exe, IEnumerable<string> args, string? workingDir = null,
        IDictionary<string, string?>? extraEnv = null)
    {
        var psi = CreateStartInfo(exe, args, workingDir, extraEnv,
            redirectInput: false, redirectOutput: false, redirectError: false);
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();
        return proc;
    }

    /// <summary>
    /// Starts a long-running process with the same output capture and process-tree
    /// cleanup contract as <see cref="RunAsync"/>. Call <see cref="RunningProcess.StopAsync"/>
    /// when the owner is done; <see cref="RunningProcess.Completion"/> never leaves
    /// stdout/stderr reader tasks unobserved.
    /// </summary>
    public static RunningProcess StartStreaming(
        string exe,
        IEnumerable<string> args,
        Action<string>? onStdOut = null,
        Action<string>? onStdErr = null,
        string? workingDir = null,
        IDictionary<string, string?>? extraEnv = null)
    {
        var psi = CreateStartInfo(exe, args, workingDir, extraEnv,
            redirectInput: false, redirectOutput: true, redirectError: true);
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();
        return new RunningProcess(proc, onStdOut, onStdErr);
    }

    private static async Task<ProcessResult> RunCoreAsync(
        string exe,
        IEnumerable<string> args,
        IEnumerable<string>? stdinLines,
        string? workingDir,
        IDictionary<string, string?>? extraEnv,
        Action<string>? onStdOut,
        Action<string>? onStdErr,
        TimeSpan? timeout,
        CancellationToken ct)
    {
        var psi = CreateStartInfo(exe, args, workingDir, extraEnv,
            redirectInput: stdinLines is not null, redirectOutput: true, redirectError: true);
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var started = Stopwatch.GetTimestamp();

        proc.Start();
        var stdoutTask = ReadLinesAsync(proc.StandardOutput, stdout, onStdOut);
        var stderrTask = ReadLinesAsync(proc.StandardError, stderr, onStdErr);
        Task? stdinTask = stdinLines is null ? null : WriteStdinAsync(proc, stdinLines);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue) linked.CancelAfter(timeout.Value);

        try
        {
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            if (stdinTask is not null) await ObserveAsync(stdinTask).ConfigureAwait(false);
            return Snapshot(proc, stdout, stderr, started, cleanupCompleted: true);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            var timedOut = !ct.IsCancellationRequested && timeout.HasValue;
            var cleanup = await StopAndDrainAsync(proc, stdoutTask, stderrTask, stdinTask).ConfigureAwait(false);
            var reason = timedOut
                ? $"{exe} exceeded timeout of {timeout!.Value}."
                : $"{exe} was cancelled.";
            var result = Snapshot(proc, stdout, stderr, started, cleanup, timedOut, !timedOut, reason);
            if (timedOut) throw new ProcessTimeoutException(reason, result);
            throw new ProcessCancelledException(reason, result, ct);
        }
        catch
        {
            await StopAndDrainAsync(proc, stdoutTask, stderrTask, stdinTask).ConfigureAwait(false);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string exe,
        IEnumerable<string> args,
        string? workingDir,
        IDictionary<string, string?>? extraEnv,
        bool redirectInput,
        bool redirectOutput,
        bool redirectError)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectError,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (extraEnv is not null)
        {
            foreach (var pair in extraEnv)
                psi.Environment[pair.Key] = pair.Value;
        }
        return psi;
    }

    private static async Task ReadLinesAsync(StreamReader reader, StringBuilder output, Action<string>? callback)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            output.AppendLine(line);
            callback?.Invoke(line);
        }
    }

    private static async Task WriteStdinAsync(Process proc, IEnumerable<string> lines)
    {
        try
        {
            foreach (var line in lines)
            {
                await proc.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
                await proc.StandardInput.FlushAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // The process may close stdin before consuming all prompts.
        }
        finally
        {
            try { proc.StandardInput.Close(); } catch { }
        }
    }

    private static async Task<bool> StopAndDrainAsync(
        Process proc,
        Task stdoutTask,
        Task stderrTask,
        Task? stdinTask)
    {
        var clean = true;
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch { clean = false; }

        try { await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch { clean = false; }
        try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch { clean = false; }
        if (stdinTask is not null)
        {
            try { await stdinTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch { clean = false; }
        }
        return clean;
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); } catch { }
    }

    private static ProcessResult Snapshot(
        Process proc,
        StringBuilder stdout,
        StringBuilder stderr,
        long started,
        bool cleanupCompleted,
        bool timedOut = false,
        bool cancelled = false,
        string? failureReason = null)
    {
        var exitCode = -1;
        try { if (proc.HasExited) exitCode = proc.ExitCode; }
        catch { }
        return new ProcessResult(exitCode, stdout.ToString(), stderr.ToString())
        {
            CleanupCompleted = cleanupCompleted,
            TimedOut = timedOut,
            Cancelled = cancelled,
            Duration = Stopwatch.GetElapsedTime(started),
            FailureReason = failureReason,
        };
    }

    public sealed class RunningProcess : IDisposable, IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _stdout = new();
        private readonly StringBuilder _stderr = new();
        private readonly Task _stdoutTask;
        private readonly Task _stderrTask;
        private readonly TaskCompletionSource<ProcessResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly long _started = Stopwatch.GetTimestamp();
        private int _stopRequested;
        private int _disposed;

        internal RunningProcess(Process process, Action<string>? onStdOut, Action<string>? onStdErr)
        {
            _process = process;
            _stdoutTask = ReadLinesAsync(process.StandardOutput, _stdout, onStdOut);
            _stderrTask = ReadLinesAsync(process.StandardError, _stderr, onStdErr);
            _ = CompleteAsync();
        }

        public bool IsRunning
        {
            get
            {
                try { return !_process.HasExited; }
                catch { return false; }
            }
        }

        public Task<ProcessResult> Completion => _completion.Task;

        public async Task<ProcessResult> StopAsync(CancellationToken ct = default)
        {
            Interlocked.Exchange(ref _stopRequested, 1);
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch { }
            return await Completion.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }

        private async Task CompleteAsync()
        {
            try
            {
                await _process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(_stdoutTask, _stderrTask).ConfigureAwait(false);
                var result = Snapshot(_process, _stdout, _stderr, _started,
                    cleanupCompleted: true,
                    cancelled: Volatile.Read(ref _stopRequested) != 0,
                    failureReason: Volatile.Read(ref _stopRequested) != 0 ? "Process stopped by owner." : null);
                _completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                _completion.TrySetResult(Snapshot(_process, _stdout, _stderr, _started,
                    cleanupCompleted: false,
                    cancelled: Volatile.Read(ref _stopRequested) != 0,
                    failureReason: ex.Message));
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Interlocked.Exchange(ref _stopRequested, 1);
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch { }
            try { _completion.Task.GetAwaiter().GetResult(); } catch { }
            try { _process.Dispose(); } catch { }
        }

        public async ValueTask DisposeAsync()
        {
            try { await StopAsync().ConfigureAwait(false); }
            catch { }
            Dispose();
        }
    }
}
