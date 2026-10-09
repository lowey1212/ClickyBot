# ClickyBot

ClickyBot is a Windows desktop macro studio for keyboard/mouse actions driven by screen conditions. It is intentionally built as a small, inspectable MVP so the rule model can grow without locking the project into a game-specific implementation.

## What is included

- Pixel matching and pixel-difference conditions.
- Screen-region color coverage conditions for “mana above X%” or lit/unlit UI elements.
- Click/drag screen selection overlay: a click records a 1×1 pixel; dragging records a rectangular region.
- Reference-region matching: capture a small screen area and require a configurable percentage of sampled pixels to stay within the RGB tolerance.
- Inverted reference matching (`RegionSnapshotDiffers`): require a valid capture with no reference match at the chosen threshold. A missing reference or failed capture blocks the rule; absence provides no matched mouse target.
- Local cooldown text detection: `CooldownTimerAbsent` allows a skill when no countdown is detected, and `CooldownTimerPresent` can gate another skill while the first is cooling down. The exact number and skill artwork are not used as reference images. Bar coverage AND gates remain independent.
- Settings page for the reference-image folder and macro folder; captures are saved as numbered PNGs named from the rule, for example `001_Skill-is-lit.png`.
- Game-grouped macro profiles: choose or type a game in the editable game dropdown, then the profile dropdown shows only JSON macros assigned to that game. Selecting a macro opens it automatically, `SAVE MACRO` writes the selected game into the profile, and `APPLY CHANGES` updates the currently opened macro.
- Switching games remembers the last active macro for each game and reopens it automatically; a new game starts with a blank profile ready to save.
- Key presses, mouse clicks, and wait actions. Literal keyboard punctuation such as `-` and `=` is supported; an invalid key name logs an action failure and leaves other rules running.
- Image search inside a selected area, with mouse movement or clicking at the centre of the found reference. Fixed-coordinate mouse actions remain available.
- Recorded combo actions containing timed keyboard and mouse input; held modifiers such as `Ctrl+C` are preserved as key-down/key-up events.
- Rising-edge triggers so a ready icon is acted on once until it goes inactive again.
- Optional repeat-while-true behavior with per-rule cooldowns.
- Throne combat mode: tap 1 continuously, interrupt for the V chain badge or purple Q defence circle, then resume 1. Q takes priority, and the circle is detected at multiple shrinking sizes.
- Optional AND gates, so a rule can require a ready pixel plus a mana threshold or a second UI pixel to be unlit.
- Rule authoring helpers: test a condition without sending input, duplicate a rule, and move rules up or down to control top-to-bottom priority.
- Live rule inspector: compare a scaled live watch area with its reference, see the main condition and AND gate separately, and read RGB/coverage/image diagnostics without sending input. Running inspection reuses engine observations and includes Throne healing HP context.
- A compact editor layout with tooltips, separate collapsible profile and rule action panels, a collapsed optional gate section, and a collapsed activity log; drag the splitters to resize the rule editor, automation map, and activity area.
- Global hotkeys: `F12` start/stop by default (changeable in `SETTINGS`), `F7` panic stop, `F8` select the watch area, `Ctrl+F8` select the gate area, and `F9` select a click target.
- While running, the configured start/stop key and `F7` also stop with Shift, Ctrl, Alt, or Windows held (including combinations). These additional shortcuts are released when the macro stops. Any shortcut conflicts are reported in the activity log.
- JSON profile save/load.
- Per-rule keyboard input mode: scan codes (the existing default), Windows key codes, or FakerInput using an already-installed driver. The setting applies to presses, holds, and every keyboard step in recorded combos; stopping releases generated keys. The first two modes use Windows software input.
- Additive `SendInput` events, with 70 ms key taps for game compatibility. A physical-key observer provides a start/stop fallback in fullscreen games; normal user input continues to pass through.
- Emergency stop releases only keys that ClickyBot generated, so cancelling a combo cannot leave a modifier held or interfere with normal keyboard input.
- ClickyBot branding uses the supplied robot-and-mouse artwork in the window toolbar, executable icon, taskbar/desktop shortcut, and installer.
- Bounded activity logging and optimized screen sampling/input replay to keep long-running profiles lighter on CPU and memory.
- Experimental opt-in resource navigation: after an `E (Hold)` prompt disappears, release E, scan with relative mouse movement, and take a limited number of short forward steps to find another prompt.
- Optional stamina bar recovery: if the bar stays absent while a macro session is started, release generated keys and restart the macro once. The watcher remains active if the inner macro stops; manual Stop and F7 cancel the session.
- GitHub release updates: use `CHECK FOR UPDATES` manually or enable the background startup check in `SETTINGS`; updates ask for confirmation before downloading and restarting the app.

## AIO2 quest prompt

Choose or type `aio2` as the game (`Aion 2` and `Aion2` also show the setup button), name a new macro, and click **SET UP ALT+1 PROMPT**. This loads the supplied 29×12 Alt+1 key badge without the changing quest text. In the selected rule, use **SELECT AREA TO WATCH** to draw the area under your minimap, then **APPLY CHANGES** and **SAVE MACRO**. No screen coordinates are assumed: the initial 1×1 search area cannot match the badge.

While **Auto Move** is visible, use **CAPTURE GATE REFERENCE** to draw tightly around just that text, excluding the changing distance. The enabled `RegionSnapshotDiffers` AND gate blocks Alt+1 until Auto Move disappears. The preset also blocks input until its gate area has been selected; missing references and failed captures block it. The supplied label reference excludes `(71m)`.

Once the Alt+1 badge is visible and Auto Move is absent, the rule sends LeftAlt down, taps 1 for 70 ms, then releases LeftAlt. It waits for the combined condition to reset before triggering again. **TEST CONDITION** verifies both conditions without sending keys. If your UI scale differs, capture just the Alt+1 badge as a new reference. Existing rules and previously selected watch/gate areas are preserved when setup is run again; use a new macro to keep this separate from any older quest automation.

Click **SET UP F PROMPT** to add the supplied F key badge as a separate rule. Select the area where F appears with **SELECT AREA TO WATCH**, then apply and save. It taps F while the badge remains visible, with a 500 ms cooldown between taps; it stops tapping when the badge disappears or Auto Move appears. You can change the cooldown in the rule editor. The initial 1×1 area blocks matching until you choose an area.

When first added, F inherits the Alt+1 rule's Auto Move gate, including any calibration already saved. If that gate is not yet configured, use **CAPTURE GATE REFERENCE** around Auto Move on the F rule too. Repeating F setup preserves its selected watch/gate areas and does not duplicate it. Existing rules are preserved; F is added before Alt+1.

Click **SET UP ESC SKIP** to add the supplied SKIP word and arrows as a separate rule. Select its watch area manually, then apply and save. It sends one Esc tap when SKIP is visible and Auto Move is absent, then waits for the combined condition to reset before sending another. Image similarity tolerates modest brightness changes. If your HUD scale differs, capture the SKIP prompt as a new reference.

The new rule is added first and inherits an existing Auto Move gate from Alt+1 or F, preferring a calibrated gate. An unconfigured or unreadable gate blocks it. Existing rules are preserved; repeating skip setup preserves its selected watch/gate areas and does not add duplicates.

For the supplied **F Gather** prompt, choose **SET UP F GATHER (0–1s)**, select the prompt's watch area and save. When the prompt is detected, the Gather rule chooses a random 0–1000 ms reaction delay before tapping F. It keeps checking the prompt while waiting and cancels the pending tap if the prompt disappears. If Gather remains visible, it can repeat with the existing 500 ms cooldown and a new reaction delay. This setup only needs the Gather prompt area; the existing quest setup retains its Auto Move gates.

Gather now uses **FakerInput (installed driver)** for its 70 ms F tap. Existing Gather presets switch to this mode once when opened, preserving their selected area, reference and timing. Save the macro to keep the change. ClickyBot communicates directly with the driver; no helper program or driver installer is bundled. If the driver is unavailable or rejects a report, ClickyBot reports the error and sends no software-input fallback. Other keyboard rules can select the same mode in the rule editor. The driver supports up to six held non-modifier keys and F1–F12. Ordinary mouse actions retain their existing input method; Aion combat uses driver mouse reports too. See [FAKERINPUT-NOTICE.txt](FAKERINPUT-NOTICE.txt) for the driver protocol attribution and MIT notice.

The standard rule editor has **Random reaction delay before action** with minimum and maximum values in milliseconds. Conditions and other rules keep polling while it waits, and STOP/F7 cancels normally. Existing profiles keep their original immediate responses unless this option is enabled.

## Aion 2 target combat

Create a separate Aion 2 macro, then choose **SET UP AION COMBAT**. Select the area where the target HP bar or target arrow appears, and apply/save. The supplied reference is cropped from the target HUD screenshot; `AionTargetBarMatches` checks the two end-marker shapes and ignores the changing name and health fill. You can also **CAPTURE REFERENCE** tightly around a single target arrow: small or tall captures track that marker alone, and combat releases when it disappears. Wider captures check both bar ends and allow padding around the markers. **TEST CONDITION** must pass with a target and wait without one. The initial 1×1 watch area blocks combat until calibrated. Startup and inspector messages identify an unloaded reference, missing marker or a watch area smaller than its reference, without resetting saved calibration.

Start with your configured hotkey while Aion 2 is focused. With no target bar, the mode turns the camera and taps Tab to select the next monster. Two successful checks confirm a target; it taps 1 for 70 ms, rechecks the target, then holds left mouse for auto attack. A missing bar for 300 ms releases left mouse and restarts the search. A capture failure, game focus loss, Stop or F7 releases generated input and stops combat. Searching stops after 24 unsuccessful attempts. The profile's `AionCombat` JSON settings include `MaxSearchAttempts` and an optional `MaxAttackMs` limit; its default 0 keeps attacking until the target bar disappears.

Camera turning defaults to holding right mouse for four 75-pixel relative steps. Uncheck **Hold right mouse while turning camera** if your game turns from movement alone. Adjust the pixel amount for your sensitivity; negative values turn left and zero disables turning while retaining Tab selection. No movement keys are sent. Keyboard and mouse output use the installed FakerInput driver, with no software-input fallback. Combat mode controls this profile while enabled; other saved rules remain available when the mode is disabled.

## Throne combat

Choose the `Throne` (or `Throne and Liberty`) game and open your macro. **USE 1920 × 1080 PRESET** uses the supplied screenshot's V area (1260,610,110×110) and main play area for Q (0,140,1540×710). For another layout, click **SET UP THRONE COMBAT**, draw a tight watch area where the V badge appears, then draw the area where the purple Q defence circle appears. The mode uses `Always → 1`, `V badge → V`, `PurpleRingMatches → Q`, and optional ready-image 7/8 healing; your other rules are preserved and ignored while this mode is on.

The bundled V reference matches the key badge rather than the changing skill artwork. If your UI size differs, select the V rule and **CAPTURE REFERENCE** around just the V badge. Use **TEST CONDITION** for V and Q with the game visible before starting. The Q detector looks for a hollow purple arc with radii from 12 to 120 pixels, allowing the outside circle to shrink without requiring an exact screenshot match. Keep the Q area tight to avoid unrelated purple effects.

Q waits 200 ms after the circle first appears, giving it time to move inward. Select the Q rule and adjust **Q delay after circle appears (ms)** to tune it: larger values press later; 0 presses immediately. The timer runs while 1, V and healing continue. Q is sent only while the circle is still detected; a vanished prompt does not leave a queued key press. **TEST CONDITION** checks detection immediately without the delay. The delay is a fixed time, rather than a measurement of ring size, and the poll interval adds to actual response time.

Each Q/V appearance is handled once. After either response, 1 resumes while the animation disappears. A prompt must stay absent for at least 120 ms to rearm; failed screen captures do not rearm it. The 1 rule's cooldown controls the minimum tap interval (100 ms minimum), with 70 ms key taps and the profile poll interval contributing to the actual pace. No E or 2/3/4 keys are sent by this mode.

Start using your configured hotkey while the game is focused. Combat pauses when a different window takes focus; F7 and the configured start/stop key cancel it as usual. Setup updates an opened macro; use **SAVE MACRO** for a new profile.

Click **SET UP 7 / 8 HEALING** to load the supplied HP frame and the ready centres of the two skill icons. The default low-HP threshold is 82%, matching the supplied 2746/3381 screenshot (about 81%). Change **Heal at or below HP (%)** for another threshold. Healing runs only when **Use 7 and 8 at low HP, when ready** and Throne combat mode are enabled.

The HP reader locates the gold HP frame near the upper-left HUD and measures green fill, excluding the changing numbers. A missing bar or failed capture blocks healing. At low HP it checks 7 and 8 independently against their ready images; a countdown/dimmed icon does not match. Q retains priority, followed by ready 7, ready 8, V and 1. A one-second debounce allows the game to display its cooldown and prevents rapidly repeating a rejected tap; the visual readiness check decides when a skill is usable again. If HP recovers or both skills are cooling down, combat continues normally.

The supplied skill areas are 984,990,58×58 for 7 and 1042,990,58×58 for 8. If your layout differs, adjust each skill's watch area and capture its ready centre; **TEST CONDITION** tests that icon's readiness, while the running mode also requires low HP. **CAPTURE HP BAR** selects the entire bordered green HP bar, excluding the portrait and mana bar, and watches that selected location.

## Licence

ClickyBot is released under the [ClickyBot Free Use Licence](LICENSE). It permits use, modification, and redistribution, but the app and modified versions must remain free of charge and may not be sold. Modified or redistributed versions must clearly identify changes and include attribution to the original ClickyBot project. This is a custom licence and is not an OSI-approved open-source licence because it prohibits charging for the Software.

## Run

```powershell
dotnet run --project .\ClickyBot.csproj
```

The project targets `net8.0-windows10.0.19041.0` and uses the Windows desktop runtime and Windows SDK OCR APIs. Cooldown detection needs the Windows English OCR language feature; if unavailable, timer conditions block actions and report the issue in the inspector. OCR is local and sends no images or text to a service.

## Cooldown timers

Choose `CooldownTimerAbsent`, then select just the central line where the skill's timer appears. Exclude the hotkey below the icon and any numbers in corner badges. Use a rectangle 16–160 pixels wide and 10–64 pixels high. The reader checks numbers such as `7s`, `35s`, `0.8s` and minute timers; it does not compare their value with the number in a saved picture. Use **TEST CONDITION** and the live inspector with the game visible to verify the area at your HUD scale.

Keep healing and mana requirements as colour coverage AND gates. To permit a fallback skill only while another skill is cooling down, use `CooldownTimerPresent` as its gate and select the other skill's central timer line. Timer conditions need no reference capture, target colour or percentage threshold.

An invalid, blank, unavailable or ambiguous reading blocks the condition, including an absent-timer condition. A successful reading with no detected countdown allows the condition; it does not check range, target, resource cost, menu state or other game requirements. Test replacement skill artwork before running. Start timer profiles with the configured hotkey while the game is focused; the entire profile pauses when that window loses focus.

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

## Live rule inspector

Select a rule and expand **LIVE RULE INSPECTOR** below the automation map. While stopped, it checks the current editor values on a separate copy, so unapplied edits can be previewed without changing your saved macro, cooldowns or trigger state. It sends no keyboard or mouse actions. The main condition and optional AND gate report **Passed**, **Waiting**, or **Unavailable**, with observed RGB, colour coverage or image matching details. Missing references and failed captures remain unavailable, rather than passing absence/difference checks.

The live image shows the selected watch/search area, scaled to at most 320 × 180 pixels, beside the main reference where applicable. It refreshes at most twice per second. Captures read the visible desktop, so keep the game area visible and avoid covering it with ClickyBot or other windows. The image is a preview; full-resolution matching still uses the existing detector.

While started, the inspector displays the engine's latest observation and its age instead of performing extra matching searches. Action status distinguishes waiting, cooldown, handled appearances and held keys in the ordinary engine. Throne healing reports its actual HP requirement, including an unavailable HP bar or HP above the threshold. Throne still controls Q/V/healing priority and rearming. Rules ignored by the active mode, disabled rules and unfocused games may have no fresh observation; that is shown explicitly. In resource navigation, controller timing and bar-fill transitions remain controlled by navigation rather than individual rule execution status.

Collapse the inspector or automation map to cancel its worker. Switching rules or starting/stopping clears pending preview results. Opening or closing the inspector does not start or stop your macro.

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

- `ClickyBot-Setup-0.1.50.exe` — compressed per-user installer. It installs to `%LOCALAPPDATA%\Programs\ClickyBot`, creates Start Menu and desktop shortcuts, and opens ClickyBot.
- `ClickyBot-Portable-0.1.50-win-x64.zip` — portable copy for users who prefer to extract and run the app.

The installer build requires Inno Setup 6. GitHub Actions installs it automatically before running the packaging script.

The installed app checks the latest GitHub release through the `SETTINGS` option when enabled. It only downloads a newer trusted ClickyBot installer after confirmation, then saves the current macro (including pending editor changes), closes ClickyBot automatically and opens the normal installer wizard so you can review and accept the ClickyBot licence before installation. The installer controls whether ClickyBot is launched after the update, so accepting its launch prompt starts only one app instance.

GitHub Actions can build the same Windows artifacts from `.github/workflows/build-windows.yml` when a `v*` tag is pushed or the workflow is run manually. Publish the installer and portable package as a GitHub release for the installed app to detect the update.

## Important limitations of this MVP

The reference matcher is deliberately lightweight: it stores raw RGB samples from a selected region and compares a sampled subset on each poll. It is not OCR or a scale/rotation-invariant computer-vision matcher. Some games also render through protected or exclusive fullscreen paths where normal desktop capture/input APIs may not work.
