using System;
using LanBridge.Core;
using Microsoft.Win32;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class StartupTests
    {
        [Fact]
        public void SetEnabled_WritesAndRemovesValue()
        {
            var keyPath = @"Software\LanBridgeTests\" + Guid.NewGuid().ToString("N");
            try
            {
                Startup.SetEnabled(true, @"C:\Tools\LanBridge.exe", keyPath);
                Assert.True(Startup.IsEnabled(keyPath));

                Startup.SetEnabled(false, @"C:\Tools\LanBridge.exe", keyPath);
                Assert.False(Startup.IsEnabled(keyPath));
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); } catch { }
            }
        }

        [Fact]
        public void IsEnabled_ReturnsFalseForMissingKey()
        {
            var keyPath = @"Software\LanBridgeTests\" + Guid.NewGuid().ToString("N");
            Assert.False(Startup.IsEnabled(keyPath));
        }
    }
}
