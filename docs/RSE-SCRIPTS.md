# RSE scripts: container and static bytecode evidence

October 9, 2026. Status: strict container/bytecode reader and static inventory
implemented for the selected corpus; **no runtime semantics verified**. This is
the file-format slice; execution in `RideVM` is described in
[RSE-VM.md](RSE-VM.md).

`RideScriptFile` (`source/OpenTPW.Files/Public/RideScriptFile.cs`) reads compiled
`RSSEQ` scripts. `RideScriptAnalysis` (`source/OpenTPW/VM/RideScriptAnalysis.cs`)
builds disassembly, a canonical listing and a corpus inventory without executing
anything.

## Layout (verified on all 308 corpus members)

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 8 | magic `52 53 53 45 51 0F 01 00` (`RSSEQ` + 3 bytes) |
| 8 | 4 | variable count (equals the number of variable-name records) |
| 12 | 4×5 | stack, time slice, limbo, bounce, walk sizes (raw int32; time slice is 50 in all 308) |
| 32 | 16 | `Pad Pad Pad Pad ` |
| 48 | 4 | code word count *n* |
| 52 | 4·*n* | code words, little-endian |
| … | 4 + *m* | string blob length *m*, then NUL-terminated strings back to back (empty in 31 files) |
| … | rest | exactly *variable count* records: uint32 length (includes NUL), name, NUL; file ends there |

Each code word is `flags << 16 | value`. Only five flag values occur:
`0x8000` opcode, `0x0000` literal, `0x1000` string (byte offset of a string start
in the blob), `0x2000` branch (code-word index of an opcode word, measured from
the first code word), `0x4000` variable (index into the name table). An
instruction is an opcode word followed by every non-opcode word up to the next
opcode word, so the stream is self-delimiting; the reader needs no arity table.
The upstream note's "4 bytes operand, n bytes opcodes" wording is inaccurate;
the observed order is opcode first, then operands.

Literal values are kept as raw `ushort`. `0xFFFF` occurs 918 times (mostly
EVENT's second operand); whether it means -1 is **unverified**, so disassembly
prints literals ≥ `0x8000` in hex rather than choosing a sign.

### Header version check

The only plausible version field is the magic tail (bytes 5–7, `0F 01 00`
after `RSSEQ`; bytes 4–7 read as one little-endian word `0x00010F51`). It is
identical in all 308 members, and the reader already requires it exactly, so a
non-fatal "differs from corpus" diagnostic could never fire on a file the reader
accepts. No version property was added. The other header words are sizes or
constants, not versions: the variable count, stack, limbo, bounce and walk sizes
vary across members, the time slice is 50 in all 308 and the padding is constant.
The original's "Script & Script interpreter are different versions" message
([RSE-VM.md](RSE-VM.md#original-binary-evidence)) shows a version comparison
exists, but which field it reads is unresolved.

## Reader policy

Input is capped at 1 MiB (largest member: 2,172 bytes); at most 65,536 code
words and 65,536 variables (operands are 16-bit). Rejected as
`InvalidDataException`: bad magic/padding, truncation anywhere, negative or
excessive counts, unknown flag values, an operand before the first opcode,
string offsets that are not a string start, undeclared variables, branch targets
that are not opcode words, an unterminated blob, zero-length/unterminated/
embedded-NUL names, more or fewer names than declared and trailing bytes.
Unknown **opcode numbers are accepted** and reported by the inventory instead.
Caller-owned streams stay open; nonseekable short reads work.

## Private corpus inventory

All 308 `.RSE` members of the 306 `Data/levels/**/*.wad` archives parse
(fantasy 71, hallow 79, jungle 81, space 77): 30,094 code words, 11,915
instructions, 84 distinct opcodes. Operands: 10,549 literal, 4,636 variable,
2,664 branch, 330 string. 294 scripts end in `BRANCH`, 14 in `RETURN`.

- **Unknown opcodes: none.** Every opcode is 0–105, inside the existing `Opcode` enum.
- **Operand counts:** every opcode has exactly one operand count across the corpus,
  and each equals the count in the upstream docs' signature line
  (`CRIT_UNLOCK`, undocumented upstream, is always 0). Consistent string/branch/
  variable kinds (NAME→string, branch ops→branch, SPAWNCHILD/SPAWNSOUND→`*.rse`
  names) support, but do not prove, the enum's numbering.
- **Doc disagreements to investigate:** `MIN`/`SEC` (`<dest>` upstream) each occur
  once with a literal operand; `RAND` occurs 4 times with a literal first operand.
- **Strings:** NAME 277 refs/186 distinct script names; SPAWNCHILD 20 refs/10
  child scripts; SPAWNSOUND 28 refs, all `EventMap.rse`; FINDSCRIPTRAND 5 refs
  (`Traffic Lights`, `Zob Upgrade`). All 48 SPAWNCHILD/SPAWNSOUND references
  resolve case-insensitively to an RSE member of the same WAD.
- **EVENT:** 527 uses, 163 distinct operand tuples (variables by name). They are
  recorded as raw numbers; `Includes/Events.cs` has theme-dependent duplicate
  values, so no event names are assigned.
- `JSR`: 70 uses. 74 distinct variable names; 129 scripts begin with the
  12-name common variable set.

Histogram (opcode:count), pinned by the private test:

```
0:2,1:150,2:240,3:1243,5:74,6:396,7:172,8:644,10:235,11:113,12:20,13:527,15:17,16:74,17:547,18:210,19:133,21:4,23:63,25:1,27:15,28:56,29:70,30:33,31:725,32:787,33:943,34:55,35:84,37:277,38:1318,39:61,42:39,43:39,44:458,46:170,47:541,49:4,50:5,51:20,53:76,54:199,55:144,56:31,57:31,58:24,59:24,60:23,61:6,62:24,63:20,64:28,65:4,66:7,67:9,69:10,70:1,71:4,72:4,73:4,74:8,75:25,76:47,77:53,78:43,79:1,80:1,81:1,86:40,87:80,88:46,89:81,90:5,91:2,92:10,93:140,95:40,96:21,100:1,101:1,102:1,103:21,104:8,105:1
```

### VM vs corpus

Superseded by [RSE-VM.md](RSE-VM.md): `RideVM` now loads `RideScriptFile`
(variables, strings, code-word branch targets) and has a handler for each of the
84 corpus opcodes (44 implemented, 40 hooked; the table in RSE-VM.md is current). Of the 22 named opcodes that
never occur in the Theme Park World corpus (SETLV, ADDOBJ_EXT, EVENT_EXT,
GETANIM, FLUSHANIM_CH, WAITANIM_CH, TRIGWAITANIM_CH, DBGMSG, PUSH, POP, WAITABS,
MULT, END, SETVARINPARENT, the four light opcodes, GETCUSTPTCLCODE,
YEAR/MONTH/DAY), 16 remain without a handler: WAITABS and MULT gained handlers
for the Theme Park Inc corpus (RSE-VM.md's gap-opcode section), and ADDOBJ_EXT one read in tp.exe (`0x5520E8`: ADDOBJ with a control operand).

## Golden scripts

Listing hash = SHA-256 of `RideScriptAnalysis.CanonicalListing`: one line per
instruction, `word:opcode` then ` Kvalue` per operand (K = L/S/B/V, raw decimal),
each line ending `\n`. Values were independently computed by a separate Python
script over WAD members extracted outside the repository.

| Member (under `Data/levels`) | File SHA-256 | Words / instrs / vars | Listing SHA-256 |
| --- | --- | --- | --- |
| `jungle/rides/totem.wad/Totem.RSE` | `5e1ab461c3692c32ead298defb847eb623db24cc4068cfe9760c53dd70233aaf` | 357 / 129 / 17 | `47cf3a92ef866d5d815be3c5def1fe4cc9930ee7c6f6dc2ce005a44f67ecb269` |
| `hallow/rides/bumper.wad/bumper.RSE` | `55f4fbb9820e9a4cf401bf1dca64677b1371ca71c048550197f5033667eb1163` | 254 / 103 / 17 | `da7928ebf4ad705cc73957b3124aff7bee2bdc73919f4fa1543ca2d02f722561` |
| `space/features/plasma.wad/Plasma.RSE` | `a253e1a30011ef29fb24d4ef7472954bc42f6f89f67af2ca4f9a97268f3e5fd8` | 25 / 8 / 1 | `2136dd00402c5e241519828c4908c1dadaba46d49637ed5679b9c5e6e4312cb1` |
| `hallow/sideshow/arcade.wad/gocatgo.RSE` | `a6ff2fe14317d31c681d2c38cf5d44d2840a5e0c993db2ace352551a6a796863` | 8 / 3 / 0 | `60811902fe255c12ccfa4174c4c4dc7229ef9f30452b1e3afd929919f700c948` |

Selected opcode counts are also pinned (e.g. Totem: 13 ADDOBJ, 9 TRIGANIM_CH,
13 TEST, 1 WALKON; Plasma: the corpus's only SPARK). Asset tests are
inconclusive without `OPENTPW_GAME_PATH`, not passes. Set
`OPENTPW_RSE_INVENTORY_OUT=<file>` while running the corpus test to write the
full text inventory (per-opcode operand-kind signatures, strings, EVENT tuples).

## Remaining gates and sources

Unknown: semantics of most operands (see upstream "Unknown" entries), literal
signedness, wait/time units, EVENT tuple meanings, header field effects, the
3 bytes after `RSSEQ` (constant in all 308; see Header version check). Next: original runtime traces to confirm opcode numbering and the semantics
the VM infers from corpus control flow ([RSE-VM.md](RSE-VM.md)). Static
parsing does not qualify any ride behaviour.

Sources: OpenTPW docs (`src/formats/rsse.md`, `rsse-vm.md`,
`rsse-vm-instructions.md`, commit `34f357fabc8a6aa064c76260c714dca8b875b148`),
this repository's earlier MIT `RideScriptFile` reader (git `8637b3f^`), and the
local original files. No original binary was executed.

## Text form and assembler

October 17, 2026. The game ships no readable script source (no `.rss`-style files anywhere in the data, only the
308 compiled `.RSE` members), so the text form below is OpenTPW's own, designed for authoring original ride scripts
(phase 0 of the content pipeline) and for reading the corpus. Code: `RideScriptDisassembler` (file to text) and
`RideScriptAssembler` (text to bytes) in `source/OpenTPW.Files/Public/RideScriptText.cs`.

**Opcode names.** The names are the `Opcode` enum, which now lives in `OpenTPW.Files`
(`source/OpenTPW.Files/Public/Opcode.cs`, same namespace) so the assembler and `RideVM`/`RideOpcodes` share one
table; `RideScriptOpcodeNames` wraps it (case-insensitive parse, `OP_n` for numbers without a name).
`RseAssemblerTests` checks that every enum member and every VM handler round-trips through the name table.

### Syntax

```
; comment to the end of the line
.stack 8                  ; header: .stack .timeslice .limbo .bounce .walk (int32, decimal or 0x hex, each at most once)
.timeslice 50
.limbo 2
.var VAR_VISITOR          ; variables, in table order: operand index = declaration order
.var "odd name"           ; names that are not identifiers are quoted
	NAME "Pool Ride"      ; one instruction per line: OPCODE operand...
loop:                     ; label: the code-word index of the next instruction
	ADDHEAD VAR_VISITOR
	WAIT 500
	DELHEAD VAR_VISITOR
	WAIT 0x01F4
	BRANCH @loop
```

Operands (the flag half of the code word in parentheses):

| Written | Kind | Notes |
| --- | --- | --- |
| `500`, `0x01F4`, `-1` | literal (`0x0000`) | 16 bits; `-32768..-1` is two's complement. Signedness in the VM is unverified, so the disassembler prints values of `0x8000` and above in hex. |
| `NAME` | variable (`0x4000`) | an identifier declared with `.var`; `$n` selects variable *n* and is used for duplicate or non-identifier names |
| `@loop` | branch (`0x2000`) | label; the target must be an instruction (a label at the end of the code cannot be branched to) |
| `"text"` | string (`0x1000`) | escapes `\" \\ \n \t \xHH`; Latin-1 only; the operand is the byte offset in the blob |

Labels (`name:`) may share a line with an instruction. The disassembler names labels `L<word>` and emits only
those that are branch targets. Opcode arity is **not** checked by the assembler (the file format is
self-delimiting); `RideVM` rejects a wrong count when it loads the script.

**String table.** By default the assembler stores each distinct string once, in order of first reference. A
script whose blob differs (unreferenced strings, a different order, duplicates) writes the whole blob as `.string
"text"` lines before the code; then `"text"` selects the first entry with that text and `#n` selects entry *n*
(needed for a duplicate). The disassembler emits `.string` lines only when the blob is not the default layout.

**Header and defaults.** The 48-byte header holds the magic (constant), the variable count (the number of `.var`
lines), the five sizes, and constant padding; nothing else is variable. A freshly authored script gets
`.timeslice 50` (verified: all 308 members), `.stack 20`, `.limbo 0`, `.bounce 0`, `.walk 0`. Only the time slice
is verified; the others are authoring choices from the corpus distribution (253 members declare stack 0, 20 is
the most common non-zero; limbo 0 in 284, bounce 0 in 304, walk 0 in 271). Set the sizes your opcodes need
(`JSR` needs stack, `LIMBO` limbo slots, `BOUNCE` bounce, `WALKON` walk). The stack size is also the size of the `HUSH`/`HOP` heap that holds the riders' ids (docs/RSE-VM.md): with `.stack 0` a `HUSH` does nothing and the later `HOP` pops 0, so a ride script that hangs heads (`HUSH VAR_LETMEON` ... `ADDHEAD`, `HOP VAR_LETMEOFF` ... `DELHEAD`) needs `.stack` of at least its capacity; the content kit's pool script uses 20.

**Errors.** Assembly errors are `RideScriptAssemblyException` with `Line` and `Column` (1-based) and the message
`line L, column C: ...`: unknown opcode or directive, undeclared variable, undefined or duplicate label, label
without instruction, bad string escape, unterminated string, literal out of range, string beyond offset 65535,
too many code words or variables. The result is finally parsed by `RideScriptFile`; a rejection there is reported
without a position.

### Roundtrip proof

`RseAssemblerTests.CorpusRoundtripsByteForByte` disassembles all 308 corpus members (both themes' WADs),
assembles the text and requires byte-identical output (string blob layout and order, variable table, header,
padding), and that disassembling the result gives the same text again. Result: 308 of 308, no exceptions; 4
members need explicit `.string` tables. The corpus test is skipped without `OPENTPW_GAME_PATH`; the authoring
tests (a hand-written ride loop with `ADDHEAD`/`DELHEAD` around waits, labels and strings that assembles, reads
back and runs in `RideVM`) and the error-position tests need no game data. What is **not** proven: that an
authored script behaves like an original ride in the game (only that `RideVM` runs it), and the header defaults.
