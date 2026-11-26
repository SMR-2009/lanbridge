using System;
using System.Drawing;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    internal sealed class ProgressForm : Form
    {
        private readonly bool _receiving;
        private readonly Action _cancel;
        private readonly Label _title;
        private readonly Label _file;
        private readonly Label _detail;
        private readonly Label _percent;
        private readonly ProgressBar _bar;
        private readonly Button _cancelButton;
        private readonly Button _closeButton;
        private readonly Timer _closeTimer;
        private bool _terminal;
        private int _lastPaint;

        public ProgressForm(string title, bool receiving, Action cancel)
        {
            _receiving = receiving;
            _cancel = cancel;

            Text = title;
            ClientSize = new Size(540, 264);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            var accent = new Panel
            {
                Dock = DockStyle.Top,
                Height = 6,
                BackColor = Color.FromArgb(37, 99, 235)
            };
            Controls.Add(accent);

            _title = new Label
            {
                Text = title,
                ForeColor = Color.FromArgb(15, 23, 42),
                Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(492, 28),
                Location = new Point(24, 22)
            };
            Controls.Add(_title);

            _file = new Label
            {
                Text = "Preparing…",
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = false,
                Size = new Size(492, 22),
                Location = new Point(24, 60)
            };
            Controls.Add(_file);

            _bar = new ProgressBar
            {
                Location = new Point(24, 90),
                Size = new Size(492, 22),
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };
            Controls.Add(_bar);

            _percent = new Label
            {
                Text = "0%",
                ForeColor = Color.FromArgb(37, 99, 235),
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(60, 22),
                Location = new Point(456, 60),
                TextAlign = ContentAlignment.MiddleRight
            };
            Controls.Add(_percent);

            _detail = new Label
            {
                Text = string.Empty,
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(492, 34),
                Location = new Point(24, 124)
            };
            Controls.Add(_detail);

            _cancelButton = MakeButton("Cancel", false);
            _cancelButton.Location = new Point(420, 202);
            _cancelButton.Click += (s, e) =>
            {
                _cancelButton.Enabled = false;
                _cancelButton.Text = "Cancelling…";
                _detail.Text = "Stopping the transfer…";
                try { _cancel?.Invoke(); } catch { }
            };
            Controls.Add(_cancelButton);

            _closeButton = MakeButton("Close", true);
            _closeButton.Location = new Point(420, 202);
            _closeButton.Visible = false;
            _closeButton.Click += (s, e) => Close();
            Controls.Add(_closeButton);

            _closeTimer = new Timer { Interval = 1400 };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                Close();
            };
        }

        public void Apply(TransferProgress progress)
        {
            if (progress == null || IsDisposed || _terminal) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<TransferProgress>(Apply), progress);
                return;
            }

            if (!progress.IsComplete && !progress.Failed)
            {
                if (Environment.TickCount - _lastPaint < 100) return;
                _lastPaint = Environment.TickCount;
            }

            if (progress.IsComplete)
            {
                _terminal = true;
                _bar.Value = 100;
                _percent.Text = "100%";
                _file.Text = progress.FileName;
                _detail.Text = _receiving
                    ? "Files saved to your received folder."
                    : "Files sent successfully.";
                _cancelButton.Visible = false;
                _closeButton.Visible = true;
                _closeButton.Focus();
                _closeTimer.Start();
                return;
            }

            if (progress.Failed)
            {
                _terminal = true;
                _file.Text = "Transfer stopped";
                _detail.Text = string.Equals(progress.ErrorCode, "cancelled", StringComparison.OrdinalIgnoreCase)
                    ? "The transfer was cancelled."
                    : "Transfer failed: " + Sanitizer.MapError(progress.ErrorCode);
                _cancelButton.Visible = false;
                _closeButton.Visible = true;
                _closeButton.Focus();
                return;
            }

            var fraction = progress.TotalBytes > 0
                ? Math.Max(0d, Math.Min(1d, (double)progress.BytesTransferred / progress.TotalBytes))
                : 0d;
            var overall = progress.FileCount > 0
                ? ((progress.FileIndex + fraction) / progress.FileCount) * 100d
                : 0d;
            var value = (int)Math.Max(0d, Math.Min(100d, overall));

            _bar.Value = value;
            _percent.Text = value + "%";
            _file.Text = progress.FileName;
            _detail.Text = UiFormat.Size(progress.BytesTransferred) + " of " + UiFormat.Size(progress.TotalBytes)
                         + (progress.FileCount > 1
                             ? "  •  file " + (progress.FileIndex + 1) + " of " + progress.FileCount
                             : string.Empty);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _closeTimer.Stop();
            _closeTimer.Dispose();
            base.OnFormClosed(e);
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
