using System;
using System.Drawing;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    internal sealed class PairingForm : Form
    {
        private readonly PeerEndpoint _peer;
        private readonly Label _subtitle;
        private readonly Label _device;
        private readonly Panel _approvalPanel;
        private readonly Panel _codePanel;
        private readonly Label _instruction;
        private readonly Label _codeLabel;
        private readonly Label _expiry;
        private readonly Button _approve;
        private readonly Button _deny;
        private readonly Button _copy;
        private readonly Timer _timer;
        private string _code;
        private DateTimeOffset _expiresAt;

        public event Action Approved;
        public event Action Denied;

        public PairingForm(PeerEndpoint peer)
        {
            _peer = peer ?? throw new ArgumentNullException(nameof(peer));

            Text = "Pair with " + _peer.Name;
            ClientSize = new Size(500, 336);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            Controls.Add(header);

            var title = new Label
            {
                Text = "Pair with " + _peer.Name + "?",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 20)
            };
            header.Controls.Add(title);

            _subtitle = new Label
            {
                Text = "Only approve if you started pairing on the other laptop.",
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = false,
                Size = new Size(452, 22),
                Location = new Point(24, 56)
            };
            header.Controls.Add(_subtitle);

            _device = new Label
            {
                Text = _peer.Name + "  •  " + _peer.Address,
                ForeColor = Color.FromArgb(15, 23, 42),
                Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(452, 24),
                Location = new Point(24, 112)
            };
            Controls.Add(_device);

            _approvalPanel = new Panel
            {
                Location = new Point(24, 152),
                Size = new Size(452, 132)
            };
            Controls.Add(_approvalPanel);

            var explanation = new Label
            {
                Text = "This lets the two laptops send files directly over your home network. "
                     + "A six-digit code will appear next so you can confirm the connection.",
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = false,
                Size = new Size(452, 56),
                Location = new Point(0, 0)
            };
            _approvalPanel.Controls.Add(explanation);

            _deny = MakeButton("Deny", false);
            _deny.Location = new Point(248, 84);
            _deny.Click += (s, e) =>
            {
                Denied?.Invoke();
                Close();
            };
            _approvalPanel.Controls.Add(_deny);

            _approve = MakeButton("Approve", true);
            _approve.Location = new Point(356, 84);
            _approve.Click += (s, e) =>
            {
                _approve.Enabled = false;
                _deny.Enabled = false;
                _subtitle.Text = "Approved. Preparing the pairing code…";
                Approved?.Invoke();
            };
            _approvalPanel.Controls.Add(_approve);

            _codePanel = new Panel
            {
                Location = new Point(24, 152),
                Size = new Size(452, 152),
                Visible = false
            };
            Controls.Add(_codePanel);

            _instruction = new Label
            {
                Text = "Type this code on " + _peer.Name + ":",
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = false,
                Size = new Size(452, 24),
                Location = new Point(0, 0)
            };
            _codePanel.Controls.Add(_instruction);

            _codeLabel = new Label
            {
                Text = "000 000",
                ForeColor = Color.FromArgb(37, 99, 235),
                Font = new Font("Consolas", 32f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(300, 54),
                Location = new Point(0, 28)
            };
            _codePanel.Controls.Add(_codeLabel);

            _copy = MakeButton("Copy code", false);
            _copy.Location = new Point(312, 36);
            _copy.Click += (s, e) => CopyCode();
            _codePanel.Controls.Add(_copy);

            _expiry = new Label
            {
                Text = "Expires in 2:00",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(452, 22),
                Location = new Point(0, 98)
            };
            _codePanel.Controls.Add(_expiry);

            _timer = new Timer { Interval = 250 };
            _timer.Tick += (s, e) => UpdateExpiry();
        }

        public void ShowCode(PairingCodeDisplay display)
        {
            if (display == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<PairingCodeDisplay>(ShowCode), display);
                return;
            }

            _code = display.Code;
            _expiresAt = display.ExpiresAtUtc;
            _approvalPanel.Visible = false;
            _codePanel.Visible = true;
            _codeLabel.Text = FormatCode(display.Code);
            _instruction.Text = "Type this code on " + display.Peer.Name + " to finish pairing.";
            _subtitle.Text = "Waiting for the other laptop…";
            Text = "Pairing code — LanBridge";
            _timer.Start();
            UpdateExpiry();
            Activate();
        }

        private void CopyCode()
        {
            if (string.IsNullOrEmpty(_code)) return;
            try
            {
                Clipboard.SetText(_code);
                _copy.Text = "Copied";
            }
            catch
            {
                _copy.Text = "Copy code";
            }
        }

        private void UpdateExpiry()
        {
            var remaining = _expiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                _expiry.Text = "This code expired. Close this window and try again.";
                _expiry.ForeColor = Color.FromArgb(185, 28, 28);
                _copy.Enabled = false;
                _timer.Stop();
                return;
            }

            _expiry.Text = "Expires in " + ((int)remaining.TotalMinutes) + ":" + remaining.Seconds.ToString("00");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }

        private static string FormatCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return "000 000";
            var digits = new string(Array.FindAll(code.ToCharArray(), char.IsDigit));
            if (digits.Length == 6) return digits.Substring(0, 3) + " " + digits.Substring(3);
            return digits;
        }

        private static Button MakeButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(96, 34),
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
