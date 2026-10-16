ClickyBot 0.1.63 rolls back the periodic backup 1 behavior introduced in 0.1.62. The Aion combat runner is restored exactly to its 0.1.61 implementation: the original target, cooldown, range recovery and held LEFT sequence runs without additional timed 1 taps. Normal saved rules are again ignored while Aion combat mode is enabled.

The earlier gathering and color-sampling fixes remain. No new combat behavior is added. This higher patch version lets existing 0.1.62 installations receive the rollback through CHECK FOR UPDATES.

Validation: combat source compared directly with the v0.1.61 tag; existing image/input/combat/gathering, WPF editor/updater and inspector checks pass without game input. Installer and portable package versions and contents verified.
