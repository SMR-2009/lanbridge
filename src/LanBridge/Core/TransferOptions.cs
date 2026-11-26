using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LanBridge.Core
{
    public sealed class TransferOptions
    {
        public TimeSpan PairApprovalTimeout { get; set; } = TimeSpan.FromSeconds(120);
        public TimeSpan PairCodeTimeout { get; set; } = TimeSpan.FromSeconds(120);
        public TimeSpan ControlTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan FileIdleTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan OfferTimeout { get; set; } = TimeSpan.FromSeconds(120);
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }

    internal static class TimeoutIo
    {
        public static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            cancellationToken.ThrowIfCancellationRequested();

            var delay = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed != task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("The operation timed out.");
            }
            return await task.ConfigureAwait(false);
        }

        public static async Task WithTimeout(Task task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            cancellationToken.ThrowIfCancellationRequested();

            var delay = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed != task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("The operation timed out.");
            }
            await task.ConfigureAwait(false);
        }

        public static Task<Frame> ReadFrameAsync(Stream stream, TimeSpan timeout, CancellationToken cancellationToken)
        {
            return WithTimeout(FrameCodec.ReadAsync(stream, cancellationToken), timeout, cancellationToken);
        }

        public static Task WriteFrameAsync(
            Stream stream,
            FrameType type,
            byte[] payload,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return WithTimeout(FrameCodec.WriteAsync(stream, type, payload, cancellationToken), timeout, cancellationToken);
        }
    }
}
