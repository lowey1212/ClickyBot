# ClickyBot 0.1.36

Add a live rule inspector beneath the automation map, inspired by the reviewed Chimpeon workflow.

- Compare a scaled live watch/search area beside its saved reference.
- See the main condition and optional AND gate separately, with RGB, coverage and image-match diagnostics.
- Preview unapplied editor values while stopped without sending input or changing saved rules, cooldowns or trigger state.
- Reuse engine observations while running, with observation age and ordinary-engine action status. No duplicate image searches are added to a running macro.
- Show Throne healing's actual HP requirement, including a missing bar or HP above the configured threshold.
- Treat failed pixel/region captures as unavailable and block execution when an AND gate cannot be read.
- Cancel previews when collapsed, hidden, changed or closed; cap live image buffers at 320 × 180 pixels and serialize inspector work.

Validation: InspectorRegression, EditorRegression, ImageMatchRegression, ThroneCombatRegression, ResourceNavigationRegression, MissingBarRecoveryRegression and HotkeyRegression. Includes real WPF inspector refresh/selection/collapse checks and a rendered layout review. Windows release build and self-contained installer/portable packaging.

Live gameplay effectiveness still depends on the game's desktop capture and input behaviour. A desktop preview can show covering windows; it is not a game-memory view. Special combat/navigation modes retain their own scheduling and priorities.

Assets:

- `ClickyBot-Setup-0.1.36.exe`
- `ClickyBot-Portable-0.1.36-win-x64.zip`
