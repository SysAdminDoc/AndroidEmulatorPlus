using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace AndroidEmulatorPlus.Services;

/// <summary>
/// Installs a proxy CA certificate (Burp Suite, mitmproxy, etc.) into Android's
/// system trust store through a small Magisk module. This avoids fragile
/// read-write /system remounts on dynamic partitions.
/// </summary>
public sealed class CaCertService
{
    private readonly AdbService _adb;
    private readonly LogService _log;

    public CaCertService(AdbService adb, LogService log)
    {
        _adb = adb;
        _log = log;
    }

    public async Task<bool> InstallCaCertAsync(string serial, string certPath,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(certPath))
        {
            _log.Error($"Certificate file not found: {certPath}");
            return false;
        }

        byte[] derBytes;
        string certSubject;
        string certFileName;
        try
        {
            progress?.Report("Reading certificate...");
            derBytes = ReadCertAsDer(certPath);
            using var cert = X509CertificateLoader.LoadCertificate(derBytes);
            certSubject = cert.Subject;
            certFileName = GetAndroidSystemCertificateFileName(derBytes);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to parse certificate: {ex.Message}");
            return false;
        }

        var hashName = Path.GetFileNameWithoutExtension(certFileName);
        var moduleId = $"aep_ca_{hashName}";
        var tmpRoot = Path.Combine(Path.GetTempPath(), "AndroidEmulatorPlus", $"ca-{Guid.NewGuid():N}");
        var tmpCert = Path.Combine(tmpRoot, certFileName);
        var tmpProp = Path.Combine(tmpRoot, $"{moduleId}.prop");

        try
        {
            Directory.CreateDirectory(tmpRoot);
            WritePemFile(derBytes, tmpCert);
            File.WriteAllText(tmpProp, BuildModuleProp(moduleId, hashName, certSubject), new UTF8Encoding(false));

            var remoteTmpCert = $"/data/local/tmp/{certFileName}";
            var remoteTmpProp = $"/data/local/tmp/{moduleId}.prop";
            var remoteModule = $"/data/adb/modules/{moduleId}";
            var remoteCertDir = $"{remoteModule}/system/etc/security/cacerts";
            var remoteCert = $"{remoteCertDir}/{certFileName}";
            var remoteProp = $"{remoteModule}/module.prop";

            progress?.Report("Pushing certificate module files...");
            var certPush = await _adb.PushAsync(serial, tmpCert, remoteTmpCert, ct);
            if (!certPush.Success)
            {
                _log.Error("Certificate push failed: " + certPush.Combined.Trim());
                return false;
            }

            var propPush = await _adb.PushAsync(serial, tmpProp, remoteTmpProp, ct);
            if (!propPush.Success)
            {
                _log.Error("Module metadata push failed: " + propPush.Combined.Trim());
                return false;
            }

            progress?.Report("Installing Magisk CA module...");
            var q = AdbService.ShellQuote;
            var install = string.Join(" && ", new[]
            {
                $"rm -rf {q(remoteModule)}",
                $"mkdir -p {q(remoteCertDir)}",
                $"cp {q(remoteTmpCert)} {q(remoteCert)}",
                $"cp {q(remoteTmpProp)} {q(remoteProp)}",
                $"chmod 755 {q(remoteModule)} {q($"{remoteModule}/system")} {q($"{remoteModule}/system/etc")} {q($"{remoteModule}/system/etc/security")} {q(remoteCertDir)}",
                $"chmod 644 {q(remoteCert)} {q(remoteProp)}",
                $"chown 0:0 {q(remoteCert)} {q(remoteProp)}",
                $"rm -f {q(remoteTmpCert)} {q(remoteTmpProp)}"
            });

            var installed = await _adb.RootShellAsync(serial, install, ct);
            if (!installed.Success)
            {
                _log.Error("CA module install failed: " + installed.Combined.Trim());
                return false;
            }

            _log.Success($"CA certificate module installed as {moduleId}. Reboot the emulator to activate it.");
            return true;
        }
        finally
        {
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true); } catch { }
        }
    }

    public async Task<List<string>> ListSystemCertsAsync(string serial, CancellationToken ct = default)
    {
        var r = await _adb.RootShellAsync(serial,
            "ls /system/etc/security/cacerts/ 2>/dev/null; find /data/adb/modules -path '*/system/etc/security/cacerts/*.0' -type f 2>/dev/null",
            ct);
        if (!r.Success) return new();
        return r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.EndsWith(".0", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList()!;
    }

    public static byte[] ReadCertAsDer(string path)
    {
        var raw = File.ReadAllBytes(path);
        var text = Encoding.ASCII.GetString(raw);
        if (text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
        {
            using var cert = X509CertificateLoader.LoadCertificateFromFile(path);
            return cert.RawData;
        }
        return raw;
    }

    public static string GetAndroidSystemCertificateFileName(byte[] derBytes)
    {
        using var cert = X509CertificateLoader.LoadCertificate(derBytes);
        var hash = MD5.HashData(cert.SubjectName.RawData);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(hash);
        return $"{value:x8}.0";
    }

    public static void WritePemFile(byte[] derBytes, string path)
    {
        var pem = "-----BEGIN CERTIFICATE-----\n" +
                  Convert.ToBase64String(derBytes, Base64FormattingOptions.InsertLineBreaks) +
                  "\n-----END CERTIFICATE-----\n";
        File.WriteAllText(path, pem, new UTF8Encoding(false));
    }

    private static string BuildModuleProp(string moduleId, string hashName, string subject)
        => string.Join('\n', new[]
        {
            $"id={moduleId}",
            $"name=AndroidEmulatorPlus CA {hashName}",
            "version=1.0",
            "versionCode=1",
            "author=SysAdminDoc",
            $"description=System trust store CA certificate for {subject}"
        }) + "\n";
}
