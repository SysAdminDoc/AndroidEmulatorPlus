using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace AndroidEmulatorPlus.Services;

public sealed record HashCheck(bool Ok, bool Known, string ActualHash, string? ExpectedHash, string Detail);

/// <summary>
/// SHA-256 verification against a small in-tree manifest (`Resources/known-hashes.json`).
///
/// The model is intentionally conservative because there is no authoritative public
/// hash source for either Magisk APK releases or the Google cmdline-tools ZIP:
///
/// - For *known* keys (entries appended after a maintainer smoke-test) a mismatch is a
///   hard failure — the partial file is deleted and the caller raises an error.
/// - For *unknown* keys (a Magisk version we haven't seen yet, or a new cmdline-tools
///   URL after the developer.android.com scrape) we compute and log the hash but do
///   not block — the user gets a "trust-on-first-use" experience instead of being
///   stuck on releases that pre-date their manifest copy.
/// </summary>
public sealed class HashVerificationService
{
    private readonly LogService _log;
    private readonly Dictionary<string, string> _magisk;
    private readonly Dictionary<string, string> _cmdlineTools;

    public HashVerificationService(LogService log)
    {
        _log = log;
        (_magisk, _cmdlineTools) = LoadManifest();
    }

    private static (Dictionary<string, string> magisk, Dictionary<string, string> cmdlineTools) LoadManifest()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("known-hashes.json", StringComparison.OrdinalIgnoreCase));
            if (resName is null) return (new(), new());
            using var s = asm.GetManifestResourceStream(resName)!;
            using var doc = JsonDocument.Parse(s);
            var magisk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cli = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("magisk", out var m) && m.ValueKind == JsonValueKind.Object)
                foreach (var p in m.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                        magisk[p.Name] = p.Value.GetString()!.Trim();
            if (doc.RootElement.TryGetProperty("cmdlineTools", out var c) && c.ValueKind == JsonValueKind.Object)
                foreach (var p in c.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                        cli[p.Name] = p.Value.GetString()!.Trim();
            return (magisk, cli);
        }
        catch
        {
            return (new(), new());
        }
    }

    public static string ComputeSha256(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        var bytes = SHA256.HashData(fs);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public HashCheck VerifyMagisk(string assetName, string filePath)
        => Verify(_magisk, assetName, filePath, "Magisk APK");

    public HashCheck VerifyCmdlineTools(string url, string filePath)
        => Verify(_cmdlineTools, url, filePath, "cmdline-tools ZIP");

    private HashCheck Verify(IReadOnlyDictionary<string, string> table, string key, string filePath, string label)
    {
        var actual = ComputeSha256(filePath);
        if (!table.TryGetValue(key, out var expected) || string.IsNullOrWhiteSpace(expected))
        {
            _log.Info($"{label} SHA-256 [{key}]: {actual} (unknown — recorded for next manifest update)");
            return new HashCheck(Ok: true, Known: false, ActualHash: actual, ExpectedHash: null,
                Detail: "no entry in known-hashes.json — trust-on-first-use");
        }
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            _log.Success($"{label} SHA-256 verified ({actual[..12]}…).");
            return new HashCheck(Ok: true, Known: true, ActualHash: actual, ExpectedHash: expected, Detail: "ok");
        }
        _log.Error($"{label} SHA-256 MISMATCH for [{key}]. Expected {expected}, got {actual}.");
        return new HashCheck(Ok: false, Known: true, ActualHash: actual, ExpectedHash: expected,
            Detail: $"expected {expected}, got {actual}");
    }
}
