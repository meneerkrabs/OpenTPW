# UI-914: Scope information history residuals

## Status and classification
- Bounded implementation: TPW native statistics histories and independent host rating persistence.
- Classification: `missing_behavior`. UI-914 remains open; this is not retirement or full park-information fidelity.
- Implementation baseline: main `dee52e34` (October 19, 2026). The original proposal progress fragment is preserved unchanged.
- No original executable execution, redistributed game assets, private source material, or native statistics export changes.

## Implemented boundary
`SaveParkStatistics` resolves through `OriginalParkImport`'s World-referenced model-13 analyser and reads five monthly histories, live arrivals and recent stays. Constructor, serializer, month producer and graph-consumer traces agree on the fixed TPW wire layout. The complete body and preceding capacities are checked before reading selected rings. Malformed selected metadata or active nonfinite happiness rejects the bundle before economy application. Signed values and each ring's independent physical head/wrapped state survive; inactive host slots become zero, while immutable parser records retain raw words.

`ParkStatisticsHistory.Rating` is independent of the financial ledger. Import creates no bank months. Month close samples rating once and shares that value with the ledger. Host JSON captures the ring, including an explicitly empty rating when ledger history exists. Only absent legacy rating or an absent legacy statistics section seeds rating from existing ledger months, once at restoration. The graph's live draw consumes the independent ring, and statistics remain outside the simulation digest.

## Evidence and tests
- Wire provenance: analyser constructor `0x4C58C0`, serializer `0x4C5D70`, word-ring serializer `0x4CDF00`, month producer `0x4C7720`, graph dispatch `0x4A4750`.
- Fixed body 73,536 bytes; first monthly record at body offset 62,590; monthly stride 589; selected ordinals people 9, arrivals 12, happiness 13, stay 14 and rating 15; recent stays at 72,014.
- Asset-free regression fixtures cover identities, offsets, truncation, prefix capacities, independent ring state, signed and floating interpretation, atomic refusal, continuation, immutable copies, legacy migration, graph drawing and ledger/RNG/hash controls.
- An asset-gated shipped-save test checks one historical month: people 0, arrivals 13, happiness 0, stay 0 and rating 18; recent stays empty. No fixture bytes are committed.
- Python source integration guards supplement the existing full C# CI suite; they do not replace C# execution. The implementation workspace has no .NET SDK, so local C# build/test execution is explicitly unverified pending CI.

## Still open
- Theme Park Inc has different variable-count analyser records; none of these TPW ordinal meanings are transferred to it.
- No real long-running/wrapped save corpus was available. Wrapped and continuation fixtures are synthetic checks against the traced contract.
- The guest stay source, count alignment/icon re-show, census semantics, cross-park graph preferences and display-resolution behavior remain separate UI-914/UI-907/UI-060/GUEST-920 work. This slice changes no icon or count-cell evidence and does not edit `ParkHud.InfoWindow.cs`.
- Native statistics export remains template preservation; normalized host JSON cannot claim byte-identical native analyser reproduction.

## Ownership and acceptance
- Coordinate with save, guest/history and UI owners; preserve concurrent bridge, winter, RecentItems, RoleOrder and ArrivalWaves work.
- Review codec and persistence independently, retain existing complete CI checks, and verify the actual published head before review/merge decisions.
- A successful source guard or synthetic test is not original execution, pixel parity, or qualification of unsupported/corpus-external cases.
