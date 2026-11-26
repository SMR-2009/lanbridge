using System;
using System.IO;
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

            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LanBridge");
            Directory.CreateDirectory(appData);

            var log = new FileLogger(Path.Combine(appData, "lanbridge.log"));
            DeviceIdentity identity = null;

            try
            {
                var config = AppConfig.Load(Path.Combine(appData, "config.json"), log);
                identity = DeviceIdentity.LoadOrCreate(
                    Path.Combine(appData, "identity"),
                    config.DeviceId,
                    log);
                var peers = PeerStore.Load(Path.Combine(appData, "peers.json"), log);

                using (var context = new TrayApplicationContext(config, identity, peers, log))
                {
                    Application.Run(context);
                }
            }
            catch (Exception ex)
            {
                log.Error("LanBridge failed to start", ex);
                MessageBox.Show(
                    "LanBridge could not start.\n\n" + ex.Message
                    + "\n\nLog file: " + Path.Combine(appData, "lanbridge.log"),
                    "LanBridge",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                identity?.Dispose();
                log.Dispose();
            }
        }
    }
}
