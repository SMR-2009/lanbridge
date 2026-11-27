using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LanBridge.Core
{
    public static class Firewall
    {
        public const string RuleName = "LanBridge";

        public static string BuildAddRuleArguments(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentNullException(nameof(exePath));

            return "advfirewall firewall add rule name=\"" + RuleName + "\" dir=in action=allow "
                 + "program=\"" + exePath + "\" enable=yes profile=private,public remoteip=LocalSubnet";
        }

        public static string BuildSetRuleArguments(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentNullException(nameof(exePath));

            return "advfirewall firewall set rule name=\"" + RuleName + "\" new "
                 + "dir=in action=allow program=\"" + exePath + "\" enable=yes profile=private,public remoteip=LocalSubnet";
        }

        public static string BuildDeleteRuleArguments()
        {
            return "advfirewall firewall delete rule name=\"" + RuleName + "\"";
        }

        public static bool IsRulePresent()
        {
            var exitCode = RunNetsh(
                "advfirewall firewall show rule name=\"" + RuleName + "\"",
                out var output);
            return exitCode == 0 && output.IndexOf(RuleName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsRulePresentFor(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return false;

            var exitCode = RunNetsh(
                "advfirewall firewall show rule name=\"" + RuleName + "\" verbose",
                out var output);
            return exitCode == 0 && IsRuleOutputValid(output, exePath);
        }

        public static bool IsRuleOutputValid(string output, string exePath)
        {
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(exePath))
                return false;

            return string.Equals(ReadField(output, "Enabled"), "Yes", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ReadField(output, "Direction"), "In", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ReadField(output, "Action"), "Allow", StringComparison.OrdinalIgnoreCase) &&
                   ContainsProfile(ReadField(output, "Profiles"), "Private") &&
                   ContainsProfile(ReadField(output, "Profiles"), "Public") &&
                   string.Equals(ReadField(output, "RemoteIP"), "LocalSubnet", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       ReadField(output, "Program"),
                       Path.GetFullPath(exePath),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadField(string output, string field)
        {
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                if (!string.Equals(line.Substring(0, colon).Trim(), field, StringComparison.OrdinalIgnoreCase))
                    continue;
                return line.Substring(colon + 1).Trim();
            }

            return string.Empty;
        }

        private static bool ContainsProfile(string profiles, string profile)
        {
            return profiles.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(value => string.Equals(value.Trim(), profile, StringComparison.OrdinalIgnoreCase));
        }

        public static Task<bool> AddRuleAsync(string exePath)
        {
            var arguments = BuildAddRuleArguments(exePath);
            return Task.Run(() => RunElevated(arguments));
        }

        public static Task<bool> SetRuleProgramAsync(string exePath)
        {
            var arguments = BuildSetRuleArguments(exePath);
            return Task.Run(() => RunElevated(arguments));
        }

        public static Task<bool> RemoveRuleAsync()
        {
            var arguments = BuildDeleteRuleArguments();
            return Task.Run(() => RunElevated(arguments));
        }

        private static bool RunElevated(string arguments)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Normal
                };

                using (var process = Process.Start(info))
                {
                    if (process == null) return false;
                    if (!process.WaitForExit(120000))
                    {
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    return process.ExitCode == 0;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static int RunNetsh(string arguments, out string output)
        {
            output = string.Empty;
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(info))
                {
                    if (process == null) return -1;
                    var standard = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(10000))
                    {
                        try { process.Kill(); } catch { }
                        return -1;
                    }
                    output = standard + error;
                    return process.ExitCode;
                }
            }
            catch
            {
                return -1;
            }
        }
    }
}
