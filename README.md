# ClickyBot

ClickyBot is a Windows desktop macro studio for keyboard/mouse actions driven by screen conditions. It is intentionally built as a small, inspectable MVP so the rule model can grow without locking the project into a game-specific implementation.

## What is included

- Pixel matching and pixel-difference conditions.
- Screen-region color coverage conditions for “mana above X%” or lit/unlit UI elements.
- Click/drag screen selection overlay: a click records a 1×1 pixel; dragging records a rectangular region.
- Reference-region matching: capture a small screen area and require a configurable percentage of sampled pixels to stay within the RGB tolerance.
- Settings page for the reference-image folder and macro folder; captures are saved as numbered PNGs named from the rule, for example `001_Skill-is-lit.png`.
- Folder-backed macro profiles: the editable profile dropdown lists JSON macros, `SAVE MACRO` writes directly to the configured folder, and `APPLY CHANGES` updates the currently opened macro.
- Key presses, mouse clicks, and wait actions.
- Recorded combo actions containing timed keyboard and mouse input; held modifiers such as `Ctrl+C` are preserved as key-down/key-up events.
- Rising-edge triggers so a ready icon is acted on once until it goes inactive again.
- Optional repeat-while-true behavior with per-rule cooldowns.
- Optional AND gates, so a rule can require a ready pixel plus a mana threshold or a second UI pixel to be unlit.
- Rule authoring helpers: test a condition without sending input, duplicate a rule, and move rules up or down to control top-to-bottom priority.
- A compact editor layout with tooltips, a collapsed optional gate section, and a collapsed activity log; drag the splitters to resize the rule editor, automation map, and activity area.
- Global hotkeys: `F6` start/stop, `F7` panic stop, `F8` select the watch area, `Ctrl+F8` select the gate area, and `F9` select a click target.
- JSON profile save/load.
- Additive `SendInput` events. The app does not install a low-level hook or suppress normal user input.
- Emergency stop releases only keys that ClickyBot generated, so cancelling a combo cannot leave a modifier held or interfere with normal keyboard input.
- Bounded activity logging and optimized screen sampling/input replay to keep long-running profiles lighter on CPU and memory.
- GitHub release updates: use `CHECK FOR UPDATES` manually or enable the background startup check in `SETTINGS`; updates ask for confirmation before downloading and restarting the app.

## Run

```powershell
dotnet run --project .\ClickyBot.csproj
```

The project targets `net8.0-windows` and uses only the Windows desktop runtime; no third-party packages are required.

## First workflow

1. Start the app and click `LOAD STARTER`.
2. Open `SETTINGS` and choose the reference-image folder and macro folder. The default macro folder is `macros` beside the ClickyBot executable.
3. Click `SELECT WATCH AREA`, then click once for a pixel or click-drag a rectangle on the game UI. Press `Esc` to cancel.
4. Choose `RegionSnapshotMatches` and click `CAPTURE REFERENCE` to save the selected area as a numbered PNG named from the rule. Set the match threshold and tolerance to control how much visual change is allowed.
5. Expand `OPTIONAL AND GATE` only when a second requirement is needed, then enable `Use an additional AND gate`; use `SELECT GATE AREA` or `CAPTURE GATE REFERENCE`. Use `RegionCoverageAtLeast` for a mana threshold or `PixelDiffers` for a button that should not be lit.
6. Use `F9` or `SELECT CLICK TARGET` to choose a click location when configuring a mouse action.
7. Use `RECORD COMBO` to open the larger combo editor. Record the desired keyboard/mouse sequence, then press `F7` to finish. Input passes through while recording. The editor lets you set a standard delay, apply it to every step, or type a custom delay into any step. The sequence becomes a `RecordedCombo` action and is saved in the profile.
8. Change the key/action and thresholds, then use `APPLY CHANGES`. If a macro is currently open, the JSON is updated automatically.
9. Use `TEST CONDITION` to check the selected rule without sending its action. Use `DUPLICATE`, `MOVE UP`, and `MOVE DOWN` to organize the rule order.
10. Type a new profile name and click `SAVE MACRO` to create a JSON file, or choose an existing name from the dropdown and click `OPEN MACRO`.
11. Press `F6` to run and `F7` to stop immediately.

The `ACTIVITY · Live engine log` panel is collapsed by default. Expand it when diagnosing a rule or engine run; the tooltips on controls explain the fields without needing the log open.

## Build a Windows release

To create a self-contained app and installer on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

The command publishes the portable app as a self-contained single executable and builds the installed app as a compressed onedir bundle with Inno Setup. It creates these files in `dist`:

- `ClickyBot-Setup-0.1.0.exe` — compressed per-user installer. It installs to `%LOCALAPPDATA%\Programs\ClickyBot`, creates Start Menu and desktop shortcuts, and opens ClickyBot.
- `ClickyBot-Portable-0.1.0-win-x64.zip` — portable copy for users who prefer to extract and run the app.

The installer build requires Inno Setup 6. GitHub Actions installs it automatically before running the packaging script.

The installed app checks the latest GitHub release through the `SETTINGS` option when enabled. It only downloads a newer trusted ClickyBot installer after confirmation, then closes, installs, and relaunches the app.

GitHub Actions can build the same Windows artifacts from `.github/workflows/build-windows.yml` when a `v*` tag is pushed or the workflow is run manually.

## Important limitations of this MVP

The reference matcher is deliberately lightweight: it stores raw RGB samples from a selected region and compares a sampled subset on each poll. It is not OCR or a scale/rotation-invariant computer-vision matcher. Some games also render through protected or exclusive fullscreen paths where normal desktop capture/input APIs may not work.
