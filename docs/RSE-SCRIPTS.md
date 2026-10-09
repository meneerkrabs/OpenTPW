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
84 corpus opcodes (33 implemented, 51 hooked). Of the 22 named opcodes that
never occur (SETLV, ADDOBJ_EXT, EVENT_EXT, GETANIM, FLUSHANIM_CH, WAITANIM_CH,
TRIGWAITANIM_CH, DBGMSG, PUSH, POP, WAITABS, MULT, END, SETVARINPARENT, the four
light opcodes, GETCUSTPTCLCODE, YEAR/MONTH/DAY), 18 remain without a handler.

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
