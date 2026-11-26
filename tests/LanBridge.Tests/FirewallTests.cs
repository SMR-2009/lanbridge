using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class FirewallTests
    {
        [Fact]
        public void AddArguments_AreScopedToTheProgramAndLocalSubnet()
        {
            var args = Firewall.BuildAddRuleArguments(@"C:\Program Files\LanBridge\LanBridge.exe");

            Assert.Contains("name=\"LanBridge\"", args);
            Assert.Contains("program=\"C:\\Program Files\\LanBridge\\LanBridge.exe\"", args);
            Assert.Contains("profile=private,public", args);
            Assert.Contains("remoteip=LocalSubnet", args);
            Assert.Contains("dir=in", args);
            Assert.Contains("action=allow", args);
        }

        [Fact]
        public void DeleteArguments_TargetTheRule()
        {
            var args = Firewall.BuildDeleteRuleArguments();
            Assert.Contains("name=\"LanBridge\"", args);
            Assert.Contains("delete rule", args);
        }
    }
}
