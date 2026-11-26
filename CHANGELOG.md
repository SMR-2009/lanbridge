# Changelog

All notable changes to LanBridge are documented here. The format is based on Keep a Changelog, and this project uses semantic versioning.

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
