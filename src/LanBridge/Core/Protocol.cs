using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace LanBridge.Core
{
    public enum FrameType : byte
    {
        Hello = 1,
        HelloAck = 2,
        Ping = 3,
        Pong = 4,
        PairPrompt = 5,
        PairProof = 6,
        PairAbort = 7,
        PairOk = 8,
        PairDeny = 9,
        Offer = 10,
        Accept = 11,
        Reject = 12,
        FileBegin = 13,
        FileData = 14,
        FileEnd = 15,
        FileOk = 16,
        FileError = 17,
        Cancel = 18,
        Done = 19,
        DoneAck = 20,
        Error = 21
    }

    public sealed class Frame
    {
        public FrameType Type { get; }
        public byte[] Payload { get; }

        public Frame(FrameType type, byte[] payload)
        {
            Type = type;
            Payload = payload ?? Array.Empty<byte>();
        }
    }

    public sealed class FileDataFrame
    {
        public FrameType Type { get; }
        public int Index { get; }
        public byte[] DataBuffer { get; }
        public int DataOffset { get; }
        public int DataLength { get; }
        public byte[] Payload { get; }

        public FileDataFrame(
            FrameType type,
            int index,
            byte[] dataBuffer,
            int dataOffset,
            int dataLength,
            byte[] payload)
        {
            Type = type;
            Index = index;
            DataBuffer = dataBuffer;
            DataOffset = dataOffset;
            DataLength = dataLength;
            Payload = payload;
        }
    }

    public sealed class ProtocolException : Exception
    {
        public string Code { get; }

        public ProtocolException(string code, string message) : base(message)
        {
            Code = code;
        }

        public ProtocolException(string code, string message, Exception inner) : base(message, inner)
        {
            Code = code;
        }
    }

    [DataContract]
    public sealed class HelloDto
    {
        [DataMember(Name = "v")] public int V { get; set; }
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "fp")] public string Fp { get; set; }
        [DataMember(Name = "nonce")] public string Nonce { get; set; }
        [DataMember(Name = "intent")] public string Intent { get; set; }
        [DataMember(Name = "paired")] public bool Paired { get; set; }
        [DataMember(Name = "port")] public int Port { get; set; }
    }

    [DataContract]
    public sealed class HelloAckDto
    {
        [DataMember(Name = "v")] public int V { get; set; }
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "fp")] public string Fp { get; set; }
        [DataMember(Name = "nonce")] public string Nonce { get; set; }
        [DataMember(Name = "paired")] public bool Paired { get; set; }
        [DataMember(Name = "busy")] public bool Busy { get; set; }
        [DataMember(Name = "port")] public int Port { get; set; }
    }

    [DataContract]
    public sealed class PairPromptDto
    {
        [DataMember(Name = "expires")] public int ExpiresSeconds { get; set; }
    }

    [DataContract]
    public sealed class PairProofDto
    {
        [DataMember(Name = "proof")] public string Proof { get; set; }
    }

    [DataContract]
    public sealed class PairDenyDto
    {
        [DataMember(Name = "reason")] public string Reason { get; set; }
    }

    [DataContract]
    public sealed class OfferDto
    {
        [DataMember(Name = "transferId")] public string TransferId { get; set; }
        [DataMember(Name = "files")] public List<OfferFileDto> Files { get; set; }
        [DataMember(Name = "totalSize")] public long TotalSize { get; set; }
    }

    [DataContract]
    public sealed class OfferFileDto
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
    }

    [DataContract]
    public sealed class AcceptDto
    {
        [DataMember(Name = "transferId")] public string TransferId { get; set; }
        [DataMember(Name = "resume")] public List<ResumeFileDto> Resume { get; set; }
    }

    [DataContract]
    public sealed class ResumeFileDto
    {
        [DataMember(Name = "index")] public int Index { get; set; }
        [DataMember(Name = "offset")] public long Offset { get; set; }
        [DataMember(Name = "prefixSha256")] public string PrefixSha256 { get; set; }
    }

    [DataContract]
    public sealed class RejectDto
    {
        [DataMember(Name = "reason")] public string Reason { get; set; }
    }

    [DataContract]
    public sealed class FileBeginDto
    {
        [DataMember(Name = "index")] public int Index { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
        [DataMember(Name = "resumeOffset")] public long ResumeOffset { get; set; }
    }

    [DataContract]
    public sealed class FileEndDto
    {
        [DataMember(Name = "index")] public int Index { get; set; }
        [DataMember(Name = "sha256")] public string Sha256 { get; set; }
    }

    [DataContract]
    public sealed class FileOkDto
    {
        [DataMember(Name = "index")] public int Index { get; set; }
    }

    [DataContract]
    public sealed class FileErrorDto
    {
        [DataMember(Name = "index")] public int Index { get; set; }
        [DataMember(Name = "code")] public string Code { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; }
    }

    [DataContract]
    public sealed class ErrorDto
    {
        [DataMember(Name = "code")] public string Code { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; }
        [DataMember(Name = "fileIndex")] public int FileIndex { get; set; }
        [DataMember(Name = "completed")] public int Completed { get; set; }
    }

    [DataContract]
    public sealed class CancelDto
    {
        [DataMember(Name = "reason")] public string Reason { get; set; }
    }

    [DataContract]
    public sealed class DoneDto
    {
        [DataMember(Name = "transferId")] public string TransferId { get; set; }
    }

    [DataContract]
    public sealed class DoneAckDto
    {
        [DataMember(Name = "transferId")] public string TransferId { get; set; }
    }
}
