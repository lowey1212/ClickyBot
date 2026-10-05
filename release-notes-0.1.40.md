# ClickyBot 0.1.40

Adds AIO2 quest prompt setup. Choose `aio2`, `Aion 2`, or `Aion2`, then click **SET UP ALT+1 PROMPT** to load the supplied key badge and a conditional Alt+1 combo.

- Select the search area under your minimap manually in the rule, then apply and save. The initial area cannot match, so setup sends no input before calibration.
- Wait while **Auto Move** is visible. Capture the gate reference around only the fixed text, excluding its changing distance. An unselected gate, missing reference, or failed read blocks input.
- Tap Alt+1 once when its badge is visible and Auto Move is absent, then wait for the combined condition to reset before rearming. Changing quest text is excluded from the reference.
- Preserve existing rules and the manually selected search area when setup is run again.

Validation: production image matching finds the actual badge in the supplied quest screenshot and at search-area edges; quest text without the badge and an unset area do not match. Auto Move presence blocks the absence gate, absence permits it, and unavailable or unselected gates block. Simulated input verifies Alt-down, one 1 tap, Alt-up, and cancellation cleanup. Image/input, WPF editor, and inspector regressions pass; Release build passes.

Assets: `ClickyBot-Setup-0.1.40.exe` and `ClickyBot-Portable-0.1.40-win-x64.zip`.
