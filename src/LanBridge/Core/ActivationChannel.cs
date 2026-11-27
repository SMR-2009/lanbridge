using System;
using System.Threading;

namespace LanBridge.Core
{
    public sealed class ActivationChannel : IDisposable
    {
        public const string EventName = @"Local\LanBridge.Activate";
        private readonly EventWaitHandle _event;
        private readonly Thread _thread;
        private volatile bool _disposed;

        private ActivationChannel(EventWaitHandle activationEvent, Action onActivate)
        {
            _event = activationEvent;
            _thread = new Thread(() =>
            {
                while (!_disposed)
                {
                    try
                    {
                        if (!_event.WaitOne()) return;
                        if (_disposed) return;
                        onActivate();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch
                    {
                    }
                }
            })
            {
                IsBackground = true,
                Name = "LanBridge activation listener"
            };
            _thread.Start();
        }

        public static ActivationChannel Create(Action onActivate, string eventName = EventName)
        {
            if (onActivate == null) throw new ArgumentNullException(nameof(onActivate));
            if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentNullException(nameof(eventName));

            var activationEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                eventName);
            return new ActivationChannel(activationEvent, onActivate);
        }

        public static bool Signal(string eventName = EventName)
        {
            if (string.IsNullOrWhiteSpace(eventName)) return false;

            try
            {
                using (var activationEvent = EventWaitHandle.OpenExisting(eventName))
                {
                    activationEvent.Set();
                    return true;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _event.Set(); } catch { }
            try { _thread.Join(1000); } catch { }
            _event.Dispose();
        }
    }
}
