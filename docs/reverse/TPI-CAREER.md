# Theme Park Inc: the career, the mission files and the Global Master Save

November 21, 2026. Static reading of Theme Park Inc's no-CD `Game.exe` (SHA-256 `77ea0c41…967a`) through REA (Ghidra 12.1.4) and the
annotated listings; the original game was not run. This page summarises what OpenTPW ports (`ThemeParkIncSamObject`, `ThemeParkIncMission`,
`ThemeParkIncCareer`, `ThemeParkIncGlobalSave`, `ThemeParkIncSession`) and what it leaves open. The research notes behind it are
`OpenTPW-notes/research/tpi-campaign/r1-zones.md`, `r3-mission-thing.md`, `r4-mission-ui.md`, `r5-gms.md` and `research/tpi-staff/missions-system.md`.
No game data is reproduced here.

## 1. SAM-bound objects and the mission file

**Binding (`0x418F60`).** Every SAM-loaded object has a schema table of 0x3C-byte records: `+0` the kind, `+4` the name, `+0x24`/`+0x28` the range of
an integer-range leaf, `+0x34` an array's element count. Kinds: 0 struct begin, 1 struct end (carries the struct's name), 2 array begin, 3 array end
(name and count), 4 integer, 5 non-negative integer, 6 integer in `[min, max)`, 7 to 9 the float kinds, 10 string, 11 map variable, 12 end. The data
base is the object's `+8`. Each leaf takes the next dword slot from slot 1. A leaf inside an array has the stride of the array's leaves. An array
consumes `count × leaves` slots plus one **count dword** after them.

**Reset (`0x419830`).** Every element of every leaf becomes 0. A range leaf becomes its minimum. Every count dword becomes 0.

**Parser (`0x419CD0`, file load `0x419B20`).**
- A missing file fails and changes nothing. An empty file succeeds without the reset.
- Any other file is parsed, with the reset first unless the caller overlays (flag 1).
- Carriage returns are removed. After the leading white space, `#` or a newline skips the line.
- A key is a group of up to 64 alphanumeric bytes, then `.` or `[digits].`, then up to 16 fields joined by `.`. Each field starts with a letter, and
  each takes the next value.
- A value is optional white space, an optional `-` and digits, followed by white space or the end. It is converted by `atol` (32-bit wrap).
- The resolver (`0x4192C0`) takes the **last** struct or array end with the group's name. An indexed key needs an array and an index below its count.
  The field is the nearest leaf of that name before the end record. Names compare byte for byte.
- An array element raises the count dword to `index + 1` before the kind's check. Kind 5 refuses negative values; kind 6 refuses values outside its range.
- **Any failure stops the parse and returns 0.** The lines before it keep what they set.
- A line is skipped from its first byte after its parse. A key after `k` blank lines is therefore parsed `k + 1` times (idempotent).

**Mission object (schema `0x9A0C68`, 0x330 bytes at career `+0x10B4`).**
- `Info{Number, NoResearch [0,2)}`
- `Completion{ThemeOpened [0,2), ThemeNumber, CutsceneObject, ObstacleOpened, EndOfGame [0,2), LoadNewHoardings [0,2), EndingTheme}`
- `TechLevel{Theme [0,3), NewMaxGroup}`
- `BoundaryClicks[64]{X, Y}`
- `ExitStatistic[5]{TextId, Type, InTheme, TargetValue, TargetTime, TargetObject, TargetStaffType}`
- `Obstacle{Type, InTheme, ResearchEffort, RemoveEffort, GoldenTickets}`
- `ObstacleStaff[5]{StaffType, Ability, Number}`
- `Promotion{Number, StockAward}`
- `RequiredChallenges{Theme, Number}`

The offsets OpenTPW binds are those of `missions-system.md` §1.2; `ThemeParkIncMissionTests` checks them on synthetic files and on all 18 shipped
`missions\miss00..17.sam`.

**Loader (`0x74C050`).** It parses `"%s\Miss%02d.sam"` for the career's counter. When that succeeds, it copies each exit statistic into its runtime
status block: state 0, type, target value, target time, zone, and the value, days and baseline set to 0. A failed parse leaves the statuses alone.

## 2. The career object `0xBFC484`

The career is 0x4740 bytes (constructor `0x74B490`, reset `0x74BB40`). OpenTPW keeps every field the global save carries.

| Offset | Field | Reset |
| --- | --- | --- |
| `+0` | firing line | 5 |
| `+4` | game finished | 0 |
| `+0x20..+0x29` | flags (`+0x20` = objectives letter posted) | 0, then 1 × 9 |
| `+0x2A..+0x31` | park awards held | 0 |
| `+0x38` | swear filter | 1 |
| `+0x50` | new career (the prebuilt Science start) | 1 |
| `+0x54`, `+0x58`, `+0x5C` | CDK registration count, cursor, 256 × (id, level, flag, amount) | 16, 0, the 16 built-ins of `0x74AC30` |
| `+0x105C` | mission counter | 0 |
| `+0x1060` | rank | 1 |
| `+0x1064[15]` | discovered staff abilities | 1 for 1, 7 and 9 |
| `+0x1074` | golden tickets | 0 |
| `+0x1078[15]` | opened overview-map regions (dwords) | only region 14 |
| `+0x10B4` | the mission object (§1) | `Miss00.sam` |
| `+0x13E4`, `+0x13E5` | obstacle researched, removed | 0 |
| `+0x13E8 + 0x30i` | five exit-statistic status blocks | state words 0, zone 4 |
| `+0x14EC` | refresh (`.GMI`) flag | |
| `+0x14F0` | `mHistory[0x284]` (raw; writers not traced) | constructor `{0, -1, 0, 0, 0}` |
| map at `+0x3C`, `+0x4C` | zone records by name, the current one | empty |

**Operations OpenTPW ports.**
- **Advance (`0x74C1B0`).** The counter rises, the obstacle bytes and the statuses clear, the region the new counter names in table `0x9E907C` opens,
  and the next mission loads.
- **Promotion (`0x74C450`).** The rank becomes `n`. The firing line rises to the word at `0xA028F8 + 4n` (32-bit, no bound) when it is below.
  - That address is in the Standard.sam object `0xA01A60` (schema `0x996684`, 590 records), which OpenTPW binds from the files' bytes (`ThemeParkIncStandard`).
  - Ranks 0 to 5 read `FiringLineForRank[n].Limit`, rank 6 its count dword, ranks 7 to 26 `ChallengesInThisLevel[n - 7].ChallengeType`, and so on through the object.
  - A word outside the object or on a string pointer is not read (`[EXT:original-undefined-reads]`): the rank is still set and the line kept. Nothing throws.
- **Tickets.** `0x74C890` adds; `0x74C8C0` spends only when there are enough.
- **Abilities (`0x74C3C0`).** The obstacle staff with a number discover their ability.
- **Zone records.**
  - `0x74F4B0` gets or creates a record. The constructor `0x74FC80` reads the zone's `global.sam`: `d_available = AvailableFromStart == 1`.
  - `0x74C490` makes an existing record available.
  - `0x74F330` makes a record current.
- **Career side of the completion tick (`0x5F0940`).** The obstacle's tickets are spent (the result is ignored). A mission that opens a theme opens
  it. The end-of-game mission sets `+4`; any other mission advances.
- **Challenge win (`0x5C4250`).** It adds the golden tickets. A `PromoGiven` posts message 0x25 (promotion). The current zone record counts the win
  (`+0xF0`) and marks all done (`+0xF4`) when every slot is empty or won.
- **Challenge loss (`0x5C4857`).** The firing line falls by one on a slot's first loss.

## 3. The Global Master Save (`.GMS`)

### 3.1 Layout

- **Header** (`0x74C8F0`): the current theme (3 for none) and version 12.
- **Body** (`0x74D840`):
  - the tickets, awards, CDK table, count and cursor, swear filter, new-career flag and the ten flags;
  - the counter, rank, firing line, abilities and the two obstacle bytes;
  - the five status blocks, `TATS`, the 15 regions, `AMTA`;
  - the zone records (`u32` name length, the name, 215 bytes);
  - `MEHT` and the options (`0x45A130`, 59 bytes);
  - the refresh flag, game finished, `mFirstHourTime` and the history;
  - the `0xA6C0A0` block (96 bytes), `DTOT` and the shares (182 bytes);
  - `RAHS`, the `0xAA0180` flags (`u32 n`, `n` bytes, a byte, three dwords);
  - `MAPS` and the ambient tags (`u32 k`, `k × 16` bytes).

The markers are dwords, so the file reads `TATS AMTA MEHT DTOT RAHS MAPS`. OpenTPW keeps every section, the untraced ones as raw bytes.
`ThemeParkIncGlobalSave.Write` reproduces the three corpus files byte for byte, read directly and through the career.

### 3.2 Lenient load (`0x74D110`)

- The career is reset and the zone map emptied before the file is read.
- A header version below 12 fails. Any higher version is taken.
- A field read that comes back short fails the load.
- A marker that differs or is short is a **successful** load that stops there. The later fields keep their reset values.
- `mFirstHourTime` cut short also stops successfully.
- The CDK entries' read results are ignored, and so are the shares' and `0xAA0180`'s. A short read leaves the stream at the end, so the next marker
  stops the load.
- The mission file of the read counter is parsed **before** the statuses are read over it.
- A repeated zone name fails. Trailing bytes are ignored.
- OpenTPW refuses one case the original would not survive: an `0xAA0180` count above the 22 entries its object holds.

### 3.3 Session sections

The options, `mFirstHourTime`, the `0xA6C0A0` block, the shares, the `0xAA0180` flags and the ambient tags belong to other objects, not the career.
OpenTPW does not port those systems (`[EXT:tpi-capability-gates]`). It carries their sections through from the last player file it loaded. A new
file takes their start-up state:
- **Options:** `0x459520` over `sound.sam`, then `Config.sam`'s `DefaultConfig` entry, as on a machine no `Config[i]` matches.
- **Shares:** the reset `0x621210`, with the company name from `TAG_SYSTEM.str` entry 172.
- **`0xAA0180`:** the constructor `0x622800`'s 17 zero flags.
- **Ambient tags:** none.

The original leaves some words as uninitialised heap: the zone records' optional and award values and sign characters after the first, two status
words, and the shares name's tail. OpenTPW writes 0 there (`[EXT:original-undefined-reads]`).

### 3.4 Where the file lives and when it is written

The original's path is `0x777B80`: `<root>\save\users\<slot + 1><name>\<name>.GMS` (`.GMI` with the refresh flag).

OpenTPW has one player (`[EXT:tpi-front-end-one-player]`), so the file is `users\1New Player\New Player.GMS` (user decision, 2026-11-21), under
`opentpw-tpi` in OpenTPW's own save folder: never in the original's `<root>\save\users`, which holds the original's own players (user decision, 2026-11-21).
When OpenTPW's save folder is inside the game folder, the file goes to the `save` folder in OpenTPW's configuration folder instead
(`GameFlow.ThemeParkIncPlayerRoot`). A file above 4 MiB is refused unread; the retail files are about 18 KB. It is used as follows:
- **Saving the Science park** writes it first, as `0x745720` writes the global save before the park files.
- **Leaving a Theme Park Inc park** runs the full save (`0x6A47B0` → `0x744E10` → `0x745720`): the player file, then the Science continuation park save
  (only the player file in a zone without an OpenTPW park slot). Nothing is saved when the park is lost, or when a load replaces the park (in OpenTPW: **Continue saved park** and **Load layout — new session**).
- **Enter Game** builds the zone's park with its career (`0x56AB40`).
- **Load A Game** takes the career from it after the park save (`0x7451E0`).

A park save also carries the career. A Theme Park Inc park save from before the career is refused with a message (user decision, 2026-11-21). A mission counter
outside the region table (0 to 23) is refused in a park save and in a player file; an advance past the table opens no region.

## 4. Debug cheats

The dialog `0x532A40` has two buttons: **Skip Mission** (`0x186A1` → the completion tick `0x5F0940`) and **Get Promoted** (`0x186A2` → message 0x25
with rank + 1 → `0x74C450`). With `opentpw.cheats` on, OpenTPW offers both in the Theme Park Inc challenge panel (docs/CHEATS.md). An earlier note
listed `0x538F30` as the Get Promoted dialog. That dialog is the obstacle and ticket debug page: it posts 0x23/0x24, adds tickets and opens regions.

## 5. Not ported yet

- **The mission controller** is ported since November 21, 2026 ([TPI-MISSIONS.md](TPI-MISSIONS.md)): the exit statistics, the requirements, the messages and the
  completion tick with its extent, next obstacle and statistics. Still open there: the camera flights, the obstacles as world things and their opening,
  the end sequence and message 0x2C.
- **The mission window**, the prebuilt-park choice of `+0x50`, the zone switch and the per-zone park files.
- **The meaning of some fields:** `+0x20..+0x29` beyond `+0x20`, `+0x58`, the history, the zone records' `+0x04`/`+0x0A` fields, and the
  `0xA6C0A0` and `0xAA0180` objects.
- **The CDK registration scan** of `data\cdk registration\*.cdk`. None ships; a folder with `.cdk` files is refused, because the validation's
  `0x43E9B0` is not traced.
