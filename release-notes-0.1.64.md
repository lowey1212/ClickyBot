ClickyBot 0.1.64 adds the Aion 2 `aion-2-cleric` macro alongside the existing Templar macro.

It repeats only right mouse and top-row 2 through FakerInput, at the current pointer position. Both inputs are released after each short tap. The preset requests a 1 ms down pulse and a 1 ms polling interval, with no cooldown, reaction delay or extra action delay; actual speed depends on Windows scheduling and game/driver processing. It sends no Tab, 1, left mouse or movement and uses no target/cooldown detection.

Start with your configured hotkey while the game is focused. Focus loss pauses input; Stop/F7 releases it. The installer and portable ZIP include the preset, and installation preserves an existing Cleric macro. Templar combat behavior is unchanged.

Validation: simulated driver checks for exact right-button/2 reports, no pointer movement or other keys, cancellation and driver failures; existing input/combat checks and WPF editor checks. Installer and portable package contents and versions checked. In-game acceptance and actual tap rate require a live game session.
