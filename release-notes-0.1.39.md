# ClickyBot 0.1.39

Fixes a ready healing icon being mistaken for a cooldown number. The gold cross artwork for skill 8 contains a tiny bright mark which Windows OCR interpreted as `1`, preventing the healing rule from passing even when the skill was ready.

- Ignore OCR words shorter than six original screen pixels vertically, which excludes this decorative mark while retaining normal HUD countdown text.
- Preserve independent health and mana bar gates, timer conditions, and existing macro actions.

Validation: the actual ready skill 8 cross artwork now reports no cooldown; supplied `7s`/`35s`, all ten digits, multi-digit, decimal and minute countdowns still block casting. Existing invalid capture and cancellation checks pass. Also verified the supplied Space prompt badge across a full 1920×1080 search, including the screen corners, absence, and a single simulated Space key tap.

Assets: `ClickyBot-Setup-0.1.39.exe` and `ClickyBot-Portable-0.1.39-win-x64.zip`.
