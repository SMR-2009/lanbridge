# LanBridge — Design

- Date: 2026-11-26
- Status: Implemented in v0.1.0
- Owner: R. Murtaza (@SMR-2009)
- Working name: **LanBridge**
- Target repo: `SMR-2009/lanbridge` (public, MIT)
- First release: `v0.1.0`

## 1. Intent

LanBridge is a small Windows tray app for sending files between two Windows PCs on the same home network.

Pair the two laptops once with a six-digit code. After that, right-click the tray icon, choose a laptop, pick files, and send. Everything stays on the LAN. There is no account, cloud service, relay, browser tab, or background internet traffic.

The first release targets two Windows 11 laptops on one home network. The code will be public and should be useful to other Windows 10/11 users with the same need.

## 2. Goals

- Two Windows 11 PCs on the same Wi-Fi or Ethernet network, no internet required.
- Install by copying one `.exe`; no runtime, installer, or admin rights for normal use.
- Tray-first workflow: send files in three clicks after pairing.
- Automatic discovery of LanBridge devices on the local network.
- Pair once per device; remember and pin the peer identity.
- Encrypted transport with mutual authentication.
- LAN-speed transfers with progress and cancel.
- Safe receiving: one receive folder, sanitized names, no overwrites, no path traversal.
- Reproducible local build and GitHub Actions build/test/release.
- Clear security model and documentation suitable for a public GitHub repo.

## 3. Non-goals for v0.1

- macOS, Linux, Android, iOS, or Windows on ARM-specific builds.
- Internet relay, NAT traversal, cloud storage, or offline queuing.
- Folder sync, clipboard sharing, chat, remote control, or screen sharing.
- Anonymous one-off sends; pairing is required.
- Resume after disconnect, parallel transfers, or multi-source transfers.
- Auto-update, code signing, MSI installer, or winget packaging.
- IPv6 discovery. v0.1 is IPv4-only.
- A Windows service. The app runs as the signed-in user.

## 4. Why build this?

LocalSend already does local file transfer well and is the right recommendation for someone who just needs files moved today. LanBridge exists for a narrower experience:

- Tray-first, no main window or browser.
- A persistent paired-device model rather than per-transfer approval.
- A pairing flow designed around one code shown on the receiving laptop.
- A codebase small enough to audit and extend.

If LanBridge does not offer a clearly better experience for this use case, the project should stop and point users at LocalSend.

## 5. Approaches considered

| Approach | Pros | Cons | Decision |
|---|---|---|---|
| Use LocalSend | Already built, cross-platform, HTTPS, PIN receiving, maintained | Does not match the tray/PIN pairing experience; not a GitHub project | Not chosen, but recommended as the immediate fallback |
| Browser app + local Python/Node server | Smallest code, no install on the receiver | Token URL sharing, no tray, browser UX, receiver still needs a browser | Rejected |
| Electron tray app | Cross-platform, modern UI, fast development | 100 MB+ runtime, slower startup, larger attack surface, heavy for a file utility | Rejected |
| Python + pystray + PyInstaller | Python already installed, small-ish exe | Tray/dialog UX is fragile, Python 3.14 packaging risk, onefile startup/AV issues | Rejected |
| C# WinForms on .NET Framework 4.8 | Native tray/menu/dialogs, one small exe, no runtime install, direct access to Schannel/DPAPI, Windows-only is acceptable | WinForms looks dated; Windows-only | **Chosen** |

## 6. Product experience

### 6.1 First run

1. User launches `LanBridge.exe`.
2. Welcome dialog: device name (prefilled with the Windows machine name), receive folder (default `%USERPROFILE%\Downloads\LanBridge`), **Start with Windows** checked, **Allow through Windows Firewall** checked.
3. Clicking Continue creates the device identity and, if checked, requests UAC once to add a firewall rule scoped to `LocalSubnet`.
4. App minimizes to the tray and starts announcing.

If the firewall step is declined, the app still runs but shows a warning that discovery may fail.

### 6.2 Pairing

Either laptop can start pairing.

1. User opens the device list and clicks an unpaired laptop.
2. The other laptop shows: **"Rohan-PC (192.168.10.7) wants to pair"** with Approve and Deny buttons, plus "Only approve if you just clicked Pair on the other laptop." No code is shown yet.
3. The approval prompt waits 120 seconds from the pairing request. If it times out, the responder sends `PAIR_DENY` and both sides close the session.
4. On Approve, it displays a six-digit code, for example **482 913**, and says "Enter this code on Rohan-PC. Expires in 2:00." The 120-second code window starts now.
5. On the initiating laptop, the user types the code. It is checked locally against the pairing channel; the code itself is never sent over the network.
6. If it matches and the other side approved, the initiator sends `PAIR_PROOF`; the responder verifies it, both store each other's full certificate fingerprint, and both show **Paired**.
7. Three wrong attempts, a code-window timeout, or a disconnect cancels the pairing. If the initiator disconnects while the responder is still deciding, the responder closes the prompt.

The receiving side never auto-approves, even if a device claims to be a repeat visitor.

### 6.3 Sending

1. Right-click the tray icon → **Send files…**
2. Pick one or more files.
3. Choose a paired laptop from the device list.
4. The receiver shows an offer with the sender name, file names, sizes, and total.
5. Receiver clicks Accept; both sides show progress; either side can cancel.
6. On completion, the receiver gets a notification and an **Open folder** button.

If the tray icon's **Devices** submenu is used, clicking a paired device opens the file picker and sends immediately after selection.

### 6.4 Receiving

- The app listens while running.
- Offers from paired devices show a prompt. The prompt focuses **Decline**, not Accept.
- `Auto-accept from paired devices` is off by default.
- Offers time out after two minutes and are declined.
- Received files are written only inside the configured receive folder and never opened automatically.

### 6.5 Tray menu

```
Send files…                  (bold/default)
Devices ▸
  Rohan-Laptop               (paired)
  Study-Laptop               (paired)
  ─────────
  Pair new device…           (opens device list)
  Refresh
─────────────────────────────
Visible on this network      (checkable, on)
Open received folder
Settings…
About LanBridge
Quit
```

Left-clicking the tray icon opens the device list.

### 6.6 Device list

- Header: **LanBridge** and a status line: "Visible on this network" / "Hidden".
- Rows: device name, state (Paired / Available / Offline / Identity changed), IP address, last seen.
- Primary button changes with selection: **Send files…** for paired (including offline), **Pair…** for available, **Review…** for an identity-changed candidate.
- An offline paired row is not a hard block: Send attempts the last known endpoint and reports a clear failure if it is unreachable.
- A new certificate claiming a paired device ID appears as a separate **Identity changed** candidate. It never replaces or auto-unpairs the existing pinned row; the user must review and pair again.
- **Refresh** sends an immediate discovery query.
- Empty state explains that the other laptop must have LanBridge running and both must be on the same network.
- When hidden, new devices cannot discover this laptop and unpaired pairing requests are rejected; paired laptops can still try the last known endpoint.
- Unpair is available from the row's context menu, with confirmation.

### 6.7 Settings

- Device name
- Receive folder + Browse
- Start with Windows
- Visible on this network
- Auto-accept from paired devices (default off)
- Repair firewall rule (UAC)
- Open logs
- About / version

### 6.8 Error and edge states

- No devices found → explain firewall, network profile, AP isolation, and VPN.
- Port already in use → clear error with the port number; app exits.
- Firewall rule missing/denied → warning banner in the device list.
- Receiver offline → "Rohan-Laptop is offline. Last seen 2 minutes ago. Send will try the last known address." If that fails, show the error and keep the row.
- Busy → "Rohan-Laptop is already receiving a file" for a second inbound offer; "A send is already in progress" for a second outbound. One inbound and one outbound may run at the same time.
- Disk full → offer declined or transfer aborted before writing; message includes required space.
- Hash mismatch → the whole transfer aborts; the failed `.part` is removed; completed files are kept; both sides show which file failed and how many completed.
- Peer certificate changed → never auto-trust and never auto-unpair. Keep the old pinned row, show the new certificate as an **Identity changed** candidate, and explain: "If you reinstalled LanBridge on that laptop, unpair the old entry and pair again."
- Pairing approval timeout → the responder sends `PAIR_DENY`; the initiator shows "No response from Rohan-Laptop."
- Pairing code timeout → both sides show "Pairing expired."
- Hidden mode → unpaired requests get "This laptop is not discoverable"; the UI explains how to turn visibility on.
- Device names are self-asserted. The pairing prompt shows the claimed name and IP; approve only if you just started the pairing.
- App crash during receive → `.part` files older than one hour are cleaned up on next start.

## 7. Architecture

```
┌──────────────────────────── LanBridge.exe ────────────────────────────┐
│                                                                        │
│  TrayApplicationContext                                                │
│    ├── NotifyIcon + ContextMenuStrip                                   │
│    ├── DeviceListForm / SettingsForm / ProgressForm                    │
│    └── UiDispatcher (SynchronizationContext)                           │
│                                                                        │
│  Core                                                                  │
│    ├── AppConfig          %APPDATA%\LanBridge\config.json              │
│    ├── DeviceIdentity     %APPDATA%\LanBridge\identity.pfx.dpapi       │
│    ├── PeerStore          %APPDATA%\LanBridge\peers.json               │
│    ├── DiscoveryService   UDP 45873 broadcast/query + TLS confirmation │
│    ├── TransferServer     TCP 45873 + SslStream + frame protocol       │
│    ├── TransferClient     TCP 45873 + SslStream + frame protocol       │
│    ├── PairingService     SAS + proof + rate limits                    │
│    ├── PathGuard          name sanitization + unique paths             │
│    └── Logger             %APPDATA%\LanBridge\logs\lanbridge.log       │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

### 7.1 Process model

- Single WinForms process, no service.
- One tray icon, one instance. A named mutex prevents a second copy.
- All networking runs on background threads/tasks. UI work is marshaled to the WinForms thread.
- Core services are constructed by `TrayApplicationContext` and disposed on exit.

### 7.2 Data at rest

| Path | Contents | Protection |
|---|---|---|
| `%APPDATA%\LanBridge\config.json` | Device name, receive folder, toggles, firewall flag | User profile ACL |
| `%APPDATA%\LanBridge\identity.pfx.dpapi` | Self-signed cert + private key | DPAPI, CurrentUser scope |
| `%APPDATA%\LanBridge\peers.json` | Paired device names, IDs, cert fingerprints, last endpoints | User profile ACL |
| `%APPDATA%\LanBridge\logs\lanbridge.log` | Metadata-only diagnostics, rotated | User profile ACL |

No file contents, pairing codes, or private keys are logged.

### 7.3 Dependencies

- Runtime: .NET Framework 4.8.1 (included in Windows 11 and current Windows 10).
- Build: .NET SDK 8 (build machine only) with `Microsoft.NETFramework.ReferenceAssemblies.net48`.
- Tests: xUnit + `Microsoft.NET.Test.Sdk`.
- Runtime NuGet dependencies: none.
- UI: WinForms; no third-party UI packages.
- Icon: generated `.ico`, embedded in the exe.

## 8. Identity and trust

### 8.1 Device identity

- On first run, generate an RSA-2048 key pair and a self-signed X.509 certificate.
- Subject: `CN=LanBridge-<device-id>`; validity 10 years; SHA-256 signature.
- Key usage: digital signature + key encipherment; EKU: server auth + client auth.
- Export as PFX, protect the bytes with `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)`, write atomically to `identity.pfx.dpapi`.
- Device ID: 128-bit random value stored in `config.json`, also included in the certificate subject. It is stable metadata, not a trust anchor.
- Fingerprint: lowercase hex SHA-256 of the DER-encoded certificate. This full fingerprint is the cryptographic identity, the peer-store primary key, and the pin. It is never truncated for storage or pinning.
- Peer store is keyed by full fingerprint, with a secondary index by device ID for identity-change warnings.
- If a pinned certificate presents a new name or device ID, update the metadata only after an authenticated paired session.
- If a new certificate claims the device ID of a paired peer, show an **Identity changed** candidate and keep the old pin. Never auto-trust the new certificate and never auto-remove the old one. The user must unpair and pair again if they recognize the change.
- A reinstall with a new certificate and a different device ID appears as a new unpaired device. A reinstall that preserves the device ID but not the key appears as an identity-changed candidate.

If the certificate is missing or unreadable, the app creates a new identity. Existing peers will reject it; the user must unpair and pair again. The app explains this instead of silently trusting the new certificate.

### 8.2 Pairing code

Both sides exchange fresh 128-bit nonces in `HELLO` / `HELLO_ACK`. The initiator is the TCP client; the responder is the TCP server.

```
fpLow, fpHigh = sort(fingerprintInitiator, fingerprintResponder)
key           = SHA-256("lanbridge-pair-v1" || fpLow || fpHigh)
sas           = HMAC-SHA-256(key, nonceInitiator || nonceResponder)
code          = first 4 bytes of sas as uint32, modulo 1,000,000, zero-padded to 6 digits
```

- The responder displays the code only after its user clicks Approve.
- The initiator asks its user to type the code and compares locally.
- The code is a short authentication string for human comparison, not a stored password. It is never sent over the wire.
- On a match, the initiator sends a proof:
  `HMAC-SHA-256(SHA-256("lanbridge-pair-key-v1" || code), "confirm" || nonceInitiator || nonceResponder || fpInitiator || fpResponder)`.
- The responder verifies the proof in constant time, then both store the peer's full fingerprint.
- Three attempts per session; 120-second approval prompt; 120-second code window; one pairing dialog at a time.
- Pairing requires explicit approval on the responder. A user cannot be paired with remotely without clicking Approve.

Why this shape: a PIN that is merely sent over the connection can be relayed by a man-in-the-middle. Deriving the code from both certificates and fresh nonces means a MITM has two different channels and two different codes; the initiator's local comparison fails. The short code is only valid for the live pairing window; the long-term identity is the full SHA-256 fingerprint, avoiding the KDE Connect truncation problem.

### 8.3 Certificate pinning

- After pairing, store the peer's full SHA-256 certificate fingerprint.
- Every future TLS connection must present that exact certificate; any change is rejected before application data is exchanged.
- Unpaired discovery confirmation verifies the server certificate against the fingerprint in the UDP announcement. A spoofed announcement pointing at a different certificate fails the TLS handshake and the device is not shown.
- Both sides also check that the fingerprint claimed in `HELLO` matches the certificate actually presented in TLS.
- `HELLO.paired` means "my peer store contains the fingerprint of the certificate I am connecting to." `HELLO_ACK.paired` means the same for the server's view of the client certificate. A session counts as paired only when both flags are true.
- If exactly one side reports paired, the app shows "The other laptop no longer recognizes this pairing. Pair again?" It never auto-trusts the claiming side and never auto-deletes the existing pin. The user can pair again or unpair.
- If both report unpaired, normal pairing is required.
- The server rejects a `send` session unless the client certificate is pinned; the client rejects a `send` session unless the server certificate is pinned.
- Hidden mode rejects unpaired `pair` and `send` sessions. Paired sessions are still accepted if the peer knows the endpoint.
- Unpairing removes the local fingerprint. It does not silently delete the peer's pin; the next session reveals the asymmetry and offers re-pairing.

### 8.4 Threat model

In scope:

- Passive eavesdropping on the home LAN: blocked by TLS 1.2 with ECDHE.
- Active man-in-the-middle during discovery or pairing: discovery confirmation binds the certificate; the pairing code binds the live channel.
- Device impersonation / spoofed discovery: full-certificate pinning and TLS callback.
- Random LAN device trying to send files: pairing required; offers only from pinned peers.
- Path traversal and malicious filenames: sanitization and one-folder confinement.
- Pairing-code brute force: three attempts, 120-second approval and code windows, human approval on the responder.
- Theft of the app-data folder while the user is logged out: private key protected by DPAPI.
- Accidental internet exposure: bind to LAN, reject non-LAN peer addresses, firewall rule scoped to `LocalSubnet`; no outbound internet calls.

Out of scope:

- A compromised endpoint (malware on either laptop can read files or the screen).
- A malicious user with physical access to an unlocked laptop.
- Router/AP compromise, ARP spoofing that can block traffic (TLS still protects confidentiality).
- Denial of service on the LAN.
- Supply-chain compromise of the .NET Framework or Windows.
- Code-signing reputation; v0.1 is unsigned.

### 8.5 TLS details

- `SslStream`, `SslProtocols.Tls12`, mutual authentication.
- Server requires a client certificate; client always presents its identity.
- Certificate validation is fingerprint-based, not chain-based; self-signed certs are expected.
- Revocation and hostname checks are intentionally skipped; the pinned certificate is the identity.
- No TLS 1.3 dependency in v0.1, because .NET Framework 4.8/Schannel support varies by OS update. TLS 1.2 with ECDHE is the compatibility baseline.

## 9. Discovery

- UDP port `45873`, same number as TCP, bound to `0.0.0.0`.
- IPv4 only in v0.1.
- Packet: UTF-8 JSON, max 1,400 bytes, with a `LNB1` magic prefix.
- `announce`: `{t:"a", v:1, id, name, port, fp, discoverable}`
- `query`: `{t:"q", v:1, id}` — peers reply with a unicast `announce` to the source endpoint.
- Announce every 5 seconds while visible; expire peers after 15 seconds without an announcement.
- On start and when the device list opens, send a query immediately.
- Ignore self by device ID/fingerprint; ignore malformed packets; rate-limit processing per source IP.
- Broadcast to each active physical Wi-Fi/Ethernet interface's subnet broadcast address and to `255.255.255.255`. Only interfaces that are `Up`, have a private IPv4 address, have a default gateway, and are not virtual/VPN/Bluetooth are eligible.
- Accept peers only when their address belongs to one of those physical LAN subnets. A VPN, Hyper-V, Bluetooth, or other virtual subnet is rejected even if it is RFC1918.
- Show a discovered device only after a TCP/TLS confirmation:
  1. Connect to the source IP and advertised port.
  2. TLS handshake with our client certificate.
  3. Require the server certificate fingerprint to equal the announced `fp`.
  4. Send `HELLO` with `intent=ping`; require `HELLO_ACK`.
  5. Close.
- Confirmation is cached per fingerprint for 10 seconds.
- Paired devices are shown from the peer store even when offline, with their last-seen time. Sending to an offline paired device attempts the last known endpoint.
- If `Visible on this network` is off, stop announcements and ignore queries, reject unpaired `pair`/`send` sessions with `hidden`, and keep the TCP listener available to paired peers that already know the endpoint.
- Multiple network interfaces: advertise/use the source address of the outgoing packet; do not advertise virtual adapter addresses.
- Re-enumerate interfaces on network-change and resume events, with a 2-second debounce. Restart discovery and update the advertised address.
- A paired peer's last endpoint is updated only by a confirmed announcement or an authenticated connection. If the laptop is hidden and its IP changes, it appears offline until visibility returns.
- AP isolation on the router will prevent discovery and connections; the empty-state help mentions it.

## 10. Wire protocol

See `docs/protocol.md` for the full byte-level spec. Summary:

- TCP 45873, wrapped in TLS, mutual auth.
- Frame: 4-byte big-endian payload length, 1-byte type, payload.
- Control frames: UTF-8 JSON, max 1 MiB.
- File-data frames: max 64 KiB + 4-byte file index.
- Protocol version 1; mismatches return `ERROR {code:"version"}`.

Message types:

| Type | Direction | Purpose |
|---|---|---|
| `HELLO` | client → server | Protocol version, device ID, name, fingerprint, nonce, intent (`ping`, `pair`, `send`), `paired` flag |
| `HELLO_ACK` | server → client | Same fields plus the server nonce, `paired` flag, and `busy` flag |
| `PING` / `PONG` | both | Discovery confirmation |
| `PAIR_PROMPT` | server → client | Responder approved; user may enter the code |
| `PAIR_PROOF` | client → server | HMAC proving knowledge of the code |
| `PAIR_ABORT` | client → server | Initiator gave up after three wrong attempts |
| `PAIR_OK` / `PAIR_DENY` | server → client | Result; deny reasons: `denied`, `timeout`, `hidden`, `busy`, `invalid` |
| `OFFER` | client → server | Transfer ID, file list (name, size), total size |
| `ACCEPT` / `REJECT` | server → client | Transfer decision |
| `FILE_BEGIN` | client → server | File index, sanitized name, size |
| `FILE_DATA` | client → server | File index + raw bytes |
| `FILE_END` | client → server | File index + SHA-256 of the file |
| `FILE_OK` / `FILE_ERROR` | server → client | Per-file result; `FILE_ERROR` aborts the whole transfer in v0.1 |
| `CANCEL` | both | Abort the transfer |
| `DONE` | client → server | All files sent |
| `DONE_ACK` | server → client | Transfer complete |
| `ERROR` | both | Code + human-readable message |

Sequence, send:

```
client                          server
  TLS handshake (mutual)          TLS handshake (mutual)
  HELLO intent=send        →
                            ←      HELLO_ACK
  OFFER                    →
                            ←      ACCEPT / REJECT
  FILE_BEGIN               →
  FILE_DATA * n            →
  FILE_END                 →
                            ←      FILE_OK
  ... repeat per file ...
  DONE                     →
                            ←      DONE_ACK
```

Rules:

- A transfer is rejected unless both sides report paired and each side has the other's full certificate fingerprint pinned.
- One inbound transfer and one outbound transfer may run concurrently. Pairing, discovery pings, and pairing-dialog connections are always allowed, but only one pairing dialog may be open at a time.
- A second inbound offer gets `ERROR {code:"busy_inbound"}`. A second outbound send is blocked in the UI with "A send is already in progress."
- The receiver asks its user unless auto-accept is enabled.
- Pairing approval timeout 120 s; pairing code window 120 s; control timeout 30 s; file-data idle timeout 60 s; offer prompt 120 s.
- Both sides can send `CANCEL`; the in-progress `.part` is deleted and already completed files are kept.
- A `FILE_ERROR` aborts the whole transfer. The server deletes the failed `.part`, keeps completed files, and sends `ERROR {code:"transfer_failed", fileIndex, completed}`. The sender shows "X of Y files completed."
- The sender hashes each file while streaming and sends the hash in `FILE_END`; the receiver computes its own hash and compares. TLS already provides integrity; the hash catches disk/write errors and gives both sides a verifiable result.
- Device names are limited to 64 characters and sanitized before display or logging. Remote error strings are mapped to local messages; raw remote text is never shown.
- No compression. Stream at disk/network speed.

## 11. File handling

- Receive folder default: `%USERPROFILE%\Downloads\LanBridge`; created on first run.
- The receiver never trusts a remote path. `PathGuard.SanitizeFileName`:
  - Strip directory components using Windows separator rules.
  - Remove control characters and characters invalid on Windows: `< > : " / \ | ? *`.
  - Remove Unicode control/format characters that can spoof names.
  - Trim trailing dots and spaces.
  - Prefix Windows reserved device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`) with `_`.
  - Limit to 180 characters while preserving the extension.
  - Fall back to `file` if the result is empty, `.` or `..`.
- Write to a unique `.part` file in the receive folder. On success, atomically move to a unique final name: `name.ext`, `name (1).ext`, `name (2).ext`, ...
- Never overwrite an existing file.
- Check free space before accepting: `totalSize + 50 MiB`. If insufficient, reject with required/available values.
- Total size is a `long`; sum with overflow checks. Negative sizes are rejected.
- Maximum 5,000 files per offer, to keep the prompt usable.
- Empty files are allowed.
- On any file error (hash mismatch, size mismatch, write failure, disk full after the initial check), abort the whole transfer: delete the current `.part`, keep already completed files, and report the failed index and completed count.
- On cancel or disconnect, delete the current `.part` and keep completed files.
- There is no automatic continuation of the remaining files after a per-file error in v0.1; the user can retry.
- On startup, delete `.part` files in the receive folder older than one hour.
- Do not auto-open received files. Only offer **Open folder**.
- Received `.exe`, `.bat`, `.ps1`, `.lnk`, and similar files are saved but never executed.

## 12. Security and privacy decisions

- No telemetry, analytics, crash reporting, update checks, or outbound internet requests.
- Firewall rule: inbound, program-scoped, profiles Private + Public, `RemoteIp=LocalSubnet`. No port opened to the internet.
- Additional runtime guard: accept peers only from the local subnets of physical Wi-Fi/Ethernet interfaces. VPN, Hyper-V, Bluetooth, and other virtual subnets are rejected even when they are RFC1918. Loopback is allowed only in tests.
- Bind TCP and UDP to `0.0.0.0`; advertise only the active LAN address.
- Mutual TLS with pinned certificates; pairing required for transfers.
- Private key: the canonical copy is DPAPI-protected (CurrentUser). Load with `X509KeyStorageFlags.EphemeralKeySet` so Schannel does not persist a second key container. If the runtime rejects an ephemeral key, fall back to `UserKeySet` and document that Windows keeps a profile-ACL-protected key container; the DPAPI blob remains canonical.
- Pairing code shown only after explicit approval on the responder; approval prompt 120 s, code window 120 s, three attempts.
- Logs contain device names, IPs, file counts/sizes, and error codes. They never contain file contents, codes, keys, or hashes of file contents.
- UI never renders remote HTML; no embedded browser.
- File names are displayed as plain text, not interpreted markup.
- Device names are limited to 64 characters and sanitized for control/format characters. Pairing prompts show the claimed name and IP.
- Remote error strings are mapped to local messages; logs strip CR/LF and control characters.

## 13. Tech stack and repository layout

```
lanbridge/
├── .github/
│   └── workflows/
│       ├── build.yml
│       └── release.yml
├── assets/
│   └── icon.ico
├── docs/
│   ├── protocol.md
│   ├── security.md
│   └── superpowers/specs/2026-11-26-lanbridge-design.md
├── src/
│   └── LanBridge/
│       ├── LanBridge.csproj
│       ├── app.manifest
│       ├── Program.cs
│       ├── TrayApplicationContext.cs
│       ├── Core/
│       │   ├── AppConfig.cs
│       │   ├── DeviceIdentity.cs
│       │   ├── PeerStore.cs
│       │   ├── DiscoveryService.cs
│       │   ├── TransferServer.cs
│       │   ├── TransferClient.cs
│       │   ├── PairingService.cs
│       │   ├── Protocol.cs
│       │   ├── PathGuard.cs
│       │   ├── LanScope.cs
│       │   ├── Sanitizer.cs
│       │   └── Logger.cs
│       └── Forms/
│           ├── DeviceListForm.cs
│           ├── PairingForm.cs
│           ├── ReceivePromptForm.cs
│           ├── ProgressForm.cs
│           ├── SettingsForm.cs
│           └── FirstRunForm.cs
├── tests/
│   └── LanBridge.Tests/
│       ├── LanBridge.Tests.csproj
│       ├── PathGuardTests.cs
│       ├── PairingCodeTests.cs
│       ├── ProtocolTests.cs
│       ├── PeerStoreTests.cs
│       └── TransferIntegrationTests.cs
├── .editorconfig
├── .gitignore
├── build.ps1
├── LanBridge.sln
├── LICENSE
├── README.md
└── SECURITY.md
```

Project settings:

- `TargetFramework=net48`, `UseWindowsForms=true`, `Platforms=AnyCPU`, `Prefer32Bit=false`.
- `LangVersion=latest`, nullable disabled for v0.1 to avoid noisy BCL annotation work.
- `ApplicationIcon=..\..\assets\icon.ico`, `ApplicationManifest=app.manifest`.
- Assembly version `0.1.0`.
- `Microsoft.NETFramework.ReferenceAssemblies.net48` is a private build-time package.
- No runtime NuGet packages.

## 14. Build, CI, and release

Local:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
.\build.ps1
```

`build.ps1` produces:

```
artifacts/
├── LanBridge.exe
├── README.md
├── LICENSE
├── install.ps1
├── uninstall.ps1
├── lanbridge-v0.1.0-win.zip
└── SHA256SUMS.txt
```

### 14.1 Install experience

The primary install command in the README:

```powershell
irm https://github.com/SMR-2009/lanbridge/releases/latest/download/install.ps1 | iex
```

`install.ps1` behaviour:

- Downloads the latest `LanBridge.exe` and `SHA256SUMS.txt` from the GitHub release.
- Verifies the SHA-256 before writing anything into the install location.
- Installs per-user to `%LOCALAPPDATA%\Programs\LanBridge\LanBridge.exe`; no admin required.
- Creates a Start Menu shortcut and launches the app.
- Does not enable auto-start or add a firewall rule behind the user's back; the app's first-run wizard asks.

Uninstall command:

```powershell
irm https://github.com/SMR-2009/lanbridge/releases/latest/download/uninstall.ps1 | iex
```

`uninstall.ps1` stops the app, removes the install folder, Start Menu shortcut, startup registry value, and firewall rule (UAC), but keeps received files and `%APPDATA%\LanBridge` unless `-RemoveData` is passed.

Release assets: `LanBridge.exe`, `lanbridge-v0.1.0-win.zip`, `install.ps1`, `uninstall.ps1`, `SHA256SUMS.txt`.

A manual download-and-run path is documented for users who do not want to pipe a script into PowerShell.

### 14.2 GitHub presentation

- Repo description: "Tiny, secure, local-only file transfer for Windows."
- Topics: `windows`, `file-transfer`, `lan`, `winforms`, `dotnet`, `local-first`, `privacy`, `tray-app`, `self-hosted`.
- README: badges (build, latest release, MIT, Windows 10/11), one-line pitch, screenshots, install command, quick-start pairing walkthrough, security model, limitations, FAQ, troubleshooting, build-from-source, credits, and a link to LocalSend for cross-platform users.
- Visuals: app icon, 1280x640 social preview, and screenshots of the device list, pairing code, transfer progress, and tray menu (generated after the UI exists).
- Docs: `docs/design.md` (public copy of this spec), `docs/protocol.md`, `docs/security.md`.
- Community files: `LICENSE`, `SECURITY.md`, `CONTRIBUTING.md`, `CHANGELOG.md`, `CODE_OF_CONDUCT.md`, issue forms, and a pull-request template.
- Release: annotated tag `v0.1.0`, release notes summarizing features/known limitations, SHA-256 checksums, and the install one-liner.
- Repo settings: issues enabled, wiki/projects disabled, default branch `main`, branch protection requiring the build workflow after the first green run.
- Commit identity: `R. Murtaza <209728396+SMR-2009@users.noreply.github.com>` (repo-local config; the GitHub account's private email is not exposed).

GitHub Actions:

- `build.yml`: on push and pull request; `windows-latest`; setup .NET 8; restore, build, test.
- `release.yml`: on tag `v*`; build/test; zip the exe, README, LICENSE; compute SHA-256; create a GitHub Release with `softprops/action-gh-release`.
- Permissions: `contents: write` only for the release workflow.

Repository policy:

- Public, MIT license, copyright Rohan.
- Default branch `main`.
- Issues enabled; no code of conduct in v0.1 (add later if the project gets users).
- README includes screenshots, install, usage, security model, limitations, build instructions, and a link to LocalSend as the cross-platform alternative.
- `SECURITY.md` documents the threat model and a private reporting path.

## 15. Testing

### Unit tests

- `PathGuard`: traversal, absolute paths, ADS, reserved names, trailing dots/spaces, Unicode controls, long names, duplicates, empty input.
- `PairingCode`: known vectors, role ordering, different certificates/nonces, six-digit formatting, proof verification, constant-time comparison behavior.
- `Protocol`: frame round-trip, oversized/negative lengths, truncated streams, JSON limits, unknown types, paired flags.
- `PeerStore`: add/update/unpair, atomic save, corrupt file fallback, fingerprint pinning, secondary device-ID index, mutual paired flags.
- `LanScope`: RFC1918, link-local, loopback, public addresses, VPN/virtual interface rejection, eligible-interface selection, same-subnet membership.
- `Sanitizer`: device names, control/format characters, log injection, error-code mapping.
- `AppConfig`: defaults, atomic save, corrupt fallback.

### Integration tests (loopback, fake UI)

- Happy-path pairing: both stores updated, subsequent connection accepted.
- Wrong code: no pairing, attempts exhausted, sockets closed.
- Unpaired send rejected.
- Paired send of multiple files: bytes and hashes match.
- Duplicate names get suffixes; traversal names stay inside the receive folder.
- Cancel and dropped connection remove `.part`.
- Changed server certificate with a pinned fingerprint is rejected.
- Discovery announcement with a mismatched certificate fingerprint is not confirmed.
- Disk-full path returns a clear error (simulate with a size larger than available or a fake writer).

Additional cases:
- Mutual pairing asymmetry: one side unpairs, the other still has the pin; session shows re-pair, no auto-trust or auto-delete.
- Identity change: same device ID, new certificate; old pin stays, new candidate is not trusted.
- Hidden mode: unpaired pairing rejected; paired session works via the last endpoint; hidden + IP change shows offline.
- VPN/virtual subnet: peer on `172.20.x` or a VPN subnet rejected; physical LAN accepted.
- Concurrency: send and receive at the same time; second inbound gets busy; pairing during a transfer works.
- Per-file abort: hash mismatch or disk error aborts the batch, keeps completed files, deletes `.part`.
- Pairing timers: approval timeout and code-window timeout produce distinct messages.
- Device-name sanitization and log injection.

### Manual two-laptop checklist

- First run and UAC firewall rule on both laptops.
- Discovery both directions on the home Wi-Fi.
- Pair from laptop A and from laptop B.
- Send 1 KB, 100 MB, and 2 GB files; measure throughput against a local file copy baseline.
- Unicode and duplicate file names.
- Cancel from sender and receiver.
- Sleep/wake, app restart, peer offline/online.
- VPN on/off; Public vs Private network profile.
- Firewall rule removed → clear failure message.
- Unpair and re-pair; changed identity → blocked message.

### Acceptance criteria for v0.1

- Fresh install → pair → send a file in under two minutes.
- Transfer is TLS-protected; no plaintext file bytes on the wire.
- Only TCP/UDP 45873 listens; firewall rule is scoped to `LocalSubnet`.
- No outbound internet traffic.
- All automated tests pass.
- Manual checklist passes on both Windows 11 laptops.
- README and SECURITY.md are accurate and screenshots are included.

## 16. Milestones

1. **M1 — Skeleton:** solution, csproj, icon, config, identity, logger, tray, single instance. Build/test.
2. **M2 — Discovery:** UDP announce/query, interface selection, TLS confirmation, device list (no pairing).
3. **M3 — Pairing:** SAS, approval flow, proof, peer store, pinning, unpair.
4. **M4 — Transfer:** offer/accept, file streaming, progress, cancel, path guard.
5. **M5 — Hardening:** first-run wizard, firewall, settings, integration tests, error states, DPI/accessibility pass.
6. **M6 — Release:** README, SECURITY, protocol docs, CI, GitHub repo, v0.1.0 release, two-laptop test.

## 17. Decisions I will make unless you object

- Name: **LanBridge**.
- License: **MIT**, copyright R. Murtaza.
- Repo: **public** at `SMR-2009/lanbridge` — "Tiny, secure, local-only file transfer for Windows."
- Receive folder: `%USERPROFILE%\Downloads\LanBridge`.
- Auto-start: offered and checked in the first-run wizard.
- Auto-accept from paired devices: **off** by default.
- Visible on network: **on** by default, with a tray toggle.
- Pairing code: six digits, three attempts, 120 s approval prompt + 120 s code window.
- Peer identity: full certificate fingerprint is the pin; device ID is metadata only; an identity change requires manual re-pair.
- Visibility: hidden stops discovery and rejects unpaired sessions; paired peers may try the last endpoint.
- Network scope: physical Wi-Fi/Ethernet subnets only; VPN/virtual adapters excluded; re-enumerate on network change.
- Transport: TLS 1.2, mutual auth, certificate pinning.
- Ports: TCP + UDP `45873`.
- Discovery: IPv4 broadcast + immediate query.
- One inbound + one outbound transfer at a time; pairing and pings always allowed.
- Per-file failure: abort the whole transfer, keep completed files, delete the current `.part`.
- Device names: max 64 chars, sanitized; pairing prompt shows name + IP.
- Private key: DPAPI blob + EphemeralKeySet; UserKeySet fallback documented.
- No anonymous sends; pairing required.
- Icon: blue rounded square, two laptops, transfer arrow.

## 18. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Windows Firewall / Public profile blocks discovery | Program-scoped rule for `LocalSubnet`; first-run UAC prompt; troubleshooting in the empty state |
| Router AP isolation | Detect discovery failure and explain; no code fix possible |
| VPN or Hyper-V virtual adapters confuse discovery | Enumerate active physical Wi-Fi/Ethernet interfaces with a private IPv4 and default gateway; ignore virtual/link-local; advertise the source address |
| Unsigned exe triggers SmartScreen/AV | Build locally; publish SHA-256; document; code signing later |
| WinForms looks dated or scales poorly | Native controls, Segoe UI, PerMonitorV2 manifest, test at 100/125/150% |
| .NET Framework 4.8 missing on older Windows 10 | Document Windows 10 1903+ / Windows 11; app shows a clear error |
| Pairing/TLS implementation bugs | Small protocol, unit + loopback integration tests, two-laptop manual test |
| Scope creep (folders, resume, mobile) | Explicit non-goals; ship v0.1 first |
| GitHub release automation/token | `gh` already authenticated as `SMR-2009`; workflows use `GITHUB_TOKEN` |
| User impatience | Ship M1–M4 as a usable alpha, then harden |

## 19. References

- LocalSend protocol: https://github.com/LocalSend/protocol/
- LocalSend discovery MITM advisory: https://github.com/localsend/localsend/security/advisories/GHSA-424h-5f6m-x63f
- KDE Connect verification-code truncation advisory: https://kde.org/info/security/advisory-20250418-3.txt
- KDE Connect paired-device impersonation advisory: https://mail.kde.org/pipermail/kde-announce/2025-November/000476.html
- Warpinator path traversal advisory: https://www.openwall.com/lists/oss-security/2022/10/24/1
- Windows Nearby Sharing: https://support.microsoft.com/en-us/windows/experience/connectivity-networking/share-things-with-nearby-devices-in-windows
- .NET Framework `CertificateRequest`: https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.certificaterequest
- .NET Framework `SslStream`: https://learn.microsoft.com/dotnet/api/system.net.security.sslstream
