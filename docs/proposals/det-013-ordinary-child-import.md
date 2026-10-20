# DET-013: bounded original ordinary-child graph import

The new `SavedScriptGraphImport` path applies only when an original RSSE block
contains ordinary child or parent references. Existing no-child imports keep their
legacy path. A failed graph import never falls through to that partial importer.
This is a partial implementation of proposal #504, not completion of DET-013.

## Original contract

The original loader (`tp.exe 0x5597a0`) reads embedded bytecode, strings and folder
(`0x559a5b`, `0x559bc4`, `0x559e03`) and preserves numeric child/parent/sound IDs.
It inserts each record at the head (`0x5599d3`), reversing file order. Normal
SPAWNCHILD (`0x555123`–`0x55519a`) writes reciprocal parent and inherited thing/mesh
identity. SPAWNSOUND stores only an owning reference (`0x555255`) and returns;
it does not establish the same reciprocal parent. Sound graphs therefore remain
refused rather than being mapped to the wrong managed parent semantics.

## Supported subset and atomicity

Every record must resolve to a forest of ordinary-child chains rooted in uniquely
identified fresh catalog-backed runtimes. There may be no extra live VMs, private
root RNG, previous script passes, sound edges, unresolved owners, cycles, aliases,
or inconsistent parent/thing/mesh links. The saved next-ID frontier must be valid
and leave room for allocation. A saved clock is mandatory.

Identity uses the complete normalized data-relative folder and exact embedded
code and string bytes, plus saved dimensions and effective slice budget. Parsed
RSE opcode/operand words and Latin1 string segments reconstruct those bytes
losslessly, including empty strings. Child filenames must be uniquely recoverable
from the owning catalog and unambiguous under the actual runtime resolver.
Arbitrary resolver delegates are never invoked. Variable names are not in RSSE
and remain sourced from the unique installed file.

Preparation validates and copies every supported state field, constructs children
without registering or executing them, and allocates replacement collections.
Only then are the prepared VM states, links, IDs, reversed list, pass and clock
committed. No RNG is drawn. Validation failures leave the whole world unchanged;
DIPMUSIC callbacks occur after a completed commit and are not misreported as an
atomic refusal if the host throws. Defensive aggregate bounds are 4,096 records,
4,096 catalog candidates, 64 MiB each of input and saved buffers, and 16 MiB of
identity comparisons. These limits are OpenTPW bounds, not original-engine facts.

## Own-save continuation

`RideScriptWorldSnapshot.ListOrder` optionally records head-first scheduler IDs
when they differ from descending-ID order. Validation requires every live ID
exactly once before restoration starts. An absent field retains old snapshot
behavior; ordinary descending-ID snapshots omit it. This preserves the imported
non-ID order across an own-save round trip without changing the child graph
format or old snapshot interpretation.

## Still not restored

Sound graphs; NAME state; original object animations and object lists; node,
bounce and walk tables remain residuals. TRIGWAITANIM retains its existing
animation-token reset/retrigger approximation. Header/table-size validation does
not mean those tables have been applied. Synthetic tests establish selected
PC/wait/graph behavior and managed own-save round-trip stability, not complete
original-engine continuation or original saved-child corpus coverage.

Validation includes nested non-topological IDs, first-sixteen-pass phase, waits,
stack/heap/limbo/timers, exact identity negatives, malformed graphs, sound refusal,
no RNG draw, and no-child compatibility. Local dotnet execution was not attempted
because of the existing sandbox IPC restriction; CI remains required.
