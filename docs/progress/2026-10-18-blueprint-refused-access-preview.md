## Refused queued-object access markers (October 18, 2026)

The ordinary object's failed preflight forces its entire ghost red, including the outside access cells that the ghost still
stamps. The earlier independent-cell colour proposal was rejected after tracing that caller. The verified omission is now
implemented narrowly: an ordinary HasQueue footprint with one entrance and one exit, wholly inside the host grid, can add its
outside cells in red after a known placement refusal. An off-grid entrance omits both outside faces; an off-grid exit keeps the
entrance face. The whole footprint stays red, without arrows.

Both HUDs share the pure presentation adapter and retain their existing money/colour decisions. Accepted previews and the
unproven no-queue, special-symbol, tool/add-on, missing/multiple-access, partly off-grid and global-refusal cases keep their
existing fallback. Placement, overwrites, path clearing, purchases, refunds and simulation are unchanged. BLUEPRINT-003 remains
open for those residuals, track markers, arrow models and the fence ghost. See the
[bounded evidence and implementation](../reverse/BLUEPRINT.md#bounded-refused-access-preview-october-18-2026).

Synthetic tests cover rotations, non-square bounds and access order, fixed-path/ownership/occupancy refusals, fallback output,
and repeated preview/cancel without cell-map writes, object changes or ledger charges. Fidelity/progress checks and their Python
tests pass. The focused .NET test command was attempted but cannot run locally because `dotnet` is absent. No original software
was executed; no original assets or disassembly are included.
