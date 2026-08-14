# AFGC PC Manager

AFGC PC Manager is an unofficial Windows app for using the first-generation
Amazon Fire TV Game Controller as a standard Xbox (XInput) or vJoy
(DirectInput) controller.

The project started because several controller inputs do not work correctly on
Windows, most importantly the analog triggers. AFGC PC Manager reads the
controller directly and remaps its controls into a usable standard gamepad.

It supports the controller's sticks, triggers, gamepad buttons, media buttons,
Home button, Game Circle button, identification lights, and reported battery
level.

## Supported controller

This project specifically supports the **Amazon Fire TV Game Controller (2014,
1st generation)**:

- Bluetooth HID model shown by Windows as **Amazon Fire Game Controller**
- Hardware ID `VID 1949`, `PID 0402`
- Three media buttons—Rewind, Play/Pause, and Fast Forward—along the lower front
- Four white player/status lights and two replaceable AA batteries
- No vibration motors; rumble is not available

The redesigned 2015 second-generation Wi-Fi Direct controller is not supported.

> [!WARNING]
> This project is alpha software. Test releases may contain bugs, especially
> around driver installation, restarts, and controller hiding.

## Install

1. Download the latest setup file from this repository's **Releases** page.
2. Run setup and follow its prompts. Setup installs the required controller
   components and resumes automatically after a required restart.
3. Open AFGC PC Manager and add your controller.

To enter Bluetooth pairing mode, hold the controller's **Home** button for
10 seconds. It appears in Windows as **Amazon Fire Game Controller**.

The uninstaller can optionally remove saved app data and controller components.
It does not remove the controller's Bluetooth pairing.

AFGC PC Manager uses **ViGEmBus** for Xbox output, **vJoy** for DirectInput
output, and **HidHide** to prevent duplicate input from the physical controller.

## Build

Development requires Windows 10 or 11 x64 and the
[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet restore AFGCPCManager.slnx
dotnet build AFGCPCManager.slnx --no-restore
dotnet test AFGCPCManager.slnx --no-build
```

Hardware release candidates should also pass the
[release validation checklist](docs/RELEASE_VALIDATION.md).

## Project status

OpenAI Codex was used to assist with the design, implementation, testing, and
documentation of this project.

AFGC PC Manager is not affiliated with or endorsed by Amazon, Microsoft,
Nefarius Software Solutions, or the vJoy maintainers.

Licensed under the [MIT License](LICENSE). Security issues should be reported
according to [SECURITY.md](SECURITY.md).
