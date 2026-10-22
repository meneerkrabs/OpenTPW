# Texture frame lifetime (MD2-900)

Bounded static PC evidence from sp20_nocd/tp.exe, SHA-256
5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805.
No original program execution or media upload was needed.

The draw reader is already implemented: 0x580530 calls the slot binder
0x57A420, which selects the stored frame only with ANIMATINGTEXTURES enabled.
The missing distinction was sampling versus cleanup, not another draw reader.

## Ordinary set-mode contract

- 0x470C90 scans keys backwards. If none precedes the current time it does
  not write; an applicable key changes slot word +2 and marks dirty bit 0x400.
- 0x472310(object, oldClip, newClip) cleans the outgoing clip. Its tail
  0x47268B–0x4726B9 tests the **outgoing** trailer bit 2 and global option
  0x7B4980 bit 8 clear, then resets **all slots with raw flag 0x40** to zero.
  This is not limited to slots addressed by the outgoing track list.
- The fixed-item mesh preservation branch 0x47249B–0x4724A6 jumps directly
  to that texture reset. Keeping mesh positions or UVs does not preserve frames.
- Ordinary replacement calls cleanup at 0x4730A9; ordinary unbind with
  newClip null calls it at 0x473131. Both require an outgoing ID other than
  12 and request flags & 0xC clear. A replacement with a delayed first key
  therefore starts at zero after cleanup, unless another channel writes it.
  A replacement without texture tracks still cleans a texture-enabled old clip.
- Endpoint requests 13/14 bypass cleanup at 0x47306B. They preserve the
  underlying bound clip ID, so an ended clip continues contributing its final
  texture sample and a later ordinary replacement cleans it.
- Hold-at-end samples its endpoint, then calls ID12 with flags4 at 0x473A30.
  This skips cleanup; subsequent channel updates return for ID12 at 0x4735F7.
  The last frame remains, but that held channel no longer competes with other
  texture writers. Replacing it skips outgoing cleanup because native ID is12.
- Ordinary posed loop replay uses the same cleanup path; a delayed first key
  must not carry the previous loop's last frame across that reset.

Host Stop/StopAll remove channels, rather than implement a script stop opcode.
They use ordinary outgoing cleanup, not the native stopped-but-bound meaning.
Save restoration is not a clip replacement and preserves valid slot history.
Missing decoded art and invalid frame keys retain the host's defensive policy
(MD2-901), not an unchecked native memory access.

## Invalid frame consumer (MD2-901)

The consumer is no longer unread. Binder `0x57A420` checks the slot pointer
and texture object for null (`0x57A468` and `0x57A46F`). With global
`0x8BD550` bit `0x40` set it zero-extends slot word `+2` into EAX
(`0x57A47C`–`0x57A47E`); otherwise it selects zero. It passes that index
to `0x575F90` at `0x57A487`. The entire accessor loads `[this+8]`, loads
the argument, reads `[array+index*4]`, then returns. There is no frame-count
comparison, clamp, or modulo operation. The subsequent null-result check
at `0x57A48E` does not protect the preceding unchecked array access.
The draw reader `0x580530` (MD2-900) is a second consumer through the same
accessor: behind the same `0x8BD550` bit `0x40` gate it zero-extends slot
word `+2` itself (`0x5805ED`, otherwise zero) and passes it to `0x575F90`
at `0x5805FA`, again without a frame-count check.

The writer independently forms `[model+0x50] + slot*8` at `0x470CBB`–`0x470CC3`
and compares, flags and writes frame word `+2` (`0x470CF2`, dirty bit `0x04`
at `0x470CF8`, the store at `0x470CFC`), without validating the slot index.
Malformed slots can therefore already cause an out-of-range memory operation
before drawing. Whether the clip loader feeding the writer's callers
(`0x472250`, `0x473691`, `0x473A0A`) checks the slot, and whether the
`[model+0x50]` array is sized exactly to the slot count, is not traced. Both
consumers walk the part's own slot pointers, so an invalid slot index never
reaches them; only the writer's stray access does. Invalid-frame native outcomes depend on memory state; this
trace does not establish a portable fallback, deterministic crash, or safe
wrapping rule. The host's slot filtering, invalid-frame refusal, and separate
missing-decoded-art policy remain deliberate defensive extensions. MD2-901
is retained as that documented divergence, not a missing consumer trace.
Existing invalid-slot/frame and held-history tests cover these host policies;
this evidence closure adds no runtime behavior or duplicate tests.

## Explicit residual scope

0x4726D0 performs a separate aggregate reset once for previous sampled clips.
Its sole direct caller is 0x44E3A0: on a posing callback, object flag0x800000
set and0x80000 clear. The non-posing list pass sets0x800000 at0x44E4D9 after
0x473C70(object,0,8); the callback clears it after restoration. This transition
is part of RIDES-032, not the host's always-posed route. Channels ID12,
channel flag0x20, or null previous sampled clip are skipped by the aggregate
helper (0x472932–0x472974).

Geometry header flag4 also gates per-update cleanup at0x473CDB–0x473D0C.
The ordinary ride loader strips that flag. Neither this additive/header4 mode
nor view-dependent list-pass routing is newly implemented here. MD2-900 remains
registered for these residual cases; no universal lifetime equivalence is claimed.

Synthetic tests cover ordinary/fixed replacement before first key, all-slot
raw-flag reset and outgoing trailer gates, true held history versus ended-bound
writers, final hold sample, restore of held history, and delayed-key loop replay.
