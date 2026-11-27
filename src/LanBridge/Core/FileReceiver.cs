using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace LanBridge.Core
{
    [DataContract]
    public sealed class ResumeMetadata
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
        [DataMember(Name = "part")] public string Part { get; set; }
        [DataMember(Name = "bytes")] public long Bytes { get; set; }
        [DataMember(Name = "prefixSha256")] public string PrefixSha256 { get; set; }
    }

    public sealed class ResumePoint
    {
        public string PartPath { get; }
        public long Offset { get; }
        public string PrefixSha256 { get; }

        public ResumePoint(string partPath, long offset, string prefixSha256)
        {
            PartPath = partPath;
            Offset = offset;
            PrefixSha256 = prefixSha256;
        }
    }

    public sealed class FileReceiver : IDisposable
    {
        private const string PartExtension = ".part";
        private const string ResumeExtension = ".resume";
        public const string PartFolderName = ".lanbridge-parts";

        private readonly string _folder;
        private readonly string _partFolder;
        private readonly string _sanitizedName;
        private readonly long _expectedSize;
        private FileStream _stream;
        private SHA256 _sha;
        private byte[] _hashScratch = new byte[65536];
        private long _written;
        private bool _completed;
        private bool _aborted;
        private bool _preserved;
        private bool _disposed;
        private bool _hashFinalized;

        public string PartPath { get; }
        public string FinalPath { get; private set; }
        public bool IsPreserved => _preserved;

        public FileReceiver(string receiveFolder, string remoteName, long expectedSize)
        {
            if (string.IsNullOrEmpty(receiveFolder)) throw new ArgumentNullException(nameof(receiveFolder));
            if (expectedSize < 0) throw new ArgumentOutOfRangeException(nameof(expectedSize));

            Directory.CreateDirectory(receiveFolder);
            _folder = Path.GetFullPath(receiveFolder);
            _partFolder = Path.Combine(_folder, PartFolderName);
            Directory.CreateDirectory(_partFolder);
            _sanitizedName = PathGuard.SanitizeFileName(remoteName);
            _expectedSize = expectedSize;
            PartPath = PathGuard.GetUniquePath(_partFolder, _sanitizedName + PartExtension);

            _stream = new FileStream(
                PartPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.SequentialScan);
            _sha = new SHA256Cng();
            WriteMetadata(0, null);
        }

        private FileReceiver(
            string receiveFolder,
            string remoteName,
            long expectedSize,
            string partPath,
            long offset)
        {
            Directory.CreateDirectory(receiveFolder);
            _folder = Path.GetFullPath(receiveFolder);
            _partFolder = Path.Combine(_folder, PartFolderName);
            Directory.CreateDirectory(_partFolder);
            _sanitizedName = PathGuard.SanitizeFileName(remoteName);
            _expectedSize = expectedSize;

            if (offset <= 0 || offset > expectedSize)
                throw new InvalidDataException("The resume offset is invalid.");
            if (string.IsNullOrEmpty(partPath) ||
                !string.Equals(Path.GetDirectoryName(Path.GetFullPath(partPath)), _partFolder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The resume file is outside the private parts folder.");

            PartPath = Path.GetFullPath(partPath);
            if (!File.Exists(PartPath)) throw new FileNotFoundException("The partial file is missing.", PartPath);
            if (new FileInfo(PartPath).Length != offset)
                throw new InvalidDataException("The partial file length changed.");

            _stream = new FileStream(
                PartPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.SequentialScan);
            _stream.Seek(0, SeekOrigin.Begin);
            _sha = new SHA256Cng();

            var buffer = new byte[65536];
            var remaining = offset;
            while (remaining > 0)
            {
                var read = _stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read <= 0) throw new InvalidDataException("The partial file ended early.");
                _sha.TransformBlock(buffer, 0, read, _hashScratch, 0);
                remaining -= read;
            }

            _stream.Seek(offset, SeekOrigin.Begin);
            _written = offset;
            WriteMetadata(offset, null);
        }

        public static FileReceiver OpenExisting(
            string receiveFolder,
            string remoteName,
            long expectedSize,
            string partPath,
            long offset)
        {
            return new FileReceiver(receiveFolder, remoteName, expectedSize, partPath, offset);
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileReceiver));
            if (_completed || _aborted || _preserved) throw new InvalidOperationException("The receiver is already closed.");
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
            if (_aborted || _preserved) throw new InvalidOperationException("The receiver is already closed.");

            var verified = false;
            try
            {
                if (_written != _expectedSize)
                    throw new InvalidDataException("The received size did not match the declared size.");

                _sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                _hashFinalized = true;
                var actual = Hex(_sha.Hash);
                if (!string.Equals(actual, expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The received file did not match its checksum.");

                _stream.Flush(true);
                _stream.Dispose();
                _stream = null;
                verified = true;

                var finalPath = PathGuard.GetUniquePath(_folder, _sanitizedName);
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        File.Move(PartPath, finalPath);
                        DeleteMetadata();
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
                if (verified)
                    PreservePartial();
                else
                    Abort();
                throw;
            }
        }

        /// <summary>
        /// Keeps a partial file so the next attempt can resume it. A failed final
        /// rename also preserves the verified bytes instead of deleting them.
        /// A bad checksum or a deliberate cancel still calls Abort and removes it.
        /// </summary>
        public void PreservePartial()
        {
            if (_completed || _aborted || _preserved || _disposed) return;

            try
            {
                if (_sha != null)
                {
                    if (!_hashFinalized)
                    {
                        _sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        _hashFinalized = true;
                    }
                    WriteMetadata(_written, Hex(_sha.Hash));
                }

                _stream?.Flush(true);
                _stream?.Dispose();
                _stream = null;
                _sha?.Dispose();
                _sha = null;
                _preserved = true;
            }
            catch
            {
                Abort();
            }
        }

        public void Abort()
        {
            if (_completed || _aborted) return;
            _aborted = true;
            _preserved = false;

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

            DeleteMetadata();
        }

        public static void DeleteResumePoint(ResumePoint point)
        {
            if (point == null || string.IsNullOrEmpty(point.PartPath)) return;
            try { if (File.Exists(point.PartPath)) File.Delete(point.PartPath); } catch { }
            try
            {
                var metadata = point.PartPath + ResumeExtension;
                if (File.Exists(metadata)) File.Delete(metadata);
            }
            catch
            {
            }
        }

        public static ResumePoint FindResume(string folder, string remoteName, long expectedSize)
        {
            if (string.IsNullOrWhiteSpace(folder) || expectedSize <= 0) return null;

            string fullFolder;
            try
            {
                fullFolder = Path.GetFullPath(folder);
                if (!Directory.Exists(fullFolder)) return null;
            }
            catch
            {
                return null;
            }

            var sanitized = PathGuard.SanitizeFileName(remoteName);
            var partFolder = Path.Combine(fullFolder, PartFolderName);
            if (!Directory.Exists(partFolder)) return null;

            IEnumerable<string> sidecars;
            try
            {
                sidecars = Directory.EnumerateFiles(
                    partFolder,
                    "*" + PartExtension + ResumeExtension,
                    SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return null;
            }

            foreach (var sidecar in sidecars)
            {
                try
                {
                    var sidecarPartName = Path.GetFileName(sidecar);
                    if (string.IsNullOrEmpty(sidecarPartName) ||
                        !sidecarPartName.EndsWith(PartExtension + ResumeExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var expectedPartName = sidecarPartName.Substring(
                        0,
                        sidecarPartName.Length - ResumeExtension.Length);
                    if (!expectedPartName.EndsWith(PartExtension, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var metadata = ProtocolJson.Deserialize<ResumeMetadata>(File.ReadAllBytes(sidecar));
                    if (metadata == null ||
                        metadata.Size != expectedSize ||
                        !string.Equals(metadata.Name, sanitized, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(metadata.Part, expectedPartName, StringComparison.OrdinalIgnoreCase) ||
                        metadata.Bytes <= 0)
                    {
                        continue;
                    }

                    var partPath = Path.Combine(partFolder, expectedPartName);
                    if (!File.Exists(partPath)) continue;

                    var offset = new FileInfo(partPath).Length;
                    if (offset != metadata.Bytes || offset > expectedSize) continue;

                    var hash = string.IsNullOrEmpty(metadata.PrefixSha256)
                        ? HashFile(partPath)
                        : metadata.PrefixSha256;
                    return new ResumePoint(partPath, offset, hash);
                }
                catch
                {
                }
            }

            return null;
        }

        private void WriteMetadata(long bytes, string prefixSha256)
        {
            try
            {
                var metadata = new ResumeMetadata
                {
                    Name = _sanitizedName,
                    Size = _expectedSize,
                    Part = Path.GetFileName(PartPath),
                    Bytes = bytes,
                    PrefixSha256 = prefixSha256 ?? string.Empty
                };
                File.WriteAllBytes(
                    PartPath + ResumeExtension,
                    ProtocolJson.Serialize(metadata));
            }
            catch
            {
            }
        }

        private void DeleteMetadata()
        {
            try
            {
                if (!string.IsNullOrEmpty(PartPath) && File.Exists(PartPath + ResumeExtension))
                    File.Delete(PartPath + ResumeExtension);
            }
            catch
            {
            }
        }

        private static string HashFile(string path)
        {
            using (var sha = new SHA256Cng())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var buffer = new byte[65536];
                var output = new byte[buffer.Length];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha.TransformBlock(buffer, 0, read, output, 0);
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return Hex(sha.Hash);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_completed && !_preserved) Abort();
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
