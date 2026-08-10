# AFGC PC Manager

AFGC PC Manager is an unofficial Windows compatibility manager for the Amazon
Fire TV Game Controller (2014, 1st gen). The controller appears in Windows
Bluetooth as **Amazon Fire Game Controller**. AFGC PC Manager reads its complete
Bluetooth HID reports—including the analog triggers that some Windows
game-controller paths miss—and exposes a conventional, remapped controller
through vJoy.

> [!WARNING]
> This project is under active development. There is not yet a supported public
> release or production installer. Do not treat builds from `main` as finished
> end-user software.

AFGC PC Manager executables are not currently Authenticode-signed. Windows may
show an unknown-publisher or SmartScreen warning. Release-manifest signing
protects update integrity but does not create Windows publisher reputation.
Only download releases from this repository, and compare the setup file against
the release's `SHA256SUMS.txt` before approving the Windows warning.

## Why this exists

Capable hardware should remain useful. The controller already sends its trigger
data; AFGC PC Manager bridges the gap between those reports and software that
expects a normal Windows game controller.

## Intended behavior

- Discover paired Amazon Fire Game Controllers automatically.
- Decode gamepad, media, Home, and Game Circle inputs.
- Preserve the measured stick centers and scale the full trigger ranges.
- Present an Xbox-style control layout through compatible vJoy devices.
- Optionally use HidHide to prevent duplicate input from the physical device.
- Optionally assign stable four-LED identification patterns and show the same
  patterns in the controller list; when disabled, the app sends no LED reports.
- Support per-controller mappings and multiple controllers without taking vJoy
  devices already owned by other feeder applications.
- Manage stable application, vJoy, and HidHide updates while preserving
  dependencies that were installed independently.

## Requirements

- Windows 10 or Windows 11 x64
- Bluetooth
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for development
- [vJoy](https://github.com/BrunnerInnovation/vJoy) for virtual controller output
- [HidHide](https://github.com/nefarius/HidHide) for optional duplicate-input suppression

The finished installer is designed to acquire verified, pinned dependency
releases. During development, do not install or replace drivers merely to build
or run the unit tests.

## Build and test

```powershell
dotnet restore AFGCPCManager.slnx
dotnet build AFGCPCManager.slnx --no-restore
dotnet test AFGCPCManager.slnx --no-build
```

The solution can be opened directly in JetBrains Rider or Visual Studio.

## Repository layout

- `src/` — application, shared UI system, controller logic, Windows transport,
  vJoy, and HidHide
- `installer/` — bootstrapper, uninstaller, verification, and installation logic
- `tests/` — hardware-independent unit tests
- `tools/` — controller capture/probe and release-signing utilities
- `docs/` — implementation plan, code specification, and capture protocol

The projects are separated so controller decoding and mapping remain testable
without Bluetooth hardware or installed drivers.

Hardware release candidates must also pass [the release-validation
checklist](docs/RELEASE_VALIDATION.md), including trigger, HidHide, reboot,
repair, uninstall, and multiple-controller tests on Windows.

## Controller research tools

The capture and probe tools are intentionally retained so results can be
reproduced on other controller revisions. Captures can contain local device
identifiers and are ignored by Git. Review and redact diagnostic output before
sharing it publicly.

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md) before submitting changes. Please report
security-sensitive installer, update, or device-hiding issues according to
[SECURITY.md](SECURITY.md), not in a public issue.

## Releases

Release builds are created only from version tags by GitHub Actions. The
workflow builds and tests the solution, publishes self-contained Windows x64
executables, creates the application archive, signs the release manifest, and
publishes SHA-256 checksums with the GitHub Release. Maintainers must configure the
`AFGC_RELEASE_SIGNING_KEY` Actions secret; the corresponding public key is
embedded in setup and committed under `installer/ReleaseSigning/`.

A locally built release can be tested without publishing it by passing its
signed bundle directly to setup:

```powershell
AFGCPCManager-Setup-x64.exe `
  --apply-archive AFGCPCManager-x64.zip `
  --version 0.1.0 `
  --manifest release-manifest.json `
  --signature release-manifest.sig
```

Setup verifies the manifest signature, version, archive size, and SHA-256 hash
before extracting the payload. Dependency installers are still downloaded only
from the official release URLs pinned by that trusted manifest.

## License and trademarks

AFGC PC Manager is available under the [MIT License](LICENSE). Third-party
components retain their own licenses.

This project is not affiliated with or endorsed by Amazon, Microsoft, Nintendo,
Nefarius Software Solutions, or the vJoy maintainers. Product names and
trademarks belong to their respective owners.
