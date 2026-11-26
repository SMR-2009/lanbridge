using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace LanBridge.Core
{
    [DataContract]
    public sealed class PeerRecord
    {
        [DataMember(Name = "fingerprint")] public string Fingerprint { get; set; }
        [DataMember(Name = "deviceId")] public string DeviceId { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "lastIp")] public string LastIp { get; set; }
        [DataMember(Name = "lastPort")] public int LastPort { get; set; }
        [DataMember(Name = "pairedAtUtc")] public DateTime PairedAtUtc { get; set; }
        [DataMember(Name = "lastSeenUtc")] public DateTime LastSeenUtc { get; set; }
    }

    [DataContract]
    public sealed class PeerStoreDocument
    {
        [DataMember(Name = "peers")] public List<PeerRecord> Peers { get; set; }
    }

    public sealed class PeerStore
    {
        private readonly string _path;
        private readonly ILogger _log;
        private readonly object _gate = new object();
        private readonly List<PeerRecord> _peers = new List<PeerRecord>();

        private PeerStore(string path, ILogger log)
        {
            _path = path;
            _log = log ?? NullLogger.Instance;
        }

        public static PeerStore Load(string path, ILogger log)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            log = log ?? NullLogger.Instance;
            var store = new PeerStore(path, log);

            if (!File.Exists(path)) return store;

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(PeerStoreDocument));
                    var document = serializer.ReadObject(stream) as PeerStoreDocument;
                    if (document?.Peers != null)
                    {
                        foreach (var peer in document.Peers)
                        {
                            if (peer != null && !string.IsNullOrWhiteSpace(peer.Fingerprint))
                                store._peers.Add(peer);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var directory = Path.GetDirectoryName(path) ?? ".";
                var baseName = Path.GetFileNameWithoutExtension(path);
                var quarantine = Path.Combine(
                    directory,
                    baseName + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json");
                try { File.Move(path, quarantine); } catch { }
                log.Warn("Peer store was unreadable; quarantined to " + quarantine);
                log.Error("Peer store load failed", ex);
            }

            return store;
        }

        public IReadOnlyList<PeerRecord> Peers
        {
            get { lock (_gate) return _peers.ToList(); }
        }

        public bool ContainsFingerprint(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint)) return false;
            lock (_gate)
            {
                return _peers.Any(p => string.Equals(p.Fingerprint, fingerprint, StringComparison.Ordinal));
            }
        }

        public PeerRecord FindByFingerprint(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint)) return null;
            lock (_gate)
            {
                return _peers.FirstOrDefault(p => string.Equals(p.Fingerprint, fingerprint, StringComparison.Ordinal));
            }
        }

        public IReadOnlyList<PeerRecord> FindByDeviceId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return Array.Empty<PeerRecord>();
            lock (_gate)
            {
                return _peers
                    .Where(p => string.Equals(p.DeviceId, deviceId, StringComparison.Ordinal))
                    .ToList();
            }
        }

        public void Upsert(PeerRecord peer)
        {
            if (peer == null) throw new ArgumentNullException(nameof(peer));
            if (string.IsNullOrEmpty(peer.Fingerprint))
                throw new ArgumentException("A peer must have a fingerprint.", nameof(peer));

            lock (_gate)
            {
                var index = _peers.FindIndex(p => string.Equals(p.Fingerprint, peer.Fingerprint, StringComparison.Ordinal));
                if (index >= 0) _peers[index] = peer;
                else _peers.Add(peer);
            }
        }

        public bool UpdateEndpoint(string fingerprint, string ipAddress, int port)
        {
            if (string.IsNullOrEmpty(fingerprint)) return false;
            lock (_gate)
            {
                var peer = _peers.FirstOrDefault(p => string.Equals(p.Fingerprint, fingerprint, StringComparison.Ordinal));
                if (peer == null) return false;
                peer.LastIp = ipAddress;
                peer.LastPort = port;
                peer.LastSeenUtc = DateTime.UtcNow;
                return true;
            }
        }

        public bool Remove(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint)) return false;
            lock (_gate)
            {
                return _peers.RemoveAll(p => string.Equals(p.Fingerprint, fingerprint, StringComparison.Ordinal)) > 0;
            }
        }

        public void Save()
        {
            lock (_gate)
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var tmp = _path + ".tmp";
                try
                {
                    using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(PeerStoreDocument));
                        serializer.WriteObject(stream, new PeerStoreDocument { Peers = _peers.ToList() });
                        stream.Flush(true);
                    }

                    if (File.Exists(_path)) File.Replace(tmp, _path, null);
                    else File.Move(tmp, _path);
                }
                finally
                {
                    if (File.Exists(tmp)) try { File.Delete(tmp); } catch { }
                }
            }
        }
    }
}
