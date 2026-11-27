# Changelog

All notable changes to LanBridge are documented here. The format is based on Keep a Changelog, and this project uses semantic versioning.

## [0.1.8] - 2026-11-28

### Security

- Partial transfers now live in a private `parts` folder under the receive folder, and resume sidecars must match their own `.part` file. A sidecar cannot point at an unrelated file.
- Receive offers have a configurable maximum size (100 GB by default) and the free-space check fails closed with a 1 GB reserve.
- Non-LAN connections are rejected before the TLS handshake, and each source IP has a bounded connection count.
- Pairing has a short global cooldown after a failed attempt, so a hostile peer cannot retry pairing in a tight loop.
- Discovery confirmation is keyed by fingerprint, address, and port, so a spoofed packet cannot reuse another host's confirmation. Discovery caches have hard size limits.
- TLS is negotiated by Windows instead of forcing TLS 1.2, and the negotiated protocol and cipher are logged. Certificate pinning and the six-digit code remain mandatory.
- Firewall validation now checks enabled state, inbound direction, allow action, Private/Public profiles, `LocalSubnet`, and the exact executable path. Repair also proves the receive folder is writable.
- Sender and receiver both sanitize file names before comparing them, fixing long names and names containing Unicode format characters such as directional marks.

### Fixed

- Receiver cancel is now delivered before the session closes, so both sides report a cancel rather than a generic failure.
- **Visible on this network** is wired to the server as well as discovery, so hidden mode rejects unpaired pairing requests.
- Quitting or sleeping preserves an in-progress partial instead of deleting it. A failed final rename keeps the verified bytes as a resumable partial.
- An unreadable receive folder no longer crashes startup or the repair report.
- Resume points are computed before the free-space decision and reused for the whole transfer instead of being re-hashed for each file.
- Discovery now persists updated peer endpoints, so paired devices found after a DHCP change keep working.
- `DONE` and `DONE_ACK` transfer IDs are checked instead of being ignored.
- Multi-file progress shows only the final completion as complete; earlier files no longer close the progress window with a false success.
- Closing the device list during an await no longer touches disposed controls, and closing the progress window cancels an active transfer.
- A second pairing request cannot inherit a closing dialog from the first.
- Settings repair runs off the UI thread, and Save is blocked while repair is running.
- Timeouts during pairing and receive prompts surface as timeouts instead of `denied`/`declined`.

### Changed

- Added **Maximum receive size (GB)** to Settings.
- Added a firewall warning to the device-list status when the rule is missing.
- Added **More → Check for updates**, which checks the latest GitHub release and opens the release page only when a newer version exists.
- The release build uses .NET 10 SDK tooling, publishes the zip with its checksums, and checks `-Version` against the actual assembly version.
- Added CodeQL analysis, Dependabot updates, warnings-as-errors in CI, and build provenance attestation.

## [0.1.7] - 2026-11-28

### Added

- Resumable transfers. Interrupted files keep a verified `.part` file and a small resume sidecar; retrying the same files continues from the verified offset instead of starting over.
- **Devices → Add device by IP...** for networks where UDP discovery is blocked. It still requires the same six-digit pairing approval.
- Opening the Start Menu shortcut while LanBridge is already running now opens the existing device list instead of doing nothing.
- A real WinForms pairing-UI regression test and activation-channel tests.

### Fixed

- Inbound TCP connections and UDP discovery work are now capped with bounded queues, preventing a noisy LAN from creating unbounded tasks.
- Free-space validation no longer adds the reserve before comparing, which could overflow for a hostile declared file size.
- First-run firewall/UAC work runs behind the message loop instead of blocking the UI thread.
- Sleep/resume no longer produces a false "UI thread has not responded" warning.
- A sender cancel is delivered before the TLS session closes, so the receiver removes the partial file instead of treating it as a reclaimable network drop.
- A resumed file is only accepted if its name, size, offset, and prefix hash still match.

### Changed

- Transfer buffers are reused across files, reducing allocation churn for folders with many small files.
- Protocol documentation now describes resume fields and matches the real 5-second connect / 30-second control timeouts.
- Supported platform is Windows 11; Windows 10 reached Microsoft end of support on 2025-10-14.

## [0.1.6] - 2026-11-26

### Fixed

- The device list now uses a table layout for its status strip and list, so the column header row is no longer clipped at the top.
- Column widths are recalculated with the window width, keeping Device, Status, Address, and Last seen readable.
- The header subtitle and title use a layout that does not overlap at different window sizes.

### Added

- Diagnostics for startup, device discovery/loss, pairing phases, transfer start/accept/completion/failure, device actions, and UI-thread stalls.
- UI-thread heartbeat warnings in `lanbridge.log` if the message loop stops responding.
- Device names are shown as tooltips when a row is narrow.

## [0.1.5] - 2026-11-26

### Fixed

- Pairing UI requests now use a dedicated WinForms control owned by the UI thread instead of relying only on the captured `SynchronizationContext`. This prevents a pairing or code window from being created on a dispatcher thread with no message loop.

### Added

- Pairing-phase diagnostics in the log: request, UI callback, form shown, approval/denial, code display, and close. Pairing codes are never written to the log.

## [0.1.4] - 2026-11-26

### Added

- Settings has a **Run repair** action that checks the receive folder, saves settings, verifies startup registration, removes stale partial transfers, and repairs the firewall rule for the current executable.
- **Repair details** explains exactly what the repair action changes.
- A `.NET Framework 4.8` runtime config enables `GCConserveMemory` and the release includes `LanBridge.exe.config` next to the executable.

### Changed

- Bulk transfer now reads files synchronously with a direct 64 KiB buffer and writes one reusable frame buffer per file. This removes a payload allocation and a copy for every chunk, and avoids async FileStream overhead.
- Sender and receiver use `SHA256Cng` for bulk hashing.
- File reads use `FileOptions.SequentialScan` with no extra FileStream buffer; writes use the same 64 KiB transfer size.
- The installer downloads, checksums, and installs the runtime config alongside the executable.

### Verification

- 118 tests pass, including a 64 MiB loopback transfer.
- Local loopback transfer measured about 87 MiB/s on this laptop.

## [0.1.3] - 2026-11-26

### Added

- Startup removes stale `.part` files older than one hour from the receive folder.
- The About window falls back to the application product version if the assembly version is unavailable.

## [0.1.2] - 2026-11-26

### Added

- Start at sign-in is now enabled by the installer by default; use `-NoStartup` to skip it.
- Screenshots and examples use neutral device names.

### Fixed

- Timeout timers are canceled as soon as each frame completes instead of remaining alive for the full timeout.
- Certificate objects created during TLS verification are disposed after use.
- The tray icon and rebuilt device menu items are disposed cleanly.
- The device list no longer rebuilds every row once per second; it updates only when device data changes and refreshes relative times every five seconds.
- UI progress updates are coalesced to avoid flooding the message loop.
- Discovery reuses its captured LAN scope and prunes stale rate-limit entries.

### Changed

- File chunks are 32 KiB instead of 64 KiB to reduce large-object heap pressure.
- The public docs no longer include the internal planning files or personal example names.

## [0.1.1] - 2026-11-26

### Fixed

- The one-line installer now resolves the latest release through the GitHub API, forces TLS 1.2 before downloading, never calls `exit`, and prints one clear failure instead of a raw PowerShell stack.
- The installer refuses to overwrite a running LanBridge and verifies that the downloaded checksum and local source files exist before copying.
- The firewall rule is checked against the installed executable's path, so a stale rule from an older folder cannot cause repeated firewall prompts.
- The installer reports a firewall or shortcut failure as a warning without aborting a successful install.

### Added

- Drag files onto the device list or its window to start a send.
- Unhandled UI exceptions are logged and shown as a short dialog instead of silently killing the tray app.
- Zero-byte and non-ASCII receive tests.
- CI parses install, uninstall, and build scripts before publishing.


## [0.1.0] - 2026-11-26

### Added

- Windows tray app with Send files, Devices, visibility, settings, received-folder, and quit actions.
- Six-digit pairing with explicit approval on the receiving laptop.
- UDP discovery confirmed over mutual TLS, so a spoofed broadcast cannot inject a fake device.
- Direct file transfer over TLS 1.2 with SHA-256 verification and atomic `.part` writes.
- Multiple-file offers, duplicate-name suffixes, per-file progress, and sender/receiver cancellation.
- Receive folder defaulting to `Downloads\LanBridge`.
- First-run setup for device name, receive folder, and Windows startup.
- Installer adds the scoped firewall rule with one UAC prompt and skips it when the rule already exists.
- Single-instance guard and per-user installation with a Start Menu shortcut.
- Build, installer, uninstaller, checksums, CI, and GitHub release workflows.

### Security

- Mutual certificate pinning: a paired peer is trusted by its full SHA-256 certificate fingerprint.
- Pairing codes are derived from both certificates and fresh nonces; the code is never sent over the wire.
- Private identity key is stored in a DPAPI-encrypted PFX; the runtime certificate uses the user key set because Schannel on .NET Framework 4.8 rejects ephemeral keys.
- Firewall rule is program-scoped, applies to Private and Public profiles, and is limited to `RemoteIp=LocalSubnet`; the app does not request UAC on every launch.
- Transfers are accepted only from physical Wi-Fi/Ethernet subnets; VPN, Hyper-V, Bluetooth, and other virtual adapters are excluded.
- Remote file names are reduced to a leaf name, reserved names are prefixed, and every file is written to a `.part` file before verification and an atomic move.

### Known limitations

- Windows 10/11 only in this release; macOS and Linux are not supported.
- IPv4 only.
- One inbound and one outbound transfer can run at a time; transfers do not resume.
- The executable is not code-signed, so Windows SmartScreen may warn on first launch.
- A device name change takes effect after restarting LanBridge.
