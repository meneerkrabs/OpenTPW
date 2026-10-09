# Original-binary route: intake evidence

Observed on 2026-10-09 from the supplied TPWORLD.ISO. This is an initial assessment,
not a completed matching decompilation or recompilation feasibility pass.

## Binary identity

| File | Architecture | SHA-256 |
| --- | --- | --- |
| tp.exe | Windows PE32 / i386 | bc7de3c69e742e26ff1d2280921ab23f686538e8925db82caa2705e4fae713b8 |
| TP.ICD | Windows PE32 / i386 | df66561b91794368674a35abade40661398cd703697089c781a685a533fe550e |

The smaller launcher has linker version 5.0 and a small Windows import surface.
TP.ICD has linker version 5.10, stripped symbols/relocations, and an image base
of 0x00400000. Linker fields are evidence, not proof of the complete compiler/ABI.
Neither file was executed; no protection removal or modified binary was created.

## Runtime dependencies observed in TP.ICD

- Windows core: KERNEL32, USER32, GDI32, ADVAPI32, ole32, USP10.
- Graphics/input/audio: DDRAW, DINPUT, WINMM, DSOUND, QMIXER.
- Networking: WSOCK32 and several `wea*` DLLs including auth/chat/upload/city/news.

Source-port work must decide which imported services are offline-essential and
which can be removed behind offline feature boundaries. Console recompiler
runtimes do not supply this Windows service surface.

## Decision status

Retain the C# asset-driven engine while producing a functioning, tested vertical
slice. A full binary-driven pivot is not justified by architecture or linker
metadata alone. The 16-engineer-hour decision experiment remains open until one
bounded original function is recovered and its behavior reproducibly agrees with
a reference oracle; no completion percentage is assigned.

Further work: identify reachable code/function boundaries, edition differences,
compiler requirements and a safe runnable reference environment. Use original
analysis selectively to resolve model/VM/map semantics regardless of a later pivot.
