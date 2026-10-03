# Screenshot verification harness

How a UI change gets its screenshot verdict.

- Launch the Debug exe with `--seed-demo --page <review|champions|trends|matchups|sessions|climb|settings>` (`AppShell.xaml.cs` reads `--page`).
- Set `HKCU:\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration=1` before capture and restore the prior value after. PrintWindow sees GPU-composited WPF content only with hardware acceleration off.
- Capture scripts live in `.m<N>shots/run_capture.ps1` (plus `run_capture_tall.ps1` where present). They hardcode main-checkout paths under `C:\Agent Projects\RiftReview` and call a PrintWindow (`PW_RENDERFULLCONTENT`) capturer at `.m2shots\Capturer\out\Capturer.exe`. No source for that capturer exists in any commit, so a new PrintWindow (`PW_RENDERFULLCONTENT`) capturer must be written at that path before any script runs.
- `DeepDiveView` is embedded in `ReviewView`, not its own nav page. Reach it via UIAutomation `SelectionItemPattern.Select()` on the first matching ListItem.
- Use the `_tall` variant when the target card sits below the default capture fold (chart and band content especially).
- The app has only `--seed-demo` and `--page`. It has no `--capture`, `--autostart` or `--done-signal` hooks.
- PNGs are gitignored and the capture scripts are committed. A new `.m<N>shots/` folder with a two-digit N needs its own `.gitignore` line, since `.m?shots/*.png` matches one digit only.
