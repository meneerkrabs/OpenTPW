# The advisor in the original lobby (not ported)

tp.exe runs the advisor in the front-end (lobby) game state; OpenTPW's lobby has none. This is the static reading of what the original does
(`ADVISOR-005`, `ADVISOR-025`), with the evidence for each claim. No original executable was run. Build: TPW `sp20_nocd/tp.exe`, SHA-256
`5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805`. The key panel the advisor's key advice refreshes is ported
([LOBBY-KEY-PANEL.md](LOBBY-KEY-PANEL.md)).

## The advisor lives in the whole front-end state

- The front-end state of the main state machine (`[0x87906C]` states 1 to 3, the jump table `0x5503A4`) sets the advisor up in state 1 (`0x54E5EC`:
  `0x429BA0` with the model path `%s\Global\Advisor`, `0x74D0C4`; `0x5989C0`) and, in the per-frame state 2, updates it (`0x599880`, `0x54E6DF`), draws it
  (`0x429DF0(0)`, `0x54E6E6`), runs the effect groups (`0x599800`) and then the lobby's own steps (`0x5D5C50`, `0x5D5CC0`, the message tube `0x4813B0`).
  State 3 tears it down (`0x598AD0`, `0x429E20(0)`, `0x54E7F5` to `0x54E7FC`). Nothing in that state's code tests which lobby screen is up: every
  screen of the lobby has the advisor, and it is drawn only while a response plays (the animation start shows the model, the sampler hides it, LIPS.md "Shown").
- The advisor is drawn before the lobby tasks draw, so it is lit by whatever ambient words `0x879DD8`-`0x879DE4` and fog state the active lobby screen
  left when it was entered. A screen sets them once, on entering: the lobby scene object (`[0xF83778]`, vtable `0x702A90`) sets them black in its setup
  `0x5D8B50` (also saves and clears the render flags, sets the fog colour `0xFF44DDFF`, start 50, end 300, and clears the fog flag `0x2000`, `0x5D8BF9`) and in its
  teardown `0x5D8D70` (which restores the saved flags and fog); the island view's enter `0x5E18D0` sets 0.267 grey (`0x5D9230(1, 0x444444)`, `0x5E1924`), the
  light colour `0xEEEEEE` (`0x5D92E0`), the fog flag (`0x5D9680`) and the fog 50 to 250 (`0x5D96A0`, `0xFF44DDFF`); the islands component's enter `0x5E1380` and the enter
  `0x5E23F0` of the island screen of vtable `0x702DD0` do the same (`0x5E13A9`, `0x5E1407`; `0x5E2445`, `0x5E24A2`); the globe's enter `0x5DCFE0` sets 0.125 grey (`0x202020`)
  and clears the fog flag (`0x5DD04C`). The advisor's own light is fixed (`0x429BA0`: direction 0.6, -0.6, 0.2, colour 1.0).
- Whether the advisor's materials are fogged is not read: the model draw adds the device fog bit `0x100000` to a primitive from the material flags `0x289`
  (`0x472DC5`, `0x464B55`) without a test of the fog flag `0x2000` that the terrain and object paths test.

## What makes it speak

The lobby's speech goes through the same response player as the park (`0x599050`, the id is the first field of a row of the 32-byte response table `0x768FB8`; it
refuses while a response plays, `[0xF7966C]`, or the speech is off, `[0x78D90C]`). The lobby queue is the task of vtable `0x702DA0` at `[[0xF85614] + 0x14]`
(created by `0x5E3DC0`, `0x5E3ECF`): its setup `0x5D5F70` clears the idle word `+0x18`, delay `+0x1C` and last time `+0x20`, its update `0x5D5F80` speaks the oldest queued id
(`0x5D5FCF`) when nothing plays, and otherwise, with the idle word set and the advisor silent for `+0x1C` ms (the timer `0x5F5FA0`, from the clock's start), queues `+0x24` once more
(`0x5D6037`); `0x5D6110(id, flush)` queues an id after emptying the queue when `flush` is nonzero, `0x5D61A0(id, delay)` sets the idle line, `0x5D6060` (its teardown) stops a
response that plays (`0x5994E0`) and empties the queue. The lines the lobby says, by response id (all with the caption tag `0x17F`, which has no text, so the lobby needs no message tube for them):

| Id | Said by | When |
| --- | --- | --- |
| 394 (`0x18A`) or 395 (`0x18B`), generated lines (samples 469, 470) | the island view's update `0x5E1830` (slot 2 of vtable `0x702EC0`, `0x5E1881`/`0x5E186D`) through `0x5D61A0(id, 90000)` | every frame while a player is current (`[[0xF81EBC] + 0x60] != -1`, `0x48BCD0`): the id is 395 for an odd `rand()` (CRT `0x67B5C0`) and 394 for an even one; so the line comes after 90 s of silence |
| 390 (`0x186`, sample 465), then 391 (`0x187`, sample 466) when no profile exists (`0x5C7BB0` counts the used of four slots), else 398 (`0x18E`, sample 471) | the player-select dialog `0x4A6580` (layout PlayerScreen `0x753C68`), which hides the lobby window `[0xF82884] + 4` (`0x4A659E`) | when it opens (`0x4A69E8`, `0x4A69FD`, `0x4A6A1B`); it sets `[0x7CB3FC]` to 1 when no profile existed |
| 393 (`0x189`, sample 468, **fixed sequence 28**) in the full simulation, 394 in Instant Action (game type `0xFB3B7C` == 2) | the dialog's close `0x4A6A50` (shows the lobby window again, `0x4A6A81`) | when a player is current and `[0x7CB3FC]` is set: before 393 `0x5AFC30` adds the first extra key to the player ledger (`+0x20`) and `0x5C8A10` saves the player (`0x4A6B27` to `0x4A6B4F`) |
| 570 (`0x23A`, sample 588) | the new player's name dialog `0x4A6E40` (layout NameDialog `0x753F50`) | when it opens (`0x4A6EA4`) |
| 592 (`0x250`), 593, 595, 596, 597 (`0x255`) | the postcard windows (the tables are the online postcard windows): Publish `0x4C4670` (`0x7595B0`, `0x4C484A`), `0x4C0AE0` (`0x759170`, `0x4C18DC`), `0x4C2110` (`0x7593E8`, `0x4C2342`), `0x4BFF70` (`0x758F40`, `0x4C02B2`), Outbox `0x4BC710` (`0x758878`, `0x4BCB50`) | when each opens (the online postcard help; OpenTPW's postcard composer is its own window, `[EXT:online-postcard-help]`) |
| 598 (`0x256`) | the globe screens' virtual `0x5DD7C0` (slot `+0x28` of vtable `0x702E70`, `0x5DD7DA`) after `0x5DD400` | when the virtual runs (not read further) |
| 600 (`0x258`) | `0x48BE80` (`0x48BEAD`, after re-entering the island view `[[0xF85614] + 4]`'s `+0x1C`), `0x4B86A0` (`0x4B879C`), `0x4C53F0` (`0x4C5591`) | not read further |

Fixed sequence 28 (response 393) is the only fixed sequence the lobby can say: its single entry is the timer of 17000 ms, which at the advisor clock (`0x599991`) starts the
Key particle (87 at (90000, 0, 7000)), plays `cat_ui` `0xC6` and calls `0x4B9340` (`0x5999CC`): the key panel's full refresh, which shows the key that `0x5AFC30` just
added. Sequence 29's responses (374 to 378, 444 to 467) are the golden ticket lines of the park.

## Size of a port

A lobby advisor in OpenTPW needs, on the existing `Advisor` (global rig, `SayResponse`, clock, effect groups) and the key panel:

1. A front-end host: an `Advisor` with its own clock, updated and rendered with the lobby frame and drawn over the lobby screens at `Advisor.BoxRectangle` (`AutomaticAdvisor`
   is bound to a level; the lobby host has none, no message tube and no captions): about 150 lines and a hook in the game flow's frame function.
2. The lobby queue (`0x5D5F80`, `0x5D6110`, `0x5D61A0`, `0x5D6060`) with the 90 s idle line and a presentation-only coin for 394/395: about 150 lines.
3. The triggers: the idle line needs "a player is current", which OpenTPW's single player always is (`[EXT:no-player-profiles]`); the other lines belong to dialogs OpenTPW
   does not have (player select, new player, the postcard windows are its own online windows, `[EXT:online-postcard-help]`). The key advice (393) has a natural counterpart in
   OpenTPW's "New Full Simulation" consent with its starter key, which is a design decision (a first-key line where the original says it, at the creation of the first player).
4. The lobby ambient and fog for the advisor (0.267 grey on the island screens, 0.125 grey on the globe; the fog question above) in `advisor.shader`.
5. The timer's Key effect and sound in the lobby (the screen-space host exists) and the key panel's refresh (`RefreshKeyPanel`).

About 600 to 900 lines with tests, and a visible new feature (a talking advisor in the lobby), so it needs a decision before it is built.
