# AFGC PC Manager reliability audit

Audit date: 2026-08-08  
Target: Windows 11 x64, installed AFGC PC Manager, real Amazon Fire Game
Controller, HidHide, and vJoy  
Status: automated gates and the prior installed-machine audit pass; the final
human-observed `joy.cpl` control sweep is pending.

This document records what was actually reproduced, changed, and verified. It
does not treat mocked driver APIs as proof of Windows behavior.

## Safety invariants

- When physical hiding is requested, a virtual bridge is not activated until a
  separate, non-whitelisted process proves that the physical controller is
  inaccessible.
- A visible or indeterminate physical controller causes the reserved vJoy output
  to be neutralized and released. The application fails closed instead of
  risking double input.
- When HidHide's active or blocked-device configuration changes, virtual output
  remains disabled until both an independent probe confirms new opens are
  blocked and the controller has been observed fully disconnected and then
  reconnected. This closes handles that existed before the rule change. The app
  never restarts a Bluetooth or PnP device programmatically.
- A physical controller is represented once even though Windows exposes its
  gamepad and consumer controls as separate composite HID collections.
- One live runtime owns at most one vJoy device, and one vJoy device cannot be
  acquired twice by the same backend even if the native status is stale.
- Shutdown, cancellation, input failure, settings changes, disconnect, repair,
  uninstall, and startup recovery all attempt neutral output and restore only
  the HidHide state owned by AFGC PC Manager.
- Installer state is durable before any external dependency action. A
  restart-required dependency cannot be resumed in the same Windows boot.
- Pre-existing vJoy/HidHide installations and unrelated HidHide whitelist,
  blocked-device, and activation state are not claimed or removed by AFGC.

## Reproduced defects and resolutions

1. **Double input after HidHide failure.** The bridge previously continued with
   vJoy output when hiding failed. A controller-output safety gate now requires
   independent proof and releases output on visible or indeterminate results.
2. **False-positive device matching.** Substring VID/PID checks matched lookalike
   product IDs. Matching now requires strict token boundaries.
3. **Composite controller duplication.** The Col01/Col02 collections and their
   `0000`/`0001` suffixes were grouped as two controllers. Collection paths are
   normalized into one physical group.
4. **Partial serial-read split.** If one collection returned the Bluetooth
   serial and its sibling did not, identity could split again. A unique serial
   is inferred across sibling collections.
5. **Unstable identity after re-pairing.** Path-derived IDs changed after
   Bluetooth endpoint replacement. Valid hardware serials now produce a stable,
   salted SHA-256 identity; migration preserves registration order, mapping
   overrides, exclusions, and the preferred vJoy number.
6. **HidHide API success mistaken for isolation.** A separately published
   `AFGCPCManager.HidVisibilityProbe.exe` now performs the outside-process check.
7. **HidHide application-path mismatch.** The driver reports NT device paths
   while the application configured DOS paths. Readback comparison now resolves
   both path namespaces safely.
8. **False hidden result after serial-read failure.** An accessible endpoint
   whose persistent identity cannot be read is indeterminate, not hidden.
9. **Incorrect interpretation of real HidHide behavior.** Hidden Raw Input paths
   remain enumerable but fail to open with `ERROR_ACCESS_DENIED`. The probe now
   accepts only that error as hiding evidence; other open failures are
   indeterminate.
10. **Stale HidHide state after a crash.** Ownership is journaled transactionally.
    A normal relaunch adopts still-valid owned rules so it does not create a new
    isolation gap; repair, uninstall, hiding-disable, and the installed recovery
    shortcut remove owned state. Entries not added by AFGC are preserved.
11. **Duplicate vJoy acquisition.** Native status can lag process ownership. An
    in-process ownership set now prevents a second acquisition.
12. **Large vJoy handle leak.** Capability calls against all missing/busy slots
    retained about 2,520 handles per enumeration on the real driver. Capability
    probing is now limited to free/owned slots.
13. **Wrong Raw Input ABI layout.** `RID_DEVICE_INFO` was 28 bytes and used
    incorrect HID field widths; Windows requires 32 bytes. The corrected layout
    made real discovery return one controller.
14. **Release payload contamination.** A project reference leaked helper runtime
    files into publish output. The helper is now a build/copy dependency, and the
    release workflow rejects anything except the four expected executables.
15. **CLI elevation argument loss.** `--cli` was dropped during UAC relaunch and
    setup silently became the wizard. Elevation arguments are now constructed
    and tested explicitly.
16. **Older app could block repair.** Setup now attempts normal IPC shutdown,
    then allows exact-install-path termination only after identity validation,
    followed by HidHide ownership recovery.
17. **Healthy repair depended on a remote manifest.** A local repair failed on a
    manifest 404 even when both drivers were independently operational. The
    safe repair path now avoids dependency download only when both dependencies
    are proven ready and no operation is pending.
18. **Vague and repeated runtime errors.** Controller-specific failures are
    deduplicated and shown in the controller row and diagnostic report. A
    size-rotated runtime log separates discovery, isolation, vJoy, mapping, and
    lifecycle stages without logging raw controller identifiers.
19. **Incomplete capture regression coverage.** Deterministic captured reports
    now cover every standard button, all eight D-pad directions, stick extremes,
    both full analog triggers, media buttons, Home/Game Circle aggregation, and
    releases.
20. **Probe timeout could leave a child process.** Cancellation and timeout now
    terminate the complete probe process tree.
21. **vJoy cleanup could skip Raw Input cleanup.** If output neutralization or
    disposal threw, the input subscription was not disposed and could retain
    reports during later reconnects. Every cleanup stage is now attempted, and
    multiple failures are reported together.
22. **Emergency neutralization masked the initiating failure.** If input failed
    and the final neutral write also failed, the vJoy error replaced the input
    error. Diagnostics now preserve and report both causes.
23. **Successful setup remained alive and blocked every later repair.** The
    setup process repeatedly loaded and unloaded `vJoyInterface.dll`; this vJoy
    build displayed `Creation of dummy window failed!`, then retained its native
    dummy-window thread and the global setup mutex. Readiness and provisioning
    now run in bounded disposable child processes. The provisioning child keeps
    the unsafe DLL loaded only until its result is durable, then Windows
    terminates that completed child without entering the broken DLL-unload path.
24. **Guide-mode Home still reached Chrome after hiding was enabled.** The saved
    mapping correctly converted Home to vJoy Guide and emitted no Browser Home
    action. Live tracing on 2026-08-10 then reproduced Browser Home with the
    manager process stopped while HidHide remained active: Chrome/Windows had
    retained a consumer-control handle opened before the new HidHide rule. A
    new-process probe could not detect that stale handle. The handle-reset gate
    is therefore persistent and configuration-sensitive: it is created only
    when AFGC adds a blocked device or activates the cloak, survives process
    restart, and clears only after a complete controller disconnect/reconnect.
    Unchanged rules do not re-prompt. Virtual output stays disabled while the
    gate is pending, and AFGC never invokes a PnP/Bluetooth restart. Direct
    regressions cover Guide press, hold, release, repress, changed versus
    unchanged isolation, stale-handle gating, and visible-device refusal.
25. **Bluetooth HID discovery could hang forever after reconnect.** Discovery
    called `HidD_GetSerialNumberString` and `HidD_GetProductString` directly on
    the live Bluetooth collections. On 2026-08-10 both Code-0 HID nodes arrived,
    the UI stayed responsive, but the runtime worker stopped before consuming
    the reconnect. Stable identity now comes from the Bluetooth address in the
    non-blocking PnP parent instance ID, and display names come from PnP
    properties; runtime discovery performs no synchronous HID string I/O. The
    journal also persists the intermediate "disconnect observed" phase so a
    crash or update between disconnect and reconnect cannot cause a second
    prompt.
26. **HidHide reconnect made Raw Input permanently empty for the manager.** A
    live allowlisted-process trace proved both hidden Fire HID interfaces were
    present and directly openable while `GetRawInputDeviceList` returned zero
    Fire devices and delivered zero `WM_INPUT` reports. This is why the manager
    could work before hiding but fail after the required reconnect. Discovery
    now enumerates present HID interfaces through Configuration Manager, and the
    bridge reads both allowlisted collections with asynchronous direct HID I/O.
    Hardware validation captured A, B, D-pad, and the Home consumer sequence
    `02 10` / `02 00` while Raw Input remained empty.

Each source-level defect above has a deterministic test or an installed-machine
validation procedure. The real-driver handle leak, Raw Input ABI failure,
HidHide access-denied behavior, installed repair, independent isolation, and
crash recovery were also reproduced on Windows hardware.

## Current automated evidence

- Release build: **0 warnings, 0 errors**.
- Tests: **343 passed, 0 failed, 0 skipped**.
  - Core: 107
  - Setup/lifecycle: 129
  - vJoy: 17
  - Windows discovery/Raw Input: 33
  - HidHide: 21
  - Application safety, reconnect gates, and UI smoke: 30
  - Independent visibility probe: 6
- `git diff --check`: pass. Line-ending notices are informational and are not
  whitespace errors.
- Self-contained `win-x64` publish: pass; exact four-file payload enforced.

## Installed-machine evidence

The final installed audit is written to
`artifacts/final-installed-audit-v2.json`. Every required check passed (13/13):

- Windows uninstall registration present.
- Expected application, probe, setup, uninstaller, and install journal present.
- Elevated PnP inventory readable.
- vJoy present.
- HidHide service present and running.
- Real Amazon Fire Game Controller present.
- Exactly one AFGC application process running.
- Independent probe result: zero visible physical controllers and two
  inaccessible Fire-controller endpoints.

The optional publisher-signature check reports `NotSigned`; signing remains a
release-distribution task and is not treated as a runtime failure.

Additional real-machine results:

- Runtime discovered one composite Fire controller and mapped it to vJoy device
  2 while retaining the saved assignment across repair and identity migration.
- Handle count changed from 770 to 762 over 20 seconds in the final installed
  build, disproving recurrence of the 2,520-handles-per-enumeration leak.
- A forced exact-path process termination left a durable HidHide journal. The
  next launch recovered stale ownership, re-established hiding, and returned to
  `Running — 1 of 1 Fire controller(s) mapped.`
- A local in-place repair completed without reinstalling healthy drivers and
  without requesting a reboot.
- Five consecutive isolated vJoy readiness probes and three provisioning probes
  completed with no survivor process. Two consecutive installed repairs then
  completed successfully, each leaving zero setup processes, no setup error
  log, and no pending dependency operation. This reproduces and closes the
  former `Creation of dummy window failed!`/permanent setup-lock defect.

## Hardware and destructive checks not claimed as performed

- The final `joy.cpl` sweep requires a person to move every physical control and
  observe vJoy device 2. This is the remaining check for doubled, missing,
  inverted, or stuck live controls.
- The Home/Guide reconnect fix has passed automated tests but is not claimed as
  hardware-validated until the updated installed build observes the controller
  power off/on and the owner confirms that Home reaches Guide without opening
  Chrome.
- Only one physical Fire controller is attached. Distinct assignment and
  reconnect ordering for two simultaneous controllers are covered by identity,
  registry, acquisition, and lifecycle tests plus the release checklist, but
  are not claimed as a two-controller hardware run.
- No Windows restart was performed because the machine owner explicitly cannot
  restart it. Restart codes 3010/1641, durable resume, same-boot prevention, and
  boot-identity reconciliation are covered by state-machine/integration tests;
  the real reboot procedure remains in `docs/RELEASE_VALIDATION.md`.
- A destructive clean driver removal/uninstall was intentionally not performed
  without explicit approval. Ownership combinations, rollback, interruption,
  update, repair, uninstall, corrupt state, and restart continuation are covered
  by setup tests and the release procedure. The real in-place repair was
  performed successfully.
