using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LanBridge.Core
{
    public static class Sanitizer
    {
        private const int MaxDeviceNameLength = 64;
        private const string DeviceFallback = "Windows PC";

        private static readonly Dictionary<string, string> Errors =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "hash_mismatch", "The file did not match its checksum." },
                { "disk_full", "There is not enough free space on the receiving laptop." },
                { "not_paired", "These devices are not paired." },
                { "hidden", "This laptop is not discoverable." },
                { "busy_inbound", "The other laptop is already receiving a file." },
                { "busy", "The other laptop is busy." },
                { "busy_outbound", "This laptop is already sending files." },
                { "offline", "The other laptop is not reachable right now. Wait for it to appear on the network." },
                { "no_endpoint", "The other laptop has no known address yet. Wait for it to appear on the network." },
                { "identity_changed", "This device's identity changed. Unpair the old entry and pair again." },
                { "pairing_failed", "Pairing failed. Try again." },
                { "no_files", "No files were selected." },
                { "not_lan", "The other laptop is not on your local network." },
                { "transfer_failed", "The transfer failed." },
                { "resume_mismatch", "The file changed since the interrupted transfer. The partial copy was removed; try again." },
                { "invalid_accept", "The other laptop sent an invalid transfer response." },
                { "cancelled", "The transfer was cancelled." },
                { "timeout", "The other laptop did not respond." },
                { "offer_rejected", "The other laptop declined the files." },
                { "version", "The other laptop is running an incompatible version." },
                { "wrong_code", "The pairing code was incorrect." },
                { "invalid", "The pairing request was invalid." }
            };

        public static string SanitizeDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return DeviceFallback;

            var builder = new StringBuilder(name.Length);
            foreach (var character in name)
            {
                if (character == '\r' || character == '\n' || character == '\t')
                {
                    builder.Append(' ');
                    continue;
                }
                if (char.IsControl(character)) continue;
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format) continue;
                builder.Append(character);
            }

            var result = CollapseWhitespace(builder.ToString()).Trim();
            if (result.Length > MaxDeviceNameLength)
                result = result.Substring(0, MaxDeviceNameLength).Trim();

            return string.IsNullOrEmpty(result) ? DeviceFallback : result;
        }

        public static string SanitizeLogValue(string value, int maxLength = 200)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (maxLength <= 0) return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (character == '\r' || character == '\n' || character == '\t')
                {
                    builder.Append(' ');
                    continue;
                }
                if (char.IsControl(character)) continue;
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format) continue;
                builder.Append(character);
            }

            var result = builder.ToString().Trim();
            if (result.Length > maxLength) result = result.Substring(0, maxLength);
            return result;
        }

        public static string MapError(string code)
        {
            if (string.IsNullOrEmpty(code)) return "The transfer failed.";
            return Errors.TryGetValue(code, out var message) ? message : "The transfer failed.";
        }

        private static string CollapseWhitespace(string value)
        {
            var builder = new StringBuilder(value.Length);
            var previousSpace = false;
            foreach (var character in value)
            {
                if (char.IsWhiteSpace(character))
                {
                    if (!previousSpace) builder.Append(' ');
                    previousSpace = true;
                    continue;
                }
                builder.Append(character);
                previousSpace = false;
            }
            return builder.ToString();
        }
    }
}
