using System;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class UpdateCheckerTests
    {
        [Theory]
        [InlineData("v0.1.9", "0.1.8", true)]
        [InlineData("0.1.8", "0.1.8", false)]
        [InlineData("v0.2.0", "0.1.8", true)]
        [InlineData("v0.1.10", "0.1.8", true)]
        [InlineData("v0.1.7", "0.1.8", false)]
        [InlineData("not-a-version", "0.1.8", false)]
        public void IsNewer_ComparesReleaseTags(string tag, string current, bool expected)
        {
            Assert.Equal(expected, UpdateChecker.IsNewer(tag, Version.Parse(current)));
        }
    }
}
