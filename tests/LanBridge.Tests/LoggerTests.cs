using System.IO;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class LoggerTests
    {
        [Fact]
        public void Logs_AreWritten_AndRotated()
        {
            using var profile = new TempProfile();
            var path = Path.Combine(profile.Root, "lanbridge.log");
            using (var log = new FileLogger(path, maxBytes: 256, keepFiles: 2))
            {
                for (var i = 0; i < 50; i++) log.Info(new string('x', 40));
            }
            Assert.True(File.Exists(path));
            Assert.True(Directory.GetFiles(profile.Root, "lanbridge*.log").Length >= 2);
        }
    }
}
