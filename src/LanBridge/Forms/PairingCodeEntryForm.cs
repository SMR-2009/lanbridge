using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    internal sealed class PairingCodeEntryForm : Form
    {
        private readonly TextBox _codeBox;
        private readonly Label _error;
        private readonly Label _attempt;

        public event Action<string> Submitted;
        public event Action Cancelled;

        public PairingCodeEntryForm(PairingCodePrompt prompt)
        {
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));

            Text = "Enter pairing code";
            ClientSize = new Size(460, 306);
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
                Height = 88,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            Controls.Add(header);

            var title = new Label
            {
                Text = "Enter the pairing code",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 18)
            };
            header.Controls.Add(title);

            var subtitle = new Label
            {
                Text = "Type the six digits shown on " + prompt.Peer.Name + ".",
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = false,
                Size = new Size(412, 22),
                Location = new Point(24, 52)
            };
            header.Controls.Add(subtitle);

            _attempt = new Label
            {
                Text = "Attempt " + prompt.Attempt + " of 3",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Location = new Point(24, 108)
            };
            Controls.Add(_attempt);

            _codeBox = new TextBox
            {
                Location = new Point(24, 134),
                Size = new Size(412, 52),
                Font = new Font("Consolas", 28f, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 7,
                BorderStyle = BorderStyle.FixedSingle
            };
            _codeBox.TextChanged += (s, e) => FilterDigits();
            _codeBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Submit();
                }
            };
            Controls.Add(_codeBox);

            _error = new Label
            {
                Text = prompt.PreviousFailed ? "That code did not match. Try again." : string.Empty,
                ForeColor = Color.FromArgb(185, 28, 28),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(412, 22),
                Location = new Point(24, 194)
            };
            Controls.Add(_error);

            var cancel = MakeButton("Cancel", false);
            cancel.Location = new Point(236, 246);
            cancel.Click += (s, e) =>
            {
                Cancelled?.Invoke();
                Close();
            };
            Controls.Add(cancel);
            CancelButton = cancel;

            var pair = MakeButton("Pair", true);
            pair.Location = new Point(340, 246);
            pair.Click += (s, e) => Submit();
            Controls.Add(pair);
            AcceptButton = pair;

            Shown += (s, e) =>
            {
                _codeBox.Focus();
                _codeBox.SelectAll();
            };
        }

        private void FilterDigits()
        {
            var digits = new string(_codeBox.Text.Where(char.IsDigit).Take(6).ToArray());
            if (_codeBox.Text != digits)
            {
                var selection = _codeBox.SelectionStart;
                _codeBox.Text = digits;
                _codeBox.SelectionStart = Math.Min(selection, digits.Length);
            }
        }

        private void Submit()
        {
            var digits = new string(_codeBox.Text.Where(char.IsDigit).ToArray());
            if (digits.Length != 6)
            {
                _error.Text = "Enter all six digits.";
                _codeBox.Focus();
                return;
            }

            Submitted?.Invoke(digits);
            Close();
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
