using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace LanBridge.Core
{
    public sealed class PeerEndpoint
    {
        public IPAddress Address { get; }
        public int Port { get; }
        public string Fingerprint { get; }
        public string DeviceId { get; }
        public string Name { get; }

        public PeerEndpoint(IPAddress address, int port, string fingerprint, string deviceId, string name)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            Port = port;
            Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
            DeviceId = deviceId ?? string.Empty;
            Name = Sanitizer.SanitizeDeviceName(name);
        }
    }

    public sealed class PairingRequest
    {
        public PeerEndpoint Peer { get; }

        public PairingRequest(PeerEndpoint peer)
        {
            Peer = peer ?? throw new ArgumentNullException(nameof(peer));
        }
    }

    public sealed class PairingCodeDisplay
    {
        public PeerEndpoint Peer { get; }
        public string Code { get; }
        public DateTimeOffset ExpiresAtUtc { get; }

        public PairingCodeDisplay(PeerEndpoint peer, string code, TimeSpan expiresIn)
        {
            Peer = peer ?? throw new ArgumentNullException(nameof(peer));
            Code = code ?? throw new ArgumentNullException(nameof(code));
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(expiresIn);
        }
    }

    public sealed class PairingCodePrompt
    {
        public PeerEndpoint Peer { get; }
        public int Attempt { get; }
        public bool PreviousFailed { get; }

        public PairingCodePrompt(PeerEndpoint peer, int attempt, bool previousFailed)
        {
            Peer = peer ?? throw new ArgumentNullException(nameof(peer));
            Attempt = attempt;
            PreviousFailed = previousFailed;
        }
    }

    public sealed class TransferFile
    {
        public string Name { get; }
        public long Size { get; }
        public string LocalPath { get; }

        public TransferFile(string name, long size, string localPath = null)
        {
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            Name = name ?? string.Empty;
            Size = size;
            LocalPath = localPath;
        }
    }

    public sealed class TransferOffer
    {
        public PeerEndpoint Peer { get; }
        public string TransferId { get; }
        public IReadOnlyList<TransferFile> Files { get; }
        public long TotalSize => Files.Sum(file => file.Size);

        public TransferOffer(PeerEndpoint peer, string transferId, IEnumerable<TransferFile> files)
        {
            Peer = peer ?? throw new ArgumentNullException(nameof(peer));
            TransferId = transferId ?? Guid.NewGuid().ToString("N");
            Files = (files ?? Enumerable.Empty<TransferFile>()).ToList().AsReadOnly();
        }
    }

    public sealed class TransferProgress
    {
        public string FileName { get; set; }
        public int FileIndex { get; set; }
        public int FileCount { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public bool IsComplete { get; set; }
        public bool Failed { get; set; }
        public string ErrorCode { get; set; }
    }

    public sealed class TransferResult
    {
        public bool Success { get; private set; }
        public int FilesCompleted { get; private set; }
        public int FilesTotal { get; private set; }
        public string ErrorCode { get; private set; }

        public static TransferResult Ok(int completed, int total)
        {
            return new TransferResult { Success = true, FilesCompleted = completed, FilesTotal = total };
        }

        public static TransferResult Fail(string errorCode, int completed = 0, int total = 0)
        {
            return new TransferResult
            {
                Success = false,
                ErrorCode = errorCode ?? "transfer_failed",
                FilesCompleted = completed,
                FilesTotal = total
            };
        }
    }

    public sealed class PairingAttemptResult
    {
        public bool Success { get; private set; }
        public string ErrorCode { get; private set; }
        public PeerRecord Peer { get; private set; }

        public static PairingAttemptResult Ok(PeerRecord peer)
        {
            return new PairingAttemptResult { Success = true, Peer = peer };
        }

        public static PairingAttemptResult Fail(string errorCode)
        {
            return new PairingAttemptResult { Success = false, ErrorCode = errorCode ?? "pairing_failed" };
        }
    }
}
