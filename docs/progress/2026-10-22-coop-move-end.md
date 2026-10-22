## Co-op: the Move tool's end is a command (October 22, 2026)

The command layer now hears when a player's Move tool ends ([COOP.md](../COOP.md), "The command layer"; `[EXT:online-coop-commands]`). The Move tool's placement skips research and the balance test, so the layer accepts it only for the type the same player picked up. Until now that type stayed carried after the tool ended, so a peer's command could place it again without either test; the co-op join smoke found it as a stale carried entry in a later capture.

- Whatever ends the Move tool (a placement without exactly Ctrl, the queue or track tool a placed ride opens, a cancel, another tool) issues an `EndMoveCommand`, and the player carries nothing after it.
- While the tool stays (a placement holding exactly Ctrl), another placement of the type is still the Move tool's and is charged in full, as in the original.
- A pick-up while the tool still carries ends the old move first.
- Recorded command logs from before replay as they did: they hold no `EndMoveCommand`, and a single-player park's world does not change.
- The co-op join smoke now requires its second capture to carry no Move entry, as it requires the other tool states the first capture staged to be gone.
