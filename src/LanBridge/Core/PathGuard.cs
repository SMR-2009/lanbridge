using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LanBridge.Core
{
    public static class PathGuard
    {
        private const int MaxNameLength = 180;

        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private static readonly HashSet<char> Invalid = BuildInvalidSet();

        public static string SanitizeFileName(string remoteName)
        {
            if (string.IsNullOrEmpty(remoteName)) return "file";

            var normalized = remoteName.Replace('/', '\\');
            var lastSeparator = normalized.LastIndexOf('\\');
            var name = lastSeparator >= 0 ? normalized.Substring(lastSeparator + 1) : normalized;
            if (string.IsNullOrEmpty(name)) return "file";

            var builder = new StringBuilder(name.Length);
            foreach (var character in name)
            {
                if (Invalid.Contains(character)) continue;
                if (char.IsControl(character)) continue;
                var category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.Control || category == UnicodeCategory.Format) continue;
                builder.Append(character);
            }

            var result = builder.ToString().TrimEnd(' ', '.');
            if (string.IsNullOrEmpty(result) || result == "." || result == "..") result = "file";

            result = LimitLength(result, MaxNameLength);

            var baseName = Path.GetFileNameWithoutExtension(result);
            if (Reserved.Contains(baseName)) result = "_" + result;
            result = LimitLength(result, MaxNameLength).TrimEnd(' ', '.');

            return string.IsNullOrEmpty(result) ? "file" : result;
        }

        public static string GetUniquePath(string folder, string fileName, int startIndex = 0)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentNullException(nameof(folder));
            if (string.IsNullOrEmpty(fileName)) fileName = "file";

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var start = Math.Max(0, startIndex);

            for (var index = start; index < start + 10000; index++)
            {
                var candidateName = index == 0
                    ? fileName
                    : baseName + " (" + index.ToString(CultureInfo.InvariantCulture) + ")" + extension;
                var candidate = Path.Combine(folder, candidateName);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }

            throw new IOException("Could not find a unique file name.");
        }

        private static string LimitLength(string name, int max)
        {
            if (name.Length <= max) return name;

            var extension = Path.GetExtension(name);
            if (extension.Length >= max) return name.Substring(0, max);

            var baseName = Path.GetFileNameWithoutExtension(name);
            var keep = max - extension.Length;
            if (keep < 1) return name.Substring(0, max);
            if (baseName.Length > keep) baseName = baseName.Substring(0, keep);
            return baseName + extension;
        }

        private static HashSet<char> BuildInvalidSet()
        {
            var set = new HashSet<char>(Path.GetInvalidFileNameChars());
            foreach (var character in "<>:\"/\\|?*") set.Add(character);
            return set;
        }
    }
}
