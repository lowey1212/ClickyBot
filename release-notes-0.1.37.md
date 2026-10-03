# ClickyBot 0.1.37

Fixes macros stopping as soon as a ready skill is bound to the literal `-` key. The activity log previously reported "Requested value '-' was not found" and the engine returned to Idle.

- Support literal `-` and `=` bindings and the other unshifted punctuation keys in key press, key hold, and recorded combo actions.
- Reject malformed key names through the existing per-action error handling so other rules and subsequent polls continue running.
- Keep existing saved profiles and skill conditions intact; no macro edits are needed for `-` and `=` bindings.

Validation: reproduced the exact exception before the fix; tested punctuation key-down/key-up pairs using a simulated input sink, invalid-key engine continuity across three polls, and the existing regression suites. Built Windows installer and portable packages.

Assets: `ClickyBot-Setup-0.1.37.exe` and `ClickyBot-Portable-0.1.37-win-x64.zip`.
