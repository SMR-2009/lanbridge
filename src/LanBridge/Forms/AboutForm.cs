using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LanBridge
{
    internal sealed class AboutForm : Form
    {
        private const string ProjectUrl = "https://github.com/SMR-2009/lanbridge";

        public AboutForm()
        {
            Text = "About LanBridge";
            ClientSize = new Size(500, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
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
                Text = "LanBridge",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 18)
            };
            header.Controls.Add(title);

            var assemblyVersion = typeof(AboutForm).Assembly.GetName().Version;
            var displayVersion = assemblyVersion == null ? Application.ProductVersion : assemblyVersion.ToString(3);
            var version = new Label
            {
                Text = "Version " + displayVersion + "  •  MIT license",
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = true,
                Location = new Point(24, 54)
            };
            header.Controls.Add(version);

            var description = new Label
            {
                Text = "Secure, local-only file transfer for Windows.\n"
                     + "Files go directly between your laptops over your home network — never through a cloud service.",
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = false,
                Size = new Size(452, 52),
                Location = new Point(24, 112)
            };
            Controls.Add(description);

            var security = new Label
            {
                Text = "Devices are paired with a six-digit code. Each laptop pins the other's certificate, "
                     + "and every transfer is encrypted with TLS 1.2.",
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(452, 44),
                Location = new Point(24, 172)
            };
            Controls.Add(security);

            var crossPlatform = new Label
            {
                Text = "Need macOS or Linux? LocalSend is a good cross-platform option.",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(452, 22),
                Location = new Point(24, 224)
            };
            Controls.Add(crossPlatform);

            var link = new LinkLabel
            {
                Text = ProjectUrl,
                ForeColor = Color.FromArgb(37, 99, 235),
                LinkColor = Color.FromArgb(37, 99, 235),
                ActiveLinkColor = Color.FromArgb(29, 78, 216),
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Location = new Point(24, 254)
            };
            link.LinkClicked += (s, e) => OpenProjectPage();
            Controls.Add(link);

            var close = UiTheme.CreateButton("Close", true);
            close.Size = new Size(96, 34);
            close.Location = new Point(380, 306);
            close.Click += (s, e) => Close();
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
        }

        private static void OpenProjectPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
            }
            catch
            {
            }
        }
    }
}
