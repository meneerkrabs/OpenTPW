## Co-op ticket purchases reconcile at hand-back (October 27, 2026)

A park returning from a co-op session credits its newly earned tickets before its ticket purchases enter the player's account. Global-ticket arbitration still happens first, and the account remains untouched while the park is shared. This prevents a purchase funded by the session's local tickets from being skipped as unaffordable and leaving those tickets available to spend again.

Two synthetic regressions cover an initially empty account earning three local tickets and buying item 1112, repeated hand-backs, and reloading the progress file. The expected account keeps the purchase identity, three earned and three spent tickets, and no available tickets; repeated reconciliation must not change the saved progress.
