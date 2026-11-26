using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

#if DEBUG
            if (Environment.GetCommandLineArgs().Any(
                argument => string.Equals(argument, "--preview", StringComparison.OrdinalIgnoreCase)))
            {
                PreviewScreens.Run();
                return;
            }
#endif

            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LanBridge");
            Directory.CreateDirectory(appData);

            var configPath = Path.Combine(appData, "config.json");
            var logPath = Path.Combine(appData, "lanbridge.log");
            var log = new FileLogger(logPath);

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, args) =>
            {
                log.Error("Unhandled UI exception", args.Exception);
                MessageBox.Show(
                    "LanBridge hit an unexpected error. The details were written to the log.\n\n"
                    + logPath,
                    "LanBridge",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                log.Error("Unhandled exception", args.ExceptionObject as Exception);
            };

            if (!SingleInstance.TryAcquire(out var mutex))
            {
                if (!Environment.GetCommandLineArgs().Any(
                    argument => string.Equals(argument, "--tray", StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(
                        "LanBridge is already running. Look for it in the notification area.",
                        "LanBridge",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                log.Dispose();
                return;
            }

            DeviceIdentity identity = null;

            try
            {
                var firstRun = !File.Exists(configPath);
                var config = firstRun
                    ? AppConfig.CreateDefault()
                    : AppConfig.Load(configPath, log);

                if (!firstRun)
                {
                    try
                    {
                        var startAtSignIn = Startup.IsEnabled();
                        if (config.StartWithWindows != startAtSignIn)
                        {
                            config.StartWithWindows = startAtSignIn;
                            config.Save(configPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Warn("Could not read the startup setting: " + ex.Message);
                    }
                }

                if (firstRun)
                {
                    bool addFirewallRule;
                    using (var setup = new FirstRunForm(config))
                    {
                        if (setup.ShowDialog() != DialogResult.OK) return;
                        config = setup.Config;
                        config.StartWithWindows = setup.StartWithWindows;
                        addFirewallRule = setup.AddFirewallRule;
                        config.Save(configPath);
                    }

                    try
                    {
                        Startup.SetEnabled(config.StartWithWindows, Application.ExecutablePath);
                    }
                    catch (Exception ex)
                    {
                        log.Warn("Could not change startup: " + ex.Message);
                    }

                    if (addFirewallRule && !Firewall.IsRulePresentFor(Application.ExecutablePath))
                    {
                        bool added;
                        try
                        {
                            added = Firewall.AddRuleAsync(Application.ExecutablePath).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            log.Warn("Could not add the firewall rule: " + ex.Message);
                            added = false;
                        }

                        if (!added)
                        {
                            MessageBox.Show(
                                "LanBridge is ready, but Windows Firewall was not updated.\n\n"
                                + "You can repair the rule later from Settings.",
                                "LanBridge",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                    }
                }

                ReceiveFolderCleanup.DeleteStalePartFiles(
                    config.ReceiveFolder,
                    DateTime.UtcNow,
                    TimeSpan.FromHours(1),
                    log);

                identity = DeviceIdentity.LoadOrCreate(
                    Path.Combine(appData, "identity"),
                    config.DeviceId,
                    log);
                var peers = PeerStore.Load(Path.Combine(appData, "peers.json"), log);

                using (var context = new TrayApplicationContext(config, identity, peers, log))
                {
                    if (Environment.GetCommandLineArgs().Any(
                        argument => string.Equals(argument, "--show", StringComparison.OrdinalIgnoreCase)))
                    {
                        context.ShowDeviceList();
                    }

                    Application.Run(context);
                }
            }
            catch (Exception ex)
            {
                log.Error("LanBridge failed to start", ex);
                MessageBox.Show(
                    "LanBridge could not start.\n\n" + ex.Message
                    + "\n\nLog file: " + logPath,
                    "LanBridge",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                identity?.Dispose();
                log.Dispose();
                try { mutex.ReleaseMutex(); } catch { }
                mutex.Dispose();
            }
        }
    }
}
