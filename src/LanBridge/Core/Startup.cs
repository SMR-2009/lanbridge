using System;
using Microsoft.Win32;

namespace LanBridge.Core
{
    public static class Startup
    {
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "LanBridge";

        public static bool IsEnabled(string runKeyPath = null)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(runKeyPath ?? RunKeyPath, false))
            {
                var value = key?.GetValue(ValueName) as string;
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        public static void SetEnabled(bool enabled, string exePath, string runKeyPath = null)
        {
            var path = runKeyPath ?? RunKeyPath;

            if (enabled)
            {
                if (string.IsNullOrWhiteSpace(exePath))
                    throw new ArgumentNullException(nameof(exePath));

                using (var key = Registry.CurrentUser.CreateSubKey(path))
                {
                    key?.SetValue(ValueName, "\"" + exePath + "\" --tray", RegistryValueKind.String);
                }
                return;
            }

            using (var key = Registry.CurrentUser.OpenSubKey(path, true))
            {
                key?.DeleteValue(ValueName, false);
            }
        }
    }
}
