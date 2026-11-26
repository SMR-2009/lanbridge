using System;
using System.IO;

namespace LanBridge.Core
{
    public static class ReceiveFolderCleanup
    {
        public static int DeleteStalePartFiles(
            string folder,
            DateTime utcNow,
            TimeSpan olderThan,
            ILogger log = null)
        {
            log = log ?? NullLogger.Instance;
            if (string.IsNullOrWhiteSpace(folder)) return 0;

            string path;
            try
            {
                path = Path.GetFullPath(folder);
            }
            catch (Exception ex)
            {
                log.Warn("Could not inspect the receive folder: " + Sanitizer.SanitizeLogValue(ex.Message));
                return 0;
            }

            if (!Directory.Exists(path)) return 0;

            var cutoff = utcNow - olderThan;
            var removed = 0;

            foreach (var candidate in Directory.EnumerateFiles(path, "*.part", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(candidate);
                    if (info.LastWriteTimeUtc >= cutoff) continue;

                    info.Delete();
                    removed++;
                    log.Info("Removed stale partial transfer " + Sanitizer.SanitizeLogValue(info.Name));
                }
                catch (Exception ex)
                {
                    log.Warn("Could not remove a stale partial transfer: " + Sanitizer.SanitizeLogValue(ex.Message));
                }
            }

            return removed;
        }
    }
}
