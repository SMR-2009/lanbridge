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
        public void SetArguments_UpdateTheExistingRuleProgram()
        {
            var args = Firewall.BuildSetRuleArguments(@"C:\Apps\LanBridge.exe");

            Assert.Contains("name=\"LanBridge\"", args);
            Assert.Contains("set rule", args);
            Assert.Contains("program=\"C:\\Apps\\LanBridge.exe\"", args);
            Assert.Contains("remoteip=LocalSubnet", args);
        }

        [Fact]
        public void IsRuleOutputValid_RequiresTheFullInboundScope()
        {
            const string exe = @"C:\Apps\LanBridge.exe";
            var valid = "Rule Name: LanBridge\r\nEnabled: Yes\r\nDirection: In\r\n"
                      + "Profiles: Private,Public\r\nRemoteIP: LocalSubnet\r\n"
                      + "Program: C:\\Apps\\LanBridge.exe\r\nAction: Allow\r\n";

            Assert.True(Firewall.IsRuleOutputValid(valid, exe));
            Assert.False(Firewall.IsRuleOutputValid(valid.Replace("RemoteIP: LocalSubnet", "RemoteIP: Any"), exe));
            Assert.False(Firewall.IsRuleOutputValid(valid.Replace("Direction: In", "Direction: Out"), exe));
            Assert.False(Firewall.IsRuleOutputValid(valid.Replace("Profiles: Private,Public", "Profiles: Private"), exe));
            Assert.False(Firewall.IsRuleOutputValid(valid.Replace("Enabled: Yes", "Enabled: No"), exe));
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
