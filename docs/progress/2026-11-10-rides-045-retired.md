## RIDES-045 retired: Theme Park Inc's engine never writes a script variable (November 10, 2026)

The entry said a Theme Park Inc object brought into a Theme Park World park keeps the variables that only Theme Park Inc's engine writes at 0, so the branches they guard never run. The follow-up was to model those writers. Reading the executable shows there are none to model.

The script instance allocates its variable array and zero-fills it at load (`0x6AEC50`), and the only writes afterwards are the interpreter's own operand-destination tails — seventeen of them from `0x6AB48D`, each storing the accumulator when a destination operand's high byte is `0x40`. The engine-side setter exists: `0x6B0780` checks the subsystem flag, the null script, the index against the count at `+0x8C` and stores through the array at `+0x1C` — but **no code calls it**: no `e8` target in the executable, no entry in the plugin thunk table, no data word holding its address. The sibling getters and field setters are equally uncalled.

So the read-only slots — capacity, duration, ride-closed, worn, break status, special, the staff room's closed — stay 0 in the original too, and the scripts only ever clear the ones they also read (`VAR_BOMB`, `VAR_FPBYPASS`, `VAR_USE`, `VAR_EMPTY`). The FastPass bypass, the stinkbomb arming and the capacity protocols are therefore vestigial in the shipped executable; the behaviours themselves are native.

OpenTPW already keeps the slots at 0, so nothing changes in behaviour. The entry is retired: the declaration now records the finding as `[BIN:TPI-EXE:0x6B0780]` and the register loses one ID.

The corpus scan behind this is recorded here rather than in the code: 287 compiled scripts in 248 archives declare `VAR_FPBYPASS` at slot 12 in all 69 that have it, `VAR_SPEED` and `VAR_SPECIAL` both at slot 10 in their 22 and 15, and the three litter bins put `VAR_BOMB` at slot 2; the slot number is per script family, so it cannot be mapped globally.
