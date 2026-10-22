# RIDES-011: Write coaster endpoint cell markers

## Status and classification
- Classification: missing.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
the cells of '+' (11) and '#' (16) are stamped as tp.exe's placement 0x528a70 stamps them (object type 4, the owner word, flag word 0x10 on the cell and its track-layer cell) and validated as its add-on mode does (a cell of a straight, unflagged tile of the selected ride, 0x535953..0x5359b3), and a track end '<' '>' 'D' needs the cell beyond it on the map (0x5295a8); the type 0x15 the commit writes on that cell with the ride's owner word (ops 0x87, 0x15, 0x83, 0x84, 0x81 at 0x5295f7..0x52963f) is not written: it is the coaster module's cell marker (0x452d30, called by the coaster tool 0x434412, 0x443057, 0x444ee7, 0x445b50), which OpenTPW's coaster builder neither writes nor reads

## Source inspection
- `source/OpenTPW/World/Objects/ObjectShape.cs:119`: 122: public static ObjectCellKind Classify( char symbol ); 123: {; 124: if ( symbol == ' ' ); 125: return ObjectCellKind.Free;

## Bounded work
Model type-0x15 owner markers for track-end placement and integrate coaster readers.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace marker lifetime through coaster edit/delete operations and second-layer ownership.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Each <, >, D endpoint writes owner/type on valid beyond-cell and refuses out-of-map target.
2. Deleting/replacing a coaster cleans markers without clearing another ride owner; add-on +/# stamping remains unchanged.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

## 2026-10-22 source reconciliation

This addendum supersedes conflicting historical implementation claims above. Source baseline: main `1c874922df6e42425dc5638f7e2554c9fde2c68c`. This is source inspection, not a new original-executable trace or an executed C# / browser test. The proposal remains draft; no whole-identifier retirement is claimed.

### Already implemented and corrected evidence

- [CoasterCellMarkers.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW/World/Coasters/CoasterCellMarkers.cs) provides live type21 owner reservations and derived secondary node references. The historical assertion that the builder neither writes nor reads these markers is no longer current.
- [COASTER-CELL-MARKERS.md](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/docs/COASTER-CELL-MARKERS.md) records the integrated station/node lifetime, stack republish, clear-versus-full-teardown, save/load rebinding and native preview priority evidence. Rotated `<` / `>` endpoints require a beyond cell; directionless `D` is refused in every rotation. Replace acceptance case 1's blanket assumption that every `D` writes a marker with this evidenced distinction. Add-on `+` / `#` behavior remains a separate preserved path.
- [CoasterCellMarkerTests.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW.Tests/CoasterCellMarkerTests.cs) supplies synthetic preview/ownership/lifetime cases. [CoasterCellMarkerLevelTests.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW.Tests/CoasterCellMarkerLevelTests.cs) covers actual placement registration and the restore-helper sequence. These assertions were inspected, not executed in this reconciliation.

### Remaining evidence and next action

Keep the documented defensive owner/type/generation lease: it deliberately avoids native unconditional foreign-cell commits and unsafe cleanup. Independent host single-cell previews do not reproduce sticky native failure globals or whole-drag aggregation. Derived tokens are live references, not native model-pointer identity, including clone/move-cancel stack reconstruction.

The Level fixture uses a null secondary restore payload. It is not proof of a populated secondary-map restore, complete disk-load order or rendered behavior. The next bounded verification is a populated secondary-map restoration fixture and exact-revision execution of the existing marker suites, followed by review of which host adaptations remain intentional. Do not reimplement type21 storage, duplicate current lifetime tests, or remove defensive ownership protections merely to match an unsafe native write. Full RIDES-011 remains open until the remaining contract and evidence are explicitly resolved.

