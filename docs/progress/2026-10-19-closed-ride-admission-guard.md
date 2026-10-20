## Recheck closed rides at the stand point (October 19, 2026)

The called guest's stand-point admission now rechecks whether the ride is open
and outside its retained broken/condemned state before writing `VAR_LETMEON`.
The guard leaves coaster/track choice tests and already-consumed boarding alone.
[Synthetic host coverage](../CLOSED-RIDE-ADMISSION.md) exercises the actual
runtime close entry point and a live guest subturn before the next ride update.
Immediate pending-call withdrawal remains a separate `GUEST-011` residual;
no approximation ID is retired, and the tests do not claim original-game execution.
