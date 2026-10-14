# Static TPI mission SAM inspection

Run `dotnet run --project source/OpenTPW -- --inspect-tpi-mission /local/mission.sam` to inspect one explicitly selected local file without starting a window, finding game assets or writing a profile. Existing export/inspection commands retain precedence if combined with this flag; select one inspection command per invocation. Invalid retained entries produce source/line diagnostics and exit code 1. A successful return means only that the checked retained entries passed these bounds.

The opt-in validator checks every retained occurrence, including duplicates:

| Entry | Accepted integer range/index |
| --- | --- |
| `Info.NoResearch` | `[0,2)` |
| `Completion.ThemeOpened`, `Completion.EndOfGame`, `Completion.LoadNewHoardings` | `[0,2)` |
| `TechLevel.Theme` | `[0,3)` |
| `BoundaryClicks[n].member` | Decimal index `0..63`; member value unchecked |
| `ExitStatistic[n].member` | Decimal index `0..4`; member value unchecked |

Bounds come from static descriptor/parser evidence in the modified no-CD TPI PE, SHA-256 `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`. The descriptor evidence ID is `ev_86b8ffde04258030d70469109d01662db40a36b9bebf282f973e2034841cd94f`; parser `0x419cd0` is `ev_fb0465ce66f6a262a6cd376a15a4af011250b1772fa83a8409694ca8f90a81de`; indexed lookup `0x4192c0` is `ev_d43208946cd99a21b72d2403e262832caae514054eef2879f6d84edfd65a2684`. This is static schema evidence, not retail or runtime parity.

The generic SAM parser and TPW settings behavior stay unchanged. Inspection uses exact case-sensitive known keys and the host reader's signed decimal integer syntax on the first retained scalar token. Native case, quoting, extra-value arity and full lexical rules remain unknown. Negative, malformed or overflowing retained group indices are explicitly refused by the host inspector. No values are clamped.

Each retained entry keeps its source, line and tokens. Unknown keys and kind-4 scalar fields remain unchecked. The output separately identifies noncomment records omitted by the generic parser, and counts skipped shape lines. Trailing free text and quoted-token syntax are not validated. Empty, partial or omitted input never establishes whole-text validity, requiredness, defaults, reset behavior or mission readiness. The command does not enable missions, research, economy, campaign progression or any TPI capability.

Only synthetic boundary/CLI fixtures are tested. No original executable is run, and no original mission files or native code are distributed.
