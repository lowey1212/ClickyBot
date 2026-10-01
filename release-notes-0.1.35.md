ClickyBot 0.1.35 delays Q after the purple defence circle first appears, giving the outer circle time to move inward.

- Q waits 200 ms by default. Select the Q rule and adjust **Q delay after circle appears (ms)** to press earlier or later; 0 restores immediate input.
- The timer runs without blocking continuous 1, V chains or ready 7/8 healing. Q takes priority once the delay expires and the circle is still detected.
- A vanished circle does not leave a queued Q. Stable absence rearms a fresh delay, short detection flicker preserves it, and failed captures cannot count as continuous absence.
- Each circle still receives one Q. Starting the macro resets the timer. Existing saved macros receive the 200 ms default; custom values persist.
- TEST CONDITION reports circle detection immediately; START applies the delay.

Validation: all six regression suites pass, including exact delay boundaries, cancelled prompts, short flicker, fresh prompt rearming, failed captures, zero/custom delay persistence, continuing 1 while waiting, one Q per appearance and the actual WPF delay editor. Existing screenshot checks for Q/V and low-HP cooldown-aware 7/8 also pass.

The delay is a fixed time and can be tuned for gameplay. The polling interval and other input actions contribute to the actual response time. Live game timing has not been verified.

Windows assets: ClickyBot-Setup-0.1.35.exe and ClickyBot-Portable-0.1.35-win-x64.zip.
