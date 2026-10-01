# ClickyBot

ClickyBot is a Windows desktop macro studio for keyboard/mouse actions driven by screen conditions. It is intentionally built as a small, inspectable MVP so the rule model can grow without locking the project into a game-specific implementation.

## What is included

- Pixel matching and pixel-difference conditions.
- Screen-region color coverage conditions for “mana above X%” or lit/unlit UI elements.
- Click/drag screen selection overlay: a click records a 1×1 pixel; dragging records a rectangular region.
- Reference-region matching: capture a small screen area and require a configurable percentage of sampled pixels to stay within the RGB tolerance.
- Settings page for the reference-image folder and macro folder; captures are saved as numbered PNGs named from the rule, for example `001_Skill-is-lit.png`.
- Game-grouped macro profiles: choose or type a game in the editable game dropdown, then the profile dropdown shows only JSON macros assigned to that game. Selecting a macro opens it automatically, `SAVE MACRO` writes the selected game into the profile, and `APPLY CHANGES` updates the currently opened macro.
- Switching games remembers the last active macro for each game and reopens it automatically; a new game starts with a blank profile ready to save.
- Key presses, mouse clicks, and wait actions.
- Image search inside a selected area, with mouse movement or clicking at the centre of the found reference. Fixed-coordinate mouse actions remain available.
- Recorded combo actions containing timed keyboard and mouse input; held modifiers such as `Ctrl+C` are preserved as key-down/key-up events.
- Rising-edge triggers so a ready icon is acted on once until it goes inactive again.
- Optional repeat-while-true behavior with per-rule cooldowns.
- Throne combat mode: tap 1 continuously, interrupt for the V chain badge or purple Q defence circle, then resume 1. Q takes priority, and the circle is detected at multiple shrinking sizes.
- Optional AND gates, so a rule can require a ready pixel plus a mana threshold or a second UI pixel to be unlit.
- Rule authoring helpers: test a condition without sending input, duplicate a rule, and move rules up or down to control top-to-bottom priority.
- A compact editor layout with tooltips, separate collapsible profile and rule action panels, a collapsed optional gate section, and a collapsed activity log; drag the splitters to resize the rule editor, automation map, and activity area.
- Global hotkeys: `F12` start/stop by default (changeable in `SETTINGS`), `F7` panic stop, `F8` select the watch area, `Ctrl+F8` select the gate area, and `F9` select a click target.
- While running, the configured start/stop key and `F7` also stop with Shift, Ctrl, Alt, or Windows held (including combinations). These additional shortcuts are released when the macro stops. Any shortcut conflicts are reported in the activity log.
- JSON profile save/load.
- Additive `SendInput` events, with 70 ms key taps for game compatibility. A physical-key observer provides a start/stop fallback in fullscreen games; normal user input continues to pass through.
- Emergency stop releases only keys that ClickyBot generated, so cancelling a combo cannot leave a modifier held or interfere with normal keyboard input.
- ClickyBot branding uses the supplied robot-and-mouse artwork in the window toolbar, executable icon, taskbar/desktop shortcut, and installer.
- Bounded activity logging and optimized screen sampling/input replay to keep long-running profiles lighter on CPU and memory.
- Experimental opt-in resource navigation: after an `E (Hold)` prompt disappears, release E, scan with relative mouse movement, and take a limited number of short forward steps to find another prompt.
- Optional stamina bar recovery: if the bar stays absent while a macro session is started, release generated keys and restart the macro once. The watcher remains active if the inner macro stops; manual Stop and F7 cancel the session.
- GitHub release updates: use `CHECK FOR UPDATES` manually or enable the background startup check in `SETTINGS`; updates ask for confirmation before downloading and restarting the app.

## Throne combat

Choose the `Throne` (or `Throne and Liberty`) game and open your macro. **USE 1920 × 1080 PRESET** uses the supplied screenshot's V area (1260,610,110×110) and main play area for Q (0,140,1540×710). For another layout, click **SET UP THRONE COMBAT**, draw a tight watch area where the V badge appears, then draw the area where the purple Q defence circle appears. The mode uses only `Always → 1`, `V badge → V`, and `PurpleRingMatches → Q`; your other rules are preserved but ignored while this mode is on.

The bundled V reference matches the key badge rather than the changing skill artwork. If your UI size differs, select the V rule and **CAPTURE REFERENCE** around just the V badge. Use **TEST CONDITION** for V and Q with the game visible before starting. The Q detector looks for a hollow purple arc with radii from 12 to 120 pixels, allowing the outside circle to shrink without requiring an exact screenshot match. Keep the Q area tight to avoid unrelated purple effects.

Each Q/V appearance is handled once. After either response, 1 resumes while the animation disappears. A prompt must stay absent for at least 120 ms to rearm; failed screen captures do not rearm it. The 1 rule's cooldown controls the minimum tap interval (100 ms minimum), with 70 ms key taps and the profile poll interval contributing to the actual pace. No E or 2/3/4 keys are sent by this mode.

Start using your configured hotkey while the game is focused. Combat pauses when a different window takes focus; F7 and the configured start/stop key cancel it as usual. Setup updates an opened macro; use **SAVE MACRO** for a new profile.

## Licence

ClickyBot is released under the [ClickyBot Free Use Licence](LICENSE). It permits use, modification, and redistribution, but the app and modified versions must remain free of charge and may not be sold. Modified or redistributed versions must clearly identify changes and include attribution to the original ClickyBot project. This is a custom licence and is not an OSI-approved open-source licence because it prohibits charging for the Software.

## Run

```powershell
dotnet run --project .\ClickyBot.csproj
```

The project targets `net8.0-windows` and uses only the Windows desktop runtime; no third-party packages are required.

## First workflow

1. Start the app and click `LOAD STARTER`. The built-in profiles are assigned to `The First Descendant`.
2. Open `SETTINGS` and choose the reference-image folder and macro folder. The default macro folder is `macros` beside the ClickyBot executable.
3. Click `SELECT WATCH AREA`, then click once for a pixel or click-drag a rectangle on the game UI. Press `Esc` to cancel.
4. Choose `RegionSnapshotMatches` and click `CAPTURE REFERENCE` to save the selected area as a numbered PNG named from the rule. Set the match threshold and tolerance to control how much visual change is allowed.
5. Expand `OPTIONAL AND GATE` only when a second requirement is needed, then enable `Use an additional AND gate`; use `SELECT GATE AREA` or `CAPTURE GATE REFERENCE`. Use `RegionCoverageAtLeast` for a mana threshold or `PixelDiffers` for a button that should not be lit.
6. Use `F9` or `SELECT CLICK TARGET` to choose a click location when configuring a mouse action.
7. Use `RECORD COMBO` to open the larger combo editor. Record the desired keyboard/mouse sequence, then press `F7` to finish. Input passes through while recording. The editor lets you set a standard delay, apply it to every step, or type a custom delay into any step. After stopping recording, use `DELETE` on a row to remove just that step; the remaining steps keep their delays and are renumbered. Use `APPLY COMBO` to keep your edits or `CANCEL` to discard them. The sequence becomes a `RecordedCombo` action and is saved in the profile.
8. Change the key/action and thresholds, then use `APPLY CHANGES`. If a macro is currently open, the JSON is updated automatically.
9. Use `TEST CONDITION` to check the selected rule without sending its action. Use `DUPLICATE`, `MOVE UP`, and `MOVE DOWN` to organize the rule order.
10. Choose `The First Descendant` or type another game in the `GAME` dropdown. The profile dropdown will then show only profiles for that game, and ClickyBot will reopen that game's last active profile automatically. Type a new profile name and click `SAVE MACRO` to create a JSON file, or choose an existing name from the filtered dropdown to open it automatically.
11. Press the configured start/stop hotkey (`F12` by default) to run and `F7` to stop immediately.

The `ACTIVITY · Live engine log` panel is collapsed by default. Expand it when diagnosing a rule or engine run; the tooltips on controls explain the fields without needing the log open.

## Click a detected fishing target

1. Choose `RegionSnapshotMatches`, then `SELECT AREA TO WATCH` around the region where the target may appear (maximum 3840×2160). Image conditions show only this one area selector; F8 selects the same area.
2. Use `CAPTURE REFERENCE` to select a tight rectangle around the target image. Capturing or replacing the reference does not change the area to watch. The reference filename and size appear below the capture button.
3. Set `THEN` to `MouseClick`, `Mouse target` to `MatchedLocation`, and `Mouse button` to `Left`. The cursor moves to the centre of the found image before clicking. Choose `MouseMove` to move without clicking.
4. Optionally set a short wait before clicking, and uncheck `Restore pointer after click` if the cursor should stay at the target. Use `TEST CONDITION` to see the detected screen coordinates without moving or clicking.
5. Choose `ImageSimilarity` for visual-pattern matching that tolerates brightness changes, or `PixelColors` when the RGB colors should match closely. Adjust the similarity or pixel-color threshold as needed. `OnRisingEdge` acts once when the target appears; `WhileTrue` acts repeatedly with the configured cooldown while it remains visible.

ImageSimilarity searches up to 256 evenly distributed reference pixels and returns the strongest qualifying location. It compares the visual pattern rather than requiring identical RGB values, so it tolerates overall brightness changes. Capture a distinctive target at the same size it appears in the game and keep the search rectangle tight. It does not compensate for scaling or rotation. No match or a failed AND gate prevents the mouse action. Existing macros retain their fixed-coordinate defaults.

The reference rectangle must be inside the selected watch area. If it is captured elsewhere, ClickyBot warns you and leaves the existing watch area unchanged. The activity log and TEST CONDITION show the similarity score and detected coordinates, which helps distinguish a target outside the area from a target that needs a lower threshold.

Mouse movement sets the cursor position and sends an absolute movement event across the virtual desktop, including monitors with negative coordinates. The activity log reports detection transitions and action coordinates, and reports rejected input instead of claiming success. TEST CONDITION detects only; start the engine to perform the selected action. Some games lock/recentre the cursor or reject generated input; successful desktop movement does not guarantee that a game will accept it.

## Experimental resource navigation

This mode is disabled by default. Check **Find next E (Hold) resource** in the profile settings to use it. The profile must contain two enabled `RegionSnapshotMatches` image-search rules: one recorded E-up step for low stamina and one recorded E-down step for recovered stamina. Set `ResourceNavigation.PromptReferenceImagePath` in the profile JSON to a cropped PNG of the `E (Hold)` prompt; its width, height, search region, and matching threshold are configurable there too.

Start with the global hotkey while the game is focused. Navigation holds E immediately, releases it at low stamina, and resumes near high stamina. When **Use stamina bar fill for low/high checks** is enabled, the bar's colored fraction controls those transitions; the changing numbers are ignored. The `LowFillPercent` and `HighFillPercent` settings default to 17% and 95%. Without bar fill mode, the existing low/high image rules are used. When the interaction prompt has been absent for the configured delay, ClickyBot releases E, turns the camera in short relative mouse movements, and probes forward in short steps. It resumes E after two matches at nearly the same prompt location. At the configured movement limit, it stops moving but keeps watching for a prompt. If the bar disappears while E is held, navigation releases E and retries a confirmed prompt. Losing game focus stops navigation. `F7` releases all generated keys immediately.

For `pax / mine`, enable **Restart if stamina bar disappears** to keep a user-started session watching the stamina bar even if the inner macro ends. The JSON needs `ResourceNavigation.BarReferenceImagePath` pointing to a bar screenshot. The detector crops its stable left rim using the `BarCrop*` settings, then measures the colored fill along `BarFillRowY` across `BarFillWidth` pixels. The displayed number does not affect either check. Resource navigation handles bar disappearance while searching or harvesting, so the recovery watcher waits for navigation to end before restarting the macro. Outside navigation mode, the watcher restarts an active macro after the bar is absent for `BarMissingMs`. It waits for the bar to reappear before allowing another automatic restart. It pauses detection while another window has focus.

This is a bounded nearby search, not map-based pathfinding. It cannot tell whether an `E (Hold)` prompt belongs to a resource rather than another interactable object, and game camera capture/input behavior requires live calibration. The profile's `TurnPixels`, `TurnsBeforeStep`, `ForwardStepMs`, and `MaxForwardSteps` can be adjusted after testing.

## Build a Windows release

To create a self-contained app and installer on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

The command publishes the portable app as a self-contained single executable and builds the installed app as a compressed onedir bundle with Inno Setup. It creates these files in `dist`:

- `ClickyBot-Setup-0.1.33.exe` — compressed per-user installer. It installs to `%LOCALAPPDATA%\Programs\ClickyBot`, creates Start Menu and desktop shortcuts, and opens ClickyBot.
- `ClickyBot-Portable-0.1.33-win-x64.zip` — portable copy for users who prefer to extract and run the app.

The installer build requires Inno Setup 6. GitHub Actions installs it automatically before running the packaging script.

The installed app checks the latest GitHub release through the `SETTINGS` option when enabled. It only downloads a newer trusted ClickyBot installer after confirmation, then closes and opens the normal installer wizard so you can review and accept the ClickyBot licence before installation. The installer controls whether ClickyBot is launched after the update, so accepting its launch prompt starts only one app instance.

GitHub Actions can build the same Windows artifacts from `.github/workflows/build-windows.yml` when a `v*` tag is pushed or the workflow is run manually. Publish the installer and portable package as a GitHub release for the installed app to detect the update.

## Important limitations of this MVP

The reference matcher is deliberately lightweight: it stores raw RGB samples from a selected region and compares a sampled subset on each poll. It is not OCR or a scale/rotation-invariant computer-vision matcher. Some games also render through protected or exclusive fullscreen paths where normal desktop capture/input APIs may not work.
