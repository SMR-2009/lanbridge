using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    internal sealed class ReceivePromptForm : Form
    {
        private readonly TransferOffer _offer;
        private readonly Button _decline;

        public event Action Accepted;
        public event Action Declined;

        public ReceivePromptForm(TransferOffer offer)
        {
            _offer = offer ?? throw new ArgumentNullException(nameof(offer));

            Text = "Incoming files";
            ClientSize = new Size(540, 430);
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
                Text = "Incoming files",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 18)
            };
            header.Controls.Add(title);

            var subtitle = new Label
            {
                Text = _offer.Peer.Name + " wants to send you " + _offer.Files.Count + " file"
                     + (_offer.Files.Count == 1 ? "." : "s."),
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = false,
                Size = new Size(492, 22),
                Location = new Point(24, 54)
            };
            header.Controls.Add(subtitle);

            var sender = new Label
            {
                Text = "From " + _offer.Peer.Name + "  •  " + _offer.Peer.Address,
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(492, 22),
                Location = new Point(24, 108)
            };
            Controls.Add(sender);

            var list = new ListBox
            {
                Location = new Point(24, 136),
                Size = new Size(492, 178),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5f),
                HorizontalScrollbar = true,
                IntegralHeight = false
            };
            foreach (var file in _offer.Files.Take(8))
            {
                list.Items.Add(file.Name + "   (" + UiFormat.Size(file.Size) + ")");
            }
            if (_offer.Files.Count > 8)
            {
                list.Items.Add("+ " + (_offer.Files.Count - 8) + " more files");
            }
            Controls.Add(list);

            var total = new Label
            {
                Text = "Total: " + UiFormat.Size(_offer.TotalSize) + "  •  Saved to your LanBridge received folder",
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                Size = new Size(492, 22),
                Location = new Point(24, 322)
            };
            Controls.Add(total);

            var accept = MakeButton("Accept", true);
            accept.Location = new Point(324, 374);
            accept.Click += (s, e) =>
            {
                Accepted?.Invoke();
                Close();
            };
            Controls.Add(accept);

            _decline = MakeButton("Decline", false);
            _decline.Location = new Point(436, 374);
            _decline.Click += (s, e) =>
            {
                Declined?.Invoke();
                Close();
            };
            Controls.Add(_decline);

            AcceptButton = _decline;
            CancelButton = _decline;
        }

                private static Button MakeButton(string text, bool primary)
        {
            return UiTheme.CreateButton(text, primary);
        }

    }
}
