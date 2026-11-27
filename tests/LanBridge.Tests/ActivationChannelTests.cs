using System;
using System.Threading;
using System.Threading.Tasks;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class ActivationChannelTests
    {
        [Fact]
        public async Task Signal_RaisesTheActivationCallback()
        {
            var name = @"Local\LanBridge.Activate.Test." + Guid.NewGuid().ToString("N");
            var calls = 0;
            using var channel = ActivationChannel.Create(
                () => Interlocked.Increment(ref calls),
                name);

            Assert.True(ActivationChannel.Signal(name));
            await WaitUntilAsync(() => Volatile.Read(ref calls) == 1, 2000);
            Assert.Equal(1, Volatile.Read(ref calls));
        }

        [Fact]
        public void Signal_WithoutAListener_ReturnsFalse()
        {
            var name = @"Local\LanBridge.Activate.Test." + Guid.NewGuid().ToString("N");
            Assert.False(ActivationChannel.Signal(name));
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }
    }
}
