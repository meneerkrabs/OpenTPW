# Isolated guest rules arithmetic

This dependency-free .NET 8 helper is outside the production project. Existing
`Guest`, `GuestSettings`, and `IRideVisitorBridge` cannot supply all recovered
operands: they lack the two four-ID histories, numeric shop effects, category
flags, and original metadata/weight bindings. No simulation calls this helper.

Run its synthetic regressions from the repository root:

```sh
dotnet run --project tools/ppc-analysis/lanes/guests/rules/GuestOriginalRules.Tests.csproj --configuration Release
```

Expected result: 23 cases passed. No original game assets or dependencies are
needed. Lookup buffers in the tests are synthetic; no complete original table
is embedded. `../evidence.py` independently validates the identified original
input, addresses, table samples, and bounded code hashes when local assets exist.

Binary identity: Feral `SimThemePark.data`, SHA-256
`04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5`;
code section 0, TOC data section 1 offset `0x8000`.

| Helper | Original arithmetic anchor | Supported scope |
| --- | --- | --- |
| `QueueCellGroup` | `0xddd2c..0xddd50` | Four positions per queue cell; no short-chain termination or geometric offsets |
| `DistanceMatch` | `0xe9320..0xe9398` | Bounded cell displacements, divisor 450, optional signed-short input to unsigned division |
| `QueueTerm` | `0xe93c0..0xe9420` | Near/far weight gate, raw object `+60`, low-32-bit arithmetic; no queue-length clamp |
| `ExcitementMatch` | `0xe9424..0xe9470` | Byte difference capped at 50; caller resolves category/weight gate |
| Need lookup helpers | `0xe94b0..0xe96bc` | Caller-supplied interpreted 11×11 and 21-entry tables; valid needs/effects 0..100 |
| `NormalizeBaseScore` | `0xe96c0..0xe9714` | Seven active register operands, low-32-bit products/additions, unsigned division |
| `ApplyHistory` | `0xe98c0..0xe9a30` | Used history first match; temporary history every match in slot order; signed truncating divisions 5/4/3/2 |
| `ApplyShopEffect` | `0xeabc4..0xeaca4` | Subtract signed effect amount and clamp the need to 0..100 |
| Ride illness helpers | `0xea4d4..0xea580` | Positive divisor, hunger-dependent integer bands, illness clamp 0..100 |

`NormalizeBaseScore` is a partial scoring stage. New/indoor/gold-ticket/cost
multipliers, candidate eligibility, selector comparisons, and history expiration
are not implemented. Callers must supply weights after the original register
truncation and queue/category gates. Six weight operands are derived from
low-16-bit fields; the nearby queue weight is loaded separately as a full word.
The helper does not guess how `GuestSettings` maps to those operands.

Unsigned score behavior is intentional evidence. With queue count 9, raw
capacity field 1, and a nearby candidate, queue match is −125 as a 32-bit
register (`4294967171`). Distance match 100 with weights distance 1 and queue 2
produces numerator −150 modulo 2³²; unsigned division by 3 yields `1431655715`.
Clamping the queue term to zero would change the witnessed arithmetic. History
division later treats the same register as signed; selector behavior must be
reviewed separately before any gameplay integration.

Used history (`guest+480..486`) exits to `0xe9978` after its first matching
slot. Temporary history (`guest+488..494`) falls through after every matching
slot at `0xe99b8`, `0xe99dc`, and `0xe9a0c`. Thus score 600, attraction ID 7,
no used matches, and temporary IDs `[7,7,7,7]` produces `600/5/4/3/2 = 5`.
Each division reinterprets the current 32-bit score as signed and truncates
toward zero. No uniqueness assumption is imposed on either caller span.

Zero/wrapped-zero divisors, invalid lookup layouts, nonfinite needs, and inputs
outside the declared bounded domains are rejected. This is a fail-closed helper
contract, not a claim about how the original handles undefined division or bad
metadata. Arithmetic is reproduced only within the supported contract.

See [the guest evidence report](../../../../../docs/reverse/PPC-guests.md) for
provenance and remaining Mac loader, cadence, sprite, script, and PC-parity gaps.
