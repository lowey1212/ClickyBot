# ClickyBot 0.1.38

Skill rules can now check for the absence of a cooldown countdown without capturing a ready image for each skill. `CooldownTimerAbsent` looks for numeric timer text in a selected central icon region; changing values such as `7s`, `35s` and decimals all block casting. `CooldownTimerPresent` supports fallback skills that require another skill to be cooling down. Healing and mana bar coverage remain independent AND gates.

- Add local Windows OCR with short-text context and a white-text mask, without comparing the timer value to a saved number. Missing OCR, failed/blank captures and ambiguous recognised text block both timer conditions.
- Add `RegionSnapshotDiffers` for a valid image capture that has no reference match at the configured threshold. Missing references do not pass an inverted condition, and absence supplies no matched mouse location.
- Add timer crop guidance to the primary/gate editors and timer diagnostics to the live inspector. Exclude bottom hotkeys and corner badges from timer selections.
- Bind profiles containing enabled timer rules to the foreground window at START. Focus the game and use the configured hotkey; leaving that window pauses the profile and releases generated holds.

Cooldown detection uses the Windows English OCR feature. The reader stays local. No detected cooldown is only a cooldown check; range, targeting and other game requirements still need their own conditions.

Validation: actual supplied `7s` and `35s` screenshots, eleven ready-artwork crops, generated countdowns covering all ten digits, multiple digits, decimals and minutes; unavailable/blank/malformed captures, cancellation, valid inversion and real engine failures; WPF timer/gate editors and existing inspector, image, hotkey, combat, navigation and bar recovery regressions. Built Windows installer and portable packages.

Assets: `ClickyBot-Setup-0.1.38.exe` and `ClickyBot-Portable-0.1.38-win-x64.zip`.
