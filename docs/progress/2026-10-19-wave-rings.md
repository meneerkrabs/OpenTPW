## Ring floaters bob in the wave pool (October 19, 2026)

A new pool activity, `waves.ring` (`WaveRingActivity`, `[EXT:pools-waves]`): while a pool's waves run, a free swimmer outside a lazy river lane may take an inflatable ring and bob on the swell for
30 to 60 seconds, drawn with the lazy river's ring art, then swim on with 4 happiness. It is offered like the other activities, at the host's cadence, and only while the pool's wave machine runs;
waves that are switched off or a machine that is taken away end the float at once, unpaid. The waves rock the floater to and fro: the new `PoolWaves.Sway` is the push of the crest along +x +y and
as much back in the trough, applied through `PoolSwimming.Drift` (free water, clear of the other bodies), and when the swell has carried the floater more than a quarter cell from the spot where it took
the ring it paddles back at 1/30 cell a turn, so it bobs about its spot instead of ending at the far wall. `PoolWaves.Push` is unchanged (it now shares its arithmetic with `Sway`).

Integers only, one draw (the float's length, from the swimmer's own stream), no new save field (the spot is in the guest's activity fields). A pool without running waves offers nothing and draws
nothing: every determinism pin is unchanged (the water-attractions run's pool is all lazy river, where no ring is offered). Checked in a silent Jungle capture of a wave pool with fourteen swimmers:
several take rings and bob among the swimmers. New tests: the offer exactly while the waves run, not without waves or with them off, never in a river lane; a floater rocked by the waves, staying within
two cells of its spot and paid at the end; switched-off waves ending the float unpaid; a save on a ring continuing identically; the sway equal to the push on the crest and as much back in the trough.
