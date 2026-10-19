## Ring floaters bob in the wave pool (October 19, 2026)

A new pool activity, `waves.ring` (`WaveRingActivity`, `[EXT:pools-waves]`): while a pool's waves run, a free swimmer may take one of the pool's inflatable rings and bob on the swell for 15 to 30
seconds, drawn with the lazy river's ring art, then swim on. It is offered like the other activities, at the host's cadence, but only while at least 40 turns of the wave machine's run are left,
never in or beside a lazy river lane or beside the pool's steps, and only while a ring is free: one per four bodies the pool takes, counted against the wave machine (so removing the machine aborts its
floaters like any attraction). A float never runs beyond the end of the waves' run. The waves rock the floater to and fro: the new `PoolWaves.Sway` is the push of the crest along +x +y and as much
back in the trough, applied through `PoolSwimming.Drift` (free water, clear of the other bodies, never into a lane), and when the swell has carried the floater more than a quarter cell from the spot
where it took the ring it paddles back at 1/20 cell a turn, so it bobs about its spot instead of ending at the far wall. A floater is paid by the waves as a free swimmer is (once per period) and 2 more at
the end of a whole float; waves switched off or a machine taken away end the float at once, without the bonus. `PoolWaves.Push` is unchanged (it now shares its arithmetic with `Sway`), and
`PoolWaveSettings.ActiveMachineIn` names the machine `ActiveIn` reads its settings from.

Integers only, one draw (the float's length, from the swimmer's own stream), no new save field (the spot is in the guest's activity fields). A pool without running waves offers nothing and draws
nothing: every determinism pin is unchanged (the water-attractions run's pool is all lazy river, where no ring is offered). Checked in a silent Jungle capture of a wave pool with fourteen swimmers:
several take rings and bob among the swimmers. New tests: offers only while enough of the run is left, with the machine as target, away from the steps and from a river lane, never more floaters than
the pool's rings, none without waves or with them off; a floater rocked by the waves within the run, staying within two cells of its spot and paid; switched-off waves ending the float without the
bonus; a save on a ring continuing identically; the sway equal to the push on the crest and as much back in the trough.
