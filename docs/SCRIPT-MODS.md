# Script mods (TPW Script)

Mods ([MODS.md](MODS.md)) may carry **scripts** that change simulation decisions, such as which attraction a guest
prefers. Scripts are written in *TPW Script*, a small deterministic language that OpenTPW interprets itself. There is no
external scripting engine, and a mod can never ship native code or .NET assemblies.

**Status.** The language, the manifest and loading, and the four guest-decision hooks are implemented and tested: `source/OpenTPW/Scripting/` holds the lexer,
compiler, interpreter and the `state` codec; a mod's scripts are loaded and compiled at start-up with "Contains scripts" on the mods screen and safe mode;
and the hooks run through the `IGuestDecisions` seam with failure counters, a disabled flag, the `ScriptMods` park-resume section, its part of the
`WorldStateHash` and measured budgets. `opentpw.example.patient-guests` is a working example. **Scripts run in the desktop app only.** The browser does not run
script mods yet: it does not embed the example and has no folders for community mods. `--test-mod` does not exist yet ([Phases](#phases)).

## Quick start

1. Enable the shipped example to see a script mod at work: Options > OpenTPW > Extensions and mods > Community mods > *Patient guests (example)* > on, then restart (or run once
   with `--enable-mod opentpw.example.patient-guests`). It is off by default ([MODS.md](MODS.md#quick-start-your-first-data-mod)).
2. Copy `content/mods/opentpw.example.patient-guests/` to `<config>/mods/community.my-hook/` and edit the manifest (`id`: `community.my-hook`, `category`: `community`).
3. Write your first hook in `scripts/main.tps` (delete the example's script file). A hook is a top-level function named in the manifest's `content.scripts.hooks`; it receives read-only views and
   `prev`, the answer the game (and the mods before yours) would give, and returns the answer to use. For example, let a guest with at least 30 happiness join a ride whose excitement is up to 10 points further from its taste than the game allows (the game refuses from a difference of 45):

   ```lua
   function joinQueue(guest, attraction, prev)
     if prev then
       return true
     end
     return guest.happiness >= 30 and attraction.joinDifference < 55 and attraction.queueCount < attraction.queueLimit
   end
   ```

   ```json
   "content": { "scripts": { "files": ["scripts/main.tps"], "hooks": { "joinQueue": "joinQueue" } } }
   ```

4. Restart. A compile error, a missing function or a wrong parameter count refuses the mod with the file, line and message (startup log and mods screen). At run time a failing call
   answers `prev`, and after 16 failures the mod is disabled for the park ([Failure counters](#failure-counters-and-the-disabled-flag)). The tables in [The views](#the-views) list
   every field; the example mod ([below](#the-example-mod)) is the model to start from.

## Why our own language

A general engine (Lua, JavaScript) cannot give the guarantees a deterministic, saveable simulation needs:

- **An uncatchable budget.** Lua's `pcall` catches hook errors and coroutines reset counters; JavaScript engine limits are
  cooperative. TPW Script has no `try`/`catch`/`pcall`, and the step counter lives in the interpreter's dispatch loop, so no
  script can catch, reset or skip it.
- **No hidden state.** Closures and their upvalues, coroutines, metatables, prototypes and mutable top-level variables
  would all hold state outside `state`, where saves cannot see it. The language does not have them.
- **Bit-exact numbers.** Floats differ in corner cases between x64, arm64 and WebAssembly. All numbers are 64-bit integers.
- **A narrow capability boundary.** A script can only call a fixed whitelist of pure functions and only sees the values a
  hook passes in.
- **AOT and trim safety.** The browser build is compiled ahead of time. The interpreter is plain C#: no reflection, no
  `Reflection.Emit`, no `dynamic`, no third-party code.

## The language

TPW Script is a subset of Lua syntax, so modders recognise it, with different semantics where Lua's would break
determinism. A script is a source file of UTF-8 text (no byte order mark).

```lua
local base = 10                      -- a top-level constant (literal values only)
local rate = fx(1.25)                -- fixed point: 125 (hundredths)

function score(visits, cost)         -- a top-level function
  local s = base * 100 // (cost + 1)
  for i = 1, visits do
    if s > 1000 then break end
    s = s + 3
  end
  state.lastScore = s                -- mutable data lives in `state`
  return clamp(s, 0, 1000)
end
```

### Statements and expressions

- `function name(a, b) ... end` (top level only), `local x = expr`, `x = expr`, `t.field = expr`, `t[i] = expr`, calls
  as statements, `if/elseif/else/end`, `while ... do ... end`, numeric `for i = a, b [, step] do ... end`, `do ... end`,
  `return [expr]` (last statement of a block), `break`, comments `-- ...` and `--[[ ... ]]`.
- Operators, low to high precedence: `or`, `and`, comparison (`< <= > >= == ~=`), `..`, `+ -`, `* // %`, unary `not` and
  `-`. Calls, `.name` and `[expr]` indexing.
- A `local` always needs an initial value. `;` is optional.
- Names are ASCII letters, digits and `_`. The names `state`, `fx` and the host functions are reserved.

### Values

| Type | Notes |
| --- | --- |
| int | 64-bit signed. The only number type. |
| bool | `true`, `false`. |
| string | Immutable, at most 1024 UTF-16 characters, an opaque sequence compared ordinally (no Unicode normalisation anywhere). Literals use `"..."` or `'...'` with `\n \t \r \\ \" \'`. |
| table | An array (1-based) **or** a map with string keys, never both. Plain data: tables of ints, bools, strings and tables. |
| host view | A read-only table a hook receives (for example a guest or an attraction), with the same entry, key and string limits as any table. It cannot be changed or stored. |

There is no `nil` and no unit value you can keep: a function without `return` value can only be called as a statement.

**Nil semantics (decided).** Reading an absent map key, an out-of-range array index or an empty table's key is a **runtime
error**, not a silent `nil`. Test first with `has(t, k)`. This keeps a typo from turning into a quiet wrong score.

**Truthiness (decided).** `if`, `while`, `and`, `or` and `not` need real bools. `if 1 then` is a runtime error. `==` and
`~=` compare two ints, two bools or two strings; any other pairing, and tables, is an error. `<` and friends compare two
ints or two strings.

### Integers

- **Overflow is a runtime error** on `+ - * //`, unary `-`, `abs` and the fixed-point helpers. There is no wrap-around.
- **`//` truncates toward zero** (`-7 // 2 == -3`; Lua floors, this differs on purpose to match the game's own integer
  maths). `%` is the matching remainder, with the sign of the left operand (`-7 % 2 == -1`).
- **Division by zero is a runtime error**, for `//`, `%` and `fxdiv`.
- **`/` is refused** at parse time with a message pointing at `//`.
- Integer literals are decimal, up to 9223372036854775807. Write the minimum as `-9223372036854775807 - 1`.
- **Fixed point.** `fx(1.25)` is the integer 125: hundredths, folded at compile time. The argument must be a literal with at
  most two decimals (`fx(2)`, `fx(-0.05)`). A bare `1.5` is an error. `fxmul(a, b)` is `a * b / 100` and `fxdiv(a, b)` is
  `a * 100 / b`, both truncating, with a 128-bit intermediate and an overflow error if the result does not fit.

### Host functions (the whole whitelist)

All are resolved by the compiler to a fixed table of typed delegates; nothing is looked up by name or reflection at run time.

| Function | Meaning |
| --- | --- |
| `min(a, b)`, `max(a, b)`, `abs(a)`, `clamp(v, lo, hi)` | Integer helpers. `clamp` with `lo > hi` is an error. |
| `fxmul(a, b)`, `fxdiv(a, b)` | Fixed-point multiply and divide (above). |
| `len(x)` | Characters of a string (UTF-16 units) or entries of a table. |
| `has(t, k)` | Is the array index or map key present. |
| `keys(m)` | The keys of a map as a new array, **sorted ordinally**. |
| `push(a, v)`, `pop(a)` | Append to an array; remove and return its last element (error if empty). |
| `del(m, k)` | Remove a map key (error if absent). |
| `rng(n)` | A deterministic value in `0 .. n-1`, see [Random numbers](#random-numbers). `n >= 1`. |

Anything else (`print`, `io`, `os`, `require`, `load`, `string`, `math`, `pcall`, `pairs`, `tostring`, ...) is an unknown
name and a parse error. Maps are iterated with `keys(m)` and a numeric `for`.

### What is excluded, and why

| Excluded | Why |
| --- | --- |
| Closures, upvalues, function values, nested functions | They keep state alive outside `state`. A function sees only its own locals, the top-level constants and `state`. Functions are called by name and are not values. |
| Coroutines | They would let a script keep a suspended computation (hidden state) and reset step counters. |
| Metatables, prototypes, methods (`:`) | Behaviour hidden behind indexing; no way to audit what `t.x` does. |
| `try`/`catch`, `pcall`, `error` | The budget and every runtime error must be uncatchable. A failing call fails as a whole. |
| Top-level mutable variables, globals | Hidden state. The top level holds only functions and constant locals with literal values; assigning to an undeclared name is a parse error. |
| Floats, `/`, exponent and hex literals | Not bit-exact across platforms. Use integers and `fx`. |
| `pairs`, `next`, `ipairs`, generic `for` | Map iteration order must never leak into the simulation. Use `keys(m)` (sorted). |
| `nil`, `repeat`, `goto`, varargs, `#`, long strings | Not needed; `nil` and truthiness are the usual sources of silent mistakes. |
| `string`, `math`, `os`, `io`, `debug`, `load` | No libraries; nothing outside the whitelist exists. |

## Execution contract

The numbers below are in `ScriptLimits`; they are part of the language version (`ScriptLimits.BehaviourVersion`, currently
1) and any change to them or to the semantics bumps it.

**Static limits (checked while parsing; errors carry line and column).**

| Limit | Value |
| --- | --- |
| Source size | 64 KiB |
| Tokens | 20 000 |
| Nesting depth (blocks, parentheses, tables, unary chains, concatenation chains) | 32 |
| Functions | 64 |
| Parameters per function / arguments per call | 8 |
| Locals per function | 64 |
| String literal length, any string | 1024 characters |
| Name length | 64 |
| Entries per table (constructors, host views and runtime growth alike) | 4096 |

**The executed bytes are the hashed bytes.** `ScriptProgram.Compile(bytes)` hashes the byte span (SHA-256, lowercase hex),
then decodes and compiles that same span. Hosts keep the compiled program in memory and never re-read the path, so a file
swapped after hashing cannot run.

**Budgets.**

- A **step budget per call** (default 5 000) and **per tick** (default 50 000, per mod), counted in interpreter steps. One step is
  one bytecode instruction; operations that touch data charge more in proportion (string concatenation by length, table
  construction and copying by entry count). A call may use `min(per-call, left in the tick)`. The counter is checked in the
  dispatch loop; running out gives `BudgetExhausted`. Where the numbers come from is in [Budgets](#budgets-measured). Steps depend only on the script and its inputs, so results are identical
  on every run and platform.
- A **call depth cap** of 32 frames, on an explicit frame array (not the C# stack), and a value stack of 4096.
- A **memory cap** of 512 KiB per call, accounted (not measured) for strings and tables created during the call, including
  the copies of `state` described below and the commit's validation.
- No wall clock and no thread is involved; there is no timeout watchdog because none is needed.

**Transactional `state`.** A call works on a copy-on-write view of `state`. Nothing is copied until the script first
touches `state`; then the root is copied, and each nested table the first time it is read through a writable path. Every copy
is charged to the call's step and memory meters inside the failure boundary, so a call that never touches `state` pays nothing
for it, and one that does pays in proportion to the entries it copies. Committed data is never modified, so the previous state is
a free snapshot. Only a call that returns normally **and** has a valid result commits:

1. the result has the hook's declared type and range (`ScriptResultSpec`), otherwise `InvalidResult`;
2. the new `state` passes the schema (below), otherwise `InvalidResult`. Validating it is charged to the same step and
   memory meters **as the work happens, on every path**: one step per node, one per 16 characters of string content and one per
   16 characters written. The budget is checked while it runs, so a call can run out of steps while committing (it fails like
   any other budget exhaustion), and a commit that fails validation still pays for the work it did. Maps are visited in sorted
   key order, so the charge does not depend on how a dictionary was built and is identical after a save round trip;
3. then the new state replaces the old one. A call that changed no table, or never touched `state`, keeps the same state object.

**Failure.** A runtime error, budget exhaustion or invalid result rolls `state` back and the call returns the `prev` value
the caller passed in: the previous mod's result in the chain, or the game's own rule, **not** the original rule. A failing
later mod therefore does not undo earlier mods. The typed outcome is `ScriptCallResult(Value, Outcome, Message, Line, Steps)`
from `ScriptHost.Call(hook, args, prev, spec, identity)` with `Outcome` one of `Ok`, `RuntimeError` (message and line),
`BudgetExhausted` or `InvalidResult`.

**Failure counters and the disabled flag** belong to the game wiring (`ScriptModInstance`), not to the language core, which only reports
the outcome. They are described under [Failure counters](#failure-counters-and-the-disabled-flag).

## The `state` schema

`state` is the one mutable global. Its root is a map; everything inside is plain data:

- integers, strings, booleans, arrays and string-keyed maps; no null, no floats, no host views;
- **no shared tables and no cycles**: a table may appear only once in `state` (to store the same data twice, copy it); this
  also rules out expanding shared subtrees, so validation work is bounded by the limits below;
- strings are at most 1024 UTF-16 characters with no unpaired surrogate, and are never normalised;
- map keys are 1 to 64 **printable ASCII** characters (0x20 to 0x7E). A longer or non-ASCII key is a runtime error in a script,
  and an `ArgumentException` for a host view.

| Limit | Value |
| --- | --- |
| Depth (root = 1) | 16 |
| Nodes (every table and every value) | 4096 |
| Canonical size | 64 KiB of UTF-8 |

Validation walks the tree once and stops at the node limit and at 64 KiB of string content (keys and values together). It
then serialises, checking the byte limit after every token, so it never builds more than about 64 KiB of text. The walk visits
at most 4096 nodes, and each costs steps as above; the 64 KiB bound is on the serialised text, not on the walk.
Hook results are an int, a bool or a string (`ScriptResultSpec` with a table or no kind is refused); a table can never be a result.

**Canonical serialisation.** `ScriptState.Serialize` writes compact JSON with maps in ordinal key order, integers in plain
decimal, and strings with only `"`, `\` and the control characters (and DEL) escaped as `\uXXXX`; other characters are written as they are. Equal states give equal bytes
on every platform, which is what the save hash needs. `ScriptState.Parse` accepts ordinary JSON whitespace and any key
order, refuses everything outside the schema (null, floats, exponents, `-0`, leading zeros, duplicate keys, an array root,
a non-ASCII or over-long key, any limit exceeded) and validates. `Serialize(Parse(x))` is a fixed point. An empty map and an empty
untouched table both serialise as `{}` and an emptied array as `[]`, and the interpreter's behaviour after a save round trip
equals continuing without one (tested).

## Random numbers

`rng(n)` is a **stateless hash**, never a generator with state. The call identity is the tuple

`(park seed, mod id, hook name, guest birth id, park turn, draw ordinal)`

where the ordinal counts the `rng` calls inside this one call (0, 1, 2, ...) and the host supplies the rest. The **hook name** is hashed, not
the name of the function that implements it (`ScriptHost.Call(function, ..., hookName)`), so renaming a function does not change a mod's numbers.
The host takes the park seed from the guests' identity state (the saved park seed), the birth id from the guest and the turn from the park turn. The value is a
SplitMix64-style mix of the tuple, taken modulo `n` (`ScriptRng.Draw`). Replays, save resumes and other platforms get the
same numbers, a failed call consumes nothing, and scripts never see `WorldRng`. The exact values are pinned by a test:
saves will depend on them.

## Saves and versioning

A new optional **`ScriptMods`** section in the park-resume data (`ParkResumeSnapshot.ScriptMods`, class `ScriptModsSave`) holds, **keyed by mod id**:

| Field | Meaning |
| --- | --- |
| `BehaviourVersion` | `ScriptLimits.BehaviourVersion` when the park was saved. |
| `ApiVersion` | `ScriptLimits.ScriptApiVersion` when the park was saved. |
| `State` | The mod's `state` as canonical text (`ScriptState.Serialize`). |
| `RuntimeErrors`, `BudgetFailures`, `InvalidResults` | The failure counters (they count toward disabling). |
| `TicksOverBudget` | Ticks in which the mod used up its per-tick budget (diagnostic; not a failure). |
| `Disabled` | The disabled flag. |

- **Present exactly when a script mod is enabled** (and so recorded in `Mods`, like any simulation-changing mod); `null` and left out of the file
  otherwise, so a park saved without script mods is **byte-identical** to one from before script mods existed. Loading checks that the section names
  exactly the active script mods (`ScriptModsSave.ActiveProblem`, and `Restore` once more).
- **Strict validation on load** (`ScriptModsSave.ShapeProblem`, run with the rest of the envelope's shape check, before anything is built): every
  key is a recorded mod; both versions equal this build's; `State` parses under the `state` schema **and is in canonical form byte for byte**;
  counters are not negative and the three failure counters sum to at most 16 (`TicksOverBudget` is unbounded); `Disabled` is true exactly when the sum is 16. A **version mismatch refuses** with a
  message such as `community.x was saved under script behaviour version 1, but this build runs version 2`; no save is resumed under other rules.
- **It joins `WorldStateHash`** (`ScriptModRuntime.AddCanonicalState`: a marker, then per mod in id order the id, both versions, the canonical
  state, the three counters and the flag) **only while a script mod is enabled**.
- **`SchemaVersion` stays 21** (decided like the smarter-guests section in 0b): the version marks a change of the hash for runs that already had a
  hash; a run without script mods hashes exactly as before (the existing pins prove it), and a run with them is a different run anyway (it records
  the mods and their content digests, and the section's own marker begins its part). Bumping it would move every pin for a feature that is off.
- **Older builds refuse a save that has the section**: the codec rejects unknown members (`UnmappedMemberHandling.Disallow`). It also refuses the
  `Mods` record, so the refusal is doubled. That is intended: an old build cannot run the hooks the park needs.
- **The guests' identity state is shared.** Script mods need birth ids and a park seed for the `rng` identity, so a park with script mods also
  carries the optional `SmarterGuests` section (`SmarterGuestsSave`: park seed, counter, birth ids). The identity state is split from the
  smarter-guests *rules*: a script policy brings the state without any rule or without the `opentpw.smarter-guests` record, and the shape check accepts the
  section when a `ScriptMods` section is present (`ShapeProblem( ..., scriptMods )`) and requires it then. Birth ids are allocated with no
  random draw, so the guests behave exactly as without them (a test runs a mod that only answers `prev` and compares the guest hash with the off
  run).
- **The behaviour version is saved.** A change to interpreter semantics or limits bumps `ScriptLimits.BehaviourVersion`. Version 1 first reached a
  save in phase 4, after its budgets were tuned from measurements (below), so the budgets of phases 2 and 3 (10 000 and 200 000 steps) never
  appeared in a save and the version did not move.
- **Ordering rule:** hooks ship only in the same PR as, or after, persistence, never before (this phase did both).

## Loading and the manifest

A mod's scripts are declared in its manifest ([MODS.md](MODS.md#scripts)):

```json
{ "scriptApi": ">=1.0.0 <2.0.0",
  "content": { "scripts": { "files": ["scripts/main.tps"], "hooks": { "leaveQueue": "leaveQueue", "joinQueue": "joinQueue" } } } }
```

- **`scriptApi`** is a version range in the usual mod syntax, checked against `ScriptLimits.ScriptApiVersion` (`1.0.0`, next to
  `BehaviourVersion`). Mods target the API, not the interpreter. A mismatch refuses the mod as incompatible. It is required with
  `content.scripts`, and refused without it.
- **`hooks`** maps a hook name to the function that implements it. Only the names in `ScriptLimits.HookParameters` are accepted
  (`scoreAttraction` and `joinQueue` and `leaveQueue` with 3 parameters, `wanderGoal` with 2), anything else fails manifest parsing.
  Each function must exist in exactly one of the mod's files and have the hook's parameter count.
- **Caps (resolve time).** At most 8 script files per mod (`ModManifest.MaximumScriptFiles`), 64 KiB per file (`ScriptLimits.MaxSourceBytes`)
  and 256 KiB of source in all (`ModManifest.MaximumScriptBytes`), against 20 000 files and 2 GiB for a mod in general. The token,
  nesting, function and name limits of the language apply to every file. A script file may not lie inside the mod's `overrides` or
  `strings` folder or be the objects file. These loading caps are not interpreter semantics, so they do not change `BehaviourVersion`.
- **A script over its cap is refused from its file size, before it is read or hashed.**
- **Any failure of the compiler on a file refuses the mod** (kind `Script`, message `could not be compiled (<exception type>)`) rather than
  failing the start; the mods screen's background resolve does the same.
- **A compile error refuses the mod** (kind `Script`): the reason names the file, the line and the message, for example
  `script 'scripts/main.tps' line 4: ...`, and the mods screen shows it as the reason. Several script files are separate programs;
  functions are not shared between them.

**Executed bytes.** The manifest bytes parsed at discovery are kept (`DiscoveredMod.ManifestBytes`), and resolving refuses the mod if the
manifest file it hashes is not byte-identical, so the file list and the hook map provably come from hashed bytes. `ModSet.Resolve` reads each script file once; the SHA-256 that goes into `ContentSha256` and the bytes that are
compiled come from that one read, and the bytes are kept in memory. `ResolvedMod.Scripts` (a `ModScriptSet`) exposes, per file, its
path, the retained `Source` bytes, and the `ScriptProgram` compiled from exactly those bytes, plus the `Hooks` table (file index and
function per declared hook). **The hooks run these programs and never read the path again** (`ScriptModInstance` builds its hosts from `Files[i].Program`): a file changed
after resolve cannot run (tested in phase 3). A script file larger than the per-file cap is hashed but not kept, and the mod is refused.

**Classification.** A mod with scripts changes the simulation unless every declared hook is UI-only (`ScriptLimits.UiOnlyHooks`, empty in
v1), so in practice it always does. It is therefore in `ModSet.SimulationMods` and recorded in park saves like any simulation-changing
mod (`ParkResumeMods`): loading a save needs the same mod, version and content digest.

**Mods screen.** A mod with scripts shows "Contains scripts" in its details (nine languages). A refused script mod shows the compile
error or other reason in the "Why not" line.

## Safe mode

A script mod that breaks the start of the game must not lock the player out. The desktop startup keeps a **crash marker**,
`script-mods-starting.marker` in the configuration folder (next to `mods.json`). It holds the writing process's id and start time.

- **Written** by `ModStartup.Initialize` as soon as the mod folders and the player's choices are known, **before any script is read,
  hashed or compiled**, when at least one enabled mod declares scripts (its manifest says so) and the run is not a smoke test or a
  safe-mode run. It is written for every kind of run that gets that far, headless commands included. If no accepted script mod is left
  after resolving (all refused), it is removed again at once.
- **Removed only on success**: after `ScriptSafeMode.CleanStartSeconds` (15) seconds of running frames, counted from the first frame
  (a slow load does not count), or by `ScriptSafeMode.CleanShutdown()` when `Game.Run` returned normally (headless commands that
  return normally). Nothing clears it from a process-exit handler, and an exception that `Program.Main` catches is a failed start: it
  leaves the marker. Only the run that wrote a marker removes it. Phase 4 may move the clean point to after the first park load.
- **If it is present at the next start and its process is gone**, that start did not finish (a crash, a hang, a compiler fault or an
  exception): this run starts in safe mode. Every script mod is refused (kind `SafeMode`) **without being read, hashed or compiled**,
  a warning goes to the log, and the mods screen shows a notice and the status "Off (safe mode)". The marker is deleted as soon as it
  has been seen, so only that one run is affected. Switch the mod off on the mods screen to fix it for good.
- **A marker whose process is still running** (same id and start time) belongs to another instance that is still starting: it is not a
  crash, it is left alone, and the second instance starts normally without writing its own.
- **A stale marker with no enabled script mod** (the mod was removed or switched off since) is deleted quietly, with no warning and no
  notice.
- **`--safe-mode`** does the same on demand, without needing a marker. Smoke tests ignore the marker (they neither read nor write it) but
  honour the flag.
- **Parked saves.** A park saved with a script mod needs it; in safe mode the load refuses with
  `needs <id> <version>, which is off in safe mode`, so the reason is clear.
- **Browser:** safe mode is a no-op. There is no marker file, no community mods yet, and the example script mod is **not embedded in
  the web build** until the AOT check of script mods (phase 5), so the browser cannot run script mods at all yet.

Mods without scripts are never affected by safe mode.

## Hook API v1

The hooks sit on the decision seam of the guests ([SMARTER-GUESTS.md](SMARTER-GUESTS.md#architecture-in-short-as-built-in-phase-0b), `IGuestDecisions`). `ScriptGuestDecisions`
(`World/Guests/Decisions/`) **wraps the base policy** (the faithful guests, or the smarter-guests policy when that extension is on): every decision first takes
the base policy's answer, and a hook a mod implements then receives it as `prev` and returns the answer to use, mod after mod in the resolved, saved mod
order, each receiving the previous result. A mod that fails returns `prev`, so it undoes neither the base policy nor the mods before it. The host keeps every
effect and every `WorldRng` draw; a hook can only change an answer. The policy is picked once, when the guest simulation is built, from the active mod set;
without a script mod nothing is wrapped (`ScriptMods` is null, nothing is saved or hashed).

| Hook | Seam point | When it runs | Result |
| --- | --- | --- | --- |
| `scoreAttraction(guest, candidate, prev)` | `ScoreAttraction` | per candidate, in the major and the minor decision (the faithful base also offers disconnected typed queue backs to the major decision; the smarter base keeps its join-cell filter); the work counts per candidate | int, 0 to 4294967295 (the host's unsigned score) |
| `joinQueue(guest, attraction, prev)` | `DecideAdmission` | when the guest has reached the back cell and the physical checks (open, not listed, room) passed | bool, true admits |
| `leaveQueue(guest, attraction, prev)` | `DecideQueuePatience` | after the existing leave tests, on every turn a waiting guest stays (never while called forward or in an interlude) | bool, true leaves |
| `wanderGoal(guest, prev)` | `ChooseWanderGoal` | after the host's own destination search and its draws | int, `y * 1024 + x` of a cell inside the map |

The booth verdict, the two comparison rules (`BeatsInMajorDecision`, `BeatsInMinorDecision`) and the use memory are not hooks in API 1; they pass through.
Tie-breaking and the final choice stay the host's. Notes on the mapping:

- **`scoreAttraction`**: `prev` is the base score as an int. The major decision compares scores as signed words (a score above 2 147 483 647 loses), the minor
  as unsigned; both are the base policy's rules and stay.
- **`joinQueue`**: `prev` is whether the base policy admits. If the answer equals the base policy's, the base decision stands unchanged, including *which* refusal
  (too exciting, not exciting enough, queue too long) and so the guest's thought. A mod that flips an admission to a refusal gives *queue too long*; one that
  flips a refusal to an admission admits (the room test was the host's, so the queue has space). Note that a guest admitted beyond the ride's `queueLimit` is dismissed
  by the host on its next turn (*queue too long*), so admitting past the limit is useless; the example mod relaxes the excitement gate instead.
- **`leaveQueue`**: `prev` is false in the original (its patience test cannot fire). A mod that returns true makes the host dismiss the guest with the thought *bored*
  and the end reason *impatient*.
- **`wanderGoal`**: `prev` is the proposed cell as `y * 1024 + x` (1024 is the largest map width, `ScriptLimits.GoalStride`). Returning `prev` keeps the proposal with its
  sub-cell point; another cell is walked to at the middle of the cell. The goal must lie inside the map (`guest.mapWidth`, `guest.mapHeight`), else the result is
  invalid. The cell does not have to be reachable: the host's path search decides, and a guest that finds no path is stuck for that attempt, as with a proposal.

### The views

Views are **plain read-only tables** built from the seam's immutable views when the hook is called: ASCII camelCase keys, values are ints, bools and (for the two history
fields) arrays of ints. Meters are the whole number the original tests (`0` to `100`). Things have **stable ids** (a guest's thing id and birth id, an attraction's id),
never an index into a list. A view cannot be changed or kept.

- **`guest`** (all hooks): `id`, `birthId`, `type`, `happiness`, `thirst`, `hunger`, `toilet`, `nausea`, `money`, `cellX`, `cellY`, `usedHistory`, `temporaryHistory`
  (arrays of 4 attraction ids, newest first, 0 = empty), `turn` (park turn), `raining`, `mapWidth`, `mapHeight`.
- **`candidate` / `attraction`**: `id`, `typeId`, `kind` (as `RideVisitorKind`: 0 ride, 1 shop, 2 sideshow, 3 facility), `hasJoinCell`, `backX`, `backY`, `entranceX`, `entranceY`, `cellModifier`,
  `queueLength`, `queueSizeInCells`, `queueLimit`, `excitement` (live), `excitementLevel` (the type's word), `thirstEffect`, `hungerEffect`, `providesRelief`, `ageDays`,
  `isIndoors`, `goldenTicketCost`, `upgradeCost`, `isOpen`, `isBroken`, `sameTypeAsLast`.
- **`joinQueue`'s attraction** adds `joinDifference` (|preferred excitement minus the ride's|; the original refuses from 45), `excitementWord`, `queueCount`.
- **`leaveQueue`'s attraction** adds `joinTurn`, `waitedTurns` (park turns since the guest joined), `position`, `queueCount`.

Each table has 17 to 28 entries, far below the view caps (4096 entries, 1024-character strings).

### Quick reference

All hooks receive the host's answer as the last parameter, `prev`, and every view is a read-only table of ints and bools (history fields are arrays of ints).

| Hook | Parameters | Returns | Views |
| --- | --- | --- | --- |
| `scoreAttraction(guest, candidate, prev)` | guest view, candidate view, `prev` int | int, 0 to 4294967295 | `guest`; `candidate` (attraction fields) |
| `joinQueue(guest, attraction, prev)` | guest view, attraction view, `prev` bool | bool (true admits) | `guest`; `attraction` plus `joinDifference`, `excitementWord`, `queueCount` |
| `leaveQueue(guest, attraction, prev)` | guest view, attraction view, `prev` bool | bool (true leaves) | `guest`; `attraction` plus `joinTurn`, `waitedTurns`, `position`, `queueCount` |
| `wanderGoal(guest, prev)` | guest view, `prev` int (`y * 1024 + x`) | int, `y * 1024 + x` of a cell inside the map | `guest` |

Guest fields: `id`, `birthId`, `type`, `happiness`, `thirst`, `hunger`, `toilet`, `nausea`, `money`, `cellX`, `cellY`, `usedHistory`, `temporaryHistory`, `turn`, `raining`, `mapWidth`, `mapHeight`.
Attraction fields: `id`, `typeId`, `kind`, `hasJoinCell`, `backX`, `backY`, `entranceX`, `entranceY`, `cellModifier`, `queueLength`, `queueSizeInCells`, `queueLimit`, `excitement`,
`excitementLevel`, `thirstEffect`, `hungerEffect`, `providesRelief`, `ageDays`, `isIndoors`, `goldenTicketCost`, `upgradeCost`, `isOpen`, `isBroken`, `sameTypeAsLast`.
A mod can implement any subset of the four hooks (`scriptApi` `>=1.0.0 <2.0.0`).

## Failure counters and the disabled flag

Per mod, `ScriptModInstance` keeps three failure counters, a ticks-over-budget counter and a flag, **simulation state** (saved in the `ScriptMods` section, hashed), because they change which hooks run:

- **`RuntimeErrors`**: the script failed (type error, overflow, division by zero, a missing key, depth, memory, an unknown function).
- **`BudgetFailures`**: a call ran out of its **own** per-call budget.
- **`InvalidResults`**: the result had the wrong type or lay outside the hook's range (a score above the unsigned word, a goal outside the map), or the new `state` broke the schema.
- **`TicksOverBudget`** is **not a failure** and never counts toward disabling. A tick in which the mod has used up its per-tick budget counts once, however many calls it skips; the counter is saved and
  hashed, and the log gets a warning for the first overrun and then every 600th. See the rule below.

**The tick budget and a decision.** The tick budget protects frame time; it must not punish a mod for a busy park (a park holds up to 1000 guests, and the calls of a tick grow with guests times attractions) and must not
mix two scales of score in one comparison. So: outside a candidate loop (`joinQueue`, `leaveQueue`, `wanderGoal`, a single score) a call needs budget left in the tick; a call cut short by what is left of the tick
rolls back its `state`, answers `prev` and is an overrun, no failure; later calls of the tick answer `prev` without running. A `scoreAttraction` candidate loop is **one decision**: the host brackets it
(`IGuestDecisions.BeginDecision` / `EndDecision`), and at its start each mod is either admitted for **all** candidates or skipped for **all** (and then every candidate has the previous result, so the scores
of one decision never mix the mod's scale with the base policy's). An admitted decision runs against the per-call budget only; the tick meter is charged and may go below zero, so the next decision of the tick is
skipped. One decision costs at most candidates times 5 000 steps. A call inside an admitted decision that fails on its own (error, invalid result, own budget) still counts as a failure and answers `prev` for that candidate only.

Every failure rolls the call's `state` back and returns `prev`. **After 16 failures in total (`ScriptLimits.DisableAfterFailures`) the mod is disabled** for the rest of the
park: the call that reaches 16 returns `prev`, sets the flag and writes one warning to the log naming the mod, the counters and the last failure; its hooks never run again,
its `state` stays as it was and is still saved. The flag is saved, so a resumed park stays disabled. Reasons for 16: a mod that is wrong fails on nearly every call and is gone within a
tick or two, while a rare edge case (an overflow in an unusual park) has a few failures to show itself in the log before the mod stops. The other mods keep running. There
is no re-enabling in the park; start it without the mod or fix the mod and start a new park. `--guest-report --guest-mod` prints failures and the number of seeds that disabled a mod.

## Budgets (measured)

The budgets are in `ScriptLimits`: `DefaultStepsPerCall` 5 000 and `DefaultStepsPerTick` 50 000 (per mod, over all its calls; the tick is one `GuestSimulation` tick, 60 per second).
They were set from measured workloads, with the guest report and its `--guest-mod` option (`--guest-scale extended`, 8 seeds, `park-scenario` and the four level parks of
the M3 gate, so the real queues and scripts of the shipped themes). Three mods ran:

- the example `opentpw.example.patient-guests` (two hooks);
- *light*: all four hooks, `scoreAttraction` with a loop over the history, three conditions and an `rng` call;
- *state*: all four hooks, each updating a map of per-attraction counters in `state` (the copy and re-validation of `state` is charged to the call).

| Mod / park | Calls (8 seeds) | Steps per call, mean | Steps per call, max | Steps per tick, max (calls) |
| --- | --- | --- | --- | --- |
| example, park-scenario | 43 618 | 9.8 | 50 | 150 (13) |
| example, jungle | 274 320 | 10.1 | 31 | 382 (32) |
| example, hallow | 44 195 | 9.9 | 10 | 74 (8) |
| example, fantasy | 356 353 | 10.0 | 31 | 422 (40) |
| example, space | 331 891 | 10.0 | 28 | 456 (43) |
| light, park-scenario | 64 971 | 35.6 | 112 | 2 142 (30) |
| light, jungle | 341 479 | 20.1 | 120 | 2 738 (55) |
| light, hallow | 71 056 | 21.2 | 124 | 1 018 (16) |
| light, fantasy | 407 063 | 16.3 | 120 | 2 034 (56) |
| light, space | 376 930 | 16.3 | 120 | 2 534 (65) |
| state, park-scenario | 65 640 | 72.1 | 84 | 2 438 (30) |
| state, jungle | 341 877 | 49.1 | 54 | 2 548 (52) |
| state, hallow | 69 072 | 36.0 | 45 | 591 (16) |
| state, fantasy | 405 095 | 50.6 | 54 | 2 813 (57) |
| state, space | 380 973 | 50.3 | 54 | 3 188 (64) |

No failure of any kind occurred. The heaviest call took 124 steps and the busiest tick 3 188 steps (64 calls); the defaults leave a factor of **40 per call and 15 per tick**
over those. Speed: the interpreter ran 77 million steps a second in a Debug build on the development Mac (a simple arithmetic loop; release builds and the browser's AOT differ), so the per-tick
budget is about 0.65 ms there: **per mod**, a mod that burns all of it every tick costs under 4% of a 16.7 ms tick (under 15% if the browser build runs four times slower). That counts interpreter steps only; building the
views for each call and the host's path search (`FindPath`) that a `wanderGoal` can force by naming a far cell are unmetered host work, outside the budget. A larger park
raises the tick's calls roughly with guests times attractions, so the per-tick budget is the one to watch. The calibration parks above hold about 30 to 90 guests and 4 to 7 attractions. Scaled up (`ParkScenario` with a
group every turn, test `ACrowdedParkNeverDisablesTheStateMod`) the scenario peaked at 271 guests and the *state* mod's busiest tick took 2 352 steps (49 calls), under the budget; projecting to the 1000-guest cap with
20 attractions (about 10 times the calls) gives 25 000 to 35 000 steps for that mod, and a mod five times heavier than *state* in such a park would overrun the tick. That is allowed to happen: the mod is then skipped for the rest of
the tick (the base policy answers) and `TicksOverBudget` grows, but it is never disabled for it.
Because the whole of `state` is copied (on the first write of a call) and validated (on every write), a mod that writes a large `state` on every call pays about two steps per node: a call that
writes into a 2 000-node `state` is at the call budget. Keep the state small or write it rarely.

## Object lights are not a script-mod hook

TPW Script mods answer the four guest hooks only. Lights belong to the objects' own RSE scripts (`ENABLELIGHT`, `DISABLELIGHT`, `SETLIGHT`, `COLOURLIGHT`) and their models, which a data mod brings with `objects.json`: see
[LIGHTS.md](LIGHTS.md) and the example `opentpw.example.lamp-lights`. A light is presentation, so such a mod needs no script and changes no simulation hash (a mod with objects is still recorded in park saves as
[MODS.md](MODS.md#mods-in-park-saves) describes).

## The example mod

`opentpw.example.patient-guests` (official, off by default, not in the web build) implements `joinQueue` and `leaveQueue` and keeps two counters in `state`:

- `joinQueue`: a guest in a good mood (`happiness >= 50`) that the game turned away only because a ride is up to 20 points too exciting or not exciting enough
  (`joinDifference < 65`, the game refuses from 45) joins anyway if the queue has room; it counts `tolerated`.
- `leaveQueue`: a guest that has waited more than 600 park turns gives up (the game's own patience never fires); it counts `gaveUp`.

`--guest-report --guest-scale extended --guest-mod opentpw.example.patient-guests` against the same run without it, through `--guest-compare` (3 standard errors, 8 seeds):

| Scenario | Metric | Baseline | With the mod | Change | Verdict |
| --- | --- | --- | --- | --- | --- |
| park-scenario | `queue.boarded.rate` | 0.560 | 0.630 | +8.7 SE | improved |
| park-scenario | `queue.rejected.rate` | 0.392 | 0.322 | -9.2 SE | improved |
| park-scenario | `use.giniRides` | 0.197 | 0.108 | -10.6 SE | improved |
| park-scenario | `use.giniAll` | 0.297 | 0.246 | -5.0 SE | changed |
| level-jungle | `queue.wait.p99` | 653 | 593 | -8.3 SE | improved |
| level-fantasy | `queue.wait.p99` | 1048 | 599 | -40.0 SE | improved |
| level-fantasy | `queue.abandoned.rate` | 0.034 | 0.087 | +11.6 SE | changed |

In the scenario the excitement refusals fall from 233 (95 too boring, 138 too scary) to 37 across the eight seeds. The long waits of the level parks are cut off by the
600-turn patience, which is also why more guests abandon queues there. The guests' end state differs from the baseline in all 8 seeds of the scenario, of jungle and of fantasy, in 2 of 8 of space and in none of hallow (that park never
refuses on excitement nor keeps a guest waiting 600 turns); the mod reports no failure. The same seeds give the same report.

## Safety and recovery

- The mods screen shows **"Contains scripts"** for such mods (phase 3). Mods only come from folders the player chose, never from the
  network or from a save.
- A startup crash marker and `--safe-mode` start the game with script mods off, without parsing their scripts (phase 3, above).
- Planned: `--test-mod <dir>` runs a mod's hooks headless on a representative scenario and reports steps, failures and budget use.

## Security notes

TPW Script is a **cooperative sandbox**: its safety comes from the language having no way to name anything dangerous, plus
budgets, not from operating-system isolation. It is meant to stop accidents and hostile-looking content from shared mods, not
to contain a compromised host process. Concretely:

- scripts cannot reach files, the network, the clock, the process, reflection or any host object other than what a hook passes
  in, and what they pass in is read-only;
- every loop, call and allocation is bounded, and bounds are enforced in C# code that scripts cannot influence;
- parse errors and runtime errors never run script code further, and an internal interpreter bug surfaces as a failed call, not
  a crash;
- a script is still code chosen by the player: **only enable mods from folders you chose and trust.** A mod can make guests
  behave badly, burn its whole budget every tick, or fill its `state` up to the limits;
- native code and assemblies are never loaded from a mod (see [MODS.md](MODS.md)).

## Implementation map (phase 2)

| File (`source/OpenTPW/Scripting/`) | Role |
| --- | --- |
| `ScriptLexer.cs`, `ScriptCompiler.cs` | Lexer and single-pass parser that emits bytecode, with all static limits and language restrictions. |
| `ScriptProgram.cs` | Compiled program, bytecode and the source hash. |
| `ScriptInterpreter.cs` | The budgeted bytecode loop. |
| `ScriptHostFunctions.cs`, `ScriptRng.cs` | The whitelist and the stateless `rng`. |
| `ScriptValue.cs`, `ScriptState.cs` | Values and tables; the `state` schema, canonical form and parser. |
| `ScriptHost.cs` | `Call`: transactions, typed results, `prev` semantics, the per-tick budget. |
| `ScriptLimits.cs`, `ScriptErrors.cs` | Every limit; error types. |

Phase 4 (game wiring, `source/OpenTPW/World/Guests/`):

| File | Role |
| --- | --- |
| `Decisions/ScriptGuestDecisions.cs`, `Decisions/ScriptHookViews.cs` | The wrapping policy, the hook mapping and the script views. |
| `ScriptMods/ScriptModRuntime.cs`, `ScriptMods/ScriptModInstance.cs` | The enabled mods in chain order; per mod the compiled programs, `state`, step meters, counters and the disabled flag. |
| `ScriptMods/ScriptModsSave.cs` | The `ScriptMods` section: capture, strict validation, restore. |
| `Client/GuestReport.cs` | `--guest-mod` and the per-mod step statistics. |

Tests: `ScriptHooksTests` (chain, `prev`, failures, budgets, disabling, every hook's mapping, views, `rng`), `ScriptModsSaveTests` (the section, validation, restore, hash, a resumed park, the whole park
save) and the two script pins in `DeterminismTests` (`ScriptModsFixedRunHash`, `ScriptModsAllHooksFixedRunHash`).

Tests: `ScriptLanguageTests` (grammar and semantics), `ScriptSandboxTests` (escape and denial-of-service corpus, limits at
their boundaries), `ScriptStateTests` (transactions, the canonical round trip, the schema, the RNG and a golden hash of a
script corpus that must match on the windows, macOS and ubuntu CI runs). `Scripting/` is also part of the forbidden-API scan
in `DeterminismTests`.

## Phases

1. Docs: this page and the rewritten rule in [MODS.md](MODS.md). **Done.**
2. Language core (this folder, tests, determinism scan). **Done.** No game wiring.
3. Manifest and loading: `content.scripts`, `hooks`, `scriptApi` ranges, the verified bytes, script limits, "contains scripts",
   safe mode, and the compile-only `opentpw.example.patient-guests`. **Done.** No hooks into the simulation.
4. Hooks with persistence: the `IGuestDecisions` seam, the `ScriptMods` section, counters, hashing (no schema bump, see above), the
   `patient-guests` example and a cross-platform continuation hash. **Done.**
5. Browser: an AOT publish-and-run check and the example mod in the browser; script mods through OPFS import once mod-system
   phase 8 exists. *Later.*
6. Read-only event hooks, UI-only hooks, more decision points, a mod-author guide and `--test-mod`. *Later.*
