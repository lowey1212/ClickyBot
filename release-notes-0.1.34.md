ClickyBot 0.1.34 adds low-HP skill 7/8 handling to Throne combat.

- At or below 82% HP (the supplied 2746/3381 example), use each of 7 and 8 only when its own ready icon is visible. The threshold is editable.
- Measures green HP bar fill independently of the changing numbers; finds the bordered HP bar in the upper-left HUD. Missing HP frames and failed captures block healing.
- The supplied 9s and 3s cooldown icons do not match the ready references. Each healing skill has an independent one-second debounce, and can be used again when its visual cooldown ends while HP remains low.
- Q defence keeps priority, then ready 7/8, V chains and continuous 1. Healthy HP or cooling-down healing skills do not interrupt normal combat.
- Includes healing setup, an enable switch, threshold control and HP-bar capture for other layouts. Ready 7/8 images and the HP frame are bundled in both Windows packages.

Validation: all six regression suites pass. New checks read the supplied HP example as 81.55%, locate the shifted full-game HUD at 74.27%, reject missing HP and mana-only frames, stop the threshold at full HP, accept the actual ready skill images and reject both supplied cooldown icons. Scheduler checks cover Q priority, each skill's independent cooldown, reuse at sustained low HP, and healthy/unknown HP blocking healing. WPF setup checks verify loaded references and preservation of existing Q/V areas and unrelated rules.

Detection is validated using supplied screenshots and simulated state changes. Live gameplay input acceptance and timing have not been verified; re-capture ready skill centres and the HP bar if the UI layout, abilities or scale differ.

Windows assets: ClickyBot-Setup-0.1.34.exe and ClickyBot-Portable-0.1.34-win-x64.zip.
