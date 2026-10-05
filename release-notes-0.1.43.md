# ClickyBot 0.1.43

Adds a per-rule **Keyboard input mode** option for applications that handle Windows-generated input differently. Under THEN — action, choose **Windows key codes (compatibility)**, apply, and save. Existing profiles retain **Scan codes (default)**.

- Apply the selected format to key presses, key holds, and all keyboard steps in recorded combos, including Alt+1 modifiers.
- Preserve tap duration and combo delays. Stop releases generated keys using the same format as their down events.
- Include the selected input format in the activity log for taps and combos.
- Preserve image conditions, watch areas, AND gates, and unrelated rules.

Both formats use the standard Windows SendInput API. This provides a compatibility test, not a guarantee of Aion 2 support. A successful send reports Windows accepting the events; the target application may still ignore them. Logitech G HUB macros working does not establish that the game accepts this API.

Validation: simulated Windows key-code F down/up, Alt+1 ordering, cancellation during a held modifier, independent cleanup of both input formats, legacy/default and save/load settings, plus the real WPF editor's selection/apply/load and action visibility. Existing image/input and editor regressions pass. Installer and portable builds pass. Live Aion 2 acceptance remains unverified.

References: [Microsoft KEYBDINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput), [Microsoft SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).

Assets: `ClickyBot-Setup-0.1.43.exe` and `ClickyBot-Portable-0.1.43-win-x64.zip`.
