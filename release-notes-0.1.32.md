ClickyBot 0.1.32 lets you remove individual steps from a recorded combo.

- After stopping recording, click DELETE beside any step to remove just that input event.
- The remaining steps keep their order and custom delays, and their numbers and the step count update automatically.
- APPLY COMBO keeps your edits; CANCEL leaves the original combo unchanged.
- Includes the previously local keyboard compatibility fixes: 70 ms key taps and a physical start/stop hotkey fallback for fullscreen games.
- Resource navigation and stamina options now appear only for the `pax` game group.

Validation: all five regression suites pass (editor, keyboard/mouse and image matching, hotkeys, missing-bar recovery, and resource navigation). The real WPF editor checks cover deleting middle, first, and last steps, preserving custom delays, and retaining the original rule until edits are applied. Input timing checks use a simulated input sink.
