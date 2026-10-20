# Live balloon script timing (GUEST-017)

Bounded static evidence read October 20, 2026 from the existing Windows v2.0
sp20_nocd/tp.exe, SHA-256
5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805.
No original executable was run; the regression fixtures are synthetic, not captured original images.

## Contract

- Creation 0x4758F0 initializes alpha255, interval62 and deadline=clock(0x785970)+62,
  with live-sprite flag+0x114 clear. Shop0x4FE722 selects held program0x74F480 through
  0x475A10. Registration does not execute the script. The first due FRAME0 makes it visible.
- Burst0x4FE950 calls direct rebind0x475B80 with0x74F4C0 then clears guest+0x210.
  Rebind0x475C23 resets program/stack bookkeeping, not deadline+0x7C, alpha,
  current frame or live-sprite flag. Until eligible, the prior intact visual persists.
- Scheduler0x475360 samples the game clock once, skips signed now<=deadline and
  steps each due player once. Deadline then becomes sampledNow+62. It never catches
  up multiple script steps after a stall. Signed comparison and wrapping addition are intentional.
- Burst program sets alpha250, enters its loop and yields at FRAME1 (0x476940).
  Subsequent due passes subtract20, yielding230 through10. The next due pass halts
  and clears visibility; a later due pass reclaims the ended record.
- Main loop0x54F5FB runs sprite scripts before world update0x54F7BB→0x516380.
  Both tour and sprite pass are gated on even substeps at0x54F5D7. The inner loop
  may repeat (0x54F8DA→0x54F4BF) before people placement0x54FA85→0x518F90.
  Thus no fixed one-render-frame delay is correct. Drawing0x475430 reads sprites,
  without stepping their programs; render callback0x54E345 invokes it.

## Host integration

BalloonSpriteState is presentation only. GuestSimulation creates, bursts and removes
players at semantic producer sites without changing HasBalloon, BalloonLife or RNG
call order. Expiration, prank and guard seizure burst; ride boarding removes. Pool
return and TPI umbrella release keep their explicit extension boundary; umbrella
release is not recategorized as TPW burst.

Players share BodySpriteClockMilliseconds and ScheduleBodySprites opportunities.
The established fixed-60Hz sampled-clock and non-pooled guest-before-object order
remain host extensions. Paused parks do not schedule; a lost park still schedules
existing players. Rendering only places held balloons and reads their current output;
it neither detects ownership transitions nor advances deadlines. Detached players
have independent monotonic identities, survive new balloon creation and guest-id
reuse, and retain the last placed geometry. If never placed, coordinates remain the
creation defaults. The held-balloon bob/placement walk is unchanged. Drawing retains the live-guest interleaving by colour guest id, orders independent players within each group by monotonic player identity (an older detached burst before a replacement), then appends remaining players in creation order. Grouping is presentation only and does not reattach a burst when a guest id is reused; exact native slot-order overlap remains unqualified. The shared bob phase/accumulator remain renderer-local and are not saved, so own-save script/geometry history does not imply full pixel-identical presentation continuation.

Own saves carry an optional version1 Balloons presentation section: held and detached
players, deadlines, visibility/program phase, identity counter and placed geometry.
Capture and restore deep-copy; validation checks section version, sprite-clock
presence, identity and owner consistency, finite geometry and program state. Restore
does not replay gameplay events. This section is absent from both manual canonical
and legacy gameplay hash projections, with no hash version or pin change.

## Explicit residuals

GUEST-017 is not retired. Native original Scripts records are parsed but their clock
origin/program phase and orphan burst-slot ownership are not imported (SAVE-013).
Old own saves without Balloons explicitly reconstruct held players at the saved
host clock, with the history-reconstructed marker; no detached history is invented.
Native render-epoch counter writers and full placement fidelity are not closed by
this work. No framebuffer/original-media equivalence is claimed.

BalloonSpriteLifecycleTests cover strict boundary, signed wrap, one-step stalls,
alpha10/halt/reclamation, create-burst-create, guard versus ride removal, frozen
positions, JSON deep-copy/validation/legacy reconstruction, actual host pause/lost
and batching without rendering, and presentation exclusion from gameplay hashes.
