# ANIM-R: MD2 record kinds, script animation binding and the ANIM-I spec

Lane: ANIM-R (research). Input: [MD2-MODELS](../MD2-MODELS.md) decodes position, rotation, scale,
quantised vertex groups, node-flag toggles and texture-frame tracks. It leaves the 12-byte vertex layout,
the other record kinds, PC equivalence and the two older-version members open. ADVISOR-006 keeps the
advisor in its bind pose because the `Advisorm*` clips were said to have "undecoded non-rigid tracks".
[PPC-formats](PPC-formats.md) covers clocks A/B, clip lifecycle, placement normals and record flag
0x10000000. [PPC-rides](PPC-rides.md) has the channel record (`object +16 + channel × 56`) and the
basic opcode channels. This lane closes the remaining record kinds for M4 (attractions animate through
their scripts; advisor poses). It traces how the script opcodes drive clips, compares the result with
OpenTPW's runtime and specifies ANIM-I.

Binary: Feral Mac `SimThemePark.data` (SHA-256 `04809cd4…e295f5`) and
`libraries/engine_shared.data` (`c549123f…bd73b`). Offsets are code-section offsets unless marked
"data". Everything is static and nothing original was executed. **These are Mac-build facts.** The PC
`TP.ICD` code is encrypted (PPC-formats), so PC behaviour is argued from data identity only (§5).

Reproduce (private assets stay outside Git):

```sh
python3 -I tools/ppc-analysis/lanes/anim/anim_witness.py /path/to/mac-feral/bin          # 185 checks
python3 -I tools/ppc-analysis/lanes/anim/anim_corpus.py  /path/to/theme-park-world/Data   # invariants
python3 -I tools/ppc-analysis/run_evidence_checks.py --mac-bin /path/to/mac-feral/bin --pc-data /path/to/theme-park-world/Data
```

The witness pins every cited operand as an exact instruction word: opcode, registers, displacement or
immediate, rotate mask, call target, TOC float, and switch-table or transition-vector entries. It also
scans three ranges for every record-flag mask they test (§1). The tests rebuild the expected words
synthetically and change the key operand of every check. All 182 mutated checks fail, plus ten named
mutations (§13). The corpus script checks relations, not counts. The numbers quoted below are this
run's report (PC baseline; Patch 2 gives an identical report) and are not pinned.

Confidence follows QUEUE-plan. **High** means an identity-pinned instruction whose operation was
interpreted. **Medium** means a role inferred from callers, data or control flow that was read but not
pinned field by field. **Open** means not traced.

## 1. Record kinds: all 16 flag bits

The corpus has 6,549 records in 1,278 clips. Each record word (+4) carries these bits:

| Bit | Records | Reader (Mac) | Meaning | Status |
| --- | ---: | --- | --- | --- |
| 0x1 | 1,216 | sampler 0xa4f68 | position track (+24) | decoded (MD2-MODELS) |
| 0x8 | 3,039 | sampler | rotation keys (+28) | decoded |
| 0x80 | 644 | sampler | scale keys (+32) | decoded |
| 0x200 / 0x400 | 71 / 62 | sampler | path parameter / derived orientation (+36) | partly traced (PPC-formats) |
| 0x1000 | 1,766 | sampler → 0xa4a58 | quantised vertex groups (+40) | decoded |
| 0x4000 | 30 | loader 0x41ac8, bind 0xa3ef8 | **12-byte block** (+40) | layout decoded; **no consumer** (§3) |
| 0x10000 | 690 | sampler → 0xa83e8 | **per-corner UV animation** (+44) | **decoded here** (§2) |
| 0x20000 | 2,536 | sampler | node-state bit 0x10 toggles (+48) | decoded; bit 0x10 consumer open (§7) |
| 0x10, 0x20, 0x40, 0x8000 | 2,077 / 25 / 937 / 29 | none | always with 0x8 | **never read** |
| 0x100 | 644 | none | always with 0x80 | **never read** |
| 0x800 | 12 | none | always with 0x200 \| 0x400 | **never read** |
| 0x2000 | 342 | none | always with 0x1000 | **never read** |

"Never read" is a bounded claim with high confidence. The witness collects every mask tested on the
record word (followed from `lwz rX, 4(record)` until the register is overwritten) in three places. The
record sampler 0xa4f68–0xa56f8 tests exactly {0x1, 0x8, 0x80, 0x200, 0x400, 0x1000, 0x4000, 0x10000,
0x20000}. The bind 0xa3e24 and the engine record relocation (engine 0x3f9d8–0x42200) test only 0x4000.
The clip bind 0xa5894 uses 0x289, 0x1000 and 0x10000 (PPC-formats). So 0x10/0x20/0x40/0x100/0x800/
0x2000/0x8000 are exporter metadata as far as the animation code is concerned. Corpus invariant: each
appears only together with its "parent" read bit (table). 0x2000 is **not** the static-group flag:
block flag 0x2 occurs with and without it (69/253/329/1,085 combinations).

Not in the record word: trailer word 0 bit 0x2 (texture-frame tracks) and the node list (trailer +56)
remain as in PPC-formats.

## 2. Flag 0x10000: per-corner texture-coordinate animation (high)

Consumer: the sampler tail calls `0xa83e8(player, record, add)` at 0xa56d8. Gate (0xa5698–0xa56c4): the
call happens if the record has 0x10000 **and** either the header flag 0x4 (relative) is set **or** both
global option bit 0x10 and node-state bit 0x4000 are clear. The option word has one store, 0
(PPC-formats). Corpus: every stored mesh record has node bits 0x4000/0x800/0x100/0x10 clear. A runtime
setter of node bit 0x4000 is not traced (§12).

Block at record +44 (20 bytes; engine-relocated): `u32 corners`, `ptr → corners × (u16 first, u16 count)`,
`u32 total keys`, `ptr → u16 ticks[total]`, `ptr → (f32 u, f32 v)[total]`. The routine reads the node
state from record +60 (the mesh record bound in §4). For corner i it searches keys
`ticks[first … first+count)` and writes
`u = (1 − t)·u[k] + t·u[k+1]`, and the same for v (`fmuls` then `fmadds`). The write goes to the mesh
record's texture-coordinate array +104 at `32·(i/4) + 4·(i%4)` (U) and `+16` (V), which is the stored
U×4/V×4 layout. It sets the value, or adds it when header flag 0x4 is set.

Key search 0xa8304 (**differs from the TRS and vertex searches**): with `tick = trunc(time)`, the
**first** key whose tick is **greater** than `tick` ends the segment: `k = i − 1`,
`t = (time − ticks[k]) / (ticks[i] − ticks[k])` (float time, so `t` is fractional within a tick). If no
key is later, `k = count − 2, t = 1.0` (TOC 0x51d0), i.e. the last key holds. A time with
`trunc(time)` equal to a key tick starts the next segment at t = 0, while the vertex cursor 0xa4a58 would
stay on the earlier segment at t = 1. Both give the same value.

Corpus invariants (685 paired blocks, 34,580 corner tracks; 4 records unpaired or on dummies). The corner
count equals the node mesh's corner count (+94). The counts sum to the key total. Every track has at
least 2 keys, starts at tick 0, increases strictly and reaches the clip duration. So the `k = −1` read
(a first tick above the time) never happens. On a clip change the bind copies the stored +104 array back
for 0x10000 nodes (PPC-formats, "Clip bind"). That copy-back is now explained.

## 3. Flag 0x4000: the 12-byte block (layout high, consumer open)

Layout at record +40: `{ptr → u32 index[count], u32 count, ptr → count × (f32 x, y, z)}`. The engine
record relocation (0x41ac8) relocates both pointers and byte-swaps `count × 3` words at the third
(`mulli r4, r0, 3`, 0x41b64). The application bind 0xa3e24 swaps the index list (count = node
positions, +88) at 0xa3f08–0xa3f10. It then sets **node bit 0x800**, and clears **node bit 0x100** when
the clip's category is not 0 (0xa3f24, 0xa3f38).
The channel reset 0xa7bb8 first sets 0x100 on every node (0xa7c88) and calls the bind for every clip
(0xa7cdc). Two passes (0xa7140, 0xa7ac0) clear 0x800 on nodes that still have 0x100 and, if any was cleared, set
`gei_System` bit 0x80000000. The sampler skips vertex animation for 0x4000 records (0xa5048).

**No code reads the float array.** A whole-image scan for `lwz x, 40(record)` followed by a `+8` read
finds no reader in the animation code. The node bits 0x800/0x100 are the only effects. Their readers
are not found among the mesh-record users that the scans reached. This is a bounded negative (medium):
a reader through another pointer path is not excluded.

Corpus (30 records, 16 clips: `speaker1–4c`, `rocksc`, `treec`, `droidc/i/m1/m2`, `gk_*m`,
`GTicketm1`, `plane_anim`). Every record also has 0x1000. The count equals the node mesh's position
count. Indices are in range and in identity order in 29 of 30. The floats are one pose. A scratch
comparison (not pinned) found them equal to the stored positions in 11 of 30 records and different in
19, so "show the stored mesh" is not equivalent in general.

## 4. Record → node binding (+60) (high; closes a PPC-formats open item)

`0xa3e24(object header, clip, category == 0)` runs from the channel reset for every clip. With
object-header bit 0x4 clear, record +20 is a node index. Index n below the mesh count gives mesh record
n (160 bytes), otherwise dummy record n − meshes (88 bytes) (0xa3ec0–0xa3ef0). With the bit set, it
searches the meshes for record +80 equal to record +20 (0xa3e98). The result is stored at record +60,
which the sampler, the UV pass and the vertex pass read. The record +60 slot belongs to the clip, so
clips shared between objects share it. OpenTPW's "mesh i is node i" pairing equals the default mode.

## 5. PC vs Mac equivalence of the vertex layouts (data level)

The Mac loaders byte-swap little-endian files, so the Mac reader consumes the PC byte layout (the
10:10:10 words through engine 0x41cac, the 12-byte floats through 0x41b68). Data identity, taken as a
contiguous byte-identical copy in the Mac `hfs.img`: **275 of 293** PC baseline WADs with MD2 members are
identical. Among them are all 9 WADs that carry 12-byte records, four of the five advisor WADs and
`totem.wad`. The 18 others (`global/advisor.wad`, the four `terrain.wad`, several coasters and rides)
are not contiguous in the image. They may be fragmented or differ; this was not resolved. The PC
reader's arithmetic stays unverified (encrypted `TP.ICD`). So the claim is: the PC data have the same
layout the Mac reader decodes. It does not establish that the PC executable decodes it the same way.

## 6. Older-version members: garrow/rarrow (24.23) and wr_tunnel (207.201)

Four members are not 221.203 (baseline and Patch 2). `generic/dynamic/garrow.MD2` and `rarrow.MD2` are
version **24.23**, loose files listed in `generic/dynamic/qickload.txt`. The ride loader reads such
quickload indexes (`%s:%s:qickload.txt`). Both are also on the Mac volume, byte-identical. The other two
are `wr_tunnel.md2`/`wr_tunnelm.md2` (207.201). Loader gate (engine 0x3f9f0, 0x3fc74–0x3fcb4): r27 keeps
argument bit 0x1. A major below 221 without that bit is refused (`DeadMesh`) before any layout is read.
Both application call sites pass 0 (0x58f00) or 2/6 (0x99acc/0x99ad4), never bit 0x1. **None of the four
loads in this build** (high). Their layout needs no decoding for parity. OpenTPW should keep rejecting
them, with the reason "refused by the original loader".

## 7. Node-state bits

| Bit | Set by | Cleared by | Read by |
| --- | --- | --- | --- |
| 0x10 | toggles (≤ 0 entry), node list at reset (0xa65b8), ride-state selector 0x33d54 (with 0x80000000) | toggles (> 0 entry), 0x33dc0 | object save 0x5b3b4 (saved as bit 1); **draw consumer not found** |
| 0x20 | — | — | object save (bit 2) |
| 0x100 | channel reset (all nodes) | bind of a 0x4000 record outside category 0 | 0xa7140/0xa7ac0 |
| 0x800 | bind of a 0x4000 record | nodes with 0x100 | open |
| 0x4000 | not traced | — | UV gate (skip) |
| 0x00010000 | vertex pass, placement | — | face-normal routes; saved as bit 8 |
| 0x00800000 | static group applied | clip bind, capture loop | vertex dispatcher |

The save pass persists bits 0x10, 0x20, 0x8000, 0x10000, 0x40000, 0x80000, 0x100000, 0x200000,
0x400000, 0x20000000 and 0x80000000 per node (0x5b3b0–0x5b4dc). It does not persist 0x100, 0x800 or
0x00800000, so a loaded object re-applies its static groups at its first sample. The toggles' meaning
stays open. The save and the ride-state selector treat 0x10 as persistent per-node state, which fits
visibility but does not prove it.

## 8. Script opcodes → clips (high)

Handlers come from the relocated switch table (data 0x3f2a4). The trigger is `0xa6cc0(object, AnimID,
sub, flags, speed f1, channel r8)` and the start is `0xa67d8`.

| Opcode | Handler | Channel | Flags | Effect on the script |
| --- | --- | --- | --- | --- |
| 15 FLUSHANIM | 0xafa68 | 0 | — | `0xa6c90`: clears **only the deferred clip** (+36 → 12); the playing clip continues |
| 16 TRIGANIM | 0xafa94 | 0 | 0 | out var = `max(ret − 300, 300)` (signed); WAIT4ANIM deadline = now + out / script speed |
| 17 WAITANIM | 0xafbbc | 0 | 0 | waits `max((ret − 300) as unsigned / speed, 300)`; clears the WAIT4ANIM deadline |
| 18 LOOPANIM | 0xafd00 | 0 | 1 (loop) | no-op if token `anim + (sub << 16)` = script +168; clears the WAIT4ANIM deadline |
| 19 TRIGWAITANIM | 0xafd94 | 0 | 0 | out var and deadline as TRIGANIM; then re-executes until channel 0's AnimID = the triggered one (**not** until the clip ends) |
| 20 GETANIM | 0xaff3c | 0 | — | acc = −1 if channel flag 0x4 (ended), else AnimID (12 = none) |
| 21 TRIGANIMSPEED | 0xaffc4 | 0 | 0 | clip speed = script speed × op/1000; deadline = now + out × 1000 / op (`divw`) |
| 22 FLUSHANIM_CH | 0xb00f4 | operand | — | deferred flush of that channel |
| 23 TRIGANIM_CH | 0xb013c | operand 4 | 0 | as TRIGANIM |
| 24 WAITANIM_CH | 0xb0268 | operand 3 | 0 | as WAITANIM **without** the speed division (0 corpus uses) |
| 25 LOOPANIM_CH | 0xb037c | operand 3 | 1 | starts the loop (no token check traced); clears the deadline |
| 26 TRIGWAITANIM_CH | 0xb0404 | operand | 0 | as TRIGWAITANIM on that channel |
| 27 GETANIM_CH | 0xb05b8 | operand 2 | — | as GETANIM |
| 46 WAIT4ANIM | 0xb07a0 | — | — | deadline 0 → continue; else block while now < deadline, then clear it |

Return value `ret` is defined as follows. The start 0xa67d8 returns `trunc(1000 × ticks / 30)` ms in single
precision, with ticks = trailer word 2 − word 1. **The speed does not enter it.** A missing clip
returns 0, which the trigger reports as **1000 ms**. A deferred trigger (flag 0x2) returns the current
clip's remaining `trunc((total − frame) × 33.333332)` ms plus the new clip's length (1000 if missing).
**No script handler passes flag 0x2**, so script triggers always replace the channel's clip at once.
WAIT4ANIM uses **one** deadline per script (+164). Each TRIGANIM-family call overwrites it, and
WAITANIM/LOOPANIM (and their _CH forms) zero it. It is not the latest end of all clips.

Degenerate case (high for the arithmetic): WAITANIM converts `ret − 300` unsigned (bias 2^52 at
0xafc88; TRIGANIM uses the signed bias and `xoris`). A returned length below 300 ms (a clip of at most 8
ticks) wraps to about 4.29 × 10⁹, saturates to INT32_MAX and makes the script wait about 24.8 days. No
corpus script was evaluated against clip lengths for this.

Completion is not an event. Scripts learn about a clip's end only through these computed deadlines and
through GETANIM's −1.

Clip end in the channel update 0xa7190, once AnimFrame > TotalAnimFrames with nothing deferred:

- object flag 0x8: stop (AnimID 12);
- object flag 0x10: hold;
- loop bit: replay (PPC-formats);
- otherwise **restart as AnimID 14** (0xa7498).

AnimID 14 sets channel flags 0x14 (frozen at the end, "ended") and, like AnimID 13, branches past the
bind (0xa694c). So **a finished non-looping clip keeps its last pose with no copy-back**, which supports
RIDES-003, and GETANIM then returns −1. A pending deferred clip starts instead, with its stored flags.

Channel mixing: the object update 0xa7960 runs the channel updates in **ascending channel order**
(stride 56, count +14). Each samples its clip in set mode, so for a node component written by several
channels **the highest channel index that writes it at that time wins**, not the most recently started
clip (RIDES-002).

Idle restart: when object flags `+4 & 0x8004` are clear, the object update re-triggers **AnimID 5, sub 0,
channel 0** while the all-ended check 0xa7000 is true (at most 3 times). Because of the 0xa7000 quirk
this checks channel 0 only. Placement sets 0x4 and 0x8000 only for placement flags 0x1/0x80
(PPC-formats), so it is not known which objects keep this behaviour (§12).

## 9. Placed instances: two update routes, and the no-reset replay (high for the routes)

Placement 0x59f00 stores each instance in the instance table (data 0x67c9c, `stwx` at 0x5a090). It
installs a callback (transition vector data 0x6408 → 0x59eb4) in the instance's render record +320
(0x5a3bc).

- **Callback route:** `0xa7960(object, r4 = 1, r5 = 0)` through 0x4d280. It marks the object 0x00200000.
  This is the route OpenTPW models: sampling, and loop replays that rebind and reset the cursors.
- **Object-list route** 0x4d354, once per main-loop pass: for every instance in the table not marked
  0x00200000 (the mark is cleared instead) and without 0x3000, it calls `0xa7960(object, r4 = 0,
  r5 = 8)`. Clocks advance and loops replay **without a bind**. A channel **samples only if object flag
  0x00400000** is set (0xa7644). The object is then marked 0x00800000.

So an instance whose callback did not run during a pass (presumably an undrawn one; the engine's call
condition is not traced) advances its clips without posing. A loop that wraps on that route keeps its
vertex cursors on the last segment. If the callback route samples the instance again before the next
rebinding wrap, the vertex dispatcher 0xa4a58 only moves the cursor forward, so a time below the kept
segment **extrapolates** (`AdvanceKey`). TRS and UV searches restart from key 0 and are unaffected.
This is the concrete form of the "no-reset object-list replay" (medium: depends on the engine's
callback condition).

## 10. Advisor clips (high for the calls)

The start helper 0x293cc calls the trigger with AnimID **5** (suffix `m`), sub = animation id − 1,
flags 0x10, channel 0 and speed 1.0 (0x29414–0x29424). The loader names member sub + 1, so animation
id n plays `Advisor<level>m<n>.MD2` (`Advisor%s.md2`, level suffix such as `_FAN`). Flag 0x10 is not
tested by 0xa6cc0 or 0xa67d8. The presentation update continues a sequence (AnimID 5, sub = entry − 1)
only when the deferred slot reads 12 (0x762c–0x7664). At creation it triggers AnimID 12 with flags 2
(0x292a8). The advisor's channels run on clock B (PPC-formats).

**ADVISOR-006's premise is stale.** The corpus invariant holds: all 15 global `Advisorm*` clips use only
decoded kinds (TRS, 0x1000 vertex groups, 0x20000 toggles) plus the never-read 0x2000. No advisor clip
has 0x4000. The level advisors add 0x10000 UV tracks (now decoded); one (`Advisor_HALm16`) has 0x200
path records. What still blocks exact poses is the meaning of node bit 0x10 (mouth/blink/accessory
visibility is the likely use, §7) and the sequence tables (PPC-advisor). Neither is an undecoded payload.

## 11. Relative-animation models and normals

Header flag 0x4 (27 geometry members: coaster pylons, carts, track; none in the object catalog) gives
add mode. The object update first rebinds every channel's clip (0xa7a30, a per-update base restore),
the passes add, and 0xa772c recomputes face normals for meshes with 0x00010000 (0xa7b2c–0xa7b60). But
the ride loader **strips** the flag (clears 0x4, sets 0x01000000) unless its argument has 0x20000,
whose ride-data source is untraced (PPC-formats). On the default path these models therefore animate
in **set mode** with their stored normals. Placement additionally recomputes all face normals once
(0x5a4b4).

## 12. OpenTPW divergences

Compared with `ObjectAnimator`, `OriginalObjectRuntime/OriginalObjectEffects`,
`OpcodeHandlers.Animation`, `RideVM` and `Advisor` at c6c19ea:

| # | OpenTPW now | Original (Mac) | APPROX |
| --- | --- | --- | --- |
| D1 | FLUSHANIM stops all channels; the VM clears pending WAIT4ANIM time | clears only the deferred clip of channel 0 (or the operand's) | new (no ID yet) |
| D2 | WAIT4ANIM waits for the latest end of all tracked clips | one deadline per script, overwritten by each trigger and zeroed by WAITANIM/LOOPANIM | new |
| D3 | TRIGANIM out var / waits = clip length | `max(len − 300, 300)`, scaled by script speed; WAITANIM unsigned wrap | new |
| D4 | TRIGWAITANIM waits for the clip length | waits until channel 0's AnimID is the triggered one | new |
| D5 | TRIGANIMSPEED ignores operand 4 | clip speed × op/1000; deadline × 1000/op | RIDES-007 (contradicted; already in PPC-rides) |
| D6 | GETANIM_CH = 1 while playing, else 0 | AnimID, −1 after a non-looping end (flag 0x4), 12 if none | RIDES-006 (contradicted) |
| D7 | LOOPANIM no-op if the animator loops the same clip on that channel | no-op if the script-local token (+168) matches; any other trigger in between does not reset the token | RIDES-004 (refine) |
| D8 | the most recently started channel wins a node | highest channel index that writes the component wins | RIDES-002 (contradicted) |
| D9 | finished non-looping clip holds | holds: AnimID 14 freezes without copy-back (r4 = 1 route) | RIDES-003 (supported) |
| D10 | 0x10000 records unsupported | per-corner UV animation | new: implement (no APPROX needed) |
| D11 | 0x2000 counted as unsupported (`UnsupportedFlags`) | never read | none: reclassify as metadata |
| D12 | 0x4000 tracks listed, stored mesh shown | float block bound, no reader found | keep, new APPROX "12-byte block shown as stored mesh" |
| D13 | relative-animation models' vertex tracks refused | default load strips 0x4 → set mode | new APPROX if kept; ANIM-I plays set mode |
| D14 | toggles and texture frames not applied | toggles flip node bit 0x10 (consumer open); texture frames gated (PPC-formats) | ADVISOR-002 / new RIDES APPROX for visibility |
| D15 | advisor stays in bind pose | plays `Advisor<level>m<n>` on channel 0, clock B, per sequence | ADVISOR-006 (premise stale) |
| D16 | every instance always samples (r4 = 1 model) | undrawn instances advance without sampling; wraps there keep cursors | new (scope: offscreen) |
| D17 | no idle restart | objects with +4 & 0x8004 clear re-trigger AnimID 5/0 when channel 0 ended | new (scope unknown) |
| D18 | 24.23 / 207.201 rejected as unsupported | refused by the original loader (`DeadMesh`) | none (parity) |

## 13. ANIM-I implementation spec

Scope: production C# in `OpenTPW.Files/Formats/Model`, `World/Objects`, `VM/Handlers/Animation.cs`,
`World/Advisor.cs`, with tests. Each item names its evidence.

1. **UV tracks (0x10000).** Decode the 20-byte block into `ModelUvAnimation { corners; per corner
   (first, count); ticks; (u, v) keys }`. Validate: corners equal the bound mesh's corner count, counts
   ≥ 2, ticks start at 0 and increase strictly, and the spans lie inside the payload and are charged to
   the table budget. Sample with the §2 search (first tick > trunc(time); otherwise count − 2 with t = 1)
   and `(1 − t)·a + t·b`. Apply after the TRS/vertex passes of the same record, in set mode. Per-instance
   UV arrays sit beside the vertex positions, and a version counter drives the dynamic buffer upload.
   Copy back the stored UVs on clip change, under the same fixed-item gate as positions. Remove 0x10000
   from `UnsupportedFlags`. Gate: header flag 0x4, or node bit 0x4000 clear (always clear in OpenTPW).
2. **Record-bit classification.** Add `MetadataFlags = 0x10|0x20|0x40|0x100|0x800|0x2000|0x8000`,
   excluded from `UnsupportedFlags` and `HasUndecodedPayload`. Keep 0x4000 and 0x600 unsupported.
3. **12-byte block.** Parse `{indices, count, floats}` (validate count = positions and indices < count)
   and expose it as `ModelVertexPose`. Do not play it. Keep the stored mesh and list it in
   `VertexLimitations` under a new APPROX ("12-byte block shown as stored mesh; no original reader
   traced").
4. **Channel mix (D8).** Sample channels in ascending index order. For each node component the last
   channel that writes it (after its first key) wins. Drop the start-serial rule and retire RIDES-002 as
   contradicted. Vertex/UV arrays follow the same rule per mesh.
5. **Script opcodes (D1–D7).** Return values: the effect returns the original `ret` (clip length via
   `trunc(1000·ticks/30)` in single precision, 1000 for a missing clip, never speed-scaled).
   - TRIGANIM, TRIGWAITANIM and TRIGANIM_CH store `max(ret − 300, 300)` in the out var and set the
     script's single WAIT4ANIM deadline to `now + trunc(out / speed)`.
   - WAITANIM waits `max(unsigned(ret − 300) / speed, 300)`, saturating like `fctiwz`. WAITANIM_CH does
     the same without the division. Both zero the deadline.
   - LOOPANIM keeps a script-local token `anim + (sub << 16)`: equal → no-op, otherwise start looping and
     store the token. Both LOOPANIM forms zero the deadline.
   - TRIGWAITANIM: trigger, then re-execute each step until channel 0's AnimID equals the triggered one.
   - TRIGANIMSPEED: clip speed = script speed × op/1000; deadline = now + out × 1000 / op (C `int`
     division).
   - WAIT4ANIM: block while the deadline is non-zero and now < deadline; then clear it.
   - FLUSHANIM(_CH): clear the channel's deferred clip only.
   - GETANIM(_CH): −1 when the channel has the ended flag, else the AnimID (12 when empty).

   Deferral (flag 0x2) is a channel feature that only native callers use. Implement it in the channel
   model for the advisor (§10) and leave the script handlers on flags 0/1.
6. **Clip end.** A non-looping clip past its end becomes "ended" (AnimID 14 semantics): frame frozen at
   total, ended flag set, no copy-back, pose held. A deferred clip starts instead if present. Keep
   RIDES-003 and cite 0xa7498/0xa694c as evidence.
7. **Advisor (D15).** Play animation id n as `Advisor<level>m<n>.MD2` on channel 0 at speed 1.0 using
   clock B (not affected by game-speed rate or hold). Advance the sequence only when nothing is deferred.
   With items 1, 2 and 9 every record of the global clips is playable. Replace ADVISOR-006 with the
   remaining sequence/visibility APPROX.
8. **Relative models (D13).** Play their vertex tracks in set mode (the default loader path strips 0x4).
   Record a new APPROX that add mode, behind the untraced 0x20000 ride argument, is not modelled.
9. **Node toggles (D14).** Keep the per-node bit 0x10 state from toggles and the trailer node list (set
   at reset for nodes whose type word has bit 31 clear). Behind a new APPROX, map bit 0x10 set to
   *hidden* for meshes. The fit with the save, the advisor mouths/blinks and the ride-state selector is
   evidence of use, not proof. Do not apply texture frames until option bit 0x8 and the slot consumer
   are traced.
10. **Older versions (D18).** Change the `NotSupportedException` message for majors below 221 to name the
    original's `DeadMesh` refusal. No layout work.
11. **Out of scope / later:** the r4 = 0 offscreen route (D16; needs the engine's callback condition),
    the idle restart (D17; needs placement flag sources), path parameters 0x200/0x400, game-speed rate.

Acceptance:

- Synthetic tests for every bullet of items 1, 4 and 5, including the WAITANIM wrap and the
  TRIGANIMSPEED division.
- A corpus test where all 685 paired UV blocks validate and sample the stored UVs at tick 0 within one
  float step. That last part is unproven and must be measured before it is pinned.
- Totem/Belly Bounce smoke unchanged.
- `--advisor-say` plays a clip.
- The register updated (RIDES-002/006/007 contradicted, ADVISOR-006 replaced).

## 14. Proved and open

**Proved (Mac, high):**

- The meaning of every record bit; which bits are never read.
- The UV-animation decode, gate and key search.
- The 12-byte layout and bind effects; record +60 binding.
- The version-24 refusal.
- Opcode→handler table and per-handler channel, flags and wait arithmetic.
- Trigger/start return values, deferral and flush.
- Channel order; the end-of-clip AnimID 14 hold.
- The two instance update routes and their r4 values.
- The advisor trigger operands.
- Node bits persisted by the save.

**Data-level (high):** PC/Mac byte identity of 275/293 MD2 WADs, including all 9 with 12-byte records and four of
five advisor WADs.

**Open:**

- A reader of the 12-byte floats and of node bit 0x800.
- The draw consumer of node bit 0x10 (visibility not proven).
- Setters of node bit 0x4000 and object flag 0x00400000.
- The engine's condition for the placement callback (which instances take the r4 = 0 route).
- Which objects keep the idle restart.
- Path parameters (0x200/0x400).
- PC executable equivalence (encrypted).
- The 18 WADs not found contiguously in the Mac image.
