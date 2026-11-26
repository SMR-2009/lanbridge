using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LanBridge.Core;

namespace LanBridge.Tests
{
    internal sealed class FakePrompter : ITransferPrompter, IClientPrompter
    {
        private readonly List<TransferOffer> _offers = new List<TransferOffer>();
        private readonly List<PairingCodePrompt> _codePrompts = new List<PairingCodePrompt>();
        private readonly List<PairingRequest> _approvalRequests = new List<PairingRequest>();
        private readonly List<CollectingProgress> _receiveProgress = new List<CollectingProgress>();
        private readonly List<CollectingProgress> _sendProgress = new List<CollectingProgress>();
        private int _closeCount;

        public bool ApprovePairing { get; set; } = true;
        public bool ApproveOffers { get; set; } = true;
        public bool BlockPairingApproval { get; set; }
        public bool BlockPairingCode { get; set; }
        public TaskCompletionSource<bool> OfferGate { get; set; }
        public string PairingCode { get; set; }
        public string LastCode { get; private set; }
        public int CloseCount => Volatile.Read(ref _closeCount);

        public IReadOnlyList<TransferOffer> Offers
        {
            get { lock (_offers) return _offers.ToArray(); }
        }

        public IReadOnlyList<PairingCodePrompt> CodePrompts
        {
            get { lock (_codePrompts) return _codePrompts.ToArray(); }
        }

        public IReadOnlyList<PairingRequest> ApprovalRequests
        {
            get { lock (_approvalRequests) return _approvalRequests.ToArray(); }
        }

        public IReadOnlyList<TransferProgress> ReceiveProgressEvents
        {
            get
            {
                lock (_receiveProgress)
                {
                    return _receiveProgress.SelectMany(progress => progress.Items).ToArray();
                }
            }
        }

        public IReadOnlyList<TransferProgress> SendProgressEvents
        {
            get
            {
                lock (_sendProgress)
                {
                    return _sendProgress.SelectMany(progress => progress.Items).ToArray();
                }
            }
        }

        public Task<bool> RequestPairingApprovalAsync(PairingRequest request, CancellationToken cancellationToken)
        {
            lock (_approvalRequests) _approvalRequests.Add(request);
            if (BlockPairingApproval)
            {
                return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                }, TaskScheduler.Default);
            }
            return Task.FromResult(ApprovePairing);
        }

        public void ShowPairingCode(PairingCodeDisplay display)
        {
            LastCode = display.Code;
        }

        public void ClosePairingCode()
        {
            Interlocked.Increment(ref _closeCount);
        }

        public async Task<bool> ApproveOfferAsync(TransferOffer offer, CancellationToken cancellationToken)
        {
            lock (_offers) _offers.Add(offer);
            if (OfferGate != null) return await OfferGate.Task.ConfigureAwait(false);
            return ApproveOffers;
        }

        public IProgress<TransferProgress> CreateReceiveProgress(TransferOffer offer, CancellationToken cancellationToken)
        {
            var progress = new CollectingProgress();
            lock (_receiveProgress) _receiveProgress.Add(progress);
            return progress;
        }

        public Task<string> RequestPairingCodeAsync(PairingCodePrompt prompt, CancellationToken cancellationToken)
        {
            lock (_codePrompts) _codePrompts.Add(prompt);
            if (BlockPairingCode)
            {
                return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return PairingCode ?? LastCode ?? "000000";
                }, TaskScheduler.Default);
            }
            return Task.FromResult(PairingCode ?? LastCode ?? "000000");
        }

        public IProgress<TransferProgress> CreateSendProgress(TransferOffer offer, CancellationToken cancellationToken)
        {
            var progress = new CollectingProgress();
            lock (_sendProgress) _sendProgress.Add(progress);
            return progress;
        }
    }
}
