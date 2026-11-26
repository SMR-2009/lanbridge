using System.IO;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class DeviceIdentityTests
    {
        [Fact]
        public void LoadOrCreate_IsStable_AndHasPrivateKey()
        {
            using var profile = new TempProfile();
            using var first = DeviceIdentity.LoadOrCreate(profile.IdentityDir, "device-1", NullLogger.Instance);
            Assert.Equal(64, first.Fingerprint.Length);
            Assert.True(first.Certificate.HasPrivateKey);
            Assert.True(File.Exists(Path.Combine(profile.IdentityDir, "identity.pfx.dpapi")));

            using var second = DeviceIdentity.LoadOrCreate(profile.IdentityDir, "device-1", NullLogger.Instance);
            Assert.Equal(first.Fingerprint, second.Fingerprint);
        }

        [Fact]
        public void CorruptIdentity_IsQuarantined_AndReplaced()
        {
            using var profile = new TempProfile();
            File.WriteAllBytes(Path.Combine(profile.IdentityDir, "identity.pfx.dpapi"), new byte[] { 1, 2, 3 });
            using var identity = DeviceIdentity.LoadOrCreate(profile.IdentityDir, "device-1", NullLogger.Instance);
            Assert.True(identity.Certificate.HasPrivateKey);
            Assert.Contains(Directory.GetFiles(profile.IdentityDir), f => f.Contains(".corrupt-"));
        }
    }
}
