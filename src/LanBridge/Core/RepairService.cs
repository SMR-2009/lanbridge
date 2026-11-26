using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace LanBridge.Core
{
    public sealed class RepairReport
    {
        public List<string> Steps { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public bool Success => Warnings.Count == 0;

        public string Summary
        {
            get
            {
                if (Success) return "Repair complete.";
                return Warnings.Count == 1
                    ? "Repair finished with one warning."
                    : "Repair finished with " + Warnings.Count + " warnings.";
            }
        }
    }

    public static class RepairService
    {
        public static async Task<RepairReport> RunAsync(
            AppConfig config,
            string configPath,
            string exePath,
            ILogger log = null,
            bool repairFirewall = true,
            bool manageStartup = true)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(configPath)) throw new ArgumentNullException(nameof(configPath));
            log = log ?? NullLogger.Instance;

            var report = new RepairReport();

            try
            {
                Directory.CreateDirectory(config.ReceiveFolder);
                report.Steps.Add("Receive folder is ready.");
            }
            catch (Exception ex)
            {
                report.Warnings.Add("Receive folder: " + Sanitizer.SanitizeLogValue(ex.Message));
                log.Warn("Repair could not create the receive folder: " + Sanitizer.SanitizeLogValue(ex.Message));
            }

            try
            {
                config.Save(configPath);
                report.Steps.Add("Settings were saved.");
            }
            catch (Exception ex)
            {
                report.Warnings.Add("Settings: " + Sanitizer.SanitizeLogValue(ex.Message));
                log.Warn("Repair could not save settings: " + Sanitizer.SanitizeLogValue(ex.Message));
            }

            if (manageStartup)
            {
                try
                {
                    Startup.SetEnabled(config.StartWithWindows, exePath);
                    report.Steps.Add(config.StartWithWindows
                        ? "Start at sign-in is registered."
                        : "Start at sign-in is disabled.");
                }
                catch (Exception ex)
                {
                    report.Warnings.Add("Startup: " + Sanitizer.SanitizeLogValue(ex.Message));
                    log.Warn("Repair could not update startup: " + Sanitizer.SanitizeLogValue(ex.Message));
                }
            }

            var stale = ReceiveFolderCleanup.DeleteStalePartFiles(
                config.ReceiveFolder,
                DateTime.UtcNow,
                TimeSpan.FromHours(1),
                log);
            if (stale > 0) report.Steps.Add(stale + " stale partial file(s) removed.");

            if (repairFirewall)
            {
                try
                {
                    var presentForThisExe = Firewall.IsRulePresentFor(exePath);
                    if (presentForThisExe)
                    {
                        report.Steps.Add("Firewall rule already covers this executable.");
                    }
                    else
                    {
                        var ruleExists = Firewall.IsRulePresent();
                        var repaired = ruleExists
                            ? await Firewall.SetRuleProgramAsync(exePath).ConfigureAwait(false)
                            : await Firewall.AddRuleAsync(exePath).ConfigureAwait(false);

                        if (repaired)
                        {
                            report.Steps.Add("Firewall rule repaired.");
                        }
                        else
                        {
                            report.Warnings.Add("Firewall rule was not updated.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    report.Warnings.Add("Firewall: " + Sanitizer.SanitizeLogValue(ex.Message));
                    log.Warn("Repair could not update the firewall rule: " + Sanitizer.SanitizeLogValue(ex.Message));
                }
            }

            log.Info("Repair finished: " + Sanitizer.SanitizeLogValue(report.Summary));
            return report;
        }
    }
}
