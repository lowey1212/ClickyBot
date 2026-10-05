# ClickyBot 0.1.42

Adds **SET UP ESC SKIP** for AIO2. The supplied SKIP word and arrows trigger a single Esc tap. Select its watch area manually, then apply and save.

- Trigger once when SKIP is visible and Auto Move is absent; wait for the combined condition to reset before triggering again.
- Inherit the Auto Move gate from Alt+1 or F, preferring a calibrated gate. An unconfigured or unreadable gate blocks input.
- Preserve existing macro rules and watch/gate calibration. Repeating setup does not add duplicates or reset selected areas.
- Use image similarity for the SKIP prompt to tolerate modest brightness changes. The initial 1×1 search area cannot match before selection.

Validation: actual supplied SKIP prompt and brightness variation, absent/unset-area rejection, simulated Esc down/up, once-per-appearance configuration, inherited Auto Move gate, rule preservation, repeated setup, and save/load. Existing image/input and WPF editor regressions pass; Release build passes. Live game acceptance still depends on matching permissions and the game's input handling.

Assets: `ClickyBot-Setup-0.1.42.exe` and `ClickyBot-Portable-0.1.42-win-x64.zip`.
