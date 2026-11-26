using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    internal sealed class TrayApplicationContext : ApplicationContext, IDeviceListHost
    {
        private readonly AppConfig _config;
        private readonly string _configPath;
        private readonly DeviceIdentity _identity;
        private readonly PeerStore _peers;
        private readonly ILogger _log;
        private readonly WinFormsUi _ui;
        private readonly Control _uiHost;
        private readonly TransferServer _server;
        private readonly TransferClient _client;
        private readonly DiscoveryService _discovery;
        private readonly NotifyIcon _tray;
        private readonly Icon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private ToolStripMenuItem _devicesMenu;
        private ToolStripMenuItem _visibleItem;
        private DeviceListForm _deviceList;
        private CancellationTokenSource _outboundCts;
        private bool _sending;
        private bool _disposed;

        public TrayApplicationContext(
            AppConfig config,
            DeviceIdentity identity,
            PeerStore peers,
            ILogger log)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _peers = peers ?? throw new ArgumentNullException(nameof(peers));
            _log = log ?? NullLogger.Instance;
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LanBridge",
                "config.json");

            Directory.CreateDirectory(_config.ReceiveFolder);

            _uiHost = new Control();
            _uiHost.CreateControl();
            _ui = new WinFormsUi(_log, CancelInbound, CancelOutbound, _uiHost);
            var options = new TransferOptions();

            _server = new TransferServer(
                _config.TcpPort,
                _config.DeviceName,
                _identity,
                _peers,
                () => _config.ReceiveFolder,
                _ui,
                _log,
                options)
            {
                AutoAcceptFromPaired = _config.AutoAcceptFromPaired
            };

            _client = new TransferClient(
                _config.DeviceName,
                _identity,
                _peers,
                _ui,
                _log,
                options,
                _config.TcpPort);

            _discovery = new DiscoveryService(
                _config.UdpPort,
                _config.DeviceName,
                _identity,
                _peers,
                (peer, token) => _client.PingAsync(peer, token),
                _log)
            {
                Discoverable = _config.Discoverable
            };

            _discovery.DeviceConfirmed += device =>
            {
                _log.Info("Device found: " + Sanitizer.SanitizeLogValue(device.Name)
                    + " at " + device.IPAddress + ":" + device.Port);
            };
            _discovery.DeviceLost += device =>
            {
                _log.Info("Device lost: " + Sanitizer.SanitizeLogValue(device.Name));
            };

            _server.DevicePaired += record =>
            {
                _log.Info("Paired with " + Sanitizer.SanitizeLogValue(record.Name));
                _ui.RunOnUi(() =>
                {
                    if (_tray != null && _tray.Visible)
                    {
                        _tray.ShowBalloonTip(
                            2500,
                            "Paired",
                            "Paired with " + Sanitizer.SanitizeDeviceName(record.Name) + ".",
                            ToolTipIcon.Info);
                    }
                });
            };

            _trayIcon = LoadIcon();
            _tray = new NotifyIcon
            {
                Icon = _trayIcon,
                Text = "LanBridge - secure local file transfer",
                Visible = true
            };
            _menu = BuildMenu();
            _tray.ContextMenuStrip = _menu;
            _tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) OpenDeviceList(null);
            };

            try
            {
                _server.Start();
                _discovery.Start();
            }
            catch
            {
                _tray.Visible = false;
                _tray.Dispose();
                _trayIcon.Dispose();
                _discovery.Dispose();
                _server.Dispose();
                _ui.CloseAll();
                _uiHost.Dispose();
                throw;
            }
        }

        public IReadOnlyList<DeviceRow> BuildRows()
        {
            return DeviceRowMapper.Build(_peers.Peers, _discovery.Devices);
        }

        public bool Discoverable => _config.Discoverable;

        public Task<PairingAttemptResult> PairAsync(DeviceRow row)
        {
            if (!TryCreateEndpoint(row, out var endpoint))
            {
                _log.Warn("Pairing requested without an endpoint for " + Sanitizer.SanitizeLogValue(row?.Name));
                return Task.FromResult(PairingAttemptResult.Fail("no_endpoint"));
            }

            _log.Info("Pairing requested with " + Sanitizer.SanitizeLogValue(row.Name)
                + " at " + endpoint.Address + ":" + endpoint.Port);
            return _client.PairAsync(endpoint);
        }

        public async Task<TransferResult> SendAsync(DeviceRow row, IReadOnlyList<string> files)
        {
            if (_sending) return TransferResult.Fail("busy_outbound");
            if (files == null || files.Count == 0) return TransferResult.Fail("no_files");
            if (!TryCreateEndpoint(row, out var endpoint)) return TransferResult.Fail("no_endpoint");

            _sending = true;
            _outboundCts = new CancellationTokenSource();
            _log.Info("Send requested to " + Sanitizer.SanitizeLogValue(row.Name)
                + ": " + files.Count + " file(s)");
            try
            {
                var result = await _client.SendAsync(endpoint, files, _outboundCts.Token);
                _log.Info("Send to " + Sanitizer.SanitizeLogValue(row.Name)
                    + (result.Success ? " completed." : " ended: " + Sanitizer.SanitizeLogValue(result.ErrorCode)));
                return result;
            }
            finally
            {
                _sending = false;
                _outboundCts.Dispose();
                _outboundCts = null;
            }
        }

        public void Unpair(DeviceRow row)
        {
            if (row == null) return;
            _peers.Remove(row.Fingerprint);
            _peers.Save();
            _log.Info("Unpaired " + Sanitizer.SanitizeLogValue(row.Name));
        }

        public void RefreshDiscovery()
        {
            _discovery.Refresh();
        }

        public void ShowDeviceList()
        {
            OpenDeviceList(null);
        }

        public void ShowSettings()
        {
            OpenSettings();
        }

        public void ShowAbout()
        {
            using (var form = new AboutForm())
            {
                if (_deviceList != null && !_deviceList.IsDisposed) form.ShowDialog(_deviceList);
                else form.ShowDialog();
            }
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 9.5f) };

            var send = new ToolStripMenuItem("Send files…");
            send.Font = new Font(menu.Font, FontStyle.Bold);
            send.Click += (s, e) => SendFiles();
            menu.Items.Add(send);

            _devicesMenu = new ToolStripMenuItem("Devices");
            menu.Items.Add(_devicesMenu);
            menu.Items.Add(new ToolStripSeparator());

            _visibleItem = new ToolStripMenuItem("Visible on this network")
            {
                CheckOnClick = true,
                Checked = _config.Discoverable
            };
            _visibleItem.Click += (s, e) => SetVisible(_visibleItem.Checked);
            menu.Items.Add(_visibleItem);
            menu.Items.Add(new ToolStripMenuItem("Settings…", null, (s, e) => OpenSettings()));

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Open received folder", null, (s, e) => OpenReceiveFolder()));
            menu.Items.Add(new ToolStripMenuItem("About LanBridge", null, (s, e) => ShowAbout()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Quit", null, (s, e) => Quit()));

            menu.Opening += (s, e) => RebuildDevicesMenu();
            return menu;
        }

        private void RebuildDevicesMenu()
        {
            ClearAndDispose(_devicesMenu.DropDownItems);
            var peers = _peers.Peers
                .OrderBy(peer => Sanitizer.SanitizeDeviceName(peer.Name), StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (peers.Count == 0)
            {
                _devicesMenu.DropDownItems.Add(new ToolStripMenuItem("No paired devices") { Enabled = false });
            }
            else
            {
                foreach (var peer in peers)
                {
                    var item = new ToolStripMenuItem(Sanitizer.SanitizeDeviceName(peer.Name));
                    item.Click += async (s, e) => await SendToPeerAsync(peer);
                    _devicesMenu.DropDownItems.Add(item);
                }
            }

            _devicesMenu.DropDownItems.Add(new ToolStripSeparator());
            _devicesMenu.DropDownItems.Add("Pair new device…", null, (s, e) => OpenDeviceList(null));
            _devicesMenu.DropDownItems.Add("Refresh", null, (s, e) => _discovery.Refresh());
        }

        private void SendFiles()
        {
            var files = ChooseFiles();
            if (files == null || files.Length == 0) return;
            OpenDeviceList(files);
        }

        private async Task SendToPeerAsync(PeerRecord peer)
        {
            if (peer == null) return;

            var files = ChooseFiles();
            if (files == null || files.Length == 0) return;

            var row = new DeviceRow
            {
                Fingerprint = peer.Fingerprint,
                DeviceId = peer.DeviceId,
                Name = Sanitizer.SanitizeDeviceName(peer.Name),
                IpAddress = peer.LastIp,
                Port = peer.LastPort,
                State = DeviceState.Paired,
                LastSeenUtc = peer.LastSeenUtc
            };

            var result = await SendAsync(row, files);
            if (!result.Success && !string.Equals(result.ErrorCode, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    Sanitizer.MapError(result.ErrorCode),
                    "LanBridge",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void OpenDeviceList(IReadOnlyList<string> files)
        {
            if (_deviceList != null && !_deviceList.IsDisposed)
            {
                _deviceList.SetSendFiles(files);
                if (_deviceList.WindowState == FormWindowState.Minimized)
                    _deviceList.WindowState = FormWindowState.Normal;
                _deviceList.Show();
                _deviceList.BringToFront();
                _deviceList.Activate();
                return;
            }

            _deviceList = new DeviceListForm(this, files, _log);
            _deviceList.FormClosed += (s, e) => _deviceList = null;
            _deviceList.Show();
            _deviceList.Activate();
        }

        private string[] ChooseFiles()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Send files with LanBridge",
                Multiselect = true,
                CheckFileExists = true,
                Filter = "All files (*.*)|*.*"
            })
            {
                return dialog.ShowDialog() == DialogResult.OK ? dialog.FileNames : null;
            }
        }

        private void SetVisible(bool visible)
        {
            _config.Discoverable = visible;
            try { _config.Save(_configPath); }
            catch (Exception ex) { _log.Error("Could not save settings", ex); }

            _discovery.Discoverable = visible;
            _visibleItem.Checked = visible;
            _log.Info("Visible on network: " + visible);
        }

        private void OpenSettings()
        {
            _log.Info("Settings opened.");
            var logPath = Path.Combine(
                Path.GetDirectoryName(_configPath) ?? string.Empty,
                "lanbridge.log");

            using (var form = new SettingsForm(_config, _configPath, logPath))
            {
                var result = _deviceList != null && !_deviceList.IsDisposed
                    ? form.ShowDialog(_deviceList)
                    : form.ShowDialog();
                if (result != DialogResult.OK) return;
            }

            _server.AutoAcceptFromPaired = _config.AutoAcceptFromPaired;
            _discovery.Discoverable = _config.Discoverable;
            _visibleItem.Checked = _config.Discoverable;
        }

        private void OpenReceiveFolder()
        {
            try
            {
                Directory.CreateDirectory(_config.ReceiveFolder);
                using (Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _config.ReceiveFolder + "\"")
                {
                    UseShellExecute = true
                }))
                {
                }
            }
            catch (Exception ex)
            {
                _log.Error("Could not open the received folder", ex);
                MessageBox.Show(
                    "Could not open " + _config.ReceiveFolder,
                    "LanBridge",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void CancelInbound()
        {
            _server.CancelActiveTransfer();
        }

        private void CancelOutbound()
        {
            try { _outboundCts?.Cancel(); } catch { }
        }

        private void Quit()
        {
            if (_disposed) return;
            _disposed = true;

            try { _outboundCts?.Cancel(); } catch { }
            _tray.Visible = false;

            _discovery.Dispose();
            _server.Dispose();
            _ui.CloseAll();

            if (_deviceList != null && !_deviceList.IsDisposed) _deviceList.Close();
            _tray.Dispose();
            _trayIcon.Dispose();
            _menu.Dispose();
            _uiHost.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                try { _outboundCts?.Cancel(); } catch { }
                _tray?.Dispose();
                _trayIcon?.Dispose();
                _menu?.Dispose();
                _discovery?.Dispose();
                _server?.Dispose();
                _ui?.CloseAll();
                _uiHost?.Dispose();
            }

            base.Dispose(disposing);
        }

        private static bool TryCreateEndpoint(DeviceRow row, out PeerEndpoint endpoint)
        {
            endpoint = null;
            if (row == null || string.IsNullOrEmpty(row.Fingerprint)) return false;
            if (string.IsNullOrEmpty(row.IpAddress) || row.Port <= 0) return false;
            if (!IPAddress.TryParse(row.IpAddress, out var address)) return false;

            endpoint = new PeerEndpoint(
                address,
                row.Port,
                row.Fingerprint,
                row.DeviceId ?? string.Empty,
                row.Name ?? "Windows PC");
            return true;
        }

        private static void ClearAndDispose(ToolStripItemCollection items)
        {
            while (items.Count > 0)
            {
                var item = items[0];
                items.RemoveAt(0);
                item.Dispose();
            }
        }

        private static Icon LoadIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch
            {
            }

            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
