using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LanBridge.Core
{
    public static class PairingCode
    {
        private const string CodeDomain = "lanbridge-pair-v1";
        private const string ProofDomain = "lanbridge-pair-key-v1";
        private const string ProofLabel = "confirm";

        public static string Compute(
            string fpInitiator,
            string fpResponder,
            byte[] nonceInitiator,
            byte[] nonceResponder)
        {
            if (fpInitiator == null) throw new ArgumentNullException(nameof(fpInitiator));
            if (fpResponder == null) throw new ArgumentNullException(nameof(fpResponder));
            if (nonceInitiator == null) throw new ArgumentNullException(nameof(nonceInitiator));
            if (nonceResponder == null) throw new ArgumentNullException(nameof(nonceResponder));

            var low = string.CompareOrdinal(fpInitiator, fpResponder) <= 0 ? fpInitiator : fpResponder;
            var high = ReferenceEquals(low, fpInitiator) ? fpResponder : fpInitiator;

            var keyMaterial = Concat(
                Encoding.UTF8.GetBytes(CodeDomain),
                Encoding.UTF8.GetBytes(low),
                Encoding.UTF8.GetBytes(high));

            byte[] key;
            using (var sha = SHA256.Create())
            {
                key = sha.ComputeHash(keyMaterial);
            }

            byte[] sas;
            using (var hmac = new HMACSHA256(key))
            {
                sas = hmac.ComputeHash(Concat(nonceInitiator, nonceResponder));
            }

            var value = ((uint)sas[0] << 24) |
                        ((uint)sas[1] << 16) |
                        ((uint)sas[2] << 8) |
                        sas[3];

            return (value % 1000000).ToString("D6", CultureInfo.InvariantCulture);
        }

        public static byte[] ComputeProof(
            string code,
            byte[] nonceInitiator,
            byte[] nonceResponder,
            string fpInitiator,
            string fpResponder)
        {
            if (code == null) throw new ArgumentNullException(nameof(code));
            if (nonceInitiator == null) throw new ArgumentNullException(nameof(nonceInitiator));
            if (nonceResponder == null) throw new ArgumentNullException(nameof(nonceResponder));
            if (fpInitiator == null) throw new ArgumentNullException(nameof(fpInitiator));
            if (fpResponder == null) throw new ArgumentNullException(nameof(fpResponder));

            byte[] proofKey;
            using (var sha = SHA256.Create())
            {
                proofKey = sha.ComputeHash(Concat(
                    Encoding.UTF8.GetBytes(ProofDomain),
                    Encoding.UTF8.GetBytes(code)));
            }

            using (var hmac = new HMACSHA256(proofKey))
            {
                return hmac.ComputeHash(Concat(
                    Encoding.UTF8.GetBytes(ProofLabel),
                    nonceInitiator,
                    nonceResponder,
                    Encoding.UTF8.GetBytes(fpInitiator),
                    Encoding.UTF8.GetBytes(fpResponder)));
            }
        }

        public static bool VerifyProof(
            string code,
            byte[] nonceInitiator,
            byte[] nonceResponder,
            string fpInitiator,
            string fpResponder,
            byte[] proof)
        {
            if (string.IsNullOrEmpty(code) || proof == null) return false;
            var expected = ComputeProof(code, nonceInitiator, nonceResponder, fpInitiator, fpResponder);
            return FixedTimeEquals(expected, proof);
        }

        public static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;

            var difference = 0;
            for (var i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }
            return difference == 0;
        }

        private static byte[] Concat(params byte[][] parts)
        {
            var length = 0;
            foreach (var part in parts) length += part.Length;

            var result = new byte[length];
            var offset = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }
            return result;
        }
    }
}
