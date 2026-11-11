## COAST-004: what a loaded park does with the saved collision lists (November 11, 2026)

The entry's open question was whether the saved collision lists and their two marks are what the original re-tests from. They are not re-tested: the loader `0x437B60` restores the flag word and the ids, rewrites every slot through `0x43E520` (`0x438262..0x4382A3`), and nothing on the load path marks a node dirty — the only chain-wide `0x200000` setter is `0x43AFA0`, reached from `0x436A39` and `0x43B2DF`. So a loaded park keeps the saved lists verbatim until an edit dirties a node.

OpenTPW instead re-derives them on the rebuild that the load itself triggers. That is not a fidelity loss, because the only consumers are the drop `0x43AD50`, the save writer, the section builder's count test `0x43F4FB` and the guest predicate `0x441970` (`[ctl+0x140] != 0`), and the count is recomputed from the same geometry — importing the saved lists would change nothing observable.

Two smaller corrections come with it. The list is eight slots at `+0xB4..+0xD0`; the comment said "up to `+0xd4`", which the pass, the drop and the writer never touch. And the three load sites the entry called ambiguous — `0x43B159`, `0x43B3E5`, `0x43B63F` — are confirmed to be a ring buffer of another object (capacity at `+0xB0`, cursors at `+0xC4`/`+0xC8`), pushed and popped as peep ids, exactly as the comment already said.

The pass itself needed no change: the cut-end branch (`0x439B72`, drop only, never tested) and the `0x439C40` cut-end skip are both already ported.
