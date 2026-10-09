ClickyBot 0.1.53 removes every right-mouse press from Aion combat. The optional camera branch no longer holds right mouse, its right-button checkbox is removed, and old saved HoldRightMouseToTurn settings are ignored and discarded on save. Optional camera movement uses relative movement with no button held.

Tab targeting, the first color hit triggering 1, held LEFT mouse attack, target-loss release, stop/focus cleanup, watch areas and reference captures retain their existing behavior.

Validation: simulated combat with an old profile explicitly enabling right-mouse camera turning produces no right-button reports; optional movement uses button mask 0. Existing targeting/attack/color/input checks and real WPF editor/inspector checks pass. Installer and portable packages built and verified. No real game input was sent during tests.
