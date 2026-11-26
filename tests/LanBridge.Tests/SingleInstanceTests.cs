using System;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class SingleInstanceTests
    {
        [Fact]
        public void SecondAcquireFails_AndReleaseAllowsAgain()
        {
            var name = "Local\\LanBridgeTests-" + Guid.NewGuid().ToString("N");

            Assert.True(SingleInstance.TryAcquire(out var first, name));
            try
            {
                Assert.False(SingleInstance.TryAcquire(out var second, name));
                second?.Dispose();
            }
            finally
            {
                first.ReleaseMutex();
                first.Dispose();
            }

            Assert.True(SingleInstance.TryAcquire(out var third, name));
            third.ReleaseMutex();
            third.Dispose();
        }
    }
}
