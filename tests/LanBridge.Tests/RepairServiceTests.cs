using System;
using System.IO;
using System.Threading.Tasks;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class RepairServiceTests
    {
        [Fact]
        public async Task Repair_CreatesFolderSavesConfigAndRemovesStalePart()
        {
            using var profile = new TempProfile();
            var config = AppConfig.CreateDefault();
            config.ReceiveFolder = Path.Combine(profile.Root, "received-new");
            Directory.CreateDirectory(config.ReceiveFolder);

            var stale = Path.Combine(config.ReceiveFolder, "old.bin.part");
            File.WriteAllText(stale, "partial");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

            var report = await RepairService.RunAsync(
                config,
                profile.ConfigPath,
                Path.Combine(profile.Root, "LanBridge.exe"),
                repairFirewall: false,
                manageStartup: false);

            Assert.True(report.Success, string.Join(" | ", report.Warnings));
            Assert.True(Directory.Exists(config.ReceiveFolder));
            Assert.True(File.Exists(profile.ConfigPath));
            Assert.False(File.Exists(stale));
        }

        [Fact]
        public async Task Repair_MissingReceiveFolderIsCreated()
        {
            using var profile = new TempProfile();
            var config = AppConfig.CreateDefault();
            config.ReceiveFolder = Path.Combine(profile.Root, "missing", "nested");

            var report = await RepairService.RunAsync(
                config,
                profile.ConfigPath,
                Path.Combine(profile.Root, "LanBridge.exe"),
                repairFirewall: false,
                manageStartup: false);

            Assert.True(report.Success, string.Join(" | ", report.Warnings));
            Assert.True(Directory.Exists(config.ReceiveFolder));
        }
    }
}
