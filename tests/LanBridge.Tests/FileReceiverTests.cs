using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class FileReceiverTests
    {
        [Fact]
        public void Complete_WritesExactContentsAndRemovesPart()
        {
            using var profile = new TempProfile();
            var bytes = Encoding.UTF8.GetBytes("hello lanbridge");
            using (var receiver = new FileReceiver(profile.ReceiveDir, "note.txt", bytes.Length))
            {
                receiver.Write(bytes, 0, bytes.Length);
                receiver.Complete(Sha256(bytes));
                Assert.True(File.Exists(receiver.FinalPath));
                Assert.Equal(bytes, File.ReadAllBytes(receiver.FinalPath));
            }
            Assert.Empty(Directory.GetFiles(profile.ReceiveDir, "*.part"));
        }

        [Fact]
        public void WrongHash_DeletesPartAndLeavesNoFinalFile()
        {
            using var profile = new TempProfile();
            var bytes = Encoding.UTF8.GetBytes("data");
            var receiver = new FileReceiver(profile.ReceiveDir, "bad.txt", bytes.Length);
            receiver.Write(bytes, 0, bytes.Length);
            Assert.Throws<InvalidDataException>(() => receiver.Complete(new string('0', 64)));
            receiver.Dispose();
            Assert.Empty(Directory.GetFiles(profile.ReceiveDir, "*.part"));
            Assert.False(File.Exists(Path.Combine(profile.ReceiveDir, "bad.txt")));
        }

        [Fact]
        public void WrongByteCount_DeletesPart()
        {
            using var profile = new TempProfile();
            var bytes = Encoding.UTF8.GetBytes("short");
            var receiver = new FileReceiver(profile.ReceiveDir, "short.txt", bytes.Length + 1);
            receiver.Write(bytes, 0, bytes.Length);
            Assert.Throws<InvalidDataException>(() => receiver.Complete(Sha256(bytes)));
            receiver.Dispose();
            Assert.Empty(Directory.GetFiles(profile.ReceiveDir, "*.part"));
        }

        [Fact]
        public void DuplicateName_GetsSuffix()
        {
            using var profile = new TempProfile();
            File.WriteAllText(Path.Combine(profile.ReceiveDir, "a.txt"), "old");
            var bytes = Encoding.UTF8.GetBytes("new");
            using (var receiver = new FileReceiver(profile.ReceiveDir, "a.txt", bytes.Length))
            {
                receiver.Write(bytes, 0, bytes.Length);
                receiver.Complete(Sha256(bytes));
                Assert.Equal("old", File.ReadAllText(Path.Combine(profile.ReceiveDir, "a.txt")));
                Assert.Equal("new", File.ReadAllText(Path.Combine(profile.ReceiveDir, "a (1).txt")));
            }
        }

        [Fact]
        public void TraversalName_StaysInsideReceiveFolder()
        {
            using var profile = new TempProfile();
            var bytes = Encoding.UTF8.GetBytes("safe");
            using (var receiver = new FileReceiver(profile.ReceiveDir, @"..\..\evil.txt", bytes.Length))
            {
                receiver.Write(bytes, 0, bytes.Length);
                receiver.Complete(Sha256(bytes));
            }

            Assert.True(File.Exists(Path.Combine(profile.ReceiveDir, "evil.txt")));
            Assert.False(File.Exists(Path.Combine(profile.Root, "evil.txt")));
        }

        private static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }
    }
}
