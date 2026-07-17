# Release validation

Run this checklist on a Windows 10 or Windows 11 x64 machine before publishing
a stable release. Unit tests do not replace this hardware test.

## Clean installation and reboot recovery

1. Start with AFGC PC Manager, vJoy, and HidHide absent.
2. Run the release setup and accept dependency installation.
3. If either dependency requests or initiates a reboot, allow it. Sign back in
   and verify setup resumes without being launched manually.
4. Confirm AFGC PC Manager opens from its tray icon and appears in **Installed
   apps**.
5. From an elevated PowerShell window in the repository, run:

   ```powershell
   .\tools\Test-InstalledAFGC.ps1 -RequireController -OutputPath .\artifacts\installed-audit.json
   ```

   The publisher-signature line is informational until releases are
   Authenticode-signed. Do not publish if any required line reports `FAIL`.

## Controller behavior

1. Pair and wake **Amazon Fire Game Controller**.
2. Open AFGC PC Manager's **Settings > Diagnostics** page. Confirm the physical
   controller is connected and has a numbered vJoy output.
3. Open `joy.cpl`, select that vJoy device, and verify every control:
   both sticks in four cardinal directions, stick clicks, D-pad in eight
   directions, A/B/X/Y, both bumpers, both analog triggers from released to
   fully pressed, Back, Menu, Home, Game Circle, Rewind, Play/Pause, and Fast
   Forward.
4. Verify both triggers travel smoothly through their full axes and return to
   zero. This is the release-blocking behavior the project exists to restore.
5. With physical hiding enabled, verify games see only the vJoy controller.
   Use the recovery shortcut and confirm the physical device becomes visible
   again.

## Multiple controllers

1. Pair and wake controllers one at a time.
2. Confirm each gets a distinct vJoy output. Approve the elevation prompt if a
   new vJoy slot must be configured.
3. Disconnect and reconnect them in a different order. Confirm their saved vJoy
   numbers are reused when available.
4. Keep another feeder application's vJoy device busy and confirm AFGC PC
   Manager leaves it untouched.

## Repair, update, and uninstall

1. Run **Repair setup** from Diagnostics and confirm settings and controller
   registrations remain intact.
2. Test an update from the previous stable version using a real, signed release
   manifest. Confirm only stable GitHub releases are offered.
3. Uninstall with both dependency boxes selected. If a dependency initiates a
   reboot, sign back in and confirm uninstall continues automatically.
4. Repeat after installing vJoy and HidHide independently. Their uninstall
   boxes must default off and the independent installations must remain.
5. Confirm modified user-owned files are preserved and physical controller
   visibility is recovered before application removal.
