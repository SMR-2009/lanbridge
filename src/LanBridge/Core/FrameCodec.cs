using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LanBridge.Core
{
    public static class FrameCodec
    {
        public const int MaxControlPayload = 1024 * 1024;
        public const int MaxFileDataPayload = 65540;

        public static async Task WriteAsync(
            Stream stream,
            FrameType type,
            byte[] payload,
            CancellationToken cancellationToken = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!Enum.IsDefined(typeof(FrameType), type))
                throw new ProtocolException("unknown_type", "Unknown frame type: " + (byte)type);

            payload = payload ?? Array.Empty<byte>();
            var max = type == FrameType.FileData ? MaxFileDataPayload : MaxControlPayload;
            if (payload.Length > max)
                throw new ProtocolException("frame_too_large", "Frame payload exceeds the allowed size.");

            cancellationToken.ThrowIfCancellationRequested();

            var buffer = new byte[5 + payload.Length];
            buffer[0] = (byte)(payload.Length >> 24);
            buffer[1] = (byte)(payload.Length >> 16);
            buffer[2] = (byte)(payload.Length >> 8);
            buffer[3] = (byte)payload.Length;
            buffer[4] = (byte)type;
            if (payload.Length > 0) Buffer.BlockCopy(payload, 0, buffer, 5, payload.Length);

            await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<Frame> ReadAsync(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var header = new byte[5];
            await ReadExactAsync(stream, header, 5, cancellationToken).ConfigureAwait(false);

            var length = ((uint)header[0] << 24) |
                         ((uint)header[1] << 16) |
                         ((uint)header[2] << 8) |
                         header[3];
            if (length > MaxControlPayload)
                throw new ProtocolException("frame_too_large", "Frame length exceeds 1 MiB.");

            var type = (FrameType)header[4];
            if (!Enum.IsDefined(typeof(FrameType), type))
                throw new ProtocolException("unknown_type", "Unknown frame type: " + header[4]);

            if (type == FrameType.FileData && length > MaxFileDataPayload)
                throw new ProtocolException("file_data_too_large", "File-data frame exceeds 65,540 bytes.");

            var payload = length == 0 ? Array.Empty<byte>() : new byte[(int)length];
            if (length > 0)
                await ReadExactAsync(stream, payload, (int)length, cancellationToken).ConfigureAwait(false);

            return new Frame(type, payload);
        }

        private static async Task ReadExactAsync(
            Stream stream,
            byte[] buffer,
            int count,
            CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(buffer, offset, count - offset, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    throw new ProtocolException("truncated", "Stream ended before the frame was complete.");
                offset += read;
            }
        }
    }
}
