using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanBridge
{
    internal sealed class UiButton : Button
    {
        private readonly bool _primary;
        private bool _hover;
        private bool _pressed;

        public UiButton(string text, bool primary)
        {
            _primary = primary;
            Text = text;
            Size = new Size(104, 36);
            MinimumSize = new Size(88, 34);
            AutoSize = false;
            FlatStyle = FlatStyle.Flat;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            ForeColor = primary ? Color.White : UiTheme.Ink;
            TextAlign = ContentAlignment.MiddleCenter;
            UseVisualStyleBackColor = false;
            Padding = new Padding(8, 0, 8, 0);
            Margin = new Padding(8, 0, 0, 0);

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var background = !Enabled
                ? Color.FromArgb(241, 245, 249)
                : _primary
                    ? (_pressed ? Color.FromArgb(30, 64, 175) : _hover ? Color.FromArgb(29, 78, 216) : UiTheme.Blue)
                    : (_pressed ? Color.FromArgb(226, 232, 240) : _hover ? Color.FromArgb(241, 245, 249) : Color.White);

            using (var brush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            using (var pen = new Pen(_primary ? UiTheme.Blue : UiTheme.Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, ClientSize.Width - 1), Math.Max(0, ClientSize.Height - 1));
            }

            var textColor = !Enabled ? Color.FromArgb(148, 163, 184) : ForeColor;
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                textColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            if (Focused && ShowFocusCues)
            {
                var focus = Rectangle.Inflate(ClientRectangle, -4, -4);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, textColor, background);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }
}
