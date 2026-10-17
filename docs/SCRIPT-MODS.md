# Script mods (TPW Script)

Mods ([MODS.md](MODS.md)) may carry **scripts** that change simulation decisions, such as which attraction a guest
prefers. Scripts are written in *TPW Script*, a small deterministic language that OpenTPW interprets itself. There is no
external scripting engine, and a mod can never ship native code or .NET assemblies.

**Status.** The language core (lexer, parser, compiler, interpreter, `state` codec) is implemented and tested in
`source/OpenTPW/Scripting/` (phase 2). Everything that connects it to the game is **not wired yet**: manifest `scripts`
and `hooks` (phase 3), the hooks into the guest simulation, the `ScriptMods` park-save section, failure counters and the
`WorldStateHash` change (phase 4), the browser check (phase 5), and safe mode and `--test-mod` (phases 3 and 6). Sections
below say what exists now and what is planned.

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
| string | Immutable, at most 1024 UTF-16 characters, compared ordinally. Literals use `"..."` or `'...'` with `\n \t \r \\ \" \'`. |
| table | An array (1-based) **or** a map with string keys, never both. Plain data: tables of ints, bools, strings and tables. |
| host view | A read-only table a hook receives (for example a guest or an attraction). It cannot be changed or stored. |

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
| Table constructor entries / entries per table | 4096 |

**The executed bytes are the hashed bytes.** `ScriptProgram.Compile(bytes)` hashes the byte span (SHA-256, lowercase hex),
then decodes and compiles that same span. Hosts keep the compiled program in memory and never re-read the path, so a file
swapped after hashing cannot run.

**Budgets.**

- A **step budget per call** (default 10 000) and **per tick** (default 200 000), counted in interpreter steps. One step is
  one bytecode instruction; operations that touch data charge more in proportion (string concatenation by length, table
  construction and copying by entry count). A call may use `min(per-call, left in the tick)`. The counter is checked in the
  dispatch loop; running out gives `BudgetExhausted`. Steps depend only on the script and its inputs, so results are identical
  on every run and platform.
- A **call depth cap** of 32 frames, on an explicit frame array (not the C# stack), and a value stack of 4096.
- A **memory cap** of 512 KiB per call, accounted (not measured) for strings and tables created during the call, including
  copies made on first write to `state`.
- No wall clock and no thread is involved; there is no timeout watchdog because none is needed.

**Transactional `state`.** A call works on a copy-on-write view of `state`: the root is copied, and each nested table is
copied the first time a script reads it through a writable path. Committed data is never modified, so the previous state is
a free snapshot. Only a call that returns normally **and** has a valid result commits:

1. the result has the hook's declared type and range (`ScriptResultSpec`), otherwise `InvalidResult`;
2. the new `state` passes the schema (below), otherwise `InvalidResult`;
3. then the new state replaces the old one. A call that changed nothing keeps the same state object.

**Failure.** A runtime error, budget exhaustion or invalid result rolls `state` back and the call returns the `prev` value
the caller passed in: the previous mod's result in the chain, or the game's own rule, **not** the original rule. A failing
later mod therefore does not undo earlier mods. The typed outcome is `ScriptCallResult(Value, Outcome, Message, Line, Steps)`
from `ScriptHost.Call(hook, args, prev, spec, identity)` with `Outcome` one of `Ok`, `RuntimeError` (message and line),
`BudgetExhausted` or `InvalidResult`.

**Failure counters and the disabled flag (planned, phase 4).** Counting failures per mod and disabling a mod after N of
them is **simulation state**: the counters and the flag are saved and hashed, because they change which hooks run. The
language core only reports the outcome; the counting belongs to the game wiring.

## The `state` schema

`state` is the one mutable global. Its root is a map; everything inside is plain data:

- integers, strings, booleans, arrays and string-keyed maps; no null, no floats, no host views;
- **no cycles** (a cycle is rejected as "nested too deep"), and the depth, node and byte limits below;
- strings are **NFC-normalised** and contain no unpaired surrogates; keys are 1 to 64 characters and NFC too.

| Limit | Value |
| --- | --- |
| Depth (root = 1) | 16 |
| Nodes (every table and every value) | 4096 |
| Canonical size | 64 KiB of UTF-8 |

Tables reachable from several places are counted and written once per place. Scripts may build state with new strings that
are not NFC; the commit normalises them in place before validating.

**Canonical serialisation.** `ScriptState.Serialize` writes compact JSON with maps in ordinal key order, integers in plain
decimal, and strings with only `"`, `\` and the control characters (and DEL) escaped as `\uXXXX`. Equal states give equal bytes
on every platform, which is what the save hash needs. `ScriptState.Parse` accepts ordinary JSON whitespace and any key
order, refuses everything outside the schema (null, floats, exponents, `-0`, leading zeros, duplicate keys, an array root,
non-NFC strings, any limit exceeded) and validates. `Serialize(Parse(x))` is a fixed point. An empty map and an empty
untouched table both serialise as `{}` and an emptied array as `[]`, and the interpreter's behaviour after a save round trip
equals continuing without one (tested).

## Random numbers

`rng(n)` is a **stateless hash**, never a generator with state. The call identity is the tuple

`(park seed, mod id, hook name, guest birth id, park turn, draw ordinal)`

where the ordinal counts the `rng` calls inside this one call (0, 1, 2, ...) and the host supplies the rest. The value is a
SplitMix64-style mix of the tuple, taken modulo `n` (`ScriptRng.Draw`). Replays, save resumes and other platforms get the
same numbers, a failed call consumes nothing, and scripts never see `WorldRng`. The exact values are pinned by a test:
saves will depend on them.

## Saves and versioning (planned, phase 4)

- A new optional **`ScriptMods`** section in the park-resume data (null, and omitted, when no script mod is on) holds per mod
  the canonical `state`, the failure counters, the disabled flag, and the language and behaviour version.
- The section joins `WorldStateHash`, and `SchemaVersion` is bumped once, when the section lands. Older builds refuse saves
  that contain the section (the codec rejects unknown properties).
- **The behaviour version is saved.** A change to interpreter semantics or limits bumps `ScriptLimits.BehaviourVersion`; a
  save from another version is refused with a message instead of being resumed under different rules.
- **Ordering rule:** hooks ship only in the same PR as, or after, persistence, never before.
- Phase 2 provides what the section needs from the language side: `ScriptState.Serialize`, `Parse` and `Validate`.

## Hook API v1 (planned, not wired yet)

The hooks follow the real decision path in `GuestSimulation`. Candidates are addressed by **stable ids**, not array indices.
Tie-breaking and the final selection stay with the host (`AttractionChoice`). Multiple mods chain in the resolved, saved mod
order, each receiving the previous result as `prev`. Each hook has a typed result.

| Hook | Called | Result |
| --- | --- | --- |
| `scoreAttraction(guest, candidate, prev)` | per candidate, in major and minor decisions; the work counts per candidate, not per guest | int in the host's score range |
| `joinQueue(guest, attraction, prev)` | when routing has reached the back cell | bool |
| `leaveQueue(guest, attraction, prev)` | after the original leave tests | bool |
| `wanderGoal(guest, prev)` | when a guest picks a wander target | int goal id |

`guest`, `candidate` and `attraction` are read-only views. The exact fields, score range and call counts are fixed in phases 3
and 4; the signatures above are a sketch.

## Safety and recovery (planned)

- The mods screen shows **"contains scripts"** for such mods. Mods only come from folders the player chose, never from the
  network or from a save.
- A startup crash marker offers **safe mode**: the game starts with script mods off, without parsing their scripts.
- `--test-mod <dir>` runs a mod's hooks headless on a representative scenario and reports steps, failures and budget use.

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

Tests: `ScriptLanguageTests` (grammar and semantics), `ScriptSandboxTests` (escape and denial-of-service corpus, limits at
their boundaries), `ScriptStateTests` (transactions, the canonical round trip, the schema, the RNG and a golden hash of a
script corpus that must match on the windows, macOS and ubuntu CI runs). `Scripting/` is also part of the forbidden-API scan
in `DeterminismTests`.

## Phases

1. Docs: this page and the rewritten rule in [MODS.md](MODS.md). **Done.**
2. Language core (this folder, tests, determinism scan). **Done.** No game wiring.
3. Manifest and loading: `content.scripts`, `hooks`, `scriptApi` ranges, the verified bytes, script limits, "contains scripts",
   safe mode. *Later.*
4. Hooks with persistence: the `IGuestDecisions` seam, the `ScriptMods` section, counters, hashing, the schema bump, the
   `patient-guests` example and a cross-platform continuation hash. *Later.*
5. Browser: an AOT publish-and-run check and the example mod in the browser; script mods through OPFS import once mod-system
   phase 8 exists. *Later.*
6. Read-only event hooks, UI-only hooks, more decision points, a mod-author guide and `--test-mod`. *Later.*
