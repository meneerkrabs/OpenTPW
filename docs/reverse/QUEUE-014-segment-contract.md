# QUEUE-014: bounded native segment implementation

The queue tool's supported plain-cell route uses whole-line passes, not repeated chain-extension operations. This is a bounded port of tp.exe, not a new host transaction policy. Synthetic tests describe expected raw fields; they are not recordings of executing the original game.

## Evidence and ordering

- The click at `0x5273DF` pushes the snapped endpoint before overwrite87, type3, links80, direction82, secondary85/86, owner83 and refresh81 passes.
- `SetCellType 0x5346D0` clears an interior ordinary path before its per-cell debit gate. Path clear `0x5367A0` unlinks all eight reciprocal neighbours. No full-line affordability transaction and no rollback follows failure.
- The first links80 cell (`0x53522D..0x53538C`) links only a cardinal neighbour equal to the selected ride's actual entry cell whose direction points back. The host resolves the owner-matched live object's last typed entrance, not the visitor bridge's outside-cell coordinates. Saved divergent native mEntryPos and overlapping/deleted entry reconstruction remain unsupported.
- The remaining links80 pass (`0x53538C`) tests the predecessor's current raw type3, all-eight link count below2 and owner zero or the tool's owner. The entire type pass precedes it; converted paths therefore have owner zero at this test. Direction82 fills only zero direction at every cell, including the first (LayLine supplies a nonzero movement step); owner83 visits every original line cell, including empty cells left by failure and unvisited suffix cells.
- Secondary85/86 return without mutation for same-cell secondary type0. Other secondary/object/pool layers are not claimed supported here.
- The write branch at `0x534589` clears the final-cell latch on every visited type cell. Only a visited last cell whose old type is1 or3 sets it (`0x5345AD`). Refresh81 consumes that latch (`0x5340C1`), rather than inspecting the endpoint's current type. Failure before a terminal path therefore keeps the tool active despite later full-line refresh.
- Active-tool Back `0x52FE50` pops the stored endpoint. A prior corner currently of type1 suppresses the second removal click. Otherwise reverse mode32 continues over empty cells without clearing their residue. Queue cleanup counts cardinal bits when recognizing a bend (`0x5369B4`), unlike the all-eight link-admission count.
- Refund uses the actual owner cell, queue price, low32 product and unsigned division100 before credit. Empty cells never refund. The older per-cell command refund remains unchanged for replay.

## Explicit supported domain and residuals

The new route is limited to primary types0/1/3, empty track records, no object/prototype/pool/bridge/slide reservation, and existing queue-tool starts or continuation of a newly committed active segment. Authoritative command admission independently requires an own/owner-zero queue start or typed same-owner residue with nonzero direction; arbitrary decoded empty/path starts are rejected. UI entry/exit clears its active-session flag, so reopening does not reuse a failed endpoint receipt. Free-start UI, other primary/secondary layers and overwrite-demolition remain the legacy route, not completed native semantics. A type3 placement counter at32767 is explicitly refused before mutation; native16-bit wrap and negative-counter persistence remain unsupported rather than clamped.

Singleton native undo is rejected unchanged: no active segment receipt generates zero movement. Completed terminal segments discard their active receipt. A closed/reopened tool reconstructs its queue corner stack and does not resurrect that receipt. Native guest effects beyond the already-supported set-down and queue refresh hooks remain residual; this document does not assert full native guest-timing equivalence.

## Persistence and replay compatibility

Raw arrays are the only runtime truth. A shape classification permits a strict exceptional empty/path snapshot representation; it does not store a second mutable value map. Required sidecar members, ordinary-record agreement, coordinate uniqueness, supported flags/types and owner bounds are checked. Object footprints, tracks and pool-bridge composites are validated before restoration. Hash additions derive current exceptional raw bytes, not classification or origin.

Resume and TPI checkpoint version2 accept version1 only without new residue fields, then validate the migrated candidate. Shared layout export refuses exceptional raw metadata before building a package, since that separate format cannot preserve it.

Distinct queue-segment and active-undo command tags16/17 use command version2. Existing command kinds retain their version1 bytes and legacy execution meaning, even in mixed version2 logs. Version1 logs cannot contain new commands. Partial commits are accepted/logged because later native passes actually mutated the map.

## Acceptance evidence

The synthetic suites cover four directions; destructive cash failure and suffix residue; lateral/diagonal unlink; owner and link-count admission; terminal/interior NoModify; explicit counter overflow exclusion; final latch; active undo, empty survival and prior-path exception; signed debit and unsigned refund arithmetic; strict snapshot and migration; shared-export refusal; raw hashing and mixed-version replay. Source review, executable test results and CI must be reported separately. This document itself does not claim any tests or CI passed.
