using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class FrameCodecTests
    {
        [Fact]
        public async Task Write_UsesBigEndianLengthAndType()
        {
            using var stream = new MemoryStream();
            await FrameCodec.WriteAsync(stream, FrameType.Hello, Encoding.UTF8.GetBytes("{\"v\":1}"), default);
            Assert.Equal(
                new byte[] { 0x00, 0x00, 0x00, 0x07, 0x01, 0x7B, 0x22, 0x76, 0x22, 0x3A, 0x31, 0x7D },
                stream.ToArray());
        }

        [Fact]
        public async Task RoundTrip_PreservesTypeAndPayload()
        {
            using var stream = new MemoryStream();
            var payload = new byte[] { 9, 8, 7 };
            await FrameCodec.WriteAsync(stream, FrameType.PairProof, payload, default);
            stream.Position = 0;
            var frame = await FrameCodec.ReadAsync(stream, default);
            Assert.Equal(FrameType.PairProof, frame.Type);
            Assert.Equal(payload, frame.Payload);
        }

        [Fact]
        public async Task OversizedControlFrame_IsRejectedBeforeAllocation()
        {
            using var stream = new MemoryStream(new byte[] { 0x00, 0x10, 0x00, 0x01, 0x01 });
            await Assert.ThrowsAsync<ProtocolException>(() => FrameCodec.ReadAsync(stream, default));
        }

        [Fact]
        public async Task TruncatedPayload_IsRejected()
        {
            using var stream = new MemoryStream(new byte[] { 0x00, 0x00, 0x00, 0x07, 0x01, 0x7B, 0x22 });
            await Assert.ThrowsAsync<ProtocolException>(() => FrameCodec.ReadAsync(stream, default));
        }

        [Fact]
        public async Task UnknownType_IsRejected()
        {
            using var stream = new MemoryStream(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x63 });
            await Assert.ThrowsAsync<ProtocolException>(() => FrameCodec.ReadAsync(stream, default));
        }

        [Fact]
        public async Task OversizedFileDataFrame_IsRejected()
        {
            using var stream = new MemoryStream();
            await Assert.ThrowsAsync<ProtocolException>(() => FrameCodec.WriteAsync(
                stream,
                FrameType.FileData,
                new byte[65541],
                default));
        }
    }
}
