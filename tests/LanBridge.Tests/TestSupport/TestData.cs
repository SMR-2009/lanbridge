using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using LanBridge.Core;

namespace LanBridge.Tests
{
    internal static class TestData
    {
        public static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public static DeviceIdentity Identity(TempProfile profile, string name)
        {
            return DeviceIdentity.LoadOrCreate(Path.Combine(profile.Root, name), name, NullLogger.Instance);
        }

        public static PeerRecord Record(string fingerprint, string deviceId, string name, string ip, int port)
        {
            return new PeerRecord
            {
                Fingerprint = fingerprint,
                DeviceId = deviceId,
                Name = name,
                LastIp = ip,
                LastPort = port,
                PairedAtUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow
            };
        }
    }

    internal sealed class CollectingProgress : IProgress<TransferProgress>
    {
        private readonly List<TransferProgress> _items = new List<TransferProgress>();

        public IReadOnlyList<TransferProgress> Items
        {
            get { lock (_items) return _items.ToArray(); }
        }

        public void Report(TransferProgress value)
        {
            lock (_items) _items.Add(value);
        }
    }

    internal sealed class CollectingLogger : ILogger
    {
        private readonly List<string> _lines = new List<string>();

        public IReadOnlyList<string> Lines
        {
            get { lock (_lines) return _lines.ToArray(); }
        }

        public void Info(string message) => Add("INFO", message);
        public void Warn(string message) => Add("WARN", message);

        public void Error(string message, Exception exception = null)
        {
            Add("ERROR", message + (exception == null ? string.Empty : ": " + exception.Message));
        }

        private void Add(string level, string message)
        {
            lock (_lines) _lines.Add(level + " " + message);
        }
    }
}
