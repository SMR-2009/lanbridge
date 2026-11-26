using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppConfig _config;
        private readonly string _configPath;
        private readonly string _logPath;
        private readonly TextBox _nameBox;
        private readonly TextBox _folderBox;
        private readonly CheckBox _startWithWindows;
        private readonly CheckBox _visible;
        private readonly CheckBox _autoAccept;
        private readonly Label _status;
        private readonly Label _error;

        public SettingsForm(AppConfig config, string configPath, string logPath)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
            _logPath = logPath;

            Text = "LanBridge settings";
            ClientSize = new Size(560, 540);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            Controls.Add(header);

            var title = new Label
            {
                Text = "Settings",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 20)
            };
            header.Controls.Add(title);

            var subtitle = new Label
            {
                Text = "Changes to the laptop name apply after restarting LanBridge.",
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(512, 22),
                Location = new Point(24, 54)
            };
            header.Controls.Add(subtitle);

            var nameLabel = new Label
            {
                Text = "This laptop's name",
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 112)
            };
            Controls.Add(nameLabel);

            _nameBox = new TextBox
            {
                Location = new Point(24, 136),
                Size = new Size(512, 26),
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
                Location = new Point(24, 180)
            };
            Controls.Add(folderLabel);

            _folderBox = new TextBox
            {
                Location = new Point(24, 204),
                Size = new Size(416, 26),
                Font = new Font("Segoe UI", 9.5f),
                Text = config.ReceiveFolder
            };
            Controls.Add(_folderBox);

            var browse = MakeButton("Browse…", false);
            browse.Location = new Point(448, 202);
            browse.Size = new Size(88, 30);
            browse.Click += (s, e) => BrowseFolder();
            Controls.Add(browse);

            _startWithWindows = new CheckBox
            {
                Text = "Start LanBridge with Windows",
                Checked = config.StartWithWindows,
                AutoSize = true,
                ForeColor = Color.FromArgb(51, 65, 85),
                Location = new Point(24, 254)
            };
            Controls.Add(_startWithWindows);

            _visible = new CheckBox
            {
                Text = "Visible on this network",
                Checked = config.Discoverable,
                AutoSize = true,
                ForeColor = Color.FromArgb(51, 65, 85),
                Location = new Point(24, 284)
            };
            Controls.Add(_visible);

            _autoAccept = new CheckBox
            {
                Text = "Automatically accept files from paired devices",
                Checked = config.AutoAcceptFromPaired,
                AutoSize = true,
                ForeColor = Color.FromArgb(51, 65, 85),
                Location = new Point(24, 314)
            };
            Controls.Add(_autoAccept);

            var autoNote = new Label
            {
                Text = "Leave this off unless both laptops are in a place you trust.",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(512, 32),
                Location = new Point(24, 340)
            };
            Controls.Add(autoNote);

            var repair = MakeButton("Repair firewall rule", false);
            repair.Location = new Point(24, 390);
            repair.Size = new Size(156, 34);
            repair.Click += async (s, e) =>
            {
                _status.Text = "Waiting for Windows permission...";
                var ok = await Firewall.AddRuleAsync(Application.ExecutablePath);
                if (IsDisposed) return;
                _status.Text = ok ? "Firewall rule added." : "The firewall rule was not added.";
            };
            Controls.Add(repair);

            var openLogs = MakeButton("Open logs", false);
            openLogs.Location = new Point(188, 390);
            openLogs.Size = new Size(112, 34);
            openLogs.Click += (s, e) => OpenLogs();
            Controls.Add(openLogs);

            _status = new Label
            {
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(512, 22),
                Location = new Point(24, 434)
            };
            Controls.Add(_status);

            _error = new Label
            {
                ForeColor = Color.FromArgb(185, 28, 28),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(512, 22),
                Location = new Point(24, 458)
            };
            Controls.Add(_error);

            var cancel = MakeButton("Cancel", false);
            cancel.Location = new Point(328, 490);
            cancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(cancel);
            CancelButton = cancel;

            var save = MakeButton("Save", true);
            save.Location = new Point(432, 490);
            save.Click += (s, e) => Save();
            Controls.Add(save);
            AcceptButton = save;
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

        private void Save()
        {
            var name = Sanitizer.SanitizeDeviceName(_nameBox.Text);
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
            _config.Discoverable = _visible.Checked;
            _config.AutoAcceptFromPaired = _autoAccept.Checked;

            try
            {
                _config.Save(_configPath);
            }
            catch (Exception ex)
            {
                _error.Text = "Could not save settings: " + ex.Message;
                return;
            }

            try
            {
                Startup.SetEnabled(_config.StartWithWindows, Application.ExecutablePath);
            }
            catch (Exception ex)
            {
                _status.Text = "Settings saved, but startup could not be changed: " + ex.Message;
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void OpenLogs()
        {
            try
            {
                var folder = string.IsNullOrEmpty(_logPath)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanBridge")
                    : Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                using (Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"")
                {
                    UseShellExecute = true
                }))
                {
                }
            }
            catch
            {
            }
        }

                private static Button MakeButton(string text, bool primary)
        {
            return UiTheme.CreateButton(text, primary);
        }

    }
}
