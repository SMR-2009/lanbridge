# Security model

LanBridge is a two-laptop file transfer tool for one home network. It does not try to replace a hardened enterprise file transfer system. This document states what it protects, what it does not, and where the boundaries are. It describes the 0.1.9 release line.

## Assets

- Files the user chooses to send.
- Files received into the receive folder.
- The LanBridge identity key and certificate.
- The peer store: which laptops are paired and their pinned fingerprints.
- Pairing codes, which exist only on screen and in memory for up to 120 seconds.

## Adversaries considered

- Someone else on the same Wi-Fi or Ethernet network who can send broadcasts and open TCP connections.
- A spoofed discovery announcement that advertises a device that does not exist, or impersonates a name.
- A man-in-the-middle trying to relay a pairing or a transfer.
- A paired device that later changes its certificate (for example after a reinstall or a copied profile).
- Accidental misconfiguration: a receive folder on the network, a hidden device, a Public network profile.

An attacker with administrative access to either laptop, malware running as the user, or physical access to an unlocked machine is outside this model. A paired device is trusted to send files; LanBridge does not inspect or sandbox file contents.

## What protects what

### Discovery is a hint, not trust

A UDP announcement is unauthenticated. Anyone on the LAN can send one claiming any name, address, or fingerprint. LanBridge therefore never adds a device to the list from an announcement alone. It opens a TCP/TLS connection to the advertised endpoint and sends `PING`; only a peer that proves possession of the private key for the advertised certificate is shown. That prevents a drive-by broadcast from injecting a fake row, but it cannot tell you whether the owner of that key is a laptop you recognize. The pairing prompt is where a human decides.

### Pairing code derived from the encrypted channel

The six-digit code is derived from both certificate fingerprints and both fresh nonces. It is never sent in a frame. The responder displays it; the initiator types it. A man-in-the-middle terminates two different TLS sessions and sees two different certificate/nonce pairs, so the two screens show different codes and the initiator's proof fails on the responder. The code has roughly 20 bits of entropy, but it is bound to an approved, short-lived session and is not reusable across sessions. Attempts are limited to three local entries in a session; the responder's approval prompt and the code both expire after two minutes.

The code is a confirmation step, not a password. The long-term identity is the full SHA-256 certificate fingerprint.

### Certificate pinning

Pairing stores the peer's full fingerprint. Every later connection verifies the server certificate against that fingerprint, and the server verifies the client's certificate fingerprint against its own peer store. There is no trust-on-first-use after pairing. If a paired device presents a new certificate, the device list marks it **Identity changed**; LanBridge does not replace the pin automatically. The user must unpair the old entry and pair again.

### Private key storage

The identity is a self-signed RSA-2048 certificate created on first launch. The canonical copy is a PFX encrypted with Windows DPAPI (`CurrentUser`) in `%APPDATA%\LanBridge\identity\identity.pfx.dpapi`. Only the same Windows user on the same machine can decrypt it.

At runtime, Schannel on .NET Framework 4.8 cannot authenticate with an ephemeral key container (`EphemeralKeySet` produces "No credentials are available in the security package"). LanBridge therefore imports the certificate with `UserKeySet`, which uses the current user's key container. The DPAPI blob remains the only persisted copy that LanBridge writes; the imported runtime key is disposed with the process's certificate object. The practical consequence is that the private key material is protected by the user profile's file permissions during the process lifetime, not only by DPAPI. See the source comment in `DeviceIdentity.LoadCertificate` and the changelog.

### Local network only

The TCP listener accepts connections only from the local subnets of physical Wi-Fi/Ethernet interfaces. VPN, Hyper-V, Bluetooth, loopback, and other virtual adapters are excluded by default. Loopback is enabled only in tests. A peer outside those subnets is rejected with `not_lan` before an offer is read.

Inbound TCP clients and UDP discovery packets run behind bounded concurrency limits. A busy or hostile LAN can still consume CPU and bandwidth, but it cannot create an unbounded number of tasks.

This is a guard against accidental exposure, not a firewall against a hostile local network.

### Firewall

The installer adds a Windows Defender Firewall rule scoped to the installed executable, applies to Private and Public profiles, limits remote addresses to `LocalSubnet`, and verifies the enabled, inbound, allow, and executable fields before treating the rule as healthy. This is the one expected UAC prompt; if the rule already exists, the installer skips it. First-run setup checks for the rule and does not ask again. Declining UAC leaves the app working for outbound sends but blocks inbound connections until the rule is repaired from Settings. The rule is tied to the executable path, so running a different copy of `LanBridge.exe` is a different program to Windows Firewall.

Windows often classifies a home Wi-Fi network as Public. The rule deliberately works on Public as well, because otherwise inbound transfers would fail on a correctly configured home network. If you do not trust the network, turn on **Visible on this network** off and disconnect.

### Received file safety

Remote names are treated as hostile:

- Only the leaf name is kept; directories and path separators are stripped.
- Control characters and invalid filename characters are removed.
- Windows reserved names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`) are prefixed with `_`.
- Trailing dots and spaces are removed.
- Empty or overlong names fall back to `file` and are length-limited.
- Files are written to a `.part` file inside a private `parts` subfolder, hashed, verified against `FILE_END`, then moved into place. Existing files are never overwritten; a numeric suffix is used.
- A user cancel deletes the partial file. A network interruption keeps it with a `.part.resume` sidecar; a retry is accepted only when the peer's declared name, size, offset, and prefix SHA-256 still match.
- The receive folder is checked for free space before the offer is accepted, with overflow-safe arithmetic.

### Logging and UI

Remote device names, error strings, and log values are sanitized before display and before writing. Control characters and newlines are removed, and values are length-limited. This prevents a remote device from injecting fake log lines or breaking the dialog layout. Logs contain device names, IP addresses, error codes, and timings; they do not contain file contents, pairing codes, private keys, or passwords.

### No internet

LanBridge does not run a background update check, contact a telemetry server, or send usage data. The only automatic traffic is UDP discovery on the local subnets and TCP/TLS to a discovered or paired endpoint. The manual **Check for updates** action contacts GitHub only when the user asks for it, and it never downloads or runs anything. The installer is the other internet-facing part of the project.

## Known limitations

- Windows 11 only in 0.1.9. Windows 10 reached Microsoft end of support on 2025-10-14 and is not a supported target.
- IPv4 only.
- Receive offers are capped by the configured maximum receive size. The free-space check reserves at least 1 GB and fails closed if the disk cannot be inspected.
- One inbound and one outbound transfer at a time. Interrupted files can resume, but there is no delta sync, queue, compression, or folder-as-a-unit transfer. A single inbound offer is capped by the configured maximum receive size.
- The executable is not code-signed. SmartScreen may warn on first launch. Check the SHA-256 against the release checksums if you downloaded it from somewhere other than the official release.
- A paired device can send you files without approval if **Automatically accept files from paired devices** is enabled. It is off by default.
- Manual endpoint entry (IPv4 plus port) is intended for networks where discovery is blocked. It still requires the six-digit pairing code; the address alone is not trusted.
- The SAS has about 20 bits of entropy. It is safe only as a confirmation bound to an approved session, not as a standalone password.
- The project does not defend against a compromised Windows account, malware, or a malicious paired device.
- Discovery is IPv4 broadcast; some networks (guest Wi-Fi, client isolation, VLANs) block it. Use the manual endpoint path if discovery is unavailable in a future release; 0.1.0 has no manual endpoint entry.

## Reporting

Please use GitHub's private vulnerability reporting on the repository's Security tab. Do not include pairing codes, private keys, or personal files. See `SECURITY.md` for the response process.
