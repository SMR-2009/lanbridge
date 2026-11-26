#if DEBUG
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    internal static class PreviewScreens
    {
        public static void Run()
        {
            var output = Path.Combine(Environment.CurrentDirectory, "docs", "screenshots");
            Directory.CreateDirectory(output);

            var peer = new PeerEndpoint(
                IPAddress.Parse("192.168.10.7"),
                45873,
                new string('a', 64),
                "device-a",
                "Laptop-A");

            Capture(new DeviceListForm(new PreviewHost()), output, "device-list");

            var firstRunConfig = AppConfig.CreateDefault();
            firstRunConfig.DeviceName = "Laptop-A";
            firstRunConfig.ReceiveFolder = @"C:\Users\Public\Downloads\LanBridge";
            Capture(new FirstRunForm(firstRunConfig), output, "first-run");

            using (var pairing = new PairingForm(peer))
            {
                pairing.Show();
                Application.DoEvents();
                pairing.ShowCode(new PairingCodeDisplay(peer, "482913", TimeSpan.FromMinutes(2)));
                Application.DoEvents();
                CaptureShown(pairing, output, "pairing");
                pairing.Close();
            }

            var offer = new TransferOffer(peer, "preview", new[]
            {
                new TransferFile("holiday-photos.zip", 84_000_000),
                new TransferFile("notes.pdf", 2_300_000),
                new TransferFile("screenshot.png", 840_000)
            });
            Capture(new ReceivePromptForm(offer), output, "receive-prompt");

            using (var progress = new ProgressForm("Sending files", false, () => { }))
            {
                progress.Show();
                Application.DoEvents();
                progress.Apply(new TransferProgress
                {
                    FileName = "holiday-photos.zip",
                    FileIndex = 1,
                    FileCount = 3,
                    BytesTransferred = 42_000_000,
                    TotalBytes = 84_000_000
                });
                Application.DoEvents();
                CaptureShown(progress, output, "progress");
                progress.Close();
            }

            Console.WriteLine("Screenshots written to " + output);
        }

        private static void Capture(Form form, string output, string name)
        {
            using (form)
            {
                form.Show();
                Application.DoEvents();
                form.Refresh();
                CaptureShown(form, output, name);
                form.Close();
            }
        }

        private static void CaptureShown(Form form, string output, string name)
        {
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
            }
        }

        private sealed class PreviewHost : IDeviceListHost
        {
            public IReadOnlyList<DeviceRow> BuildRows()
            {
                var now = DateTime.UtcNow;
                return new List<DeviceRow>
                {
                    new DeviceRow
                    {
                        Fingerprint = new string('a', 64),
                        DeviceId = "device-a",
                        Name = "Laptop-A",
                        IpAddress = "192.168.10.7",
                        Port = 45873,
                        State = DeviceState.Paired,
                        LastSeenUtc = now
                    },
                    new DeviceRow
                    {
                        Fingerprint = new string('b', 64),
                        DeviceId = "device-studio",
                        Name = "Studio-PC",
                        IpAddress = "192.168.10.9",
                        Port = 45873,
                        State = DeviceState.Available,
                        LastSeenUtc = now.AddSeconds(-4)
                    },
                    new DeviceRow
                    {
                        Fingerprint = new string('c', 64),
                        DeviceId = "device-old",
                        Name = "Old-Laptop",
                        IpAddress = "192.168.10.4",
                        Port = 45873,
                        State = DeviceState.Offline,
                        LastSeenUtc = now.AddHours(-6)
                    }
                };
            }

            public bool Discoverable => true;

            public Task<PairingAttemptResult> PairAsync(DeviceRow row)
            {
                return Task.FromResult(PairingAttemptResult.Fail("preview"));
            }

            public Task<TransferResult> SendAsync(DeviceRow row, IReadOnlyList<string> files)
            {
                return Task.FromResult(TransferResult.Fail("preview"));
            }

            public void Unpair(DeviceRow row)
            {
            }

            public void RefreshDiscovery()
            {
            }

            public void ShowSettings()
            {
            }

            public void ShowAbout()
            {
            }
        }
    }
}
#endif
