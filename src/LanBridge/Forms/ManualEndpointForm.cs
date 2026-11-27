using System;
using System.Drawing;
using System.Net;
using System.Windows.Forms;

namespace LanBridge
{
    internal sealed class ManualEndpointForm : Form
    {
        private readonly TextBox _address;
        private readonly TextBox _port;

        public IPAddress Address { get; private set; }
        public int Port { get; private set; }

        public ManualEndpointForm(string initialAddress = null, int initialPort = 45873)
        {
            Text = "Add a device by IP";
            ClientSize = new Size(420, 280);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            var title = new Label
            {
                Text = "Connect by address",
                ForeColor = Color.FromArgb(15, 23, 42),
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 22)
            };
            Controls.Add(title);

            var note = new Label
            {
                Text = "Use this when discovery is blocked. Enter the other laptop's IPv4 address. "
                     + "You will still approve a six-digit pairing code.",
                ForeColor = Color.FromArgb(71, 85, 105),
                AutoSize = false,
                Size = new Size(372, 48),
                Location = new Point(24, 58)
            };
            Controls.Add(note);

            var addressLabel = new Label
            {
                Text = "IPv4 address",
                ForeColor = Color.FromArgb(51, 65, 85),
                AutoSize = true,
                Location = new Point(24, 122)
            };
            Controls.Add(addressLabel);

            _address = new TextBox
            {
                Text = initialAddress ?? string.Empty,
                Location = new Point(24, 146),
                Size = new Size(240, 26),
                Font = new Font("Consolas", 10f)
            };
            Controls.Add(_address);

            var portLabel = new Label
            {
                Text = "Port",
                ForeColor = Color.FromArgb(51, 65, 85),
                AutoSize = true,
                Location = new Point(284, 122)
            };
            Controls.Add(portLabel);

            _port = new TextBox
            {
                Text = initialPort.ToString(),
                Location = new Point(284, 146),
                Size = new Size(112, 26),
                Font = new Font("Segoe UI", 10f)
            };
            Controls.Add(_port);

            var cancel = UiTheme.CreateButton("Cancel", false);
            cancel.Location = new Point(196, 220);
            cancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(cancel);
            CancelButton = cancel;

            var connect = UiTheme.CreateButton("Pair", true);
            connect.Location = new Point(300, 220);
            connect.Click += (s, e) => Submit();
            Controls.Add(connect);
            AcceptButton = connect;

            Shown += (s, e) =>
            {
                _address.Focus();
                _address.SelectAll();
            };
        }

        private void Submit()
        {
            if (!IPAddress.TryParse(_address.Text.Trim(), out var address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                MessageBox.Show(this, "Enter a valid IPv4 address, for example 192.168.10.7.",
                    "LanBridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _address.Focus();
                _address.SelectAll();
                return;
            }

            if (!int.TryParse(_port.Text.Trim(), out var port) || port <= 0 || port > 65535)
            {
                MessageBox.Show(this, "Enter a port between 1 and 65535.",
                    "LanBridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _port.Focus();
                _port.SelectAll();
                return;
            }

            Address = address;
            Port = port;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
