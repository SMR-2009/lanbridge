# Contributing to LanBridge

Thanks for helping. LanBridge is deliberately small: a Windows tray app that sends files directly over the local network.

## Build and test

Requirements: Windows 11, .NET SDK 10, and the .NET Framework 4.8 targeting pack (installed by the SDK on Windows).

```powershell
dotnet restore LanBridge.sln
dotnet build LanBridge.sln -c Release
dotnet test LanBridge.sln -c Release
```

To build the release assets and run the full test suite in one step:

```powershell
.\build.ps1
```

Run the app from `src\LanBridge\bin\Release\net48\LanBridge.exe`.

## What we look for

- Small, reviewable diffs that follow the existing structure and naming.
- Tests first for behavior changes. The suite is the specification; a failing test is the best bug report.
- No runtime NuGet dependencies without discussion. The app targets .NET Framework 4.8 so both laptops need nothing installed.
- No telemetry, analytics, update pings, or cloud calls.
- Security-sensitive changes need a short threat note in the pull request: what an attacker on the LAN could do, and why the change is safe.
- Keep names, error messages, and logs free of remote input unless it has been sanitized.

## Security

Do not open a public issue for a vulnerability. Use GitHub's private vulnerability reporting on the repository's Security tab, or open a minimal issue asking for a private channel. Never include pairing codes, private keys, or personal files in a report.

## Pull requests

- Explain what changes and why.
- List the commands you ran and the results.
- Run the manual checklist in `docs/manual-test.md` if you touched pairing, discovery, or transfers.
- Note anything you could not test, especially if it needs two laptops.

By contributing you agree that your changes are licensed under the MIT License and that you will follow the Code of Conduct.
