# ClickyBot 0.1.41

Adds **SET UP F PROMPT** for AIO2. The supplied F badge triggers repeated F taps while visible, with a 500 ms cooldown between taps. Select its watch area manually in the rule, then apply and save.

- Stop tapping when the F badge disappears or Auto Move is visible. New F rules inherit the Alt+1 rule's Auto Move gate; unconfigured or unreadable gates block input.
- Preserve existing Alt+1 rules, saved watch areas, and Auto Move calibration. Repeating F setup updates its reference without adding duplicates or resetting selected areas.
- Add F before Alt+1 so interaction checks run first. The initial 1×1 search area sends no input before selection.

Validation: actual supplied F badge detection, absent/unset-area rejection, simulated F down/up, repeating-tap configuration, inherited Auto Move gate, rule preservation, setup repetition, and save/load. Existing image/input and WPF editor regressions pass; Release build passes.

Assets: `ClickyBot-Setup-0.1.41.exe` and `ClickyBot-Portable-0.1.41-win-x64.zip`.
