using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanBridge.Core
{
    public static class Tls
    {
        public static string Fingerprint(X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(certificate.RawData);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static SslStream CreateServer(Stream inner, X509Certificate2 serverCertificate)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (serverCertificate == null) throw new ArgumentNullException(nameof(serverCertificate));
            return new SslStream(inner, false, (sender, certificate, chain, errors) => true);
        }

        public static SslStream CreateClient(
            Stream inner,
            X509Certificate2 clientCertificate,
            string expectedServerFingerprint)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (clientCertificate == null) throw new ArgumentNullException(nameof(clientCertificate));
            if (string.IsNullOrWhiteSpace(expectedServerFingerprint))
                throw new ArgumentNullException(nameof(expectedServerFingerprint));

            return new SslStream(inner, false, (sender, certificate, chain, errors) =>
            {
                if (certificate == null) return false;

                var verifiable = certificate as X509Certificate2;
                if (verifiable != null)
                {
                    return FixedTimeEquals(Fingerprint(verifiable), expectedServerFingerprint);
                }

                using (var copy = new X509Certificate2(certificate))
                {
                    return FixedTimeEquals(Fingerprint(copy), expectedServerFingerprint);
                }
            });
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;

            var difference = 0;
            for (var i = 0; i < left.Length; i++)
            {
                difference |= char.ToLowerInvariant(left[i]) ^ char.ToLowerInvariant(right[i]);
            }
            return difference == 0;
        }
    }
}
