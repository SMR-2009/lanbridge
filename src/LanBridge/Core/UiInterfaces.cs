using System;
using System.Threading;
using System.Threading.Tasks;

namespace LanBridge.Core
{
    public interface ITransferPrompter
    {
        Task<bool> RequestPairingApprovalAsync(PairingRequest request, CancellationToken cancellationToken);
        void ShowPairingCode(PairingCodeDisplay display);
        void ClosePairingCode();
        Task<bool> ApproveOfferAsync(TransferOffer offer, CancellationToken cancellationToken);
        IProgress<TransferProgress> CreateReceiveProgress(TransferOffer offer, CancellationToken cancellationToken);
    }

    public interface IClientPrompter
    {
        Task<string> RequestPairingCodeAsync(PairingCodePrompt prompt, CancellationToken cancellationToken);
        IProgress<TransferProgress> CreateSendProgress(TransferOffer offer, CancellationToken cancellationToken);
    }
}
