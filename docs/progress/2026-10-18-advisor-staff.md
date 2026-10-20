## The advisor's staff callbacks: hiring, patrols, training, limits and the staff room (October 18, 2026)

- **38 more scan callbacks are ported from `tp.exe`, line by line.** `AdvisorStaffCallbacks` (`World/AdvisorScanStaff.cs`) scores the hire-guards and hire-entertainers lines (11, 13, 14), the set-a-patrol-area lines of the mechanics, handymen, guards and
  entertainers (18, 20, 23, 26), the train-your-staff lines (28-32), the too-many-staff lines (108-112), the park-limit lines (114-123), the staff-happy and staff-trained congratulations
  (144, 146-150), the staff-room lines (169, 263) and the general hire, patrol and training lines (170-172). Each reads the park through `IAdvisorParkState` and the new `IAdvisorStaffState`, which `EconomyParkState`
  implements from the staff roster, the staff HQ's training state, the object list and the guest census without writing to any of them. The advisor stays outside the world hash, so no determinism pin moved.
  The producer inventory marks the 38 rows wired (171 in all); the coverage rows 19, 21 and 27 and the prank rows 12, 22 and 98 stay blocked.
- **Reading the bytes corrected the first formulas.** The patrol-area lines score `(100 - percent) x ScorePerUnpatrolledPct` (`DontBotherIfPatrolledUp` only bounds the percent); the "training in progress" word is the role's
  budget in the staff HQ, and the average training is the grades, not the training points; the staff-happy line lets equality pass; the park-limit words are `StaffPoolInfo`'s `Max<Role>InPark`, and the researchers' limit line
  compares the guards' limit, a copy slip of the original that is ported as it is.
- **One piece of state the original read and OpenTPW did not keep.** The staff HQ's `mHaveEverTrained` byte, which the training window sets once a budget above 0 is stored and which only the "train your staff" line (172) reads,
  is now `ParkStaff.HaveEverTrained`: set by `SetTrainingBudget`, imported from an original save's staff HQ, and written to the park save only when set (older saves derive it from the budgets).
- **The guest census gained one statistic.** `IParkGuestStatistics.CensusPercentBelowHappiness` is the original's `0x4C9750`: the percent of the census guests whose happiness byte is below a level (the entertainer line asks for 30).
- **Tests.** `AdvisorScanStaffTests`: each callback just below, at and above its thresholds over hand-built park states with a different value per key (so a swapped key or role shows), the zero without a park, a staff view or
  a threshold, the scan's row order and pacing, a pass through the real queue, the statistics over a real `ParkEconomy`, the training flag through the park save, and the guest bridge. ADVISOR-009 narrowed: 79 scanned callbacks
  remain (21 constant-zero stubs, 58 computed).
