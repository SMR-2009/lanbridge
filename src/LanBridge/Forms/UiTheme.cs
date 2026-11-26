using System.Drawing;
using System.Windows.Forms;

namespace LanBridge
{
    internal static class UiTheme
    {
        public static readonly Color Ink = Color.FromArgb(15, 23, 42);
        public static readonly Color Muted = Color.FromArgb(71, 85, 105);
        public static readonly Color Blue = Color.FromArgb(37, 99, 235);
        public static readonly Color Border = Color.FromArgb(203, 213, 225);

        public static Button CreateButton(string text, bool primary)
        {
            return new UiButton(text, primary);
        }
    }
}
