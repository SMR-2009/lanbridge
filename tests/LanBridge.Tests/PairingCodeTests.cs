using System;
using System.Linq;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class PairingCodeTests
    {
        [Fact]
        public void KnownVector_MatchesReference()
        {
            var fpI = new string('a', 64);
            var fpR = new string('b', 64);
            var nonceI = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
            var nonceR = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();

            var code = PairingCode.Compute(fpI, fpR, nonceI, nonceR);
            Assert.Equal("407104", code);

            var proof = PairingCode.ComputeProof(code, nonceI, nonceR, fpI, fpR);
            Assert.Equal("Ihd2+Z1iLAvvBEqpDJtz8oI1ICLUIQkyyQTIU5HbTeE=", Convert.ToBase64String(proof));
            Assert.True(PairingCode.VerifyProof(code, nonceI, nonceR, fpI, fpR, proof));
            Assert.False(PairingCode.VerifyProof(code, nonceI, nonceR, fpI, fpR, new byte[32]));
        }

        [Fact]
        public void Code_IsAlwaysSixDigits()
        {
            for (var i = 0; i < 25; i++)
            {
                var code = PairingCode.Compute(
                    new string((char)('a' + i % 6), 64),
                    new string('f', 64),
                    BitConverter.GetBytes(i),
                    BitConverter.GetBytes(i * 7));
                Assert.Equal(6, code.Length);
                Assert.True(code.All(char.IsDigit));
            }
        }

        [Fact]
        public void DifferentNonces_ProduceDifferentCode()
        {
            var a = PairingCode.Compute(new string('a', 64), new string('b', 64), new byte[16], new byte[16]);
            var b = PairingCode.Compute(new string('a', 64), new string('b', 64), new byte[16], new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void DifferentFingerprints_ProduceDifferentCode()
        {
            var a = PairingCode.Compute(new string('a', 64), new string('b', 64), new byte[16], new byte[16]);
            var b = PairingCode.Compute(new string('a', 64), new string('c', 64), new byte[16], new byte[16]);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void FixedTimeEquals_HandlesLengthsAndNulls()
        {
            Assert.True(PairingCode.FixedTimeEquals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }));
            Assert.False(PairingCode.FixedTimeEquals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 4 }));
            Assert.False(PairingCode.FixedTimeEquals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2 }));
            Assert.False(PairingCode.FixedTimeEquals(null, new byte[] { 1 }));
            Assert.False(PairingCode.FixedTimeEquals(new byte[] { 1 }, null));
        }
    }
}
