# Completed-preview link cursor

The path and queue tools now supply the link pointer from their existing successful preview validators. A path preview records a valid queue-end conversion; a queue preview records its existing final-cell join to a path. These are the code-8 branches already documented in ParkPathBuilder and QueueLineWriter.

The HUD uses this indication only for a completed current path/queue ghost. It does not infer a link from the broader `EndedOnExisting` field. Preview replaces the indication; commit, cancel and tool changes clear the ghost. Preview does not write cells or spend money. Existing UI, ride/camcorder, placement and cash pointer precedence is unchanged.

This is a bounded host adaptation. UI-100 remains open for native refused-preview link/noplace arbitration, end-pointer state, the additional cash writer and crosshair art. The completed-preview guard intentionally does not claim those transitions are reconstructed.

Asset-free tests cover both actual preview producers, ordinary cells, queue-end validation, object-overwrite normalization, refusal before/after a link, real tool reset paths and pointer priority. C# tests require exact-head CI; source review alone is not execution or rendered visual proof.
