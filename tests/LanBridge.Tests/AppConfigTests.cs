using System;
using System.IO;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class AppConfigTests
    {
        [Fact]
        public void Defaults_AreSafe()
        {
            var c = AppConfig.CreateDefault();
            Assert.Equal(Environment.MachineName, c.DeviceName);
            Assert.EndsWith(Path.Combine("Downloads", "LanBridge"), c.ReceiveFolder);
            Assert.True(c.Discoverable);
            Assert.False(c.AutoAcceptFromPaired);
            Assert.True(c.StartWithWindows);
            Assert.Equal(45873, c.TcpPort);
            Assert.Equal(45873, c.UdpPort);
            Assert.Equal(32, c.DeviceId.Length);
        }

        [Fact]
        public void RoundTrip_PreservesValues()
        {
            using var profile = new TempProfile();
            var c = AppConfig.CreateDefault();
            c.DeviceName = "Laptop-A";
            c.Discoverable = false;
            c.Save(profile.ConfigPath);
            var loaded = AppConfig.Load(profile.ConfigPath, NullLogger.Instance);
            Assert.Equal("Laptop-A", loaded.DeviceName);
            Assert.False(loaded.Discoverable);
            Assert.Equal(c.DeviceId, loaded.DeviceId);
        }

        [Fact]
        public void CorruptFile_IsQuarantined_AndDefaultsReturned()
        {
            using var profile = new TempProfile();
            File.WriteAllText(profile.ConfigPath, "{not json");
            var c = AppConfig.Load(profile.ConfigPath, NullLogger.Instance);
            Assert.Equal(Environment.MachineName, c.DeviceName);
            Assert.Contains(Directory.GetFiles(profile.Root), f => f.Contains(".corrupt-"));
        }
    }
}
