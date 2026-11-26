using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    internal interface IDeviceListHost
    {
        IReadOnlyList<DeviceRow> BuildRows();
        bool Discoverable { get; }
        Task<PairingAttemptResult> PairAsync(DeviceRow row);
        Task<TransferResult> SendAsync(DeviceRow row, IReadOnlyList<string> files);
        void Unpair(DeviceRow row);
        void RefreshDiscovery();
        void ShowSettings();
        void ShowAbout();
    }

    internal sealed class DeviceListForm : Form
    {
        private readonly IDeviceListHost _host;
        private readonly ListView _list;
        private readonly Label _empty;
        private readonly Label _status;
        private readonly Label _headerTitle;
        private readonly Label _headerSubtitle;
        private readonly Button _primary;
        private readonly Button _refresh;
        private readonly Button _settings;
        private readonly Button _about;
        private readonly Button _close;
        private readonly Timer _timer;
        private readonly Font _boldFont;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _unpairItem;
        private readonly ToolStripMenuItem _copyItem;
        private List<string> _sendFiles;
        private bool _busy;

        public DeviceListForm(IDeviceListHost host, IReadOnlyList<string> sendFiles = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));

            Text = "LanBridge";
            ClientSize = new Size(780, 540);
            MinimumSize = new Size(680, 460);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 9.5f);
            Icon = SystemIcons.Application;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));
            Controls.Add(root);

            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            root.Controls.Add(header, 0, 0);

            _headerTitle = new Label
            {
                Text = "LanBridge",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 17)
            };
            header.Controls.Add(_headerTitle);

            _headerSubtitle = new Label
            {
                Text = "Secure local file transfer",
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = false,
                Size = new Size(560, 22),
                Location = new Point(24, 52)
            };
            header.Controls.Add(_headerSubtitle);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 10, 24, 10),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            root.Controls.Add(body, 0, 1);

            _status = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft
            };
            body.Controls.Add(_status);

            var listPanel = new Panel { Dock = DockStyle.Fill };
            body.Controls.Add(listPanel);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                GridLines = false,
                BorderStyle = BorderStyle.FixedSingle,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Segoe UI", 9.5f)
            };
            _list.Columns.Add("Device", 280);
            _list.Columns.Add("Status", 120);
            _list.Columns.Add("Address", 170);
            _list.Columns.Add("Last seen", 140);
            _list.SelectedIndexChanged += (s, e) => UpdatePrimary();
            _list.DoubleClick += async (s, e) => await PrimaryActionAsync();
            _list.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                var item = _list.GetItemAt(e.X, e.Y);
                if (item != null) item.Selected = true;
            };
            listPanel.Controls.Add(_list);

            _boldFont = new Font(_list.Font, FontStyle.Bold);

            _empty = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 10f),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            listPanel.Controls.Add(_empty);
            _empty.BringToFront();

            var footer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 14, 24, 14),
                BackColor = Color.White
            };
            root.Controls.Add(footer, 0, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true
            };
            footer.Controls.Add(buttons);

            _primary = MakeButton("Send files…", true);
            _primary.Click += async (s, e) => await PrimaryActionAsync();
            buttons.Controls.Add(_primary);

            _refresh = MakeButton("Refresh", false);
            _refresh.Click += (s, e) =>
            {
                _host.RefreshDiscovery();
                RefreshRows();
            };
            buttons.Controls.Add(_refresh);

            _settings = MakeButton("Settings", false);
            _settings.Click += (s, e) => _host.ShowSettings();
            buttons.Controls.Add(_settings);

            _about = MakeButton("About", false);
            _about.Click += (s, e) => _host.ShowAbout();
            buttons.Controls.Add(_about);

            _close = MakeButton("Close", false);
            _close.Click += (s, e) => Close();
            buttons.Controls.Add(_close);

            _menu = new ContextMenuStrip();
            _unpairItem = new ToolStripMenuItem("Unpair this device");
            _unpairItem.Click += (s, e) => UnpairSelected();
            _menu.Items.Add(_unpairItem);
            _copyItem = new ToolStripMenuItem("Copy fingerprint");
            _copyItem.Click += (s, e) => CopyFingerprint();
            _menu.Items.Add(_copyItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Refresh", null, (s, e) =>
            {
                _host.RefreshDiscovery();
                RefreshRows();
            }));
            _menu.Opening += (s, e) =>
            {
                var row = SelectedRow;
                _unpairItem.Enabled = row != null && (row.State == DeviceState.Paired || row.State == DeviceState.Offline);
                _copyItem.Enabled = row != null;
            };
            _list.ContextMenuStrip = _menu;

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, e) => RefreshRows();
            _timer.Start();

            SetSendFiles(sendFiles);
            RefreshRows();
        }

        public void SetSendFiles(IReadOnlyList<string> sendFiles)
        {
            _sendFiles = sendFiles != null && sendFiles.Count > 0 ? sendFiles.ToList() : null;

            if (_sendFiles != null)
            {
                long total = 0;
                foreach (var file in _sendFiles)
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }

                _headerTitle.Text = "Send files";
                _headerSubtitle.Text = _sendFiles.Count + " file" + (_sendFiles.Count == 1 ? string.Empty : "s")
                                     + "  •  " + UiFormat.Size(total) + "  •  choose a device";
                Text = "Send files — LanBridge";
            }
            else
            {
                _headerTitle.Text = "LanBridge";
                _headerSubtitle.Text = "Secure local file transfer";
                Text = "LanBridge";
            }

            UpdatePrimary();
        }

        private async Task PrimaryActionAsync()
        {
            var row = SelectedRow;
            if (row == null || _busy) return;

            if (row.State == DeviceState.IdentityChanged)
            {
                MessageBox.Show(
                    this,
                    "This laptop is advertising a different certificate than the one you paired.\n\n"
                    + "That can happen after a reinstall. For your safety LanBridge will not pair automatically. "
                    + "Unpair the old entry, then pair again with a new six-digit code.",
                    "Identity changed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _busy = true;
                UpdatePrimary();

                if (row.State == DeviceState.Available)
                {
                    _primary.Text = "Pairing…";
                    var result = await _host.PairAsync(row);
                    if (!result.Success)
                    {
                        ShowError(result.ErrorCode);
                        return;
                    }

                    _host.RefreshDiscovery();
                    RefreshRows();
                    var pairedRow = _host.BuildRows().FirstOrDefault(candidate => candidate.Fingerprint == row.Fingerprint);
                    if (_sendFiles != null && pairedRow != null)
                    {
                        await SendFilesAsync(pairedRow, _sendFiles);
                    }
                    return;
                }

                if (_sendFiles != null)
                {
                    await SendFilesAsync(row, _sendFiles);
                    return;
                }

                var files = ChooseFiles();
                if (files != null && files.Length > 0)
                {
                    await SendFilesAsync(row, files);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "LanBridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _busy = false;
                RefreshRows();
            }
        }

        private async Task SendFilesAsync(DeviceRow row, IReadOnlyList<string> files)
        {
            if (row == null || files == null || files.Count == 0) return;
            if (!row.HasEndpoint)
            {
                ShowError("no_endpoint");
                return;
            }

            _primary.Enabled = false;
            _primary.Text = "Sending…";
            var result = await _host.SendAsync(row, files);
            if (!result.Success && !string.Equals(result.ErrorCode, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                ShowError(result.ErrorCode);
            }
        }

        private void RefreshRows()
        {
            var rows = _host.BuildRows();
            var selected = SelectedRow;
            var selectedFingerprint = selected?.Fingerprint;

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var row in rows)
            {
                var item = new ListViewItem(row.Name) { Tag = row, UseItemStyleForSubItems = true };
                item.SubItems.Add(UiFormat.State(row.State));
                item.SubItems.Add(row.HasEndpoint ? row.IpAddress + ":" + row.Port : "—");
                item.SubItems.Add(UiFormat.Relative(row.LastSeenUtc));
                item.ForeColor = StateColor(row.State);
                if (row.State == DeviceState.Paired) item.Font = _boldFont;
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            var isEmpty = rows.Count == 0;
            _list.Visible = !isEmpty;
            _empty.Visible = isEmpty;
            _empty.Text = EmptyText();
            if (isEmpty) _empty.BringToFront(); else _list.BringToFront();

            _status.Text = BuildStatus(rows);

            if (!isEmpty)
            {
                ListViewItem toSelect = null;
                if (selectedFingerprint != null)
                {
                    foreach (ListViewItem item in _list.Items)
                    {
                        var row = item.Tag as DeviceRow;
                        if (row != null && row.Fingerprint == selectedFingerprint)
                        {
                            toSelect = item;
                            break;
                        }
                    }
                }
                if (toSelect == null) toSelect = _list.Items[0];
                toSelect.Selected = true;
                toSelect.Focused = true;
            }

            UpdatePrimary();
        }

        private void UpdatePrimary()
        {
            if (_busy)
            {
                _primary.Enabled = false;
                return;
            }

            var sending = _sendFiles != null;
            var row = SelectedRow;
            if (row == null)
            {
                _primary.Enabled = false;
                _primary.Text = sending ? "Send" : "Send files…";
                return;
            }

            switch (row.State)
            {
                case DeviceState.Paired:
                    _primary.Text = sending ? "Send" : "Send files…";
                    _primary.Enabled = row.HasEndpoint;
                    break;
                case DeviceState.Available:
                    _primary.Text = sending ? "Pair & send" : "Pair…";
                    _primary.Enabled = row.HasEndpoint;
                    break;
                case DeviceState.Offline:
                    _primary.Text = sending ? "Send" : "Send files…";
                    _primary.Enabled = row.HasEndpoint;
                    break;
                case DeviceState.IdentityChanged:
                    _primary.Text = "Review…";
                    _primary.Enabled = true;
                    break;
                default:
                    _primary.Enabled = false;
                    break;
            }
        }

        private DeviceRow SelectedRow
        {
            get
            {
                if (_list.SelectedItems.Count != 1) return null;
                return _list.SelectedItems[0].Tag as DeviceRow;
            }
        }

        private string BuildStatus(IReadOnlyList<DeviceRow> rows)
        {
            var hidden = _host.Discoverable ? string.Empty : "  •  Hidden";
            if (rows.Count == 0)
            {
                return _host.Discoverable
                    ? "Looking for devices on your network…"
                    : "Hidden — other laptops cannot find this one.";
            }

            var paired = rows.Count(row => row.State == DeviceState.Paired);
            var available = rows.Count(row => row.State == DeviceState.Available);
            return paired + " paired  •  " + available + " available" + hidden;
        }

        private string EmptyText()
        {
            if (!_host.Discoverable)
            {
                return "You are hidden.\n\nTurn on “Visible on this network” from the tray menu to be found.";
            }

            return "No devices yet.\n\n"
                 + "Make sure both laptops are on the same Wi-Fi and LanBridge is running on the other one.\n"
                 + "Then click Refresh.";
        }

        private static Color StateColor(DeviceState state)
        {
            switch (state)
            {
                case DeviceState.Paired: return Color.FromArgb(21, 128, 61);
                case DeviceState.Available: return Color.FromArgb(37, 99, 235);
                case DeviceState.Offline: return Color.FromArgb(100, 116, 139);
                case DeviceState.IdentityChanged: return Color.FromArgb(180, 83, 9);
                default: return Color.FromArgb(30, 41, 59);
            }
        }

        private void UnpairSelected()
        {
            var row = SelectedRow;
            if (row == null) return;

            var answer = MessageBox.Show(
                this,
                "Unpair " + row.Name + "?\n\nYou will need a new six-digit code to pair again.",
                "Unpair",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            _host.Unpair(row);
            RefreshRows();
        }

        private void CopyFingerprint()
        {
            var row = SelectedRow;
            if (row == null) return;
            try
            {
                Clipboard.SetText(row.Fingerprint);
            }
            catch
            {
            }
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
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileNames : null;
            }
        }

        private void ShowError(string code)
        {
            MessageBox.Show(
                this,
                Sanitizer.MapError(code),
                "LanBridge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            _menu.Dispose();
            _boldFont.Dispose();
            base.OnFormClosed(e);
        }

        private static Button MakeButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(104, 36),
                Margin = new Padding(8, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(30, 41, 59)
            };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            return button;
        }
    }
}
