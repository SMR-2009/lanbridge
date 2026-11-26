using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanBridge.Core
{
    public sealed class DeviceIdentity : IDisposable
    {
        private const string IdentityFileName = "identity.pfx.dpapi";

        public string DeviceId { get; }
        public X509Certificate2 Certificate { get; }
        public string Fingerprint { get; }

        private DeviceIdentity(string deviceId, X509Certificate2 certificate)
        {
            DeviceId = deviceId;
            Certificate = certificate;
            Fingerprint = Tls.Fingerprint(certificate);
        }

        public static DeviceIdentity LoadOrCreate(string appDataDir, string deviceId, ILogger log)
        {
            if (string.IsNullOrWhiteSpace(appDataDir)) throw new ArgumentNullException(nameof(appDataDir));
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentNullException(nameof(deviceId));
            log = log ?? NullLogger.Instance;

            Directory.CreateDirectory(appDataDir);
            var path = Path.Combine(appDataDir, IdentityFileName);

            if (File.Exists(path))
            {
                try
                {
                    var protectedBytes = File.ReadAllBytes(path);
                    var pfx = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                    var certificate = LoadCertificate(pfx, log);
                    return new DeviceIdentity(deviceId, certificate);
                }
                catch (Exception ex)
                {
                    var quarantine = Path.Combine(
                        appDataDir,
                        "identity.corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".pfx");
                    try { File.Move(path, quarantine); } catch { }
                    log.Warn("Identity was unreadable; quarantined to " + quarantine);
                    log.Error("Identity load failed", ex);
                }
            }

            return Create(appDataDir, deviceId, log);
        }

        private static DeviceIdentity Create(string appDataDir, string deviceId, ILogger log)
        {
            using (var rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    "CN=LanBridge-" + deviceId,
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection
                    {
                        new Oid("1.3.6.1.5.5.7.3.1"),
                        new Oid("1.3.6.1.5.5.7.3.2")
                    },
                    false));

                using (var certificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddYears(10)))
                {
                    var pfx = certificate.Export(X509ContentType.Pfx, string.Empty);
                    var protectedBytes = ProtectedData.Protect(pfx, null, DataProtectionScope.CurrentUser);
                    var path = Path.Combine(appDataDir, IdentityFileName);
                    var tmp = path + ".tmp";

                    File.WriteAllBytes(tmp, protectedBytes);
                    if (File.Exists(path)) File.Replace(tmp, path, null);
                    else File.Move(tmp, path);

                    var loaded = LoadCertificate(pfx, log);
                    return new DeviceIdentity(deviceId, loaded);
                }
            }
        }

        private static X509Certificate2 LoadCertificate(byte[] pfx, ILogger log)
        {
            // Schannel on .NET Framework 4.8 cannot authenticate with an ephemeral
            // key container ("No credentials are available in the security package"),
            // so the runtime copy uses the user profile's key container. The DPAPI
            // blob remains canonical; SECURITY.md documents this fallback.
            log.Info("Using UserKeySet for the runtime certificate; the DPAPI blob remains canonical.");
            return new X509Certificate2(
                pfx,
                string.Empty,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
        }

        public void Dispose()
        {
            Certificate?.Dispose();
        }
    }
}
