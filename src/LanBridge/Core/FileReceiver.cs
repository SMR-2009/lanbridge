using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LanBridge.Core
{
    public sealed class FileReceiver : IDisposable
    {
        private readonly string _folder;
        private readonly string _sanitizedName;
        private readonly long _expectedSize;
        private FileStream _stream;
        private SHA256 _sha;
        private byte[] _hashScratch = new byte[65536];
        private long _written;
        private bool _completed;
        private bool _aborted;
        private bool _disposed;

        public string PartPath { get; private set; }
        public string FinalPath { get; private set; }

        public FileReceiver(string receiveFolder, string remoteName, long expectedSize)
        {
            if (string.IsNullOrEmpty(receiveFolder)) throw new ArgumentNullException(nameof(receiveFolder));
            if (expectedSize < 0) throw new ArgumentOutOfRangeException(nameof(expectedSize));

            Directory.CreateDirectory(receiveFolder);
            _folder = receiveFolder;
            _sanitizedName = PathGuard.SanitizeFileName(remoteName);
            _expectedSize = expectedSize;

            PartPath = PathGuard.GetUniquePath(_folder, _sanitizedName + ".part");
            _stream = new FileStream(
                PartPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                65536,
                FileOptions.SequentialScan);
            _sha = SHA256.Create();
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileReceiver));
            if (_completed || _aborted) throw new InvalidOperationException("The receiver is already closed.");
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (_written + count > _expectedSize)
            {
                Abort();
                throw new InvalidDataException("The sender wrote more bytes than declared.");
            }

            if (count == 0) return;

            _stream.Write(buffer, offset, count);
            if (_hashScratch.Length < count) _hashScratch = new byte[count];
            _sha.TransformBlock(buffer, offset, count, _hashScratch, 0);
            _written += count;
        }

        public void Complete(string expectedSha256Hex)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileReceiver));
            if (_completed) throw new InvalidOperationException("The receiver is already complete.");
            if (_aborted) throw new InvalidOperationException("The receiver was aborted.");

            try
            {
                if (_written != _expectedSize)
                    throw new InvalidDataException("The received size did not match the declared size.");

                _sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var actual = Hex(_sha.Hash);
                if (!string.Equals(actual, expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The received file did not match its checksum.");

                _stream.Flush(true);
                _stream.Dispose();
                _stream = null;

                var finalPath = PathGuard.GetUniquePath(_folder, _sanitizedName);
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        File.Move(PartPath, finalPath);
                        FinalPath = finalPath;
                        _completed = true;
                        _sha.Dispose();
                        _sha = null;
                        return;
                    }
                    catch (IOException)
                    {
                        finalPath = PathGuard.GetUniquePath(_folder, _sanitizedName, attempt + 1);
                    }
                }

                throw new IOException("Could not move the received file to its final name.");
            }
            catch
            {
                Abort();
                throw;
            }
        }

        public void Abort()
        {
            if (_completed || _aborted) return;
            _aborted = true;

            try { _stream?.Dispose(); } catch { }
            _stream = null;
            try { _sha?.Dispose(); } catch { }
            _sha = null;

            try
            {
                if (!string.IsNullOrEmpty(PartPath) && File.Exists(PartPath)) File.Delete(PartPath);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_completed) Abort();
        }

        private static string Hex(byte[] bytes)
        {
            if (bytes == null) return string.Empty;
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }
}
