# LanBridge v0.1.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a Windows tray app that pairs two Windows 11 laptops with a six-digit code and sends files directly over the LAN with TLS and certificate pinning.

**Architecture:** One WinForms process with a tray icon. Core services are a DPAPI-protected RSA identity, UDP discovery confirmed over TLS, a mutual-TLS TCP server/client speaking a length-prefixed frame protocol, SAS-based pairing, and a file receiver confined to one folder. No runtime NuGet dependencies.

**Tech Stack:** C# on .NET Framework 4.8, WinForms, `SslStream`, `TcpListener`/`UdpClient`, DPAPI, xUnit, .NET SDK 8 for building, GitHub Actions for CI/releases.

**Spec:** `docs/superpowers/specs/2026-11-26-lanbridge-design.md`

## Global Constraints

- Windows 10 1903+ and Windows 11; v0.1 is tested on two Windows 11 laptops.
- `net48`, `AnyCPU`, WinForms. No runtime NuGet packages. Build with .NET SDK 8.
- TCP and UDP port `45873`.
- TLS 1.2 only, mutual authentication, full SHA-256 certificate fingerprint pinning.
- Pairing: six digits, three attempts, 120-second approval prompt + 120-second code window, explicit approval on the responder.
- One inbound and one outbound transfer may run concurrently; pairing and discovery pings are always allowed, one pairing dialog at a time.
- Receive folder default `%USERPROFILE%\Downloads\LanBridge`; sanitize every name; never overwrite; write `.part`, verify SHA-256, move atomically.
- Hidden mode stops announcements/queries and rejects unpaired sessions; paired peers may try the last endpoint.
- Accept peers only on physical Wi-Fi/Ethernet subnets; reject VPN, Hyper-V, Bluetooth, and other virtual subnets.
- No telemetry, analytics, update checks, or outbound internet requests.
- Firewall rule is program-scoped, profiles Private + Public, `RemoteIp=LocalSubnet`.
- Canonical private key copy is a DPAPI-encrypted PFX; load with `EphemeralKeySet`, fall back to `UserKeySet` only if the runtime rejects it.
- MIT license, public repo `SMR-2009/lanbridge`, commit identity `R. Murtaza <209728396+SMR-2009@users.noreply.github.com>`.
- No code comments unless the code cannot explain itself. Small diffs. One commit per task.

## Review Focus

These are the inputs most likely to bite a real user; each has a test in the owning task.

1. **Hostile file names** (`..\evil`, `C:\Windows\x`, `CON`, trailing dots, Unicode controls, duplicates): must never escape the receive folder or overwrite a file. Tasks 5 and 10.
2. **Identity mismatch and one-sided pairing** (reinstall, changed certificate, one peer store reset): must never auto-trust or silently delete a pin. Tasks 2 and 9.
3. **Network context** (VPN connected, Hyper-V adapter, hidden mode, Wi-Fi change, sleep/wake): must not accept a virtual subnet or let a hidden laptop look available. Tasks 6, 11, and 14.
4. **Partial transfer** (hash mismatch, disk error, cancel, disconnect): the current `.part` must be removed, completed files kept, and both sides told what happened. Tasks 5, 10, and 14.
5. **Pairing-code attacks** (wrong code, relay, timeout, disconnected initiator): no pairing, clear message, no stuck dialog. Tasks 4, 9, and 14.

## File Structure

```
lanbridge/
├── .github/
│   ├── ISSUE_TEMPLATE/bug_report.yml
│   ├── ISSUE_TEMPLATE/feature_request.yml
│   ├── PULL_REQUEST_TEMPLATE.md
│   └── workflows/build.yml, release.yml
├── assets/icon.ico, social-preview.png
├── docs/
│   ├── design.md
│   ├── protocol.md
│   ├── security.md
│   └── superpowers/
│       ├── specs/2026-11-26-lanbridge-design.md
│       └── plans/2026-11-26-lanbridge-v0.1.0.md
├── src/LanBridge/
│   ├── LanBridge.csproj
│   ├── app.manifest
│   ├── Program.cs
│   ├── TrayApplicationContext.cs
│   ├── WinFormsUi.cs
│   ├── Core/
│   │   ├── AppConfig.cs
│   │   ├── DeviceIdentity.cs
│   │   ├── Tls.cs
│   │   ├── Protocol.cs
│   │   ├── ProtocolJson.cs
│   │   ├── FrameCodec.cs
│   │   ├── PairingCode.cs
│   │   ├── PeerStore.cs
│   │   ├── LanScope.cs
│   │   ├── DiscoveryService.cs
│   │   ├── TransferServer.cs
│   │   ├── TransferServerSession.cs
│   │   ├── TransferClient.cs
│   │   ├── TransferModels.cs
│   │   ├── TransferOptions.cs
│   │   ├── UiInterfaces.cs
│   │   ├── FileReceiver.cs
│   │   ├── PathGuard.cs
│   │   ├── Sanitizer.cs
│   │   ├── Firewall.cs
│   │   ├── Startup.cs
│   │   ├── SingleInstance.cs
│   │   └── Logger.cs
│   └── Forms/
│       ├── FirstRunForm.cs
│       ├── DeviceListForm.cs
│       ├── PairingForm.cs
│       ├── PairingCodeEntryForm.cs
│       ├── ReceivePromptForm.cs
│       ├── ProgressForm.cs
│       ├── SettingsForm.cs
│       └── AboutForm.cs
├── tests/LanBridge.Tests/
│   ├── LanBridge.Tests.csproj
│   ├── AppConfigTests.cs
│   ├── LoggerTests.cs
│   ├── DeviceIdentityTests.cs
│   ├── TlsLoopbackTests.cs
│   ├── FrameCodecTests.cs
│   ├── ProtocolJsonTests.cs
│   ├── PairingCodeTests.cs
│   ├── PathGuardTests.cs
│   ├── SanitizerTests.cs
│   ├── FileReceiverTests.cs
│   ├── PeerStoreTests.cs
│   ├── LanScopeTests.cs
│   ├── TransportTests.cs
│   ├── PairingIntegrationTests.cs
│   ├── TransferIntegrationTests.cs
│   ├── DiscoveryTests.cs
│   ├── DeviceRowTests.cs
│   └── TestSupport/
│       ├── FakePrompter.cs
│       ├── TempProfile.cs
│       └── TestData.cs
├── .editorconfig
├── .gitignore
├── global.json
├── build.ps1
├── install.ps1
├── uninstall.ps1
├── LanBridge.sln
├── LICENSE
├── README.md
├── SECURITY.md
├── CONTRIBUTING.md
├── CHANGELOG.md
└── CODE_OF_CONDUCT.md
```

---

### Task 1: Solution, configuration, and logging

**Files:**
- Create: `global.json`, `.gitignore`, `.editorconfig`, `LanBridge.sln`
- Create: `src/LanBridge/LanBridge.csproj`, `src/LanBridge/app.manifest`, `src/LanBridge/Program.cs`
- Create: `src/LanBridge/Core/AppConfig.cs`, `src/LanBridge/Core/Logger.cs`
- Create: `tests/LanBridge.Tests/LanBridge.Tests.csproj`, `tests/LanBridge.Tests/AppConfigTests.cs`, `tests/LanBridge.Tests/LoggerTests.cs`
- Create: `tests/LanBridge.Tests/TestSupport/TempProfile.cs`

**Interfaces:**
- Produces:
  - `AppConfig.CreateDefault()`, `AppConfig.Load(string path, ILogger log)`, `AppConfig.Save(string path)`
  - `ILogger.Info/Warn/Error`, `FileLogger(string logPath)`, `NullLogger.Instance`
  - `TempProfile` (IDisposable test helper with `Root`, `ConfigPath`, `PeerPath`, `IdentityDir`)

- [ ] **Step 1: Install the .NET 8 SDK**

Run:
```powershell
winget install --id Microsoft.DotNet.SDK.8 --source winget --accept-package-agreements --accept-source-agreements
dotnet --version
```
Expected: a version starting with `8.0.`

- [ ] **Step 2: Create the solution and project files**

Create `global.json`:
```json
{ "sdk": { "version": "8.0.100", "rollForward": "latestFeature" } }
```

Create `.gitignore`:
```gitignore
bin/
obj/
.vs/
artifacts/
*.user
*.suo
*.pfx
*.dpapi
*.log
```

After writing the two csproj files below, initialize git and create the solution:
```powershell
git init
git config user.name "R. Murtaza"
git config user.email "209728396+SMR-2009@users.noreply.github.com"
dotnet new sln -n LanBridge
dotnet sln add src/LanBridge/LanBridge.csproj tests/LanBridge.Tests/LanBridge.Tests.csproj
```

`src/LanBridge/LanBridge.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <Platforms>AnyCPU</Platforms>
    <Prefer32Bit>false</Prefer32Bit>
    <AssemblyName>LanBridge</AssemblyName>
    <RootNamespace>LanBridge</RootNamespace>
    <Version>0.1.0</Version>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="System.Drawing" />
    <Reference Include="System.Security" />
    <Reference Include="System.Runtime.Serialization" />
    <Reference Include="System.Web.Extensions" />
    <Reference Include="System.Windows.Forms" />
  </ItemGroup>
</Project>
```

`tests/LanBridge.Tests/LanBridge.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\LanBridge\LanBridge.csproj" />
  </ItemGroup>
</Project>
```

`app.manifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="LanBridge.app" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security><requestedPrivileges><requestedExecutionLevel level="asInvoker" uiAccess="false" /></requestedPrivileges></security>
  </trustInfo>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

`Program.cs` for now:
```csharp
using System.Windows.Forms;

namespace LanBridge
{
    internal static class Program
    {
        [System.STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
    }
}
```

- [ ] **Step 3: Write the failing configuration tests**

`tests/LanBridge.Tests/AppConfigTests.cs`:
```csharp
using System;
using System.IO;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class AppConfigTests
    {
        [Fact]
        public void Defaults_AreSafe()
        {
            var c = AppConfig.CreateDefault();
            Assert.Equal(Environment.MachineName, c.DeviceName);
            Assert.EndsWith(Path.Combine("Downloads", "LanBridge"), c.ReceiveFolder);
            Assert.True(c.Discoverable);
            Assert.False(c.AutoAcceptFromPaired);
            Assert.False(c.StartWithWindows);
            Assert.Equal(45873, c.TcpPort);
            Assert.Equal(45873, c.UdpPort);
            Assert.Equal(32, c.DeviceId.Length);
        }

        [Fact]
        public void RoundTrip_PreservesValues()
        {
            using var profile = new TempProfile();
            var c = AppConfig.CreateDefault();
            c.DeviceName = "Rohan-Laptop";
            c.Discoverable = false;
            c.Save(profile.ConfigPath);
            var loaded = AppConfig.Load(profile.ConfigPath, NullLogger.Instance);
            Assert.Equal("Rohan-Laptop", loaded.DeviceName);
            Assert.False(loaded.Discoverable);
            Assert.Equal(c.DeviceId, loaded.DeviceId);
        }

        [Fact]
        public void CorruptFile_IsQuarantined_AndDefaultsReturned()
        {
            using var profile = new TempProfile();
            File.WriteAllText(profile.ConfigPath, "{not json");
            var c = AppConfig.Load(profile.ConfigPath, NullLogger.Instance);
            Assert.Equal(Environment.MachineName, c.DeviceName);
            Assert.Contains(Directory.GetFiles(profile.Root), f => f.Contains(".corrupt-"));
        }
    }
}
```

`tests/LanBridge.Tests/TestSupport/TempProfile.cs`:
```csharp
using System;
using System.IO;

namespace LanBridge.Tests
{
    public sealed class TempProfile : IDisposable
    {
        public string Root { get; }
        public string ConfigPath => Path.Combine(Root, "config.json");
        public string PeerPath => Path.Combine(Root, "peers.json");
        public string IdentityDir => Path.Combine(Root, "identity");
        public string ReceiveDir => Path.Combine(Root, "received");

        public TempProfile()
        {
            Root = Path.Combine(Path.GetTempPath(), "lanbridge-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(IdentityDir);
            Directory.CreateDirectory(ReceiveDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}
```

`tests/LanBridge.Tests/LoggerTests.cs`:
```csharp
using System.IO;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class LoggerTests
    {
        [Fact]
        public void Logs_AreWritten_AndRotated()
        {
            using var profile = new TempProfile();
            var path = Path.Combine(profile.Root, "lanbridge.log");
            using (var log = new FileLogger(path, maxBytes: 256, keepFiles: 2))
            {
                for (var i = 0; i < 50; i++) log.Info(new string('x', 40));
            }
            Assert.True(File.Exists(path));
            Assert.True(Directory.GetFiles(profile.Root, "lanbridge*.log").Length >= 2);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run:
```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~AppConfigTests|FullyQualifiedName~LoggerTests"
```
Expected: FAIL because `AppConfig`, `ILogger`, `FileLogger`, and `NullLogger` do not exist.

- [ ] **Step 5: Implement `AppConfig` and logging**

`AppConfig` requirements:
- `CreateDefault()`: all values from Global Constraints; `DeviceId = Guid.NewGuid().ToString("N")`; `DeviceName = Environment.MachineName`; receive folder under `Environment.SpecialFolder.UserProfile`.
- `Load(path, log)`: if missing, return `CreateDefault()` and save it. If invalid JSON, rename the bad file to `config.corrupt-<utc timestamp>.json`, return defaults, and save a fresh file. If `DeviceId` is empty, generate one.
- `Save(path)`: create the directory, write to `path + ".tmp"`, then `File.Replace` if the destination exists, otherwise `File.Move`. Use `DataContractJsonSerializer` with UTF-8.
- `ILogger`: `void Info(string)`, `void Warn(string)`, `void Error(string, Exception = null)`.
- `FileLogger`: append with a lock; create the directory; rotate when the file exceeds `maxBytes` (default 2 MiB), keeping `keepFiles` (default 3) numbered files; `Dispose` flushes.
- `NullLogger.Instance`: no-op.

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~AppConfigTests|FullyQualifiedName~LoggerTests"
dotnet build LanBridge.sln -c Release
```
Expected: PASS; build succeeds with 0 errors.

- [ ] **Step 7: Commit**

```powershell
git add .
git commit -m "chore: scaffold LanBridge with config and logging"
```

---

### Task 2: Device identity and TLS pinning

**Files:**
- Create: `src/LanBridge/Core/DeviceIdentity.cs`, `src/LanBridge/Core/Tls.cs`
- Create: `tests/LanBridge.Tests/DeviceIdentityTests.cs`, `tests/LanBridge.Tests/TlsLoopbackTests.cs`
- Modify: `tests/LanBridge.Tests/LanBridge.Tests.csproj` only if a reference is missing

**Interfaces:**
- Produces:
  - `DeviceIdentity.LoadOrCreate(string appDataDir, string deviceId, ILogger log)`
  - `DeviceIdentity.DeviceId`, `.Certificate`, `.Fingerprint`
  - `Tls.Fingerprint(X509Certificate2)`
  - `Tls.CreateServer(Stream inner, X509Certificate2 serverCert)`
  - `Tls.CreateClient(Stream inner, X509Certificate2 clientCert, string expectedServerFingerprint)`

- [ ] **Step 1: Write the failing identity tests**

`DeviceIdentityTests.cs`:
```csharp
[Fact]
public void LoadOrCreate_IsStable_AndHasPrivateKey()
{
    using var profile = new TempProfile();
    using var first = DeviceIdentity.LoadOrCreate(profile.IdentityDir, "device-1", NullLogger.Instance);
    Assert.Equal(64, first.Fingerprint.Length);
    Assert.True(first.Certificate.HasPrivateKey);
    Assert.True(File.Exists(Path.Combine(profile.IdentityDir, "identity.pfx.dpapi")));

    using var second = DeviceIdentity.LoadOrCreate(profile.IdentityDir, "device-1", NullLogger.Instance);
    Assert.Equal(first.Fingerprint, second.Fingerprint);
}

[Fact]
public void CorruptIdentity_IsQuarantined_AndReplaced()
{
    using var profile = new TempProfile();
    File.WriteAllBytes(Path.Combine(profile.IdentityDir, "identity.pfx.dpapi"), new byte[] { 1, 2, 3 });
    using var identity = DeviceIdentity.LoadOrCreate(profile.IdentityDir, "device-1", NullLogger.Instance);
    Assert.True(identity.Certificate.HasPrivateKey);
    Assert.Contains(Directory.GetFiles(profile.IdentityDir), f => f.Contains(".corrupt-"));
}
```

`TlsLoopbackTests.cs`:
```csharp
[Fact]
public async Task PinnedServer_Handshake_Succeeds_And_WrongPin_Fails()
{
    using var profile = new TempProfile();
    using var server = DeviceIdentity.LoadOrCreate(Path.Combine(profile.Root, "server"), "server", NullLogger.Instance);
    using var client = DeviceIdentity.LoadOrCreate(Path.Combine(profile.Root, "client"), "client", NullLogger.Instance);
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;

    var accept = Task.Run(async () =>
    {
        using var tcp = await listener.AcceptTcpClientAsync();
        using var ssl = Tls.CreateServer(tcp.GetStream(), server.Certificate);
        await ssl.AuthenticateAsServerAsync(server.Certificate, true, SslProtocols.Tls12, false);
        var buffer = new byte[4];
        await ssl.ReadAsync(buffer, 0, 4);
        await ssl.WriteAsync(buffer, 0, 4);
    });

    using var clientTcp = new TcpClient();
    await clientTcp.ConnectAsync(IPAddress.Loopback, port);
    using var clientSsl = Tls.CreateClient(clientTcp.GetStream(), client.Certificate, server.Fingerprint);
    await clientSsl.AuthenticateAsClientAsync("lanbridge", new X509CertificateCollection { client.Certificate }, SslProtocols.Tls12, false);
    var remote = new X509Certificate2(clientSsl.RemoteCertificate);
    Assert.Equal(server.Fingerprint, Tls.Fingerprint(remote));
    await accept;
}
```

The wrong-pin test is a second `[Fact]` that calls `Tls.CreateClient(..., expectedServerFingerprint: new string('0', 64), ...)` and asserts `AuthenticateAsClientAsync` throws `AuthenticationException` or `IOException`.

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~DeviceIdentityTests|FullyQualifiedName~TlsLoopbackTests"
```
Expected: FAIL because the types do not exist.

- [ ] **Step 3: Implement identity generation and TLS helpers**

`DeviceIdentity` requirements:
- RSA 2048, SHA-256, self-signed, 10-year validity, CN `LanBridge-<deviceId>`, EKU server auth + client auth, digital signature + key encipherment.
- Export PFX, protect with `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)`, write atomically to `identity.pfx.dpapi`.
- Load: unprotect, `new X509Certificate2(bytes, string.Empty, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet)`. If that throws, retry with `UserKeySet` and log a warning. Never leave the PFX bytes unencrypted on disk.
- Fingerprint: lowercase hex SHA-256 of `Certificate.RawData`.
- On corrupt/unreadable file: rename to `identity.corrupt-<timestamp>.pfx`, create a new identity, log a warning. Do not silently delete the old file.

`Tls` requirements:
- `CreateServer`: `new SslStream(inner, false, (sender, cert, chain, errors) => true)`; after handshake, the caller reads `ssl.RemoteCertificate` and computes its fingerprint.
- `CreateClient`: callback computes the remote certificate fingerprint and returns `constant-time comparison` with `expectedServerFingerprint`; capture the certificate in `serverCert`. Reject null or mismatched certificates.
- Use `SslProtocols.Tls12`; no revocation checks.
- The server requires a client certificate; the client supplies its identity.

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~DeviceIdentityTests|FullyQualifiedName~TlsLoopbackTests"
```
Expected: PASS. If `EphemeralKeySet` is rejected, the warning is logged and the identity still loads; record the result in the task notes.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat: add pinned device identity and TLS helpers"
```

---

### Task 3: Frame protocol and JSON messages

**Files:**
- Create: `src/LanBridge/Core/Protocol.cs`, `src/LanBridge/Core/ProtocolJson.cs`, `src/LanBridge/Core/FrameCodec.cs`
- Create: `tests/LanBridge.Tests/FrameCodecTests.cs`, `tests/LanBridge.Tests/ProtocolJsonTests.cs`

**Interfaces:**
- Produces:
  - `enum FrameType : byte` with exact values: `Hello=1, HelloAck=2, Ping=3, Pong=4, PairPrompt=5, PairProof=6, PairAbort=7, PairOk=8, PairDeny=9, Offer=10, Accept=11, Reject=12, FileBegin=13, FileData=14, FileEnd=15, FileOk=16, FileError=17, Cancel=18, Done=19, DoneAck=20, Error=21`
  - `FrameCodec.WriteAsync(Stream, FrameType, byte[], CancellationToken)`
  - `FrameCodec.ReadAsync(Stream, CancellationToken)` returning `Frame { FrameType Type; byte[] Payload; }`
  - `ProtocolJson.Serialize<T>(T)` / `Deserialize<T>(byte[])`
  - DTOs: `HelloDto`, `HelloAckDto`, `PairPromptDto`, `PairProofDto`, `PairDenyDto`, `OfferDto`, `OfferFileDto`, `AcceptDto`, `RejectDto`, `FileBeginDto`, `FileEndDto`, `FileErrorDto`, `ErrorDto`, `CancelDto`, `DoneDto`

- [ ] **Step 1: Write the failing frame tests**

Assertions:
- `WriteAsync` for `Hello` with payload `{"v":1}` produces bytes `00 00 00 07 01 7B 22 76 22 3A 31 7D` (length 7, type 1, payload).
- A stream containing a 5-byte header and payload reads back the same type/payload.
- A declared length of `1_048_577` throws `ProtocolException` without allocating.
- A truncated payload throws `EndOfStreamException`/`ProtocolException`.
- An unknown frame type 99 throws `ProtocolException`.
- A `FileData` payload larger than `65_540` throws `ProtocolException`.

`ProtocolJsonTests`:
- Round-trip `HelloDto` with `V=1, Id="abc", Name="Rohan-PC", Fp=<64 hex>, Nonce=<base64>, Intent="pair", Paired=false`.
- Round-trip `OfferDto` with two files and `TotalSize`.
- `ErrorDto` carries `Code`, `FileIndex`, `Completed`; unknown JSON fields are ignored.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~FrameCodecTests|FullyQualifiedName~ProtocolJsonTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement framing and DTOs**

Frame header: 4-byte big-endian **payload length**, 1-byte type, then payload. Read the 4-byte length first, reject `> 1_048_576`; read the type; reject unknown; if type is `FileData`, reject payload `> 65_540`. Write the header and payload with a single buffered write; use `Stream.WriteAsync` and `ReadExactlyAsync`.

Use `DataContractJsonSerializer` with `[DataContract]`/`[DataMember(Name="...")]`; wire names are lowercase as specified. `ProtocolJson` must enforce a 1 MiB maximum and throw `ProtocolException` on invalid JSON.

`ProtocolException` derives from `Exception` and carries a machine-readable `Code` string.

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~FrameCodecTests|FullyQualifiedName~ProtocolJsonTests"
```
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat: add length-prefixed protocol framing and JSON messages"
```

---

### Task 4: Pairing code and proof

**Files:**
- Create: `src/LanBridge/Core/PairingCode.cs`
- Create: `tests/LanBridge.Tests/PairingCodeTests.cs`

**Interfaces:**
- Produces:
  - `PairingCode.Compute(string fpInitiator, string fpResponder, byte[] nonceInitiator, byte[] nonceResponder)`
  - `PairingCode.ComputeProof(string code, byte[] nonceInitiator, byte[] nonceResponder, string fpInitiator, string fpResponder)`
  - `PairingCode.VerifyProof(...)`
  - `PairingCode.FixedTimeEquals(byte[] a, byte[] b)`

- [ ] **Step 1: Write the failing known-vector test**

```csharp
[Fact]
public void KnownVector_MatchesReference()
{
    var fpI = new string('a', 64);
    var fpR = new string('b', 64);
    var nonceI = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
    var nonceR = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();

    var code = PairingCode.Compute(fpI, fpR, nonceI, nonceR);
    Assert.Equal("407104", code);

    var proof = PairingCode.ComputeProof(code, nonceI, nonceR, fpI, fpR);
    Assert.Equal("Ihd2+Z1iLAvvBEqpDJtz8oI1ICLUIQkyyQTIU5HbTeE=", Convert.ToBase64String(proof));
    Assert.True(PairingCode.VerifyProof(code, nonceI, nonceR, fpI, fpR, proof));
    Assert.False(PairingCode.VerifyProof(code, nonceI, nonceR, fpI, fpR, new byte[32]));
}
```

Additional tests: code is always six digits with leading zeros; different nonces produce a different code; different fingerprints produce a different code; `FixedTimeEquals` handles different lengths and equal arrays.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~PairingCodeTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement the code and proof**

- `key = SHA256(UTF8("lanbridge-pair-v1") || UTF8(fpLow) || UTF8(fpHigh))` with `fpLow`/`fpHigh` sorted ordinal.
- `sas = HMACSHA256(key, nonceInitiator || nonceResponder)`.
- `code = (uint)bigEndian(sas[0..4]) % 1_000_000`, formatted `D6`.
- Proof key: `SHA256(UTF8("lanbridge-pair-key-v1") || UTF8(code))`.
- Proof data: `UTF8("confirm") || nonceInitiator || nonceResponder || UTF8(fpInitiator) || UTF8(fpResponder)`.
- `VerifyProof` recomputes and uses a constant-time comparison; return false on null/incorrect lengths.

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~PairingCodeTests"
```
Expected: PASS with code `407104`.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat: add SAS pairing code and proof"
```

---

### Task 5: File-name safety, sanitization, and atomic receiving

**Files:**
- Create: `src/LanBridge/Core/PathGuard.cs`, `src/LanBridge/Core/Sanitizer.cs`, `src/LanBridge/Core/FileReceiver.cs`
- Create: `tests/LanBridge.Tests/PathGuardTests.cs`, `tests/LanBridge.Tests/SanitizerTests.cs`, `tests/LanBridge.Tests/FileReceiverTests.cs`

**Interfaces:**
- Produces:
  - `PathGuard.SanitizeFileName(string remoteName)`
  - `PathGuard.GetUniquePath(string folder, string fileName, int startIndex = 0)`
  - `Sanitizer.SanitizeDeviceName(string name)`
  - `Sanitizer.SanitizeLogValue(string value, int maxLength = 200)`
  - `Sanitizer.MapError(string code)`
  - `FileReceiver(string receiveFolder, string remoteName, long expectedSize)`, `.Write(byte[], int, int)`, `.Complete(string expectedSha256Hex)`, `.Abort()`, `.PartPath`

- [ ] **Step 1: Write the failing path and sanitizer tests**

`PathGuardTests` cases:
- `SanitizeFileName(@"..\..\Windows\System32\evil.dll")` returns `evil.dll`.
- `SanitizeFileName(@"C:\Windows\evil.txt")` returns `evil.txt`.
- `SanitizeFileName("CON")`, `"aux.txt"`, `"COM1.log"`, `"LPT9"` are prefixed with `_`.
- `SanitizeFileName("name.")`, `"name   "` trim trailing dots/spaces.
- `SanitizeFileName("a:b.txt")` removes the colon.
- `SanitizeFileName("bad\u0001name.txt")` removes the control character.
- `SanitizeFileName("")`, `"."`, `".."` return `file`.
- A 300-character name is at most 180 characters and keeps its extension.
- `GetUniquePath(folder, "a.txt")` returns `a.txt`, then `a (1).txt`, then `a (2).txt` when files exist.

`SanitizerTests`:
- Device names strip CR/LF/control/format characters, trim, max 64, fallback `Windows PC`.
- `SanitizeLogValue("a\r\nb", 10)` returns `a b` or `ab` with no newline and at most 10 chars.
- `MapError("hash_mismatch")` returns a human message; `MapError("unknown_remote_text")` returns a generic message and never echoes the input.

`FileReceiverTests`:
- Write bytes, complete with correct SHA-256 → final file exists inside the receive folder with exact contents; `.part` is gone.
- Complete with wrong hash → throws `InvalidDataException`, `.part` is gone, no final file.
- Wrong byte count → throws, `.part` gone.
- Duplicate name → final name is `name (1).ext`.
- Traversal name → final file is inside the receive folder.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~PathGuardTests|FullyQualifiedName~SanitizerTests|FullyQualifiedName~FileReceiverTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement the guards and receiver**

- `PathGuard.SanitizeFileName`: replace `/` with `\`, take `Path.GetFileName`, remove invalid Windows characters and Unicode control/format characters, trim trailing dots/spaces, prefix reserved device names, limit to 180 characters preserving the extension, fallback to `file`.
- `PathGuard.GetUniquePath`: use `Path.Combine(folder, name)`, append ` (n)` before the extension, and loop until a non-existing path is found; start at `startIndex`; throw `IOException` after 10,000 attempts.
- `Sanitizer`: implement the exact rules above; `MapError` uses a fixed dictionary and a generic fallback.
- `FileReceiver`: constructor sanitizes the name, ensures the folder exists, creates a unique `.part` path, opens a `FileStream` with `FileOptions.SequentialScan`, and creates a `SHA256` instance. `Write` writes to the file and hashes the exact bytes. `Complete` verifies the byte count and hash, flushes/closes, then moves to a unique final path with retry on `IOException`. `Abort` closes and deletes the `.part`. `Dispose` calls `Abort` if not completed.

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~PathGuardTests|FullyQualifiedName~SanitizerTests|FullyQualifiedName~FileReceiverTests"
```
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat: add safe file naming and atomic receiver"
```

---

### Task 6: Peer store and LAN scope

**Files:**
- Create: `src/LanBridge/Core/PeerStore.cs`, `src/LanBridge/Core/LanScope.cs`
- Create: `tests/LanBridge.Tests/PeerStoreTests.cs`, `tests/LanBridge.Tests/LanScopeTests.cs`

**Interfaces:**
- Produces:
  - `PeerRecord` with `Fingerprint`, `DeviceId`, `Name`, `LastIp`, `LastPort`, `PairedAtUtc`, `LastSeenUtc`
  - `PeerStore.Load(string path, ILogger log)`, `.Peers`, `.ContainsFingerprint(string)`, `.FindByFingerprint(string)`, `.FindByDeviceId(string)`, `.Upsert(PeerRecord)`, `.UpdateEndpoint(string fp, string ip, int port)`, `.Remove(string fp)`, `.Save()`
  - `LanInterface` with `Name`, `Address`, `Mask`, `Broadcast`
  - `LanScope(IEnumerable<LanInterface>)`, `LanScope.Capture()`, `.Interfaces`, `.IsEligiblePeer(IPAddress)`
  - `LanScope.IsVirtualInterface(string description, NetworkInterfaceType type)`

- [ ] **Step 1: Write the failing peer-store tests**

Assertions:
- Save then load preserves a record and its fields.
- `Upsert` replaces the record with the same fingerprint.
- Two records with the same `DeviceId` but different fingerprints both survive (identity-change case); `FindByDeviceId` returns both.
- `UpdateEndpoint` changes `LastIp`/`LastPort`/`LastSeenUtc` only.
- `Remove` deletes by fingerprint.
- A corrupt `peers.json` is renamed to `peers.corrupt-<timestamp>.json` and loads as empty.
- `ContainsFingerprint` uses ordinal comparison and is false for null/empty.

- [ ] **Step 2: Write the failing LAN-scope tests**

```csharp
[Fact]
public void Only_same_physical_subnet_is_eligible()
{
    var scope = new LanScope(new[]
    {
        new LanInterface("Wi-Fi", IPAddress.Parse("192.168.10.5"), IPAddress.Parse("255.255.255.0"), IPAddress.Parse("192.168.10.255"))
    });

    Assert.True(scope.IsEligiblePeer(IPAddress.Parse("192.168.10.99")));
    Assert.False(scope.IsEligiblePeer(IPAddress.Parse("192.168.11.5")));
    Assert.False(scope.IsEligiblePeer(IPAddress.Parse("8.8.8.8")));
    Assert.False(scope.IsEligiblePeer(IPAddress.Parse("172.20.128.10")));
}
```

`IsVirtualInterface` cases:
- `"Hyper-V Virtual Ethernet Adapter"` + `Ethernet` → true
- `"Bluetooth Network Connection"` + `Ethernet` → true
- `"Tailscale Tunnel"` + `Ethernet` → true
- `"Intel(R) Wi-Fi 6E AX211"` + `Wireless80211` → false
- `"Realtek PCIe GbE Family Controller"` + `Ethernet` → false

- [ ] **Step 3: Run the tests to verify they fail**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~PeerStoreTests|FullyQualifiedName~LanScopeTests"
```
Expected: FAIL.

- [ ] **Step 4: Implement `PeerStore` and `LanScope`**

`PeerStore`:
- JSON via `DataContractJsonSerializer`; atomic save (`.tmp` + `File.Replace`/`File.Move`).
- On corrupt load: rename to `peers.corrupt-<utc timestamp>.json`, log, return empty.
- Key by full fingerprint. Keep all records; the device-ID lookup is a filter, not a merge.
- `Save` is idempotent and thread-safe under a private lock.

`LanScope`:
- `Capture()`: enumerate `NetworkInterface.GetAllNetworkInterfaces()`; require `OperationalStatus.Up`, type `Wireless80211` or `Ethernet`, not `IsVirtualInterface`, IPv4 unicast address in `10/8`, `172.16/12`, or `192.168/16`, a non-empty mask, and at least one gateway. Compute the broadcast address from address + mask.
- `IsEligiblePeer`: IPv4 only; same subnet as any captured interface; reject loopback (the server has an explicit test-only override).
- `IsVirtualInterface`: case-insensitive description contains `Hyper-V`, `Virtual`, `VPN`, `TAP`, `Tailscale`, `WireGuard`, `Bluetooth`, `Loopback`, or `Pseudo`; or type is `Tunnel`, `Ppp`, `Loopback`, or `Bluetooth`.

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~PeerStoreTests|FullyQualifiedName~LanScopeTests"
```
Expected: PASS. On this laptop, `LanScope.Capture()` should contain `192.168.10.5/24` and not `172.20.128.1`.

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "feat: add peer store and physical LAN scope"
```

---

### Task 7: Transfer models, TLS transport, and PING

**Files:**
- Create: `src/LanBridge/Core/TransferModels.cs`, `src/LanBridge/Core/TransferOptions.cs`, `src/LanBridge/Core/UiInterfaces.cs`
- Create: `src/LanBridge/Core/TransferServer.cs`, `src/LanBridge/Core/TransferClient.cs`
- Create: `tests/LanBridge.Tests/TransportTests.cs`
- Create: `tests/LanBridge.Tests/TestSupport/FakePrompter.cs`, `tests/LanBridge.Tests/TestSupport/TestData.cs`

**Interfaces:**
- Produces:
  - `PeerEndpoint(IPAddress Address, int Port, string Fingerprint, string DeviceId, string Name)`
  - `PairingRequest`, `PairingCodeDisplay`, `PairingCodePrompt`, `TransferFile`, `TransferOffer`, `TransferProgress` (`IsComplete`, `Failed`), `TransferResult`, `PairingAttemptResult`
  - `ITransferPrompter`: `Task<bool> RequestPairingApprovalAsync(PairingRequest, CancellationToken)`, `void ShowPairingCode(PairingCodeDisplay)`, `void ClosePairingCode()`, `Task<bool> ApproveOfferAsync(TransferOffer, CancellationToken)`, `IProgress<TransferProgress> CreateReceiveProgress(TransferOffer, CancellationToken)`
  - `IClientPrompter`: `Task<string> RequestPairingCodeAsync(PairingCodePrompt, CancellationToken)`, `IProgress<TransferProgress> CreateSendProgress(TransferOffer, CancellationToken)`
  - `TransferOptions` with the spec timeouts; defaults 120 s pairing approval, 120 s code, 30 s control, 60 s file idle, 120 s offer, 5 s connect.
  - `TransferServer(int port, string deviceName, DeviceIdentity identity, PeerStore peers, Func<string> receiveFolder, ITransferPrompter prompter, ILogger log, TransferOptions options = null, bool allowLoopback = false, Func<LanScope> scopeFactory = null)`; `.Start()`, `.Stop()`, `.Discoverable`, `.IsReceiving`, event `DevicePaired`
  - `TransferClient(string deviceName, DeviceIdentity identity, PeerStore peers, IClientPrompter prompter, ILogger log, TransferOptions options = null)`; `Task<bool> PingAsync(PeerEndpoint, CancellationToken)`, `Task<PairingAttemptResult> PairAsync(PeerEndpoint, CancellationToken)`, `Task<TransferResult> SendAsync(PeerEndpoint, IReadOnlyList<string>, CancellationToken)`

- [ ] **Step 1: Write the failing transport tests**

`TransportTests`:
- `Ping_Succeeds`: start a server on a free loopback port; client `PingAsync` with the server fingerprint; assert true.
- `Ping_WrongFingerprint_Fails`: client expects 64 zeros; assert false or `AuthenticationException` is handled as a failed result.
- `Hello_RejectsPublicPeer`: server with a `LanScope` that contains only `10.0.0.0/8` and `allowLoopback=false`; client on 127.0.0.1; assert the session is rejected.
- `Hello_VersionMismatch_ReturnsVersionError`: a raw test client sends `HelloDto { V = 99 }` over TLS/frames; read an `ErrorDto` with `Code == "version"`.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~TransportTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement models and the transport skeleton**

- `TransferServer` binds `TcpListener(IPAddress.Any, port)`, accepts on a background task, and handles each client in a task. On stop, cancel the listener and active sessions.
- Server handshake: TLS server with `DeviceIdentity.Certificate`; require client cert; read `HelloDto`; verify `V == 1`, `hello.Fp ==` TLS client cert fingerprint, remote address eligible (or loopback allowed), and `Intent` in `ping/pair/send`; send `HelloAckDto` with the server's device ID, name, fingerprint, fresh nonce, `Paired = peers.ContainsFingerprint(clientFp)`, and `Busy = IsReceiving`. For `intent=ping`, read `PING`, send `PONG`, close. For pair/send in this task, send `ERROR not_implemented` and close.
- Client handshake: connect with a 5 s timeout, TLS client with pin, send `HelloDto` with a fresh nonce, read `HelloAckDto`, verify `V`, `Fp` equals the peer, `Intent` matches. For `PingAsync`, send `PING`, read `PONG`, return true; close.
- `FakePrompter` for tests: approves pairing and offers by default, records calls, and exposes a `PairingCodeRelay` for later tasks.
- `TestData`: free-port helper, identity factory, temp file writer, `CollectingProgress`.

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~TransportTests"
```
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat: add mutual-TLS transport and ping"
```

---

### Task 8: Pairing flow

**Files:**
- Modify: `src/LanBridge/Core/TransferServer.cs`, `src/LanBridge/Core/TransferClient.cs`, `src/LanBridge/Core/TransferModels.cs`, `src/LanBridge/Core/Protocol.cs`
- Create: `tests/LanBridge.Tests/PairingIntegrationTests.cs`
- Modify: `tests/LanBridge.Tests/TestSupport/FakePrompter.cs`

**Interfaces:**
- Consumes: Task 4 `PairingCode`, Task 6 `PeerStore`, Task 7 transport.
- Produces:
  - `TransferServer.DevicePaired` event
  - `TransferClient.PairAsync` returning `PairingAttemptResult { bool Success, string ErrorCode, PeerRecord Peer }`
  - `TransferServer` handles `intent=pair`; `PairingDenyDto` reason values `denied`, `timeout`, `hidden`, `busy`, `invalid`

- [ ] **Step 1: Write the failing pairing tests**

`PairingIntegrationTests`:
- `Pair_HappyPath`: fake server prompter approves; the fake's `ShowPairingCode` stores the code in a relay; the fake client prompt returns that code; assert both peer stores contain the other fingerprint, both records have the correct device IDs/names, and the result is success.
- `Pair_WrongCode_ThreeAttempts_NoPairing`: client prompt returns `"000000"` three times; assert failure, both stores empty, and the server's pairing dialog was closed.
- `Pair_Denied_NoPairing`: server prompter returns false; assert failure with `denied`.
- `Pair_ApprovalTimeout`: options approval timeout 100 ms; server prompter never completes; assert failure with `timeout` and the dialog closed.
- `Pair_CodeTimeout`: options code timeout 100 ms; client never submits a code; assert failure with `timeout`.
- `Pair_Hidden_UnpairedRejected`: server `Discoverable=false`; assert failure with `hidden`.
- `Pair_IdentityChanged_KeepsOldPin`: server store has an old record with device ID `d1` and fingerprint `f1`; a new client identity with the same device ID pairs; assert both `f1` and the new fingerprint remain, and `FindByDeviceId("d1")` returns two records.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~PairingIntegrationTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement the server pairing state machine**

In the server's `intent=pair` branch (the common `HELLO_ACK` from Task 7 is already sent and carries the server nonce and paired flag):
1. If `!Discoverable && !peers.ContainsFingerprint(clientFp)`, send `PAIR_DENY hidden` and close.
2. Try to acquire the single pairing lock without waiting; if unavailable, send `PAIR_DENY busy`.
3. Call `prompter.RequestPairingApprovalAsync` with `PairApprovalTimeout`. On false/timeout, send `PAIR_DENY` and close.
4. Compute `code = PairingCode.Compute(clientFp, serverFp, clientNonce, serverNonce)`.
5. Send `PAIR_PROMPT { expiresSeconds = 120 }`; call `prompter.ShowPairingCode` with the code, peer name/IP, and expiry.
6. Read `PAIR_PROOF` with `PairCodeTimeout`; verify with `PairingCode.VerifyProof`. On failure, send `PAIR_DENY invalid` and close.
7. Upsert a `PeerRecord` (client fp, device ID, name, remote IP, port, `PairedAtUtc = now`, `LastSeenUtc = now`), save, send `PAIR_OK`, raise `DevicePaired`, close.
8. In `finally`, call `prompter.ClosePairingCode()` and release the pairing lock.

- [ ] **Step 4: Implement the client pairing state machine**

1. Connect/TLS/HELLO with `intent=pair` and `Paired = peers.ContainsFingerprint(serverFp)`.
2. Read `HELLO_ACK`; verify version/fingerprint. If both sides report paired, upsert/refresh the peer and return success.
3. Wait for `PAIR_PROMPT` or `PAIR_DENY` with `PairApprovalTimeout`.
4. Compute the local code. For attempts 1..3, call `prompter.RequestPairingCodeAsync(new PairingCodePrompt(peer, attempt, previousFailed), ct)` with a token linked to `PairCodeTimeout`; on cancellation/expiry, send `PAIR_ABORT` and return `timeout`; close the form.
5. If the typed code (after trimming spaces) equals the local code, send `PAIR_PROOF` with the base64 proof and wait for `PAIR_OK`/`PAIR_DENY` with `PairCodeTimeout`.
6. On `PAIR_OK`, upsert the server peer record, save, return success. On deny, return the reason. On three wrong codes, send `PAIR_ABORT`, return `wrong_code`; never send the typed code.
7. Do not auto-delete or auto-trust a one-sided pairing; the user's explicit Pair action is the consent.

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~PairingIntegrationTests"
```
Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "feat: add mutual pairing with SAS approval"
```

---

### Task 9: File transfer

**Files:**
- Modify: `src/LanBridge/Core/TransferServer.cs`, `src/LanBridge/Core/TransferClient.cs`, `src/LanBridge/Core/TransferModels.cs`, `src/LanBridge/Core/Protocol.cs`
- Create: `tests/LanBridge.Tests/TransferIntegrationTests.cs`
- Modify: `tests/LanBridge.Tests/TestSupport/FakePrompter.cs`

**Interfaces:**
- Consumes: Task 5 `FileReceiver`, Task 6 `PeerStore`, Task 8 pairing.
- Produces:
  - `TransferClient.SendAsync` returning `TransferResult { bool Success, int FilesCompleted, int FilesTotal, string ErrorCode }`
  - `TransferServer.IsReceiving`
  - Server `receiveFolder` factory; optional `Func<string, string, long, FileReceiver>` receiver factory for fault-injection tests

- [ ] **Step 1: Write the failing transfer tests**

- `Send_TwoFiles_WritesThem`: pair, send two files, assert names/contents/hashes in the receive folder.
- `Send_DuplicateName_GetsSuffix`: pre-create `a.txt`; send `a.txt`; assert `a (1).txt`.
- `Send_Unpaired_Rejected`: no pairing; assert `not_paired` and no file.
- `Send_OneSidedPairing_Rejected`: pair, clear one peer store, then send; assert `not_paired`, no auto-trust, and no auto-delete of the remaining pin.
- `Send_Declined_NoFile`: server prompter declines; assert `offer_rejected`.
- `Send_BusyInbound`: first offer is held pending by the fake prompter; second client sends; assert `busy_inbound`.
- `Send_Cancel_DeletesPart`: cancel mid-transfer; assert no `.part`, no final file, completed files kept if any.
- `Send_FileError_AbortsAndKeepsCompleted`: inject a receiver factory that throws on file 2; assert file 1 exists, file 2 `.part` is gone, result `transfer_failed` with `FilesCompleted == 1`.
- `Send_And_Receive_AreIndependent`: start an inbound transfer held at the offer prompt; a separate client sends to a second server while the first is receiving; assert both succeed. This proves the app-level concurrency rule.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~TransferIntegrationTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement the server transfer state machine**

1. For `intent=send`: reject unless the client is pinned in `PeerStore` and both `HELLO.paired` and `HELLO_ACK.paired` are true. Send `ERROR not_paired`.
2. Acquire the inbound semaphore with zero wait; if busy, send `ERROR busy_inbound`.
3. Read `OFFER` with `OfferTimeout`; validate 1–5,000 files, non-negative sizes, total overflow-safe, and free space `total + 50 MiB`.
4. Call `prompter.ApproveOfferAsync` with `OfferTimeout`; on false, send `REJECT` and close.
5. Send `ACCEPT`; for each file read `FILE_BEGIN`, create `FileReceiver` (or the injected factory), stream `FILE_DATA` until `FILE_END`, enforce the declared size, verify the SHA-256, commit to a unique final name, send `FILE_OK`, and report progress.
6. On any file error, abort the current receiver, send `FILE_ERROR` and `ERROR transfer_failed` with `fileIndex` and `completed`, keep prior files, and close.
7. Read `DONE`, send `DONE_ACK`, report `IsComplete=true`, close; release the semaphore in `finally`.

- [ ] **Step 4: Implement the client transfer state machine**

1. Connect/TLS/HELLO with `intent=send` and `Paired = peers.ContainsFingerprint(serverFp)`.
2. If either side is not paired, return `not_paired` without sending an offer.
3. Build an `OfferDto` from `Path.GetFileName` and `FileInfo.Length`; send `OFFER`; read `ACCEPT`/`REJECT`; on reject return `offer_rejected` with the reason.
4. For each file: send `FILE_BEGIN`, stream 64 KiB `FILE_DATA` chunks while hashing, send `FILE_END`, read `FILE_OK`/`FILE_ERROR`; on error return `transfer_failed`.
5. Send `DONE`, read `DONE_ACK`, report complete, return success.
6. On cancellation, send `CANCEL` best-effort, close, return `cancelled`; never leave a `.part` on the receiver.

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~TransferIntegrationTests"
```
Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "feat: add paired file transfer with cancel and hash verification"
```

---

### Task 10: Discovery

**Files:**
- Create: `src/LanBridge/Core/DiscoveryService.cs`
- Create: `tests/LanBridge.Tests/DiscoveryTests.cs`

**Interfaces:**
- Produces:
  - `DiscoveryService(int port, string deviceName, DeviceIdentity identity, PeerStore peers, Func<PeerEndpoint, CancellationToken, Task<bool>> confirm, ILogger log, bool allowLoopback = false)`
  - `.Start()`, `.Stop()`, `.Discoverable`, `.Refresh()`, `.Devices`, events `DeviceConfirmed`, `DeviceLost`
  - `DiscoveredDevice { DeviceId, Name, Fingerprint, IPAddress, Port, LastSeenUtc }`

- [ ] **Step 1: Write the failing discovery tests**

- `Announcement_RoundTrips`: serialize/parse a discovery packet with all fields; assert the 1,400-byte cap and `LNB1` magic.
- `MalformedPacket_IsIgnored`: send random bytes to the service; assert no event.
- `ConfirmedDevice_IsRaised`: bind the service to a free UDP port; send a unicast announcement from a local `UdpClient`; the fake confirm returns true; assert `DeviceConfirmed` fires with the right fingerprint.
- `ConfirmFalse_DoesNotRaise`: fake confirm returns false; assert no event.
- `SelfAnnouncement_IsIgnored`: send the service's own device ID/fingerprint; assert no event.
- `DiscoverableFalse_SuppressesAnnouncements`: start with `Discoverable=false`, capture outbound packets on a loopback socket, assert none; set true, `Refresh()`, assert a query is sent.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~DiscoveryTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement discovery**

- Bind a `UdpClient` to `0.0.0.0:port`; enable broadcast. Send announcements to each `LanScope.Capture().Interfaces` broadcast address and `255.255.255.255`; send an immediate query on start/refresh.
- Packet DTO: `{t, v, id, name, port, fp, discoverable}`; max 1,400 bytes; magic `LNB1`; UTF-8 JSON via `ProtocolJson`.
- On receipt: ignore self by device ID/fingerprint; ignore malformed; reject addresses outside `LanScope` (or loopback if allowed); rate-limit per source IP and fingerprint.
- Confirm with the injected callback at most once per fingerprint per 10 seconds; only raise `DeviceConfirmed` after success. Cache last seen; expire after 15 seconds and raise `DeviceLost`.
- `Discoverable=false`: stop announcements and ignore incoming queries; keep the socket open for paired outbound connections (the TCP listener is separate).
- On `NetworkChange.NetworkAddressChanged` and `SystemEvents.PowerModeChanged` resume, re-capture `LanScope` with a 2-second debounce and restart discovery; do not kill active transfers.
- `Devices` returns a snapshot; all events are raised on background threads and the UI marshals.

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~DiscoveryTests"
```
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat: add confirmed UDP discovery"
```

---

### Task 11: Tray app, device list, and transfer dialogs

**Files:**
- Create: `src/LanBridge/Core/DeviceRow.cs`
- Create: `src/LanBridge/TrayApplicationContext.cs`, `src/LanBridge/WinFormsUi.cs`
- Create: `src/LanBridge/Forms/DeviceListForm.cs`, `PairingForm.cs`, `PairingCodeEntryForm.cs`, `ReceivePromptForm.cs`, `ProgressForm.cs`, `AboutForm.cs`
- Modify: `src/LanBridge/Program.cs`
- Create: `tests/LanBridge.Tests/DeviceRowTests.cs`

**Interfaces:**
- Produces:
  - `enum DeviceState { Paired, Available, Offline, IdentityChanged }`
  - `DeviceRow { Fingerprint, DeviceId, Name, IpAddress, Port, State, LastSeenUtc }`
  - `DeviceRowMapper.Build(IReadOnlyList<PeerRecord> peers, IReadOnlyList<DiscoveredDevice> discovered)`
  - `TrayApplicationContext` owns `TransferServer`, `TransferClient`, `DiscoveryService`, `PeerStore`, `AppConfig`, `WinFormsUi`
  - `WinFormsUi` implements `ITransferPrompter` and `IClientPrompter`; marshals to the WinForms thread

- [ ] **Step 1: Write the failing device-row tests**

`DeviceRowTests`:
- A discovered fingerprint not in the peer store → `Available`.
- A discovered fingerprint in the peer store → `Paired`.
- A paired peer not discovered → `Offline` with `LastSeenUtc` preserved.
- A discovered new fingerprint whose device ID matches a paired peer with a different fingerprint → `IdentityChanged`; the old paired peer remains `Offline`.
- Duplicate discovered rows with the same fingerprint collapse to one, newest wins.
- The output is sorted paired-first, then available, then offline, then identity-changed; within a group by name.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~DeviceRowTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement the device-row mapper**

- Build paired rows from `PeerStore`; build discovered rows from `DiscoveryService.Devices`.
- Key by fingerprint. If a discovered fingerprint matches a peer, mark `Paired` and update name/IP/last seen in the row only.
- If a discovered fingerprint is absent from the store but its `DeviceId` matches a stored peer, mark `IdentityChanged`; keep the old paired row as `Offline`.
- Do not mutate the peer store from the mapper.

- [ ] **Step 4: Implement the tray shell and forms**

- `Program.Main`: load or create the default config; create identity, peer store, logger; start the tray context. Task 12 adds the single-instance mutex and the first-run wizard.
- `TrayApplicationContext`:
  - `NotifyIcon` with `Icon.ExtractAssociatedIcon(Application.ExecutablePath)`; fallback to `SystemIcons.Application`.
  - Menu: `Send files…`, `Devices ▸` (paired devices, separator, `Pair new device…`, `Refresh`), `Visible on this network` (checkable), `Open received folder`, `About LanBridge`, `Quit`. Task 12 adds `Settings…`.
  - Left-click opens `DeviceListForm`.
  - `Send files…` opens a multi-select `OpenFileDialog`, then the device list in send mode with the selected file list.
  - Start `TransferServer`, `DiscoveryService`, and the discovery confirmation delegate (`TransferClient.PingAsync`).
  - Wire `TransferServer.DevicePaired` and `DiscoveryService` events to the UI; refresh the device list on a 1-second `System.Windows.Forms.Timer` while the form is open.
  - Only one outbound `SendAsync` at a time; show a message if already sending. Inbound and outbound may run at once.
- `WinFormsUi`:
  - `RequestPairingApprovalAsync`: show `PairingForm` with name/IP, Approve/Deny; return a `Task<bool>` resolved by the form or cancellation.
  - `ShowPairingCode`/`ClosePairingCode`: transition/close the same `PairingForm`, display `482 913`, the peer name/IP, and a 2:00 countdown.
  - `RequestPairingCodeAsync`: show `PairingCodeEntryForm` with the attempt number and previous-failure message; return the six-character string or null.
  - `ApproveOfferAsync`: show `ReceivePromptForm` with the sender, file names/sizes, total, and Accept/Decline; Decline is the default button.
  - `CreateReceiveProgress`/`CreateSendProgress`: show `ProgressForm`; update at most 10 times per second; expose Cancel; close on `IsComplete` or `Failed`.
- `DeviceListForm`:
  - Header/status, `ListView` with columns Device, State, Address, Last Seen.
  - Primary button: Send files… (Paired), Pair… (Available), Review… (IdentityChanged).
  - Refresh, Settings (wired in Task 12), and Unpair context menu.
  - Send mode: title includes file count/total; available rows offer Pair & send; no devices shows the empty-state help.
- `AboutForm`: product name, version `0.1.0`, MIT, GitHub link, LocalSend cross-platform note.

- [ ] **Step 5: Run the tests and a manual smoke test**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~DeviceRowTests"
dotnet build LanBridge.sln -c Release
```
Expected: PASS and build succeeds. Launch the app from `src\LanBridge\bin\Release\net48\LanBridge.exe`; the first-run form appears, the tray icon is present, and the device list opens.

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "feat: add tray app and transfer dialogs"
```

---

### Task 12: First run, settings, firewall, startup, and single instance

**Files:**
- Create: `src/LanBridge/Core/Firewall.cs`, `src/LanBridge/Core/Startup.cs`, `src/LanBridge/Core/SingleInstance.cs`
- Create: `src/LanBridge/Forms/FirstRunForm.cs`, `src/LanBridge/Forms/SettingsForm.cs`
- Modify: `src/LanBridge/Program.cs`, `src/LanBridge/TrayApplicationContext.cs`, `src/LanBridge/WinFormsUi.cs`
- Create: `tests/LanBridge.Tests/StartupTests.cs`, `tests/LanBridge.Tests/SingleInstanceTests.cs`, `tests/LanBridge.Tests/FirewallTests.cs`

**Interfaces:**
- Produces:
  - `Startup.IsEnabled(string runKeyPath = null)`, `Startup.SetEnabled(bool, string exePath, string runKeyPath = null)`
  - `SingleInstance.TryAcquire(out Mutex mutex, string name = "Local\\LanBridge.SingleInstance")`
  - `Firewall.BuildAddRuleArguments(string exePath)`, `Firewall.BuildDeleteRuleArguments()`, `Firewall.IsRulePresent()`, `Task AddRuleAsync(string exePath)`, `Task RemoveRuleAsync()`

- [ ] **Step 1: Write the failing tests**

- `Startup_SetEnabled_WritesAndRemovesValue`: use a temporary HKCU run-key path such as `Software\LanBridgeTests\<guid>`; set true, assert value; set false, assert removed.
- `SingleInstance_SecondAcquire_Fails`: acquire one; a second `TryAcquire` returns false; dispose the first; a third succeeds.
- `Firewall_Arguments_AreScopedAndQuoted`: assert the add arguments contain the quoted exe path, `profile=private,public`, and `remoteip=LocalSubnet`; delete contains the rule name `LanBridge`.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~StartupTests|FullyQualifiedName~SingleInstanceTests|FullyQualifiedName~FirewallTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement startup, single instance, and firewall**

- `Startup`: HKCU `Software\Microsoft\Windows\CurrentVersion\Run`; value `LanBridge` = `"<exe>" --tray`; create/delete; no admin. Keep the run-key path injectable for tests.
- `SingleInstance`: named mutex held for the process lifetime; second instance exits quietly when launched with `--tray`, otherwise shows a short message.
- `Firewall`:
  - Add: `netsh advfirewall firewall add rule name="LanBridge" dir=in action=allow program="<exe>" enable=yes profile=private,public remoteip=LocalSubnet`.
  - Delete: `netsh advfirewall firewall delete rule name="LanBridge"`.
  - Check: `netsh advfirewall firewall show rule name="LanBridge"` and inspect the exit code/output.
  - Add/delete run elevated with `ProcessStartInfo { UseShellExecute = true, Verb = "runas" }`; catch `Win32Exception` code 1223 (UAC cancelled) and return a friendly failure.

- [ ] **Step 4: Build the first-run and settings UI**

- `FirstRunForm`: device name (machine name), receive folder (default, Browse), **Start with Windows** checked, **Allow through Windows Firewall** checked. Continue saves config, sets startup if checked, calls `Firewall.AddRuleAsync`, and shows a non-blocking warning if declined. Cancel exits.
- `SettingsForm`: edit name/folder, start with Windows, visible, auto-accept, Repair firewall rule (UAC), Open logs, About.
- `Program.Main`: check `File.Exists(configPath)` before loading; if missing, show `FirstRunForm` and use its result; otherwise load the config and start the tray. The tray context uses `config.ReceiveFolder` and `config.Discoverable` live.
- `TrayApplicationContext`: on `Visible` toggle, save config and update `DiscoveryService.Discoverable`; on Settings save, refresh discovery name/folder.

- [ ] **Step 5: Run the tests and manual smoke test**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~StartupTests|FullyQualifiedName~SingleInstanceTests|FullyQualifiedName~FirewallTests"
dotnet build LanBridge.sln -c Release
```
Expected: PASS. First run writes `%APPDATA%\LanBridge\config.json`; UAC firewall prompt appears; the rule is visible in `wf.msc`; second launch exits.

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "feat: add first-run setup, firewall, and startup"
```

---

### Task 13: Hardening and edge-case verification

**Files:**
- Modify: any core files as needed
- Modify: `tests/LanBridge.Tests/PairingIntegrationTests.cs`, `TransferIntegrationTests.cs`, `PairingCodeTests.cs`, `DeviceIdentityTests.cs`
- Create: `tests/LanBridge.Tests/EndToEndTests.cs`

**Interfaces:**
- No new public API; this task proves the existing interfaces under the Review Focus conditions.

- [ ] **Step 1: Add the missing failure-mode tests**

`EndToEndTests`:
- `Pair_Disconnect_WhileApprovalPending_ClosesDialog`: start pairing, close the client socket before approval; assert the server's `ClosePairingCode` was called and the pairing lock is free.
- `Pair_OneSided_RepairPrompt`: one store reset; assert the result is `not_paired`/re-pair, and neither store is mutated automatically.
- `Send_HiddenPaired_LastEndpoint_Works`: pair, set server hidden, send via the saved endpoint; assert success.
- `Send_HiddenUnpaired_Rejected`: hidden server, unpaired client; assert `hidden`/`not_paired` and no file.
- `Send_VpnSubnet_Rejected`: fake `LanScope` with a different subnet; assert rejection before any offer.
- `Send_ReceiverError_AbortsAndKeepsCompleted`: force a receiver error on file 2; assert file 1 exists, file 2 `.part` is gone, and the result reports one completed.
- `Send_ConcurrentInboundAndOutbound`: hold an inbound offer pending; send outbound to a second server; both complete.
- `Sanitizer_LogInjection`: device name `"Rohan\r\nFAKE LOG"` is sanitized and never produces a raw newline in a log line.
- `PairingCode_AttemptsResetPerSession`: three wrong attempts in session 1; a new session starts with three.

- [ ] **Step 2: Run the new tests and fix failures**

```powershell
dotnet test tests/LanBridge.Tests/LanBridge.Tests.csproj -c Release --filter "FullyQualifiedName~EndToEndTests"
```
Expected: PASS. If a failure reveals a state-machine bug, fix it and re-run the full suite.

- [ ] **Step 3: Run the full suite**

```powershell
dotnet test LanBridge.sln -c Release
```
Expected: all tests pass.

- [ ] **Step 4: Commit**

```powershell
git add .
git commit -m "test: harden pairing, transfer, and network edge cases"
```

---

### Task 14: Build scripts, installers, CI, and GitHub templates

**Files:**
- Create: `build.ps1`, `install.ps1`, `uninstall.ps1`
- Create: `.github/workflows/build.yml`, `.github/workflows/release.yml`
- Create: `.github/ISSUE_TEMPLATE/bug_report.yml`, `feature_request.yml`, `.github/PULL_REQUEST_TEMPLATE.md`
- Create: `LICENSE`, `CONTRIBUTING.md`, `CHANGELOG.md`, `CODE_OF_CONDUCT.md`
- Modify: `src/LanBridge/LanBridge.csproj` (version/icon when assets exist)

**Interfaces:**
- Produces:
  - `build.ps1` → `artifacts/LanBridge.exe`, `README.md`, `LICENSE`, `install.ps1`, `uninstall.ps1`, `lanbridge-v0.1.0-win.zip`, `SHA256SUMS.txt`
  - `install.ps1 -InstallDir <path> -SourceDir <path> -NoShortcut -SkipLaunch` for tests
  - GitHub release assets matching the spec

- [ ] **Step 1: Write `build.ps1`**

Commands: clean `artifacts`, `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release`, copy `src\LanBridge\bin\Release\net48\LanBridge.exe`, copy README/LICENSE/install/uninstall (README only if it exists at this point; Task 15 adds it before the release), zip, and write `SHA256SUMS.txt` with `Get-FileHash -Algorithm SHA256`. Fail on any non-zero exit code (`$ErrorActionPreference = "Stop"`).

- [ ] **Step 2: Write install/uninstall scripts**

`install.ps1`:
- `[CmdletBinding()] param([string]$InstallDir = "$env:LOCALAPPDATA\Programs\LanBridge", [switch]$NoShortcut, [switch]$SkipLaunch)`.
- Download `LanBridge.exe` and `SHA256SUMS.txt` from `https://github.com/SMR-2009/lanbridge/releases/latest/download/` into a temp dir. When `-SourceDir` is supplied, copy and verify from that local directory instead.
- Verify the hash; abort on mismatch.
- Copy to `$InstallDir`, create a Start Menu shortcut with `WScript.Shell`, optionally `Start-Process` the exe. No admin.
- `uninstall.ps1`: stop `LanBridge`; remove `$InstallDir`, Start Menu shortcut, HKCU Run value; remove the firewall rule via elevated `netsh` (UAC); keep `%APPDATA%\LanBridge` and received files unless `-RemoveData`.

- [ ] **Step 3: Write GitHub Actions and community files**

`build.yml`:
```yaml
name: build
on:
  push:
  pull_request:
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: 8.0.x }
      - run: dotnet restore LanBridge.sln
      - run: dotnet build LanBridge.sln -c Release --no-restore
      - run: dotnet test LanBridge.sln -c Release --no-build
```

`release.yml`: trigger on tags `v*`; `permissions: contents: write`; run `build.ps1`; upload `LanBridge.exe`, zip, `install.ps1`, `uninstall.ps1`, `SHA256SUMS.txt` with `softprops/action-gh-release@v2`.

Community files:
- MIT `LICENSE` with `Copyright (c) 2026 R. Murtaza`.
- `CONTRIBUTING.md`: build/test commands, style, security reporting, no telemetry.
- `CHANGELOG.md`: Keep a Changelog; `v0.1.0` entry.
- `CODE_OF_CONDUCT.md`: Contributor Covenant 2.1 link or short text.
- Issue forms: bug report asks Windows version, network profile, steps, logs; feature request asks problem/use case.
- PR template: what/why/tests/manual checklist.
- `.gitignore`: `bin/`, `obj/`, `.vs/`, `artifacts/`, `*.user`, `*.pfx`, `*.dpapi`, `config.json`, `peers.json`, `screenshots/` only if not committed.

- [ ] **Step 4: Run the build and install-script tests**

```powershell
.\build.ps1
Test-Path artifacts\LanBridge.exe
Get-Content artifacts\SHA256SUMS.txt
.\install.ps1 -InstallDir "$env:TEMP\lanbridge-install-test" -SourceDir artifacts -NoShortcut -SkipLaunch
Test-Path "$env:TEMP\lanbridge-install-test\LanBridge.exe"
Remove-Item "$env:TEMP\lanbridge-install-test" -Recurse -Force
```
Expected: build/test pass; artifacts exist; hash file has one entry; install copies the exe. The GitHub workflow files are validated by `gh workflow view` only after the repo exists.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "chore: add build, installers, CI, and community files"
```

---

### Task 15: README, docs, icon, social preview, and screenshots

**Files:**
- Create: `README.md`, `SECURITY.md`
- Create: `docs/design.md`, `docs/protocol.md`, `docs/security.md`
- Create: `assets/icon.ico`, `assets/social-preview.png`
- Create: `docs/screenshots/device-list.png`, `pairing.png`, `progress.png`, `tray.png`
- Modify: `src/LanBridge/LanBridge.csproj` to embed `assets/icon.ico`

**Interfaces:**
- No code API; the deliverable is the public presentation and the byte-level protocol/security docs.

- [ ] **Step 1: Write the docs**

- `docs/design.md`: move/copy the approved spec; update internal links to `docs/design.md`; keep the security decisions and milestones.
- `docs/protocol.md`: frame header, type values, every DTO field, handshake/pair/transfer sequences, timeouts, error codes, limits, and versioning. State that the code is never sent over the wire.
- `docs/security.md`: threat model, certificate pinning, SAS derivation, DPAPI/EphemeralKeySet, firewall scope, LAN-only guard, logging policy, limitations, and reporting.
- `README.md`: badges; one-line pitch; "Why not LocalSend?"; screenshots; install/uninstall one-liners; quick-start pairing; tray menu; security summary; limitations (Windows-only, IPv4, one inbound/outbound, no resume, unsigned); build-from-source; FAQ/troubleshooting (firewall, Public profile, AP isolation, VPN, port conflict); license/credits.
- `SECURITY.md`: supported versions, GitHub private advisory link, response expectations, out-of-scope items.

- [ ] **Step 2: Generate the icon and social preview**

Use Pillow in the dev environment to create a 256/128/64/48/32/16 `.ico` and a 1280x640 PNG: blue rounded square, two white laptop shapes, a transfer arrow. Commit the binaries. Add `<ApplicationIcon>..\..\assets\icon.ico</ApplicationIcon>` to the csproj and rebuild.

- [ ] **Step 3: Capture screenshots**

Run the app with a temporary profile seeded with two sample paired peers (offline) so the device list is populated. Capture:
- Device list with paired/available rows.
- Pairing code dialog.
- Transfer progress.
- Tray menu open.

Use the real WinForms UI; do not fabricate controls. Add the images under `docs/screenshots/` and reference them in the README.

- [ ] **Step 4: Verify links and rendering**

- Check every relative link in README/docs resolves.
- Check the install one-liner URL pattern.
- Check the README's screenshots render in a local Markdown preview.
- Run `dotnet build` after adding the icon.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "docs: add README, protocol, security, and release visuals"
```

---

### Task 16: Two-laptop test, GitHub publish, and v0.1.0 release

**Files:**
- Modify: any fixes found during the manual test
- Create: `docs/superpowers/plans/2026-11-26-lanbridge-v0.1.0.md` (this plan, already saved)

**Interfaces:**
- This task is the release gate: no new features after it starts.

- [ ] **Step 1: Run the manual checklist**

On both Windows 11 laptops:
- First run/UAC firewall rule; config name/folder.
- Discovery both directions on the home Wi-Fi.
- Pair from A and from B; wrong code; approval timeout; code timeout.
- Send 1 KB, 100 MB, and 2 GB; throughput compared with a local copy.
- Unicode and duplicate names; cancel from sender and receiver; receiver declines.
- Sleep/wake; app restart; peer offline/online; Wi-Fi change.
- VPN on/off; Public vs Private network profile; firewall rule removed.
- Hidden mode; unpair/re-pair; identity change.
- Only TCP/UDP 45873 listens; no outbound internet.
Expected: all pass. Fix defects and re-run until green.

- [ ] **Step 2: Initialize git and create the local commits**

```powershell
git add .
git commit -m "release: LanBridge v0.1.0"
git branch -M main
```

- [ ] **Step 3: Create the GitHub repo and push**

```powershell
gh repo create SMR-2009/lanbridge --public --source . --remote origin --description "Tiny, secure, local-only file transfer for Windows."
git push -u origin main
gh repo edit SMR-2009/lanbridge --enable-issues --enable-wiki=false --add-topic windows --add-topic file-transfer --add-topic lan --add-topic winforms --add-topic dotnet --add-topic local-first --add-topic privacy --add-topic tray-app
```
Expected: the repo exists at `https://github.com/SMR-2009/lanbridge`, issues on, wiki off, topics set.

- [ ] **Step 4: Tag and release**

```powershell
git tag -a v0.1.0 -m "LanBridge v0.1.0"
git push origin main --tags
```
Wait for the release workflow. Verify the release assets: `LanBridge.exe`, `lanbridge-v0.1.0-win.zip`, `install.ps1`, `uninstall.ps1`, `SHA256SUMS.txt`. Verify the README one-liner against the published release.

- [ ] **Step 5: Enable branch protection**

After the first green `build` workflow on `main`, enable branch protection requiring the `build` check. If the API is awkward, do it in GitHub Settings → Branches and note it in the release checklist. Do not require PR reviews for a solo v0.1.

- [ ] **Step 6: Final acceptance**

- Fresh install → pair → send a file in under two minutes.
- No plaintext file bytes on the wire.
- Only TCP/UDP 45873 listens; firewall scoped to `LocalSubnet`.
- No outbound internet traffic.
- All automated tests pass; manual checklist passed.
- README, SECURITY, protocol, design, screenshots, and install one-liner are accurate.
- Commit/tag pushed; release assets downloadable.

---

## Execution Notes

- Tasks 1–10 are sequential: each depends on the previous task's interfaces.
- Tasks 11–12 are tightly coupled UI work; implement them in order.
- Task 13 is the hardening gate; do not start packaging until it is green.
- Task 16 requires the physical second laptop; do not substitute a VM for the final acceptance test.
- Recommended execution: **Native** in this session. The protocol, peer store, and UI share interfaces that would cost more to re-establish in fresh subagent contexts than they save.
