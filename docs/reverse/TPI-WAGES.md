# Theme Park Inc: wage demands, the wage negotiation, the dismissal and strikes

Static research only, on the no-CD `Game.exe` (SHA-256 `77ea0c41…`); the original was not run. OpenTPW's port is `ThemeParkIncWageNegotiation` (the decision),
`ParkEconomy.ThemeParkIncWages` (the demands, the negotiation's end, the dismissal and the month's strikes), `ThemeParkIncStaffWorld.Strikes` (the strike states and the destroy of a
dismissed member), `ThemeParkIncWageDemandWindow` and `ThemeParkIncWageNegotiationWindow` (the two windows) and `ThemeParkIncHud.Wages`. The staff economy around it (the wage review, the
grievance and the month's escalation) is in [TPI-STAFF.md](TPI-STAFF.md) §4 and §7; the tag tube in [TPI-TAG-TUBE.md](TPI-TAG-TUBE.md).

## 1. The demand

- **The review** (`0x62CDA0`, after each payday and salary change) records the shortfall `+0x270` and, at low motivation, a demand day `+0x26C = rand() % 27 + 1` (CRT `rand()`
  `0x870D38` at `0x62D038`, drawn from the world generator under `[EXT:tpi-staff-crt-rand]`). The expected wage is computed inline at `0x62CE35..0x62CE7A`, and that code is the same as
  `0x62CD30` (`ExpectedWage`): e above MaxExperience takes the role's MaxWage (0 for another kind), a negative e takes 0, else `((e << 14) / MaxExp × M) / 16384` toward zero; every branch
  takes off `w % 20` and answers at least 10. It then adds the raw `InitialWageModifier` of the agency (`0xA02420 + 36 × agency`). The port's description was right; Ghidra's
  "`InitialWageModifier + 10`" misread the `max(w, 10)`.
- **The day routine** (`0x628CA0`) on the demand day, at most once per 30 game days for all staff (`0xAA07F4`): the member's older demand rows go (`0x636C90(0x90, handle)`), the advisor
  message 0x1C8 is posted, the kind is 0xA (a grievance in a park younger than `MinMonthsForStrike` months) or 0xB, and the letter `0x5CEFB0(kind, member, shortfall)` is posted as
  message 0x19, which the tag system files at once.
- **The letter** (`0x5CEFB0`, vtable `0x93DFD0`): sender row key 5 of `0x9CD6D8` (Miss Moody, Human Resources Director, picture 5); subject TAG_SYSTEM 0xFB "Urgent Wage Demand";
  body 0xFC (kind 0xA, "…They're threatening to call a strike…") or 0xFD (0xB) with the member's name (parameter 8) and the demand (9); the row fill `0x5CF3F0`: tag 0x90, kind word 0x97,
  go-to 0xE with the demand at `+0xE18`, the member's handle at `+0xE14`, category 2; no expiry; `0x5CD060` accepts only kinds 6, 7 and 8 (`0x5CD063..0x5CD070`), so no letter window opens for any `0x5CEFB0` kind (0xA to 0xD) and the row is unread.
- The training letters come from the same constructor: kinds 0xC (`0x62AC4D`, the training's finish) and 0xD (`0x62E8D6`, its cancel) with subject 0x4E/0x50, body 0x4F/0x51 (the
  name and the ability), tag 0x8E/0x8F, go-to 5 (the staff window `0x58C6C0`, which the text HUD's staff panel stands in for), category 0.
- The debug menu's test demand (`0x5382A2`, `rand() % 200`) is not a play path and is not ported.

## 2. The wage demand window

- **Opening.** The tube window's go-to (`0x5511B0`) sends kind 0xE to `0x55147A`: `0x59ABB0` on the controller `0xA6C2B0` with the thing of `+0xE14` and the demand `+0xE18`. The builder
  does nothing while its window is open; else `+4` = the member, `+0x14` = `+0x18` = the demand, the HUD's window closes, table `0x9C9F64` (procedure `0x40CCAC` → `0x59A8A0` →
  `0x59A900`).
- **Controls** (`0x59ACA0`): the title 0xCA199 (UITEXT 0xFB "Wage Demand"), the member's name (0xCA187), "Your Offer" (0xCA197, UITEXT 0xFC) over the varibox 0xCA185 (range
  `AllStaffConstants.MinWage`..`MaxWage`, step `WageStep`, value `+0x14`; its value field is layout command 19, a type-5 node the builder makes without a stream, `0x84E56F`), the demand
  text 0xCA186 (UITEXT 0xFD with the offer word as parameter 13), the statistics labels (UITEXT 0xBE, 0xBF, 0xC1, 0xC3, 0xC4, 0xC5, 0xC6) with the salary, the agency (UITEXT
  `0x6E + agency`, table `0x9D2CD8`) and the ability (ABILITIES.str), and three gauges refreshed every two seconds (`0x59B9F0`): happiness (`+0x230`, happygrad), motivation (`+0x234`,
  ridestatbar), experience (`(+0x210 << 10) / MaxExperience`).
- **Procedure** (`0x59A900`): message 0x800 stores the offer; command 0xCA184 (b_okay) or -1 opens the negotiation with (member, `+0x18`, `+0x14`); 0xCA182 (b_close) and -2 (b_cancel)
  first set the offer to 0; 0xCA183 (b_fire) dismisses the member (`0x629040`) without a negotiation; message 5 (the HUD's) and the Escape key (the default procedure `0x507FA0` posts
  message 4 on key 0x1B) close the window without one.
- Not shown: the member's picture (0xCA198, `0x502B60` renders the person) and the "Employed for" value (UITEXT 0x3B8 with the days since the hire date `+0x208`, `0x6297E0`, which
  OpenTPW's member does not keep).

## 3. The negotiation

- **The dialog** (`0x559C40`, table `0x9BE518`, procedure `0x559DA0` → `0x559860`): `+4` member, `+8` offer, `+0xC` demand; the title 0xB28A (UITEXT 0xFE), the name pair 0xB287 (0xA2)
  and 0xB288, the body frame 0xB286, the choice panel 0xB28C with the `checkbox2` toggles 0xB28D and 0xB28E and their labels (UITEXT 0x102 "Accept the demand", 0x103 "Let them leave").
- **The decision** (`0x559E00`): o = salary + offer, d = salary + demand (32-bit); o ≥ d gives 2 (body UITEXT 0xFF); else the single-precision `o / d × 100` (the game thread's x87 runs
  at the 24-bit control word, so the quotient and the product round to singles) below `100 − PercentageForBadWageDecision` gives 0 (body 0x101 with the demand as parameter 13), else 1
  (0x100). Results 1 and 2 hide the panel; 0 shows it with 0xB28D selected.
- **The close** applies the decision: b_okay and b_close post message 4 (`0x5599A1`), as do the HUD's message 5 and the Escape key; the destroy's message 0x14 (`0x5598B6`) then, for
  1 or 2, deletes the member's demand rows (`0x636C90(0x90, handle)`) and sets the salary to salary + offer (`0x62D160`: at least MinWage, the review only when it changed); for 0, the
  toggle 0xB28D sets offer = demand and does the same, 0xB28E dismisses the member.

## 4. The dismissal (`0x629040`)

1. A striker (display state 4) who is his role's only striker clears the role's strike slot (`0x634390`: active and level to 0, the turn kept).
2. One salary is paid (the bank's debit `0x5C07B0` and the park's wage total `+0x1FA58`).
3. Every tag tube row keyed by his handle goes (`0x636B10`).
4. The thing is destroyed (`0x637C40`): message 0x21 with the handle to the staff HQ (thing 1: `0x634730`, the slot release), the research lab (thing 6, `0x61DAD0`: a researcher
   leaves both teams; another member who is a consultant is replaced by `0x61D970`, whose staff walk still finds the leaver), every attraction (`0x5DEF4E`: the claim word that names
   him clears) and a guard's chase (not ported); then the kind's destroy: the role list's unlink, the handyman's litter claim (`0x5D9010`), the mechanic's or gardener's object claim
   (`0x5E37B0`: a claimant goes idle, the claim clears), the entertainer's or guard's cell effect off at the stored cell with the current radius (`0x5D7530`), the researcher's lab state
   (`0x61E4E0`); the person's destroy (`0x609130`) and the thing's queued destroy (`0x6493B0`).
- A resting member's staff room keeps the occupancy his rest took: no destroy decrements it.
- Not ported: the pick-up tool's reset (`0xA434B4`; the pick-up is not ported) and the sound 0x13 at his position (`0x60B700`).

## 5. Strikes

- **The month** (`0x632C20`, the staff HQ's message 0x0C; the HQ is thing 1, so it runs before the paydays' reviews): a role on strike ends it (`0x6342B0`, message kind 5); the others are
  considered (`0x6330B0`; TPI-STAFF.md §7). The messages (type 0x17) reach only the advisor's listener `0x7176E0`.
- **The join** (`0x62BBA0`, the gate before every search): the role's active word (`0x634210`) and the gate's script variable 1 (`0x64FA50`, VAR_STATUS) equal to 1;
  `[EXT:tpi-park-open]`: the Theme Park Inc park runs no gate script, so its gate stands open and the test reads 1. `+0x1A0 = 1`, four world draws (x = `StrikeAreaStartX + R %
  StrikeAreaSizeX`, the x fraction `R & 0xFF`, y likewise), the staff route `0x62EB10`; a route sets state 4, none falls through to the rest branch.
- **State 4** (`0x628855`): the walk step; arrived or stuck: `+0x1A0 = 0` and state 5. The setter gives the speed bonus 25 and template 0xF.
- **State 5** (`0x62A4C0`): one world draw a tick (a presentation turn of the sprite angle); a closed park without guests (`0x64FA30`, `0x5B2060(4)`) ends the strike; once the role is
  no longer on strike the member routes to the centre of `EntranceAPosX/Y` (state 1) or idles. The setter clears the angle, the bonus and queues template 0x1B, which is tp.exe's strike
  template 0x13 opcode for opcode (SLOT 5, FRAME 0, 1, 2, 3 and six 0s).
- Every role's table sends 4 and 5 to the common handler `0x6287A0` (handyman `0x5D58C4`, mechanic `0x5DA358`, gardener `0x5D2B68`, entertainer `0x5D1F70`, guard `0x5D3DD8`, researcher
  `0x61B620`'s default). Both states show "On strike" (display state 4) and count the on-strike day counter.

## 6. Saves and labels

- The Science continuation save writes `TpiWages` (rules 1); a Science save without it is refused before its assets are checked (refuse and document); a Theme Park World save carrying it
  is refused. Members in states 4 and 5, the strike records and the demand and training rows round-trip.
- `[EXT:tpi-capability-gates]`: the advisor message 0x1C8 and the strike messages are noted, not said (Theme Park Inc's advisor queue takes only the forecasts and the letters so far). `[EXT:tpi-text-hud]`: the
  training letter's go-to opens the text HUD's staff panel. `[EXT:tpi-staff-crt-rand]` covers the demand day. No other CRT `rand()` is on these paths.
