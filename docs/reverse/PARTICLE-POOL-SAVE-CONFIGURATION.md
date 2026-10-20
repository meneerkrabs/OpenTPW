# Saved particle pool configuration (bounded SAVE-013)

## Evidence manifest

Static PC evidence from `sp20_nocd/tp.exe`, SHA-256
`5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805`,
inspected on October 20, 2026 against OpenTPW main `2d7a1ae88127cd103ec6f211f79697ea93d6b7eb`.
The executable was not run or uploaded. This is instruction-level evidence, not
an original-game runtime/visual comparison. All newly added fixtures are synthetic.

- Writer `0x51F680` checks the initialized flag, not the live emitter or effector
  count. `0x51F6EC..0x51F6FA` writes the entire `0x9E68` pool table at `0x80CED0`.
- Loader `0x51F851..0x51F862` reads the entire table back. Its saved globals
  include low-detail at `+0x08`, capacity at `+0x0C`, and density at `+0x20`.
- `0x51F8AE..0x51F8DC` allocates `52 * savedCapacity`. With no saved particle
  bytes, `0x51F91B..0x51F9FA` builds an empty free list and clears emitter live
  particle counts/first links. It does not replace settings with host defaults.
- `0x51F7D1..0x51F7E5` keeps the host ground callback/sine-table pointers;
  `0x51F9FC..0x51FA06` restores only those two pointers after loading.
- Initialization `0x51FAB3..0x51FAC5` clamps capacity to 100..32767, and
  `0x51FAE1..0x51FAEA` maps density zero to one. **Load does neither.**
- Low-detail setter `0x51FD80` copies a full word. Constructors `0x521959`,
  `0x521E89`, and `0x522386` check zero/nonzero; storing a bool preserves that
  behavior, not arbitrary raw flag bits.
- Density scale `0x521D60..0x521E53` uses wrapped 32-bit multiplication and
  arithmetic right shift by ten, signed-byte rates and signed-word cap. The
  current implementation already follows those operations. A bounded scan of
  `0x51F300..0x522530` found no separate direct density/capacity setter; this is
  not an executable-wide absence claim.

## Managed restore contract

`ParticleSystemState.Configuration` is optional for older own-format saves.
Missing/null keeps the receiving pool's **current** settings, including any
settings from a previous restore. Present configuration carries signed density,
capacity and low-detail semantics. Original import always supplies it for an
initialized pool. An initialized empty pool is captured too, preserving its RNG,
counters and free-list order for the next new emitter or effector.

Capacity outside 100..32767 is rejected before mutation. This is a managed
supported-domain/allocation guard aligned with native initialization, **not**
native loader validation. The reader still reports the original format; conversion
and the own-format validator enforce the runtime boundary. Capacity is not clamped.
All list/counter/template checks complete before any configuration or pool change,
and a resized particle array is allocated before it is assigned. The original
host ground callback remains in place. RNG restoration, handles, list order and
post-load screen-space/effector cleanup are unchanged.

Density preserves all 32 bits, including zero and negative values. It is not a
preset-only range. Existing saved emitter rates/cap/burst remain as saved; only
future creations use the restored density. A scaled burst is between -2097152 and
2097151 and its spawn loop stops at pool exhaustion, before a refused spawn draws
RNG; rates remain signed bytes, so tick emission stays bounded too. Density zero
still changes nonzero authored rates and positive cap/burst to one, not to disabled
emission. No new density arithmetic is introduced by this change.

## Verification and limits

`ParticlePoolConfigurationTests` adds synthetic original-block to own-envelope
round trips for empty/live pools, density1500 loading into host500 followed by a
**new** emitter compared with an uninterrupted1500 control, non-default empty RNG
history, low-detail refusals (ordinary/directed/effector), capacity shrink/grow and
exhaustion at both bounds, density0/-1/signed extremes, refusal atomicity for
invalid counts/lists/templates, absent/null legacy settings and ground-callback
retention, required configuration-member rejection and sliced/manual JSON equality. Existing save tests retain handle/list/RNG and post-load cleanup checks;
the optional real-save corpus expectation now includes empty initialized pools.

No local .NET execution is claimed. Source review and exact published-head CI are
required before merge; CI results belong to the PR, not an invented local pass.
The optional original-data corpus is not claimed executed when data is absent.

SAVE-013 remains open for sprite VM/animation phase, saved template and emitter
flags/groups, and effector identity matching. This slice changes none of those,
nor Level.Objects, track restoration, the world hash, or the save envelope version.
