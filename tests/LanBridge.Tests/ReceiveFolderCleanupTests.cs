using System;
using System.IO;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class ReceiveFolderCleanupTests : IDisposable
    {
        private readonly string _folder;

        public ReceiveFolderCleanupTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "lanbridge-cleanup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [Fact]
        public void DeletesOnlyExpiredPartsInsideThePrivateFolder()
        {
            var parts = Path.Combine(_folder, FileReceiver.PartFolderName);
            Directory.CreateDirectory(parts);
            var expired = Path.Combine(parts, "holiday.zip.part");
            var fresh = Path.Combine(parts, "notes.pdf.part");
            var finished = Path.Combine(_folder, "done.zip");
            var userFileNamedPart = Path.Combine(_folder, "report.part");
            File.WriteAllText(expired, "partial");
            File.WriteAllText(fresh, "partial");
            File.WriteAllText(finished, "complete");
            File.WriteAllText(userFileNamedPart, "user data");
            File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(userFileNamedPart, DateTime.UtcNow.AddHours(-2));

            var removed = ReceiveFolderCleanup.DeleteStalePartFiles(
                _folder,
                DateTime.UtcNow,
                TimeSpan.FromHours(1));

            Assert.Equal(1, removed);
            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(fresh));
            Assert.True(File.Exists(finished));
            Assert.True(File.Exists(userFileNamedPart));
        }

        [Fact]
        public void MissingFolderIsSafe()
        {
            var removed = ReceiveFolderCleanup.DeleteStalePartFiles(
                Path.Combine(_folder, "missing"),
                DateTime.UtcNow,
                TimeSpan.FromHours(1));

            Assert.Equal(0, removed);
        }

        public void Dispose()
        {
            try { Directory.Delete(_folder, true); } catch { }
        }
    }
}
