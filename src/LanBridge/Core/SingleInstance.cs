using System;
using System.Threading;

namespace LanBridge.Core
{
    public static class SingleInstance
    {
        public const string DefaultName = "Local\\LanBridge.SingleInstance";

        public static bool TryAcquire(out Mutex mutex, string name = DefaultName)
        {
            mutex = null;

            try
            {
                var createdNew = false;
                var candidate = new Mutex(true, name, out createdNew);
                if (!createdNew)
                {
                    candidate.Dispose();
                    return false;
                }

                mutex = candidate;
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
        }
    }
}
