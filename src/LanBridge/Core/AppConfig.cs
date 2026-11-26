using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LanBridge.Core
{
    [DataContract]
    public sealed class AppConfig
    {
        [DataMember] public string DeviceName { get; set; }
        [DataMember] public string ReceiveFolder { get; set; }
        [DataMember] public bool StartWithWindows { get; set; }
        [DataMember] public bool Discoverable { get; set; }
        [DataMember] public bool AutoAcceptFromPaired { get; set; }
        [DataMember] public bool FirewallRuleAdded { get; set; }
        [DataMember] public string DeviceId { get; set; }
        [DataMember] public int TcpPort { get; set; }
        [DataMember] public int UdpPort { get; set; }

        public static AppConfig CreateDefault()
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new AppConfig
            {
                DeviceName = Environment.MachineName,
                ReceiveFolder = Path.Combine(profile, "Downloads", "LanBridge"),
                StartWithWindows = true,
                Discoverable = true,
                AutoAcceptFromPaired = false,
                FirewallRuleAdded = false,
                DeviceId = Guid.NewGuid().ToString("N"),
                TcpPort = 45873,
                UdpPort = 45873
            };
        }

        public static AppConfig Load(string path, ILogger log)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            log = log ?? NullLogger.Instance;

            if (!File.Exists(path))
            {
                var created = CreateDefault();
                created.Save(path);
                return created;
            }

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                    var loaded = serializer.ReadObject(stream) as AppConfig;
                    if (loaded == null) throw new SerializationException("Config was empty.");
                    loaded.Normalize();
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                var quarantine = Path.Combine(
                    Path.GetDirectoryName(path) ?? ".",
                    Path.GetFileNameWithoutExtension(path) + ".corrupt-" +
                    DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json");
                try { File.Move(path, quarantine); } catch { }
                log.Warn("Config was unreadable; quarantined to " + quarantine);
                log.Error("Config load failed", ex);
                var defaults = CreateDefault();
                defaults.Save(path);
                return defaults;
            }
        }

        public void Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            Normalize();

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var tmp = path + ".tmp";
            try
            {
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                    serializer.WriteObject(stream, this);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
            }
            finally
            {
                if (File.Exists(tmp)) try { File.Delete(tmp); } catch { }
            }
        }

        private void Normalize()
        {
            if (string.IsNullOrWhiteSpace(DeviceName)) DeviceName = Environment.MachineName;
            if (string.IsNullOrWhiteSpace(DeviceId)) DeviceId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(ReceiveFolder))
            {
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                ReceiveFolder = Path.Combine(profile, "Downloads", "LanBridge");
            }
            if (TcpPort <= 0 || TcpPort > 65535) TcpPort = 45873;
            if (UdpPort <= 0 || UdpPort > 65535) UdpPort = 45873;
        }
    }
}
