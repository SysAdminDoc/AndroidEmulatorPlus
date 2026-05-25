using System.Diagnostics;
using System.Text;

namespace AndroidEmulatorPlus.Helpers;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
    public string Combined => string.IsNullOrEmpty(StdErr) ? StdOut : $"{StdOut}\n{StdErr}";
}

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string exe,
        IEnumerable<string> args,
        string? workingDir = null,
        IDictionary<string, string?>? extraEnv = null,
        Action<string>? onStdOut = null,
        Action<string>? onStdErr = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (extraEnv != null)
            foreach (var kv in extraEnv)
                psi.Environment[kv.Key] = kv.Value;

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stdout.AppendLine(e.Data);
            onStdOut?.Invoke(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stderr.AppendLine(e.Data);
            onStdErr?.Invoke(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var timeoutCts = timeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue) timeoutCts.CancelAfter(timeout.Value);

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.HasValue)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{exe} exceeded timeout of {timeout.Value}.");
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        return new ProcessResult(proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Runs a process with a finite stream of stdin lines piped to it before reading
    /// its output. Used for `sdkmanager --licenses` / install (y-spam) and
    /// `avdmanager create avd` (writes a single "no" to skip the profile prompt).
    /// </summary>
    public static async Task<ProcessResult> RunWithStdinAsync(
        string exe,
        IEnumerable<string> args,
        IEnumerable<string> stdinLines,
        string? workingDir = null,
        IDictionary<string, string?>? extraEnv = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (extraEnv != null)
            foreach (var kv in extraEnv) psi.Environment[kv.Key] = kv.Value;

        using var proc = Process.Start(psi)!;
        try
        {
            foreach (var line in stdinLines)
            {
                if (proc.HasExited) break;
                await proc.StandardInput.WriteLineAsync(line);
            }
            proc.StandardInput.Close();
        }
        catch { /* the process may have already closed stdin */ }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue) linked.CancelAfter(timeout.Value);

        try
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(linked.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(linked.Token);
            await proc.WaitForExitAsync(linked.Token);
            return new ProcessResult(proc.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.HasValue)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{exe} exceeded timeout of {timeout.Value}.");
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Runs a process and streams stdout/stderr lines to <paramref name="onLine"/>.
    /// No buffered StringBuilder — useful for long-running tools (logcat, rootAVD.sh)
    /// whose output is the point and the stdout buffer would grow unbounded.
    /// </summary>
    public static async Task<int> StreamAsync(
        string exe,
        IEnumerable<string> args,
        Action<string> onLine,
        string? workingDir = null,
        IDictionary<string, string?>? extraEnv = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (extraEnv != null)
            foreach (var kv in extraEnv) psi.Environment[kv.Key] = kv.Value;

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue) linked.CancelAfter(timeout.Value);
        try
        {
            await proc.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.HasValue)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{exe} exceeded timeout of {timeout.Value}.");
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        return proc.ExitCode;
    }

    /// <summary>Launches a process detached (not awaited) and returns immediately.</summary>
    public static Process StartDetached(string exe, IEnumerable<string> args, string? workingDir = null,
        IDictionary<string, string?>? extraEnv = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (extraEnv != null)
            foreach (var kv in extraEnv)
                psi.Environment[kv.Key] = kv.Value;
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();
        return proc;
    }
}
