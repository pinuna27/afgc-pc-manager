# AFGC PC Manager UI system

The interface is intentionally plain: it should feel like a careful Windows
utility, not a game launcher. Product surfaces share one visual vocabulary so
new screens do not introduce one-off colors, spacing, fonts, or buttons.

## Foundations

- **Typography:** Segoe UI for interface text; Cascadia Mono for diagnostic and
  lifecycle logs.
- **Layout:** 24–28 px page margins, 18–24 px card padding, and compact 8 px
  spacing between related controls.
- **Surfaces:** a light neutral canvas, white cards, one-pixel neutral borders,
  and no decorative shadows or gradients.
- **Color:** navy-indigo is reserved for primary actions and information;
  green, amber, and red communicate success, caution, and destructive states.
- **Actions:** one primary action per surface. Destructive actions use the red
  button style and remain visually separate from ordinary navigation.
- **Language:** labels describe effects in Windows terms. Mapping help must say
  whether an input appears in `joy.cpl` or is emitted as a Windows media action.

The implementation lives in `src/AFGCPCManager.UI`. `UiTheme` owns tokens and
form chrome; `AfgcButton`, `AfgcCard`, `AfgcTabControl`, `AfgcStatusBanner`, and
`AfgcCallout` are the shared primitives. User-facing projects reference this
library rather than duplicating styles.

## Product icon

`src/AFGCPCManager.UI/Assets/afgc-icon.png` is the transparent master and
`afgc-icon.ico` is the multi-resolution Windows icon. Both are embedded in the
shared UI library. The ICO is also compiled into the app, setup, uninstaller,
and UI-preview executables so Windows Explorer, shortcuts, title bars, and UAC
surfaces do not fall back to the generic application icon. The tray icon uses
the same embedded mark.

The mark is a text-free controller on a navy tile with a warm revival/power
accent. It is designed to remain identifiable at 16 and 32 pixels.

## Preview and visual checks

The preview harness renders the real forms with sample data and never starts
the controller runtime or setup lifecycle:

```powershell
dotnet run --project tools/AFGCPCManager.UiPreview -- main
dotnet run --project tools/AFGCPCManager.UiPreview -- settings-controller
dotnet run --project tools/AFGCPCManager.UiPreview -- setup
dotnet run --project tools/AFGCPCManager.UiPreview -- uninstall
```

Add `--capture <path.png>` for a deterministic PNG. Supported surfaces are
`main`, `settings`, `settings-controller`, `settings-diagnostics`, `add`,
`add-empty`, `setup`, `uninstall`, and `uninstall-progress`. `--width` and
`--height` exercise constrained layouts.

All forms are authored in logical 96-DPI pixels. Because they are composed in
code instead of by the WinForms designer, `UiTheme.Apply` explicitly declares
`AutoScaleDimensions = 96 x 96` and must be called only after the complete
initial control tree has been added. Metrics that WinForms does not autoscale
(for example grid rows, tab items, list items, and custom-painted insets) use
`UiTheme.Scale` when a handle or DPI context is available.

Run the clipping audit before release. It checks all nine surfaces at both
default and minimum sizes, including button preferred sizes, fixed-label text,
tab labels, mapping inputs, ancestor clipping, and preview grid content:

```powershell
tools\AFGCPCManager.UiPreview\bin\Release\net10.0-windows\AFGCPCManager.UiPreview.exe --audit-layout
tools\AFGCPCManager.UiPreview\bin\Release\net10.0-windows\AFGCPCManager.UiPreview.exe --audit-layout --simulate-scale 1.0
tools\AFGCPCManager.UiPreview\bin\Release\net10.0-windows\AFGCPCManager.UiPreview.exe --audit-layout --simulate-scale 1.25
tools\AFGCPCManager.UiPreview\bin\Release\net10.0-windows\AFGCPCManager.UiPreview.exe --audit-layout --simulate-scale 1.5
tools\AFGCPCManager.UiPreview\bin\Release\net10.0-windows\AFGCPCManager.UiPreview.exe --audit-layout --simulate-scale 2.0
```

Before release, inspect every surface at its default size, the main and settings
windows at their declared minimum sizes, disabled and destructive actions,
empty controller state, long status text, and 100%, 125%, 150%, and 200%
Windows display scaling.
