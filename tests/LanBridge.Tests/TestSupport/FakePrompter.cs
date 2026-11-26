using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanBridge.Core;

namespace LanBridge.Tests
{
    internal sealed class FakePrompter : ITransferPrompter, IClientPrompter
    {
        private readonly List<TransferOffer> _offers = new List<TransferOffer>();
        private readonly List<PairingCodePrompt> _codePrompts = new List<PairingCodePrompt>();
        private int _closeCount;

        public bool ApprovePairing { get; set; } = true;
        public bool ApproveOffers { get; set; } = true;
        public bool BlockPairingApproval { get; set; }
        public bool BlockPairingCode { get; set; }
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

        public Task<bool> RequestPairingApprovalAsync(PairingRequest request, CancellationToken cancellationToken)
        {
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

        public Task<bool> ApproveOfferAsync(TransferOffer offer, CancellationToken cancellationToken)
        {
            lock (_offers) _offers.Add(offer);
            return Task.FromResult(ApproveOffers);
        }

        public IProgress<TransferProgress> CreateReceiveProgress(TransferOffer offer, CancellationToken cancellationToken)
        {
            return new CollectingProgress();
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
            return new CollectingProgress();
        }
    }
}
