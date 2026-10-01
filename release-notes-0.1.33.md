ClickyBot 0.1.33 adds a simple Throne combat mode: continuously tap 1, respond to V chains and Q defence prompts, then resume 1.

- Q takes priority over V, and V takes priority over the next 1 tap. Each prompt is handled once while it stays visible; 1 resumes immediately after the response.
- Detects the purple defence ring at multiple shrinking sizes without matching the background or waiting for a particular circle size.
- Matches the V key badge, allowing the skill artwork above it to change. The supplied full game screenshot detects V at 1313,692.
- Includes a 1920 × 1080 screenshot preset and a two-area setup for other layouts. The preset watches V in 1260,610,110×110 and Q in the main play area at 0,140,1540×710.
- Uses only Q, V and 1 while combat mode is enabled. Existing E/2/3/4 and unrelated rules are preserved and ignored by this mode.
- Pauses combat when another window takes focus. The normal start/stop and F7 panic hotkeys remain available.

Validation: all six regression suites pass (editor, image/input, hotkeys, missing-bar recovery, resource navigation and Throne combat). The new checks cover priority and returning to 1, prompt flicker, failed captures, the real V badge with different skill artwork, the supplied Q prompt, seven shrinking ring sizes, and rejection of the purple beam and character effects in the supplied game screenshot. Real WPF checks cover setup, bundled reference extraction, Q editor fields and game-specific controls.

Detection is validated against the supplied screenshots and synthetic ring variations. Live gameplay input acceptance and timing have not been verified. The Q detector supports ring radii of 12–120 pixels; adjust watch areas or re-capture the V badge if the game layout or UI size differs.

Windows assets: ClickyBot-Setup-0.1.33.exe and ClickyBot-Portable-0.1.33-win-x64.zip.
