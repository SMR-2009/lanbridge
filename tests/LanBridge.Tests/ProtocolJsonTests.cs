using System;
using System.Collections.Generic;
using System.Text;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class ProtocolJsonTests
    {
        [Fact]
        public void Hello_RoundTrips()
        {
            var hello = new HelloDto
            {
                V = 1,
                Id = "abc",
                Name = "Rohan-PC",
                Fp = new string('a', 64),
                Nonce = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                Intent = "pair",
                Paired = false
            };

            var bytes = ProtocolJson.Serialize(hello);
            var loaded = ProtocolJson.Deserialize<HelloDto>(bytes);

            Assert.Equal(1, loaded.V);
            Assert.Equal("abc", loaded.Id);
            Assert.Equal("Rohan-PC", loaded.Name);
            Assert.Equal(new string('a', 64), loaded.Fp);
            Assert.Equal(hello.Nonce, loaded.Nonce);
            Assert.Equal("pair", loaded.Intent);
            Assert.False(loaded.Paired);
        }

        [Fact]
        public void Offer_RoundTripsWithFiles()
        {
            var offer = new OfferDto
            {
                TransferId = "t1",
                TotalSize = 42,
                Files = new List<OfferFileDto>
                {
                    new OfferFileDto { Name = "a.txt", Size = 10 },
                    new OfferFileDto { Name = "b.txt", Size = 32 }
                }
            };

            var bytes = ProtocolJson.Serialize(offer);
            var loaded = ProtocolJson.Deserialize<OfferDto>(bytes);

            Assert.Equal("t1", loaded.TransferId);
            Assert.Equal(42, loaded.TotalSize);
            Assert.Equal(2, loaded.Files.Count);
            Assert.Equal("a.txt", loaded.Files[0].Name);
            Assert.Equal(32, loaded.Files[1].Size);
        }

        [Fact]
        public void Error_CarriesFailureContext()
        {
            var error = new ErrorDto
            {
                Code = "transfer_failed",
                Message = "hash mismatch",
                FileIndex = 2,
                Completed = 1
            };

            var loaded = ProtocolJson.Deserialize<ErrorDto>(ProtocolJson.Serialize(error));

            Assert.Equal("transfer_failed", loaded.Code);
            Assert.Equal("hash mismatch", loaded.Message);
            Assert.Equal(2, loaded.FileIndex);
            Assert.Equal(1, loaded.Completed);
        }

        [Fact]
        public void UnknownFields_AreIgnored()
        {
            var json = Encoding.UTF8.GetBytes("{\"v\":1,\"id\":\"abc\",\"name\":\"n\",\"fp\":\"f\",\"nonce\":\"n\",\"intent\":\"ping\",\"paired\":true,\"unknown\":42}");
            var loaded = ProtocolJson.Deserialize<HelloDto>(json);

            Assert.Equal(1, loaded.V);
            Assert.Equal("abc", loaded.Id);
            Assert.True(loaded.Paired);
        }
    }
}
