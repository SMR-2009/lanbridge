using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class SanitizerTests
    {
        [Fact]
        public void DeviceName_RemovesLineBreaksAndControls()
        {
            var result = Sanitizer.SanitizeDeviceName("Rohan\r\nFAKE\u0001");
            Assert.DoesNotContain("\r", result);
            Assert.DoesNotContain("\n", result);
            Assert.DoesNotContain("\u0001", result);
            Assert.Equal("Rohan FAKE", result);
        }

        [Fact]
        public void DeviceName_IsLimitedAndHasFallback()
        {
            Assert.Equal(64, Sanitizer.SanitizeDeviceName(new string('x', 100)).Length);
            Assert.Equal("Windows PC", Sanitizer.SanitizeDeviceName(""));
            Assert.Equal("Windows PC", Sanitizer.SanitizeDeviceName("\u0001\u0002"));
        }

        [Fact]
        public void LogValue_RemovesNewlinesAndLimitsLength()
        {
            var result = Sanitizer.SanitizeLogValue("a\r\nb", 10);
            Assert.DoesNotContain("\r", result);
            Assert.DoesNotContain("\n", result);
            Assert.True(result.Length <= 10);
        }

        [Fact]
        public void MapError_KnownAndUnknown()
        {
            Assert.Contains("checksum", Sanitizer.MapError("hash_mismatch"));
            Assert.Equal("The transfer failed.", Sanitizer.MapError("not_a_real_code"));
            Assert.DoesNotContain("not_a_real_code", Sanitizer.MapError("not_a_real_code"));
        }
    }
}
