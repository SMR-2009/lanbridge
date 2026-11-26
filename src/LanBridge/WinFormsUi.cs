using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LanBridge.Core;

namespace LanBridge
{
    public sealed class WinFormsUi : ITransferPrompter, IClientPrompter
    {
        private readonly SynchronizationContext _context;
        private readonly ILogger _log;
        private readonly Action _cancelReceive;
        private readonly Action _cancelSend;
        private PairingForm _pairingForm;
        private PairingCodeEntryForm _codeForm;
        private ReceivePromptForm _offerForm;
        private ProgressForm _receiveProgress;
        private ProgressForm _sendProgress;

        public WinFormsUi(ILogger log, Action cancelReceive, Action cancelSend)
        {
            _log = log ?? NullLogger.Instance;
            _context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _cancelReceive = cancelReceive;
            _cancelSend = cancelSend;
        }

        public void RunOnUi(Action action)
        {
            if (action == null) return;

            if (SynchronizationContext.Current == _context)
            {
                InvokeSafely(action);
                return;
            }

            _context.Post(_ => InvokeSafely(action), null);
        }

        public Task<bool> RequestPairingApprovalAsync(
            PairingRequest request,
            CancellationToken cancellationToken)
        {
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.IsCancellationRequested)
            {
                result.TrySetResult(false);
                return result.Task;
            }

            RunOnUi(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.TrySetResult(false);
                    return;
                }

                if (_pairingForm != null && !_pairingForm.IsDisposed)
                {
                    _pairingForm.Activate();
                    return;
                }

                var form = new PairingForm(request.Peer);
                _pairingForm = form;
                form.Approved += () => result.TrySetResult(true);
                form.Denied += () => result.TrySetResult(false);
                form.FormClosed += (s, e) =>
                {
                    if (_pairingForm == form) _pairingForm = null;
                    result.TrySetResult(false);
                };
                form.Show();
                form.Activate();
            });

            var registration = cancellationToken.Register(() => RunOnUi(() =>
            {
                var form = _pairingForm;
                _pairingForm = null;
                if (form != null && !form.IsDisposed) form.Close();
                result.TrySetResult(false);
            }));
            result.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);

            return result.Task;
        }

        public void ShowPairingCode(PairingCodeDisplay display)
        {
            if (display == null) return;

            RunOnUi(() =>
            {
                if (_pairingForm == null || _pairingForm.IsDisposed)
                {
                    _pairingForm = new PairingForm(display.Peer);
                    _pairingForm.Show();
                }
                _pairingForm.ShowCode(display);
                _pairingForm.Activate();
            });
        }

        public void ClosePairingCode()
        {
            RunOnUi(() =>
            {
                var form = _pairingForm;
                _pairingForm = null;
                if (form != null && !form.IsDisposed) form.Close();
            });
        }

        public Task<string> RequestPairingCodeAsync(
            PairingCodePrompt prompt,
            CancellationToken cancellationToken)
        {
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new int[1];

            if (cancellationToken.IsCancellationRequested)
            {
                result.TrySetResult(null);
                return result.Task;
            }

            RunOnUi(() =>
            {
                if (Volatile.Read(ref cancelled[0]) != 0 || cancellationToken.IsCancellationRequested)
                {
                    result.TrySetResult(null);
                    return;
                }

                if (_codeForm != null && !_codeForm.IsDisposed) _codeForm.Close();

                var form = new PairingCodeEntryForm(prompt);
                _codeForm = form;
                form.Submitted += code =>
                {
                    result.TrySetResult(code);
                    if (!form.IsDisposed) form.Close();
                };
                form.Cancelled += () =>
                {
                    result.TrySetResult(null);
                    if (!form.IsDisposed) form.Close();
                };
                form.FormClosed += (s, e) =>
                {
                    if (_codeForm == form) _codeForm = null;
                    result.TrySetResult(null);
                };
                form.Show();
                form.Activate();
            });

            var registration = cancellationToken.Register(() =>
            {
                Interlocked.Exchange(ref cancelled[0], 1);
                RunOnUi(() =>
                {
                    var form = _codeForm;
                    _codeForm = null;
                    if (form != null && !form.IsDisposed) form.Close();
                    result.TrySetResult(null);
                });
            });
            result.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);

            return result.Task;
        }

        public Task<bool> ApproveOfferAsync(TransferOffer offer, CancellationToken cancellationToken)
        {
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new int[1];

            if (cancellationToken.IsCancellationRequested)
            {
                result.TrySetResult(false);
                return result.Task;
            }

            RunOnUi(() =>
            {
                if (Volatile.Read(ref cancelled[0]) != 0 || cancellationToken.IsCancellationRequested)
                {
                    result.TrySetResult(false);
                    return;
                }

                if (_offerForm != null && !_offerForm.IsDisposed) _offerForm.Close();

                var form = new ReceivePromptForm(offer);
                _offerForm = form;
                form.Accepted += () => result.TrySetResult(true);
                form.Declined += () => result.TrySetResult(false);
                form.FormClosed += (s, e) =>
                {
                    if (_offerForm == form) _offerForm = null;
                    result.TrySetResult(false);
                };
                form.Show();
                form.Activate();
            });

            var registration = cancellationToken.Register(() =>
            {
                Interlocked.Exchange(ref cancelled[0], 1);
                RunOnUi(() =>
                {
                    var form = _offerForm;
                    _offerForm = null;
                    if (form != null && !form.IsDisposed) form.Close();
                    result.TrySetResult(false);
                });
            });
            result.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);

            return result.Task;
        }

        public IProgress<TransferProgress> CreateReceiveProgress(
            TransferOffer offer,
            CancellationToken cancellationToken)
        {
            ProgressForm form = null;
            RunOnUi(() =>
            {
                if (_receiveProgress != null && !_receiveProgress.IsDisposed) _receiveProgress.Close();
                form = new ProgressForm("Receiving files", true, _cancelReceive);
                _receiveProgress = form;
                form.FormClosed += (s, e) =>
                {
                    if (_receiveProgress == form) _receiveProgress = null;
                };
                form.Show();
                form.Activate();
            });

            return new UiProgress(progress => RunOnUi(() =>
            {
                if (form != null && !form.IsDisposed) form.Apply(progress);
            }));
        }

        public IProgress<TransferProgress> CreateSendProgress(
            TransferOffer offer,
            CancellationToken cancellationToken)
        {
            ProgressForm form = null;
            RunOnUi(() =>
            {
                if (_sendProgress != null && !_sendProgress.IsDisposed) _sendProgress.Close();
                form = new ProgressForm("Sending files", false, _cancelSend);
                _sendProgress = form;
                form.FormClosed += (s, e) =>
                {
                    if (_sendProgress == form) _sendProgress = null;
                };
                form.Show();
                form.Activate();
            });

            return new UiProgress(progress => RunOnUi(() =>
            {
                if (form != null && !form.IsDisposed) form.Apply(progress);
            }));
        }

        public void CloseAll()
        {
            RunOnUi(() =>
            {
                if (_pairingForm != null && !_pairingForm.IsDisposed) _pairingForm.Close();
                if (_codeForm != null && !_codeForm.IsDisposed) _codeForm.Close();
                if (_offerForm != null && !_offerForm.IsDisposed) _offerForm.Close();
                if (_receiveProgress != null && !_receiveProgress.IsDisposed) _receiveProgress.Close();
                if (_sendProgress != null && !_sendProgress.IsDisposed) _sendProgress.Close();
            });
        }

        private void InvokeSafely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _log.Error("UI action failed", ex);
            }
        }

        private sealed class UiProgress : IProgress<TransferProgress>
        {
            private readonly Action<TransferProgress> _apply;
            private int _lastReportTick;

            public UiProgress(Action<TransferProgress> apply)
            {
                _apply = apply;
            }

            public void Report(TransferProgress value)
            {
                if (value == null) return;

                if (!value.IsComplete && !value.Failed)
                {
                    var now = Environment.TickCount;
                    if (now - _lastReportTick < 100) return;
                    _lastReportTick = now;
                }

                try { _apply(value); } catch { }
            }
        }
    }
}
