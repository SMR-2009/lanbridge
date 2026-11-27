using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    internal interface IDeviceListHost
    {
        IReadOnlyList<DeviceRow> BuildRows();
        bool Discoverable { get; }
        bool FirewallRulePresent { get; }
        Task<PairingAttemptResult> PairAsync(DeviceRow row);
        Task AddManualDeviceAsync();
        Task<TransferResult> SendAsync(DeviceRow row, IReadOnlyList<string> files);
        void Unpair(DeviceRow row);
        void RefreshDiscovery();
        void ShowSettings();
        void ShowAbout();
        Task CheckForUpdatesAsync();
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
        private readonly Button _more;
        private readonly Timer _timer;
        private readonly Timer _resizeTimer;
        private readonly ToolTip _toolTip;
        private bool _manualBusy;
        private readonly Font _boldFont;
        private readonly ContextMenuStrip _menu;
        private readonly ContextMenuStrip _moreMenu;
        private readonly ILogger _log;
        private readonly ToolStripMenuItem _unpairItem;
        private readonly ToolStripMenuItem _copyItem;
        private List<string> _sendFiles;
        private string _lastRowsSignature;
        private int _lastSeenRefreshTick;
        private bool _busy;

        public DeviceListForm(IDeviceListHost host, IReadOnlyList<string> sendFiles = null, ILogger log = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _log = log ?? NullLogger.Instance;

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
                Margin = Padding.Empty,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            root.Controls.Add(header, 0, 0);

            var headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 12, 24, 8),
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            header.Controls.Add(headerLayout);

            _headerTitle = new Label
            {
                Text = "LanBridge",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
                AutoSize = false,
                Margin = Padding.Empty
            };
            headerLayout.Controls.Add(_headerTitle, 0, 0);

            _headerSubtitle = new Label
            {
                Text = "Secure local file transfer",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft,
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = false,
                Margin = Padding.Empty
            };
            headerLayout.Controls.Add(_headerSubtitle, 0, 1);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(24, 8, 24, 8),
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.FromArgb(248, 250, 252)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.Controls.Add(body, 0, 1);

            _status = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            body.Controls.Add(_status, 0, 0);

            var listPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(1),
                BackColor = Color.FromArgb(203, 213, 225)
            };
            body.Controls.Add(listPanel, 0, 1);

            _list = new BufferedListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                GridLines = false,
                BorderStyle = BorderStyle.None,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Segoe UI", 9.5f),
                ShowItemToolTips = true
            };
            _list.Columns.Add("Device", 280);
            _list.Columns.Add("Status", 120);
            _list.Columns.Add("Address", 170);
            _list.Columns.Add("Last seen", 140);
            _resizeTimer = new Timer { Interval = 80 };
            _resizeTimer.Tick += (s, e) =>
            {
                _resizeTimer.Stop();
                ResizeColumns();
                if (!IsDisposed) _list.Scrollable = true;
            };
            _list.Resize += (s, e) =>
            {
                // Hide scrollbars while the user drags the window edge. Column
                // widths are recalculated after the resize settles, which avoids
                // the one-frame scrollbar flash.
                _list.Scrollable = false;
                _resizeTimer.Stop();
                _resizeTimer.Start();
            };
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
                Margin = Padding.Empty,
                Padding = new Padding(24, 14, 24, 14),
                BackColor = Color.White
            };
            root.Controls.Add(footer, 0, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = false,
                Height = 40,
                Margin = Padding.Empty,
                Padding = new Padding(0, 2, 0, 0)
            };
            footer.Controls.Add(buttons);

            _primary = MakeButton("Send files…", true);
            _primary.Click += async (s, e) => await PrimaryActionAsync();
            buttons.Controls.Add(_primary);

            _refresh = MakeButton("Refresh", false);
            _refresh.Click += (s, e) =>
            {
                _host.RefreshDiscovery();
                RefreshRows(true);
            };
            buttons.Controls.Add(_refresh);
            _toolTip = new ToolTip();
            _toolTip.SetToolTip(_refresh, "Refresh the device list (F5)");

            _settings = MakeButton("Settings", false);
            _settings.Click += (s, e) => _host.ShowSettings();
            _toolTip.SetToolTip(_settings, "Open settings (Ctrl+,)");
            buttons.Controls.Add(_settings);

            _more = MakeButton("More…", false);
            _moreMenu = new ContextMenuStrip
            {
                ShowImageMargin = false
            };
            _moreMenu.Items.Add(new ToolStripMenuItem("Check for updates…", null, async (s, e) => await _host.CheckForUpdatesAsync()));
            _moreMenu.Items.Add(new ToolStripMenuItem("Add device by IP…", null, async (s, e) => await AddManualAsync()));
            _moreMenu.Items.Add(new ToolStripSeparator());
            _moreMenu.Items.Add(new ToolStripMenuItem("About LanBridge", null, (s, e) => _host.ShowAbout()));
            _more.Click += (s, e) => _moreMenu.Show(_more, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
            _toolTip.SetToolTip(_more, "Updates, add a device by IP, and about");
            buttons.Controls.Add(_more);

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
                RefreshRows(true);
            }));
            _menu.Opening += (s, e) =>
            {
                var row = SelectedRow;
                _unpairItem.Enabled = row != null && (row.State == DeviceState.Paired || row.State == DeviceState.Offline);
                _copyItem.Enabled = row != null;
            };
            _list.ContextMenuStrip = _menu;

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5)
                {
                    _host.RefreshDiscovery();
                    RefreshRows(true);
                    e.SuppressKeyPress = true;
                }
                else if (e.Control && e.KeyCode == Keys.Oemcomma)
                {
                    _host.ShowSettings();
                    e.SuppressKeyPress = true;
                }
            };

            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            _list.AllowDrop = true;
            _list.DragEnter += OnDragEnter;
            _list.DragDrop += OnDragDrop;

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, e) => RefreshRows(false);
            _timer.Start();

            ResizeColumns();
            SetSendFiles(sendFiles);
            RefreshRows(true);
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

        private async Task AddManualAsync()
        {
            if (_manualBusy) return;
            _manualBusy = true;
            try
            {
                await _host.AddManualDeviceAsync();
            }
            finally
            {
                _manualBusy = false;
                if (!IsDisposed) RefreshRows(true);
            }
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
                    if (IsDisposed) return;
                    if (!result.Success)
                    {
                        ShowError(result.ErrorCode);
                        return;
                    }

                    _host.RefreshDiscovery();
                    RefreshRows(true);
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
                _log.Error("Device list action failed", ex);
                if (!IsDisposed)
                    MessageBox.Show(this, ex.Message, "LanBridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _busy = false;
                if (!IsDisposed) RefreshRows(true);
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
            if (!IsDisposed &&
                !result.Success &&
                !string.Equals(result.ErrorCode, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                ShowError(result.ErrorCode);
            }
        }

        private void RefreshRows(bool force)
        {
            var rows = _host.BuildRows();
            var signature = BuildRowSignature(rows);
            var changed = force || !string.Equals(signature, _lastRowsSignature, StringComparison.Ordinal);

            if (changed)
            {
                _list.Scrollable = false;
                _resizeTimer.Stop();
                _resizeTimer.Start();

                var selected = SelectedRow;
                var selectedFingerprint = selected?.Fingerprint;

                _list.BeginUpdate();
                _list.Items.Clear();
                foreach (var row in rows)
                {
                    var item = new ListViewItem(row.Name) { Tag = row, UseItemStyleForSubItems = true, ToolTipText = row.Name };
                    item.SubItems.Add(UiFormat.State(row.State));
                    item.SubItems.Add(row.HasEndpoint ? row.IpAddress + ":" + row.Port : "—");
                    item.SubItems.Add(UiFormat.Relative(row.LastSeenUtc));
                    item.ForeColor = StateColor(row.State);
                    if (row.State == DeviceState.Paired) item.Font = _boldFont;
                    _list.Items.Add(item);
                }
                _list.EndUpdate();

                _lastRowsSignature = signature;
                _lastSeenRefreshTick = Environment.TickCount;

                var isEmpty = rows.Count == 0;
                _list.Visible = !isEmpty;
                _empty.Visible = isEmpty;
                _empty.Text = EmptyText();
                if (isEmpty) _empty.BringToFront(); else _list.BringToFront();

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
            }
            else if (Environment.TickCount - _lastSeenRefreshTick >= 5000)
            {
                _lastSeenRefreshTick = Environment.TickCount;
                foreach (ListViewItem item in _list.Items)
                {
                    var row = item.Tag as DeviceRow;
                    if (row != null && item.SubItems.Count > 3)
                    {
                        item.SubItems[3].Text = UiFormat.Relative(row.LastSeenUtc);
                    }
                }
            }

            _status.Text = BuildStatus(rows);
            UpdatePrimary();
        }

        private static string BuildRowSignature(IReadOnlyList<DeviceRow> rows)
        {
            var builder = new StringBuilder();
            foreach (var row in rows)
            {
                builder.Append(row.Fingerprint).Append('\u001e')
                    .Append(row.Name).Append('\u001e')
                    .Append((int)row.State).Append('\u001e')
                    .Append(row.IpAddress).Append('\u001e')
                    .Append(row.Port).Append('\u001f');
            }
            return builder.ToString();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) SetSendFiles(files);
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
            var firewall = _host.FirewallRulePresent ? string.Empty : "  •  Firewall rule missing";
            if (rows.Count == 0)
            {
                return (_host.Discoverable
                    ? "Looking for devices on your network…"
                    : "Hidden — other laptops cannot find this one.") + firewall;
            }

            var paired = rows.Count(row => row.State == DeviceState.Paired);
            var available = rows.Count(row => row.State == DeviceState.Available);
            return paired + " paired  •  " + available + " available" + hidden + firewall;
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
            RefreshRows(true);
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
            _log.Warn("Device list operation failed: " + Sanitizer.SanitizeLogValue(code));
            if (IsDisposed) return;
            MessageBox.Show(
                this,
                Sanitizer.MapError(code),
                "LanBridge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void ResizeColumns()
        {
            var width = _list.ClientSize.Width;
            if (width <= 0 || _list.Columns.Count < 4) return;

            var device = Math.Max(180, (int)(width * 0.40));
            var status = Math.Max(90, (int)(width * 0.18));
            var address = Math.Max(130, (int)(width * 0.25));
            var last = Math.Max(70, width - device - status - address);

            _list.Columns[0].Width = device;
            _list.Columns[1].Width = status;
            _list.Columns[2].Width = address;
            _list.Columns[3].Width = last;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            _resizeTimer.Stop();
            _resizeTimer.Dispose();
            _menu.Dispose();
            _moreMenu.Dispose();
            _toolTip.Dispose();
            _boldFont.Dispose();
            base.OnFormClosed(e);
        }

        private sealed class BufferedListView : ListView
        {
            public BufferedListView()
            {
                DoubleBuffered = true;
                SetStyle(
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.AllPaintingInWmPaint,
                    true);
            }
        }

        private static Button MakeButton(string text, bool primary)
        {
            return UiTheme.CreateButton(text, primary);
        }

    }
}
