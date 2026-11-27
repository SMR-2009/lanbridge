using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Serialization.Json;

namespace LanBridge.Core
{
    public static class ProtocolJson
    {
        public const int MaxPayloadBytes = 1024 * 1024;
        private const int MaxObjectGraphItems = 8192;
        private static readonly ConcurrentDictionary<Type, DataContractJsonSerializer> Serializers =
            new ConcurrentDictionary<Type, DataContractJsonSerializer>();

        public static byte[] Serialize<T>(T value)
        {
            var serializer = GetSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                var bytes = stream.ToArray();
                if (bytes.Length > MaxPayloadBytes)
                    throw new ProtocolException("json_too_large", "JSON payload exceeds 1 MiB.");
                return bytes;
            }
        }

        private static DataContractJsonSerializer GetSerializer(Type type)
        {
            return Serializers.GetOrAdd(type, value => new DataContractJsonSerializer(
                value,
                new DataContractJsonSerializerSettings
                {
                    MaxItemsInObjectGraph = MaxObjectGraphItems
                }));
        }

        public static T Deserialize<T>(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > MaxPayloadBytes)
                throw new ProtocolException("json_too_large", "JSON payload exceeds 1 MiB.");

            try
            {
                var serializer = GetSerializer(typeof(T));
                using (var stream = new MemoryStream(payload))
                {
                    var value = serializer.ReadObject(stream);
                    if (value == null)
                        throw new ProtocolException("json_invalid", "JSON payload was empty.");
                    return (T)value;
                }
            }
            catch (ProtocolException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ProtocolException("json_invalid", "Invalid JSON payload.", ex);
            }
        }
    }
}
