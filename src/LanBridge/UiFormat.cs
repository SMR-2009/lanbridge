using System;
using System.Globalization;
using LanBridge.Core;

namespace LanBridge
{
    internal static class UiFormat
    {
        public static string Size(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024) return bytes.ToString(CultureInfo.CurrentCulture) + " B";

            var value = (double)bytes;
            var units = new[] { "KB", "MB", "GB", "TB" };
            var index = -1;
            do
            {
                value /= 1024d;
                index++;
            }
            while (value >= 1024d && index < units.Length - 1);

            return value.ToString("0.0", CultureInfo.CurrentCulture) + " " + units[index];
        }

        public static string Relative(DateTime utc)
        {
            if (utc == DateTime.MinValue || utc == default) return "Never";

            var delta = DateTime.UtcNow - utc.ToUniversalTime();
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;

            if (delta.TotalSeconds < 60) return "just now";
            if (delta.TotalMinutes < 60) return ((int)delta.TotalMinutes) + "m ago";
            if (delta.TotalHours < 24) return ((int)delta.TotalHours) + "h ago";
            if (delta.TotalDays < 7) return ((int)delta.TotalDays) + "d ago";
            return utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        }

        public static string State(DeviceState state)
        {
            switch (state)
            {
                case DeviceState.Paired: return "Paired";
                case DeviceState.Available: return "Available";
                case DeviceState.Offline: return "Offline";
                case DeviceState.IdentityChanged: return "Identity changed";
                default: return state.ToString();
            }
        }
    }
}
