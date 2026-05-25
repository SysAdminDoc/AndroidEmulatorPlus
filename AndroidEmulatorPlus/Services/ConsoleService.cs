using AndroidEmulatorPlus.Helpers;
using System.Text;

namespace AndroidEmulatorPlus.Services;

/// <summary>
/// Sends commands through <c>adb -s &lt;serial&gt; emu …</c> — the emulator console
/// proxy. This is the same surface as telnetting localhost:5554 but without the
/// auth-token dance because adb already authenticated the session.
///
/// Reference: https://developer.android.com/studio/run/emulator-console
/// </summary>
public sealed class ConsoleService
{
    private readonly SdkLocator _sdk;
    private readonly LogService _log;

    public ConsoleService(SdkLocator sdk, LogService log)
    {
        _sdk = sdk;
        _log = log;
    }

    private static readonly Dictionary<string, string?> NoPathConv = new()
    {
        ["MSYS_NO_PATHCONV"] = "1",
        ["MSYS2_ARG_CONV_EXCL"] = "*",
    };

    /// <summary>Send a free-form `adb emu …` command (e.g. "geo fix -122.084 37.422").</summary>
    public Task<ProcessResult> SendAsync(string serial, IEnumerable<string> emuArgs, CancellationToken ct = default)
    {
        var args = new List<string> { "-s", serial, "emu" };
        args.AddRange(emuArgs);
        return ProcessRunner.RunAsync(_sdk.AdbRequired, args, extraEnv: NoPathConv, ct: ct);
    }

    /// <summary>
    /// Splits a free-form emulator-console command into argv tokens. Whitespace
    /// separates tokens, while single or double quotes preserve spaces inside an
    /// argument, e.g. <c>sms send 5551234 "Hello, world"</c>.
    /// </summary>
    public static IReadOnlyList<string> ParseEmuArgs(string input)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        var tokenStarted = false;
        var escaping = false;

        foreach (var ch in input)
        {
            if (escaping)
            {
                current.Append(ch);
                tokenStarted = true;
                escaping = false;
                continue;
            }

            if (quote is not null)
            {
                if (ch == '\\')
                {
                    escaping = true;
                    tokenStarted = true;
                    continue;
                }
                if (ch == quote)
                {
                    quote = null;
                    tokenStarted = true;
                    continue;
                }
                current.Append(ch);
                tokenStarted = true;
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                tokenStarted = true;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (tokenStarted)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    tokenStarted = false;
                }
                continue;
            }

            current.Append(ch);
            tokenStarted = true;
        }

        if (escaping) current.Append('\\');
        if (quote is not null) throw new FormatException("Unclosed quote in console command.");
        if (tokenStarted) args.Add(current.ToString());
        return args;
    }

    public Task<ProcessResult> GeoFixAsync(string serial, double lng, double lat, CancellationToken ct = default)
        => SendAsync(serial, new[] { "geo", "fix", lng.ToString(System.Globalization.CultureInfo.InvariantCulture), lat.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ct);

    public Task<ProcessResult> PowerCapacityAsync(string serial, int percent, CancellationToken ct = default)
        => SendAsync(serial, new[] { "power", "capacity", percent.ToString() }, ct);

    public Task<ProcessResult> PowerStatusAsync(string serial, string state, CancellationToken ct = default)
        => SendAsync(serial, new[] { "power", "status", state }, ct);

    public Task<ProcessResult> GsmCallAsync(string serial, string number, CancellationToken ct = default)
        => SendAsync(serial, new[] { "gsm", "call", number }, ct);

    public Task<ProcessResult> SmsSendAsync(string serial, string number, string text, CancellationToken ct = default)
        => SendAsync(serial, new[] { "sms", "send", number, text }, ct);

    public Task<ProcessResult> NetworkSpeedAsync(string serial, string preset, CancellationToken ct = default)
        => SendAsync(serial, new[] { "network", "speed", preset }, ct);

    public Task<ProcessResult> NetworkDelayAsync(string serial, string preset, CancellationToken ct = default)
        => SendAsync(serial, new[] { "network", "delay", preset }, ct);

    /// <summary>Reads the clipboard text via cmd clipboard get-primary (B-07).</summary>
    public Task<ProcessResult> ClipboardGetAsync(AdbService adb, string serial, CancellationToken ct = default)
        => adb.ShellAsync(serial, "cmd clipboard get-primary", ct);

    /// <summary>Sets the clipboard text via cmd clipboard set-primary (B-07).</summary>
    public Task<ProcessResult> ClipboardSetAsync(AdbService adb, string serial, string text, CancellationToken ct = default)
    {
        var escaped = text.Replace("'", "'\\''");
        return adb.ShellAsync(serial, $"cmd clipboard set-primary '{escaped}'", ct);
    }
}
