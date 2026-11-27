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
                        while (!Application.OpenForms.Cast<Form>().ToList().Any(
                                   form => form.Text.StartsWith("Pair with", StringComparison.Ordinal)) &&
                               DateTime.UtcNow < deadline)
                        {
                            Application.DoEvents();
                            Thread.Sleep(10);
                        }

                        Assert.Contains(
                            Application.OpenForms.Cast<Form>().ToList(),
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


namespace LanBridge.Tests
{
    public sealed class PairingCodeUiTests
    {
        [Fact]
        public void PairingCodeEntry_ShowsAnInputBoxAndReturnsSixDigits()
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
                        var prompt = new PairingCodePrompt(new PeerEndpoint(
                            IPAddress.Loopback,
                            45873,
                            new string('a', 64),
                            "test-device",
                            "Test device"), 1, false);

                        Task<string> code = null;
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            code = ui.RequestPairingCodeAsync(prompt, CancellationToken.None);
                        });

                        var deadline = DateTime.UtcNow.AddSeconds(3);
                        while (code == null && DateTime.UtcNow < deadline)
                        {
                            Application.DoEvents();
                            Thread.Sleep(10);
                        }

                        Assert.NotNull(code);
                        deadline = DateTime.UtcNow.AddSeconds(3);
                        while (!Application.OpenForms.Cast<Form>().ToList().Any(
                                   form => form.Text == "Enter pairing code") &&
                               DateTime.UtcNow < deadline)
                        {
                            Application.DoEvents();
                            Thread.Sleep(10);
                        }

                        var form = Application.OpenForms.Cast<Form>().ToList()
                            .First(form => form.Text == "Enter pairing code");
                        var box = form.Controls.Cast<Control>().OfType<TextBox>().Single();
                        Assert.Equal(7, box.MaxLength);
                        box.Text = "482913";
                        var pair = form.Controls.Cast<Control>().OfType<Button>()
                            .First(button => button.Text == "Pair");
                        pair.PerformClick();

                        deadline = DateTime.UtcNow.AddSeconds(3);
                        while (!code.IsCompleted && DateTime.UtcNow < deadline)
                        {
                            Application.DoEvents();
                            Thread.Sleep(10);
                        }

                        Assert.True(code.IsCompleted);
                        Assert.Equal("482913", code.Result);
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

            Assert.True(finished, "The pairing code UI test did not finish.");
            Assert.Null(failure);
        }
    }
}
