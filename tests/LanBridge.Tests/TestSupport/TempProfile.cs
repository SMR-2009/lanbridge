using System;
using System.IO;

namespace LanBridge.Tests
{
    public sealed class TempProfile : IDisposable
    {
        public string Root { get; }
        public string ConfigPath => Path.Combine(Root, "config.json");
        public string PeerPath => Path.Combine(Root, "peers.json");
        public string IdentityDir => Path.Combine(Root, "identity");
        public string ReceiveDir => Path.Combine(Root, "received");

        public TempProfile()
        {
            Root = Path.Combine(Path.GetTempPath(), "lanbridge-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(IdentityDir);
            Directory.CreateDirectory(ReceiveDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}
