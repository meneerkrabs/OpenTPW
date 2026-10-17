## Hooked opcodes, flags, walk table and time base read from tp.exe (October 17, 2026)

The roughly 47 hooked script opcodes of the RSE VM (interpreter `0x551CB0`, jump table `0x5567D8`) are now read from their tp.exe handlers instead of being inferred.
Each operand is documented as the handler reads it (RSE-VM.md, "Hooked opcodes: the operands as the handlers read them"), and RideVM was changed where it differed.
The walk table (`WALKON`/`WALKOFF`/`WALKGET`, update `0x557D80`) is ported with first-free entries, states 1 to 4 and table-order `WALKGET`; the walk float, the 200 ms
`UNBOUNCE`/`FORCEUNBOUNCE` window, the `BOUNCE` deadline, the `REPAIREFFECT` polarity, `SCREAMLEVEL`, `SPARK`, the result flags of `GETTIME`, the trigger family and the
`BOUNCE` family, the cross-script variable opcodes and `END` follow the original. The script time unit is the millisecond of the global timer `0x785970`, and the critical flag
`0x87919C` has a single reader, the slice loop at `0x551725`. Corpus tests over the 308 shipped scripts pin the operand domains (no game bytes committed). TOUR-011 is narrowed,
not retired: the walking body between the nodes is still not drawn. Park-resume JSON gets an optional, validated walk table.
