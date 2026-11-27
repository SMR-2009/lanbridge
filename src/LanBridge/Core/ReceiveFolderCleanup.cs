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

            var partFolder = Path.Combine(path, FileReceiver.PartFolderName);
            if (!Directory.Exists(partFolder)) return 0;

            var cutoff = utcNow - olderThan;
            var removed = 0;

            try
            {
                foreach (var candidate in Directory.EnumerateFiles(partFolder, "*.part", SearchOption.TopDirectoryOnly))
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
            }
            catch (Exception ex)
            {
                log.Warn("Could not enumerate the private parts folder: " + Sanitizer.SanitizeLogValue(ex.Message));
                return removed;
            }

            // A resume sidecar is meaningful only beside its own .part file in
            // the private parts folder. Never infer a path from user-visible names.
            try
            {
                foreach (var metadata in Directory.EnumerateFiles(
                    partFolder,
                    "*.part.resume",
                    SearchOption.TopDirectoryOnly))
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
            }
            catch (Exception ex)
            {
                log.Warn("Could not enumerate the private resume folder: " + Sanitizer.SanitizeLogValue(ex.Message));
            }

            return removed;
        }
    }
}
