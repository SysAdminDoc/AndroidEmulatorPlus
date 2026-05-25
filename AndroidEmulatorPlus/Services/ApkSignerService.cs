using System.Text.RegularExpressions;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

public sealed record SignerInfo(bool Verified, string? Sha256, string Raw);

/// <summary>
/// Thin wrapper around <c>build-tools/&lt;ver&gt;/apksigner.bat verify --print-certs</c>
/// (R-08). Used to extract a signing cert SHA-256 from an APK before installing and
/// to compare it against the installed package's cert on the device.
/// </summary>
public sealed class ApkSignerService
{
    private readonly SdkLocator _sdk;
    private readonly LogService _log;

    public ApkSignerService(SdkLocator sdk, LogService log)
    {
        _sdk = sdk;
        _log = log;
    }

    public bool IsAvailable => _sdk.ApkSignerBat is not null;

    public async Task<SignerInfo> InspectAsync(string apkPath, CancellationToken ct = default)
    {
        if (_sdk.ApkSignerBat is null)
            return new SignerInfo(false, null, "apksigner.bat not found in SDK build-tools");
        var r = await ProcessRunner.RunAsync("cmd.exe",
            new[] { "/c", _sdk.ApkSignerBat, "verify", "--print-certs", apkPath },
            timeout: TimeSpan.FromMinutes(1),
            ct: ct);
        var verified = r.ExitCode == 0;
        var sha = ExtractCertSha(r.Combined);
        return new SignerInfo(verified, sha, r.Combined);
    }

    private static string? ExtractCertSha(string text)
    {
        var m = Regex.Match(text, @"Signer #?\d+ certificate SHA-256 digest:\s*([0-9a-fA-F]{64})");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Returns the SHA-256 of the certificate of <paramref name="pkg"/> as installed
    /// on the device. Uses <c>pm dump &lt;pkg&gt;</c> which prints signing info.
    /// Returns null when the package is not installed or no cert info was returned.
    /// </summary>
    public async Task<string?> InstalledCertShaAsync(AdbService adb, string serial, string pkg, CancellationToken ct = default)
    {
        var r = await adb.ShellAsync(serial, $"pm dump {pkg}", ct);
        if (!r.Success) return null;
        // `pm dump` includes a line like:
        //   signatures=PackageSignatures{… [signatures-sha256=<hex>] }
        // The exact wording shifts between AOSP versions; match any 64-hex run
        // following 'sha256' (case-insensitive) to be tolerant.
        var m = Regex.Match(r.StdOut, @"sha256[^0-9a-fA-F]+([0-9a-fA-F]{64})", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }
}
