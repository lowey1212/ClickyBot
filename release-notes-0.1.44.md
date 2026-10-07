ClickyBot 0.1.44 adds **TEST F INPUT (FREE)** to the Profile panel for `aio2`, `Aion 2`, and `Aion2`.

Stop the macro, click the test, and focus Aion 2 within five seconds. ClickyBot requests one 200 ms F tap using the legacy Windows `keybd_event` API. No driver, paid licence, or image-area configuration is needed. The activity log identifies the method and explains that submitting input does not confirm game acceptance.

The test requires AION2.exe in the foreground, rejects held modifiers or F before starting, releases early when focus changes, and cancels through F7, the test button, macro start, or application close. It does not change saved macros or their keyboard input modes.

Validation: fake-input regression checks cover timing, foreground guards, cancellation before/during the tap, exception cleanup, and truthful logging. Real WPF editor checks cover the new control and prevention of concurrent macro start. Aion 2 acceptance remains unverified and requires an in-game test.

Assets: `ClickyBot-Setup-0.1.44.exe` and `ClickyBot-Portable-0.1.44-win-x64.zip`.
