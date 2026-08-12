# Release validation

Run this checklist on a Windows 10 or Windows 11 x64 machine before publishing
a stable release. Unit tests do not replace this hardware test.

## Clean installation and reboot recovery

1. Start with AFGC PC Manager, vJoy, ViGEmBus, and HidHide absent.
2. Run the release setup and accept dependency installation.
3. If any dependency requests or initiates a reboot, allow it. Sign back in
   and verify setup resumes without being launched manually.
4. Confirm AFGC PC Manager opens from its tray icon and appears in **Installed
   apps**.
5. From an elevated PowerShell window in the repository, run:

   ```powershell
   .\tools\Test-InstalledAFGC.ps1 -RequireController -RequirePhysicalHidden -OutputPath .\artifacts\installed-audit.json
   ```

   The publisher-signature line is informational until releases are
   Authenticode-signed. Do not publish if any required line reports `FAIL`.

## Controller behavior

1. Pair and wake **Amazon Fire Game Controller**.
2. Remove it from the controller-row context menu. Confirm its Bluetooth pairing
   remains intact, virtual output is released, its AFGC-owned HidHide rule is
   removed, and automatic registration does not immediately add it again.
   Open **Add controller** while it is disconnected and confirm the empty-state
   explanation appears. Wake it, add it again, and confirm its stable identity
   and Controller 1 assignment are reused.
3. Select **vJoy (DirectInput)** in Settings. Open **Settings > Diagnostics**
   and confirm the physical controller is connected and has a numbered vJoy
   output.
4. Open `joy.cpl`, select that vJoy device, and verify every gamepad control:
   both sticks in four cardinal directions, stick clicks, D-pad in eight
   directions, A/B/X/Y, both bumpers, both analog triggers from released to
   fully pressed, Back, Menu, Home, Game Circle, Rewind, Play/Pause, and Fast
   Forward.
5. Verify both triggers travel smoothly through their full axes and return to
   zero. This is the release-blocking behavior the project exists to restore.
6. Select **Xbox (XInput)** and confirm the vJoy device is released, an Xbox
   output appears, and the same gamepad controls work in an XInput-aware tester
   or game. Switch back to DirectInput and confirm the Xbox output is removed.
7. With physical hiding enabled, verify games see only the selected virtual controller.
   The installed-system audit must report that the independent, non-whitelisted
   visibility probe sees no physical Fire controller. When the app first adds
   or changes a HidHide device rule, confirm virtual output remains disabled and
   the row asks for one controller power-cycle. Turn only that controller off
   and back on; do not restart its Windows PnP device. Confirm the prompt clears
   and does not return on later monitoring loops, mapping edits, app crash/relaunch,
   or ordinary controller reconnects while the rule remains unchanged.
8. Force-terminate the Manager after the reconnect gate has cleared, then relaunch
   it. Confirm the unchanged HidHide rule remains active, virtual output resumes,
   and no second reconnect is requested. A normal user-requested Exit is different:
   it restores physical visibility, so relaunching while the controller remains
   connected safely requires a new power-cycle.
9. Disable physical hiding and confirm the app releases its virtual output before
   restoring the physical controller. Use the recovery shortcut and confirm the
   physical device becomes visible again.

## Multiple controllers

1. Pair and wake controllers one at a time.
2. Confirm each gets a distinct vJoy output. Approve the elevation prompt if a
   new vJoy slot must be configured.
3. Enable **Use controller identification lights**. Confirm the physical
   four-light patterns match the patterns shown in the controller list. Disable
   it, power-cycle a controller, and confirm the app leaves its LEDs untouched.
4. Disconnect and reconnect them in a different order. Confirm their saved vJoy
   numbers are reused when available.
5. Keep another feeder application's vJoy device busy and confirm AFGC PC
   Manager leaves it untouched.
6. Select **Xbox (XInput)** with five controllers connected. Confirm only the
   first four registered controllers receive outputs and the fifth row explains
   the four-controller limit. Select **vJoy (DirectInput)** and confirm all five
   can receive distinct outputs.

## Repair, update, and uninstall

1. Run **Repair setup** from Diagnostics and confirm settings and controller
   registrations remain intact.
2. Test an update from the previous stable version using a real, signed release
   manifest. Confirm only stable GitHub releases are offered.
   Confirm the main window keeps an **Update to X.Y.Z** button visible after the
   notification disappears. Start the update from both the button and the Windows
   notification, confirm the version prompt appears, and confirm setup downloads,
   verifies, installs, closes, and reopens the Manager.
3. Uninstall with all three dependency boxes selected. If a dependency initiates a
   reboot, sign back in and confirm uninstall continues automatically.
4. Repeat after installing vJoy, ViGEmBus, and HidHide independently. Their
   uninstall boxes must default off and the independent installations must remain.
5. Confirm modified user-owned files are preserved and physical controller
   visibility is recovered before application removal.
6. With **Start with Windows** enabled, uninstall only AFGC PC Manager. Confirm
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` no longer contains the
   `AFGC PC Manager` value, so Windows is not left with a broken startup command.
7. Place a uniquely named user-owned file in the install directory, uninstall,
   and confirm the file is preserved. Confirm setup then refuses to claim the
   non-empty unowned directory; remove the test file and verify reinstall succeeds.
