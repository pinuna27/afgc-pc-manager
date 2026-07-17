# Amazon Fire Game Controller guided capture

This document describes the current `FireController.Capture` and
`analyze-capture.ps1` tools. They target the first-generation 2014 Amazon Fire
Game Controller (`VID 1949`, `PID 0402`) and record its Raw Input HID reports.

The capture tool listens to gamepad, joystick, consumer-control, keyboard, and
system-control top-level collections, but writes reports only for the target
Amazon vendor and product IDs. The CSV can contain local device paths, so the
`captures/` directory is ignored by Git.

## Start a capture

1. Pair the controller in **Windows Settings > Bluetooth & devices**.
2. Close games, Steam Input, x360ce, controller testers, and other programs
   that may react to controller or media buttons.
3. Put the controller on a flat surface with all controls released.
4. From the repository root, run:

   ```powershell
   dotnet run --project tools/FireController.Capture -- captures/fire-controller.csv
   ```

   The parent directory is created automatically. If the filename is omitted,
   the tool writes `fire-controller-YYYYMMDD-HHMMSS.csv` in the current working
   directory.

## Wake the controller

The tool initially waits up to 60 seconds and updates one terminal status line:

- If Windows shows the controller as **Connected**, tap and release **A**.
- If Windows shows it as **Paired**, press **Home/Amazon** once, wait for the
  controller lights, then tap and release **A**.

The wake-up A press only establishes that reports are arriving; it is not the
guided A-button sample. After the first report, release A and leave the
controller neutral. The first guided step begins after a 750 ms pause.

## How guided steps advance

- There are 36 steps and no Enter-key prompts.
- Ordinary steps allow up to 10 seconds. Trigger and stick-circle steps allow
  up to 30 seconds.
- A step ends early after an input change returns near its starting report and
  remains there for about 700 ms.
- Analog controls may settle a few raw units away from their starting value;
  the detector tolerates a small residual offset based on the observed motion.
- If no change is detected before the timeout, the tool records that fact and
  continues rather than stopping the entire capture.
- After each accepted step, the tool waits 500 ms before displaying the next
  instruction.
- Press **R** once if a sample was wrong. During the current step it repeats
  that step; immediately afterward it can revert the most recently accepted
  step. The CSV records `REPEAT` or `REVERT` markers accordingly.
- The tool marks each accepted sample automatically. Do not press Enter.

Slight diagonal movement during a cardinal stick sample is acceptable. Repeat
the step only if the wrong control moved or the intended direction was not
meaningfully reached.

## Current 36-step sequence

### Face and shoulder controls

1. A
2. B
3. X
4. Y
5. L1 / left shoulder
6. R1 / right shoulder
7. Slowly squeeze L2 fully, hold briefly, then slowly release
8. Slowly squeeze R2 fully, hold briefly, then slowly release
9. Slowly squeeze L2 and R2 fully together, hold, then release both

### D-pad

10. Up
11. Right
12. Down
13. Left
14. Up+Right
15. Down+Right
16. Down+Left
17. Up+Left

Press both directions together for diagonals, then release both.

### Left stick

18. Fully left, hold briefly, then release to center
19. Fully right, hold briefly, then release to center
20. Fully up, hold briefly, then release to center
21. Fully down, hold briefly, then release to center
22. From center, move straight to the top; trace exactly two full clockwise
    circles along the outer edge; finish at the top; release directly to center
23. L3 / left-stick click without deliberately tilting the stick

The circle path is: **center -> top -> two clockwise laps -> top -> center**.

### Right stick

24. Fully left, hold briefly, then release to center
25. Fully right, hold briefly, then release to center
26. Fully up, hold briefly, then release to center
27. Fully down, hold briefly, then release to center
28. From center, move straight to the top; trace exactly two full clockwise
    circles along the outer edge; finish at the top; release directly to center
29. R3 / right-stick click without deliberately tilting the stick

### Navigation and first-generation media controls

30. Back
31. Menu
32. GameCircle
33. Rewind
34. Play/Pause
35. Fast Forward
36. Home once; return to the terminal if Windows opens another application

Home and media actions may cause a Windows shell action even when their Raw
Input report is captured. The tool remains registered as an input sink while
its hidden window is running.

## Completion and CSV contents

After step 36, the tool writes a `COMPLETE` marker, reports the saved filename,
waits about 1.5 seconds, and exits automatically. **Ctrl+C** can stop an
incomplete run.

The CSV columns are:

- `utc_time` and `elapsed_ms`
- `kind` (`REPORT`, `START`, `END`, `ACCEPT`, `REPEAT`, `REVERT`, or `COMPLETE`)
- `step` and `label`
- the Raw Input device path
- the complete report as uppercase hexadecimal bytes

There are no synthetic opening or closing neutral-baseline steps. The analyzer
infers a per-device baseline from the latest report before each step and falls
back to the final report in that sample when necessary.

## Analyze a capture

Analyze the newest guided-capture CSV under `captures/` (unrelated CSV schemas
are skipped):

```powershell
& .\tools\analyze-capture.ps1
```

Analyze a specific file:

```powershell
& .\tools\analyze-capture.ps1 captures\fire-controller.csv
```

Use a different output directory:

```powershell
& .\tools\analyze-capture.ps1 captures\fire-controller.csv `
  -OutputDirectory captures\analysis
```

The analyzer uses the last accepted attempt for each label, groups reports by
Raw Input collection, and creates:

- `<capture>.analysis.md` with a readable changed-byte/changed-bit table
- `<capture>.analysis.json` with baselines, ranges, XOR masks, observed reports,
  report counts, lengths, collection names, and device paths

Byte indices in both outputs include the HID report ID at byte 0.

## Notes to retain separately

- Windows version
- Controller label and model markings
- Unexpected disconnects or shell actions
- Steps repeated or performed incorrectly
- Controls physically absent from the tested revision

Redact device paths and local identifiers before sharing captures or analysis.

## Official references

- [Amazon Game Controller Input](https://developer.amazon.com/docs/fire-tv/game-controller-input.html)
- [Amazon Controller Behavior Guidelines](https://developer.amazon.com/docs/fire-tv/controller-behavior-guidelines.html)
- [Amazon Controller Input with Unity](https://developer.amazon.com/docs/fire-tv/controller-input-with-unity.html)
