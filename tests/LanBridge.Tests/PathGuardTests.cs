using System.IO;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class PathGuardTests
    {
        [Theory]
        [InlineData(@"..\..\Windows\System32\evil.dll", "evil.dll")]
        [InlineData(@"C:\Windows\evil.txt", "evil.txt")]
        [InlineData("a/b/c.txt", "c.txt")]
        [InlineData("CON", "_CON")]
        [InlineData("aux.txt", "_aux.txt")]
        [InlineData("COM1.log", "_COM1.log")]
        [InlineData("LPT9", "_LPT9")]
        [InlineData("NUL.tar.gz", "_NUL.tar.gz")]
        [InlineData("COM1.foo.bar", "_COM1.foo.bar")]
        [InlineData("CON .txt", "_CON .txt")]
        [InlineData("LPT9.a.b", "_LPT9.a.b")]
        [InlineData("name.", "name")]
        [InlineData("name   ", "name")]
        [InlineData("a:b.txt", "ab.txt")]
        [InlineData("bad\u0001name.txt", "badname.txt")]
        [InlineData("", "file")]
        [InlineData(".", "file")]
        [InlineData("..", "file")]
        public void SanitizeFileName_ProducesSafeNames(string input, string expected)
        {
            Assert.Equal(expected, PathGuard.SanitizeFileName(input));
        }

        [Fact]
        public void SanitizeFileName_TruncatesButKeepsExtension()
        {
            var input = new string('a', 300) + ".txt";
            var result = PathGuard.SanitizeFileName(input);
            Assert.True(result.Length <= 180);
            Assert.EndsWith(".txt", result);
        }

        [Fact]
        public void GetUniquePath_AddsSuffixes()
        {
            using var profile = new TempProfile();
            var first = Path.Combine(profile.ReceiveDir, "a.txt");
            File.WriteAllText(first, "one");

            var second = PathGuard.GetUniquePath(profile.ReceiveDir, "a.txt");
            Assert.Equal(Path.Combine(profile.ReceiveDir, "a (1).txt"), second);

            File.WriteAllText(second, "two");
            var third = PathGuard.GetUniquePath(profile.ReceiveDir, "a.txt");
            Assert.Equal(Path.Combine(profile.ReceiveDir, "a (2).txt"), third);
        }
    }
}
