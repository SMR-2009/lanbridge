# Security policy

## Supported versions

LanBridge 0.1.x is the current supported line. Security fixes land on `main` and are released as a patch version.

## Reporting a vulnerability

Do not open a public issue. Use GitHub's private vulnerability reporting on the repository's **Security** tab. If that is unavailable, open a minimal public issue asking for a private channel and include no technical detail.

Please include:

- The version and Windows build.
- Whether the laptops were paired.
- The network profile (Private/Public) and whether a VPN or virtual adapter was active.
- A minimal reproduction.
- The impact you believe is possible.

Never include pairing codes, private keys, personal files, or another person's data.

## What to expect

- Acknowledgement within 7 days.
- A triage decision and severity assessment within 14 days.
- A fix or a documented mitigation as soon as is practical.
- Credit in the release notes if you want it.

## Scope

In scope: pairing and identity handling, TLS and certificate pinning, discovery, file-name and path handling, the receive-folder write path, the firewall rule, and the installer scripts.

Out of scope: a compromised Windows account, malware, a malicious paired device, denial of service from flooding the LAN, code signing, and vulnerabilities in Windows, .NET Framework, or GitHub.

See `docs/security.md` for the full threat model.
