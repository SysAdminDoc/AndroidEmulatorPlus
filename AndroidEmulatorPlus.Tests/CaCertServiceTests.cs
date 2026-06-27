using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class CaCertServiceTests
{
    [Fact]
    public void AndroidCertificateFileName_IsOpenSslStyleHashName()
    {
        var der = CreateCertificateDer();

        var name = CaCertService.GetAndroidSystemCertificateFileName(der);

        Assert.Matches("^[0-9a-f]{8}\\.0$", name);
    }

    [Fact]
    public void PemRoundTrip_ReturnsOriginalDer()
    {
        var der = CreateCertificateDer();
        var path = Path.Combine(Path.GetTempPath(), $"aep-cert-{Guid.NewGuid():N}.pem");
        try
        {
            CaCertService.WritePemFile(der, path);

            var parsed = CaCertService.ReadCertAsDer(path);

            Assert.Equal(der, parsed);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static byte[] CreateCertificateDer()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=AndroidEmulatorPlus Test CA",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        return cert.Export(X509ContentType.Cert);
    }
}
