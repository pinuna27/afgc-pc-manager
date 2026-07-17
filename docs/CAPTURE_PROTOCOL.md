# Amazon Fire Game Controller (1st Generation) Capture Protocol

This protocol documents the guided capture built into the repository tool. The
tool displays each step, detects raw HID report changes, embeds step markers in
the CSV, and lets you accept or repeat every sample. It targets the 2014 Amazon
Fire Game Controller (`VID 1949`, `PID 0402`).

Amazon's official controller documentation lists Home, Back, Menu, A/B/X/Y,
the D-pad, both analog sticks and their clicks, L1/R1, analog L2/R2, and the
first-generation-only Rewind, Play/Pause, and Fast Forward buttons. The 2014
hardware also has a GameCircle button, which is included in the guided capture.

## Before recording

1. Connect the controller in Windows Bluetooth settings. Do not remove or
   re-pair it.
2. Close games, controller testers, Steam Input, and x360ce.
3. Open PowerShell in the repository root and start the capture tool:

   ```powershell
   dotnet run --project tools/FireController.Capture -- captures/fire-controller.csv
   ```

   The tool creates the `captures` directory if needed. Omit the filename to
   save a timestamped CSV in the current directory. Stop recording with
   **Ctrl+C**.
4. Put the controller on a flat surface with nothing touching the controls.
5. At the startup screen, check **Windows Settings > Bluetooth & devices**. If
   the controller is already shown as **Connected**, tap and release **A**. If
   it is only **Paired**, press its **Home/Amazon** button once, wait for its
   lights, and then tap and release **A**. The tool prints a status line every
   second until the first raw report arrives.
6. Follow each displayed action and let the control return to neutral. The tool
   detects the action and advances automatically; there are no per-step Enter
   or confirmation prompts.
7. If you make a mistake, press **R** once. The tool reverts and repeats the
   most recently captured step. This key is explained once at startup rather
   than repeated after every instruction.
8. Slight diagonal stick movement is okay. The complete raw motion is retained,
   and the four cardinal samples are analyzed together later. Repeat only if
   you moved the wrong control or the movement was substantially incorrect.

Do not rush. The quiet three-second gaps are part of the test data.

## Capture sequence

### Baseline

1. **Neutral baseline:** touch nothing for ten seconds.

### Face buttons

2. Press **A**.
3. Press **B**.
4. Press **X**.
5. Press **Y**.

### Shoulder controls

6. Press **L1 / left shoulder**.
7. Press **R1 / right shoulder**.
8. Slowly squeeze **L2 / left trigger** from released to fully pressed over
   three seconds; hold fully pressed for two seconds; slowly release over three
   seconds; then wait three seconds.
9. Slowly squeeze **R2 / right trigger** using the same timing.
10. Slowly squeeze **L2 and R2 together** to full pressure over three seconds;
    hold both for two seconds; slowly release both over three seconds; then wait
    three seconds.

### D-pad

11. Press **D-pad Up**.
12. Press **D-pad Right**.
13. Press **D-pad Down**.
14. Press **D-pad Left**.
15. Press and hold **D-pad Up+Right** together for one second, then release.
16. Press and hold **D-pad Down+Right** together for one second, then release.
17. Press and hold **D-pad Down+Left** together for one second, then release.
18. Press and hold **D-pad Up+Left** together for one second, then release.

### Left analog stick

For steps 19–22, move directly from center to the edge, hold one second, let
the stick spring back to center, and wait three seconds.

19. Move the **left stick fully left**.
20. Move the **left stick fully right**.
21. Move the **left stick fully up**.
22. Move the **left stick fully down**.
23. Starting from center, move the **left stick straight to the top**. From the
    top, trace exactly two full clockwise circles along the outer edge, ending
    at the top again. Release the stick directly to center, then wait three
    seconds. The path is: **center -> top -> two clockwise laps -> top ->
    center**.
24. Press the **left stick click / L3** without tilting the stick.

### Right analog stick

For steps 25–28, use the same center-to-edge procedure as the left stick.

25. Move the **right stick fully left**.
26. Move the **right stick fully right**.
27. Move the **right stick fully up**.
28. Move the **right stick fully down**.
29. Starting from center, move the **right stick straight to the top**. From the
    top, trace exactly two full clockwise circles along the outer edge, ending
    at the top again. Release the stick directly to center, then wait three
    seconds. The path is: **center -> top -> two clockwise laps -> top ->
    center**.
30. Press the **right stick click / R3** without tilting the stick.

### Navigation and first-generation media buttons

31. Press **Back**.
32. Press **Menu**.
33. Press **GameCircle**.
34. Press **Rewind**.
35. Press **Play/Pause**.
36. Press **Fast Forward**.
37. Press **Home** once. On Windows this may produce no application-visible
    report or may invoke a system action; record what happens and return to the
    capture window if necessary.

### Closing baseline

38. Touch nothing for ten seconds.
39. Stop and save the capture without pressing any more controller controls.

## Notes to record after the run

- Date and local time:
- Windows version:
- Controller label/model markings:
- Capture filename:
- Controller disconnects or unexpected behavior:
- Steps performed incorrectly or repeated:
- Any control physically absent from this controller:

## Official references

- [Amazon Game Controller Input](https://developer.amazon.com/docs/fire-tv/game-controller-input.html)
- [Amazon Controller Behavior Guidelines](https://developer.amazon.com/docs/fire-tv/controller-behavior-guidelines.html)
- [Amazon Controller Input with Unity](https://developer.amazon.com/docs/fire-tv/controller-input-with-unity.html)
