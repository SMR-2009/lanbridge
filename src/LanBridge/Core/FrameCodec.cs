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
        public const int FileDataHeaderSize = 9;
        public const int MaxFileChunk = MaxFileDataPayload - 4;

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
            var type = (FrameType)header[4];
            ValidateHeader(length, type, header[4]);

            var payload = length == 0 ? Array.Empty<byte>() : new byte[(int)length];
            if (length > 0)
                await ReadExactAsync(stream, payload, (int)length, cancellationToken).ConfigureAwait(false);

            return new Frame(type, payload);
        }

        public static void WriteFileDataHeader(byte[] buffer, int fileIndex, int dataLength)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length < FileDataHeaderSize)
                throw new ArgumentException("The frame buffer is too small.", nameof(buffer));
            if (dataLength < 0 || dataLength > MaxFileChunk)
                throw new ArgumentOutOfRangeException(nameof(dataLength));

            var payloadLength = dataLength + 4;
            buffer[0] = (byte)(payloadLength >> 24);
            buffer[1] = (byte)(payloadLength >> 16);
            buffer[2] = (byte)(payloadLength >> 8);
            buffer[3] = (byte)payloadLength;
            buffer[4] = (byte)FrameType.FileData;
            buffer[5] = (byte)(fileIndex >> 24);
            buffer[6] = (byte)(fileIndex >> 16);
            buffer[7] = (byte)(fileIndex >> 8);
            buffer[8] = (byte)fileIndex;
        }

        public static Task WriteFileDataAsync(
            Stream stream,
            byte[] buffer,
            int count,
            CancellationToken cancellationToken = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (count < FileDataHeaderSize || count > FileDataHeaderSize + MaxFileChunk || count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            return stream.WriteAsync(buffer, 0, count, cancellationToken);
        }

        public static async Task<FileDataFrame> ReadFileDataAsync(
            Stream stream,
            byte[] dataBuffer,
            CancellationToken cancellationToken = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (dataBuffer == null) throw new ArgumentNullException(nameof(dataBuffer));
            if (dataBuffer.Length < MaxFileDataPayload)
                throw new ArgumentException("The data buffer is too small.", nameof(dataBuffer));

            await ReadExactAsync(stream, dataBuffer, 5, cancellationToken).ConfigureAwait(false);

            var length = ((uint)dataBuffer[0] << 24) |
                         ((uint)dataBuffer[1] << 16) |
                         ((uint)dataBuffer[2] << 8) |
                         dataBuffer[3];
            var type = (FrameType)dataBuffer[4];
            ValidateHeader(length, type, dataBuffer[4]);

            if (type == FrameType.FileData)
            {
                if (length < 4)
                    throw new ProtocolException("short_file_data", "FILE_DATA is missing its file index.");

                await ReadExactAsync(stream, dataBuffer, (int)length, cancellationToken).ConfigureAwait(false);
                return new FileDataFrame(
                    type,
                    ReadInt32(dataBuffer, 0),
                    dataBuffer,
                    4,
                    (int)length - 4,
                    null);
            }

            var payload = length == 0 ? Array.Empty<byte>() : new byte[(int)length];
            if (length > 0)
                await ReadExactAsync(stream, payload, (int)length, cancellationToken).ConfigureAwait(false);

            return new FileDataFrame(type, -1, null, 0, 0, payload);
        }

        private static void ValidateHeader(uint length, FrameType type, byte rawType)
        {
            if (length > MaxControlPayload)
                throw new ProtocolException("frame_too_large", "Frame length exceeds 1 MiB.");
            if (!Enum.IsDefined(typeof(FrameType), type))
                throw new ProtocolException("unknown_type", "Unknown frame type: " + rawType);
            if (type == FrameType.FileData && length > MaxFileDataPayload)
                throw new ProtocolException("file_data_too_large", "File-data frame exceeds 65,540 bytes.");
        }

        private static int ReadInt32(byte[] buffer, int offset)
        {
            return (buffer[offset] << 24) |
                   (buffer[offset + 1] << 16) |
                   (buffer[offset + 2] << 8) |
                   buffer[offset + 3];
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
