using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class PairingUiTests
    {
        [Fact]
        public void PairingApproval_IsCreatedAndClosedOnTheUiThread()
        {
            Exception failure = null;
            var finished = false;

            var thread = new Thread(() =>
            {
                try
                {
                    using (var host = new Control())
                    {
                        host.CreateControl();
                        var log = new CollectingLogger();
                        using var ui = new WinFormsUi(log, () => { }, () => { }, host);
                        var request = new PairingRequest(new PeerEndpoint(
                            IPAddress.Loopback,
                            45873,
                            new string('a', 64),
                            "test-device",
                            "Test device"));

                        Task<bool> approval = null;
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            approval = ui.RequestPairingApprovalAsync(request, CancellationToken.None);
                        });

                        var deadline = DateTime.UtcNow.AddSeconds(3);
                        while (approval == null && DateTime.UtcNow < deadline)
                        {
                            Application.DoEvents();
                            Thread.Sleep(10);
                        }

                        Assert.NotNull(approval);
                        deadline = DateTime.UtcNow.AddSeconds(3);
                        while (!Application.OpenForms.Cast<Form>().Any(
                                   form => form.Text.StartsWith("Pair with", StringComparison.Ordinal)) &&
                               DateTime.UtcNow < deadline)
                        {
                            Application.DoEvents();
                            Thread.Sleep(10);
                        }

                        Assert.Contains(
                            Application.OpenForms.Cast<Form>(),
                            form => form.Text.StartsWith("Pair with", StringComparison.Ordinal));
                        Assert.False(approval.IsCompleted);

                        ui.CloseAll();
                        deadline = DateTime.UtcNow.AddSeconds(3);
                        while (!approval.IsCompleted && DateTime.UtcNow < deadline)
                        {
                            Application.DoEvents();
                            Thread.Sleep(10);
                        }

                        Assert.True(approval.IsCompleted);
                        Assert.False(approval.Result);
                    }

                    finished = true;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    finished = true;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(8000);

            Assert.True(finished, "The pairing UI test did not finish.");
            Assert.Null(failure);
        }
    }
}
