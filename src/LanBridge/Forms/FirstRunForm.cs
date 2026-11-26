using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    internal sealed class FirstRunForm : Form
    {
        private readonly AppConfig _config;
        private readonly TextBox _nameBox;
        private readonly TextBox _folderBox;
        private readonly CheckBox _startWithWindows;
        private readonly CheckBox _firewall;
        private readonly Label _error;

        public AppConfig Config => _config;
        public bool StartWithWindows => _startWithWindows.Checked;
        public bool AddFirewallRule => _firewall.Checked && _firewall.Enabled;

        public FirstRunForm(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            Text = "Welcome to LanBridge";
            ClientSize = new Size(580, 486);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 104,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            Controls.Add(header);

            var title = new Label
            {
                Text = "Welcome to LanBridge",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 20)
            };
            header.Controls.Add(title);

            var subtitle = new Label
            {
                Text = "Send files directly between your laptops. No account, no cloud.",
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = false,
                Size = new Size(520, 22),
                Location = new Point(24, 58)
            };
            header.Controls.Add(subtitle);

            var nameLabel = new Label
            {
                Text = "This laptop's name",
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 126)
            };
            Controls.Add(nameLabel);

            _nameBox = new TextBox
            {
                Location = new Point(24, 150),
                Size = new Size(516, 26),
                Font = new Font("Segoe UI", 10f),
                Text = config.DeviceName
            };
            Controls.Add(_nameBox);

            var folderLabel = new Label
            {
                Text = "Save received files to",
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 194)
            };
            Controls.Add(folderLabel);

            _folderBox = new TextBox
            {
                Location = new Point(24, 218),
                Size = new Size(420, 26),
                Font = new Font("Segoe UI", 9.5f),
                Text = config.ReceiveFolder
            };
            Controls.Add(_folderBox);

            var browse = MakeButton("Browse…", false);
            browse.Location = new Point(452, 216);
            browse.Size = new Size(88, 30);
            browse.Click += (s, e) => BrowseFolder();
            Controls.Add(browse);

            _startWithWindows = new CheckBox
            {
                Text = "Start LanBridge with Windows",
                Checked = true,
                AutoSize = true,
                ForeColor = Color.FromArgb(51, 65, 85),
                Location = new Point(24, 270)
            };
            Controls.Add(_startWithWindows);

            var firewallRulePresent = Firewall.IsRulePresentFor(Application.ExecutablePath);
            _firewall = new CheckBox
            {
                Text = firewallRulePresent
                    ? "Windows Firewall is already configured"
                    : "Allow LanBridge through Windows Firewall",
                Checked = true,
                Enabled = !firewallRulePresent,
                AutoSize = true,
                ForeColor = Color.FromArgb(51, 65, 85),
                Location = new Point(24, 300)
            };
            Controls.Add(_firewall);

            var firewallNote = new Label
            {
                Text = firewallRulePresent
                    ? "The installer already added the rule. This setup will not ask for administrator permission."
                    : "Windows will ask for permission once (UAC). The rule is limited to your local network. You can skip it and repair it later in Settings.",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(516, 36),
                Location = new Point(24, 332)
            };
            Controls.Add(firewallNote);

            _error = new Label
            {
                ForeColor = Color.FromArgb(185, 28, 28),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(516, 22),
                Location = new Point(24, 380)
            };
            Controls.Add(_error);

            var cancel = MakeButton("Cancel", false);
            cancel.Location = new Point(348, 426);
            cancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(cancel);
            CancelButton = cancel;

            var ok = MakeButton("Continue", true);
            ok.Location = new Point(452, 426);
            ok.Click += (s, e) => Continue();
            Controls.Add(ok);
            AcceptButton = ok;
        }

        private void BrowseFolder()
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Choose where LanBridge saves received files",
                SelectedPath = Directory.Exists(_folderBox.Text) ? _folderBox.Text : _config.ReceiveFolder,
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _folderBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void Continue()
        {
            var name = Sanitizer.SanitizeDeviceName(_nameBox.Text);
            if (string.IsNullOrWhiteSpace(name))
            {
                _error.Text = "Choose a name for this laptop.";
                return;
            }

            var folder = (_folderBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(folder))
            {
                _error.Text = "Choose a folder for received files.";
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                _error.Text = "Could not create that folder: " + ex.Message;
                return;
            }

            _config.DeviceName = name;
            _config.ReceiveFolder = folder;
            _config.StartWithWindows = _startWithWindows.Checked;
            _config.Discoverable = true;

            DialogResult = DialogResult.OK;
            Close();
        }

        private static Button MakeButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(104, 34),
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
