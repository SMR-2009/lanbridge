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

            // A resume sidecar is meaningless without its part file. Remove
            // orphaned or expired sidecars so they cannot accumulate forever.
            foreach (var metadata in Directory.EnumerateFiles(path, "*.part.resume", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(metadata);
                    var partPath = metadata.Substring(0, metadata.Length - ".resume".Length);
                    if (info.LastWriteTimeUtc >= cutoff && File.Exists(partPath)) continue;

                    info.Delete();
                    log.Info("Removed stale resume metadata " + Sanitizer.SanitizeLogValue(info.Name));
                }
                catch (Exception ex)
                {
                    log.Warn("Could not remove stale resume metadata: " + Sanitizer.SanitizeLogValue(ex.Message));
                }
            }

            return removed;
        }
    }
}
