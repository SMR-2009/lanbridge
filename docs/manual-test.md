# Manual two-laptop checklist

This is the release gate. Run it once with the build you intend to publish. Both laptops should use the same `LanBridge.exe`.

## Prepare

- [ ] Copy `artifacts\lanbridge-v0.1.9-win.zip` to laptop B, extract it, and run `LanBridge.exe`.
- [ ] Run `artifacts\LanBridge.exe` on laptop A.
- [ ] Complete first-run on both: device name, receive folder, and startup choice.
- [ ] On first install only, approve the UAC prompt for the firewall rule. There should be no UAC prompt on later launches.
- [ ] Confirm both tray icons exist and `%APPDATA%\LanBridge\lanbridge.log` has a listening line.

## Pair

- [ ] On A: tray → **Send files…**, pick a small text file, select B.
- [ ] On B: the pairing dialog names A, then shows **Approve** and **Deny**. Approve it.
- [ ] On B: a six-digit code appears and counts down from 2:00.
- [ ] On A: enter the six digits. Both devices show as **Paired**.
- [ ] Send the small file and open it from B's `Downloads\LanBridge`.

## Transfers

- [ ] Send a 100 MB or larger file. Progress advances on both laptops.
- [ ] Start a second send while the first is running. The second is refused as busy, not silently dropped.
- [ ] While receiving, click **Cancel** on B. Both progress dialogs close; no `.part` file remains in B's private `parts` folder.
- [ ] Send the same file name twice. The second copy becomes `name (1).ext`; the first is never overwritten.
- [ ] Restart both apps. Send again without pairing; it goes straight through.
- [ ] Turn **Visible on this network** off on B, then send from A to the paired B. It should still work via B's last endpoint.
- [ ] Optional: reinstall B with a fresh identity. A should show **Identity changed** and refuse to auto-trust it.
- [ ] Add a device by IP from A, approve the code on B, and pair successfully without discovery.
- [ ] Start a 100 MB+ transfer, disable Wi-Fi on B for a few seconds, re-enable it, then send the same file again. It should resume from the partial file and finish with matching SHA-256.

## Failure checks

- [ ] Stop LanBridge on B and send from A. A reports the peer unreachable/offline without hanging.
- [ ] On B, decline an incoming offer. A reports that the offer was declined; B writes no file.
- [ ] Try a filename with `..\`, a slash, or `CON` from a test harness or a renamed file. B saves only a safe leaf name.

## Record the result

Reply with:

```text
Version:
Windows build A/B:
Pairing: passed/failed
Small transfer: passed/failed
Large transfer: passed/failed
Cancel: passed/failed
Restart/send: passed/failed
Notes:
```

If something fails, include the timestamp and the relevant `%APPDATA%\LanBridge\lanbridge.log` lines from both laptops. Remove device names or IPs if you prefer.
