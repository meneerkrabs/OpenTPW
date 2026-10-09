# Patch data analysis

Read-only metadata comparison for two existing Theme Park World installation
roots containing `Data/`. Uses the repository's `BaseFileSystem`, `WadArchive`,
`SAMParser` and `RideScriptFile`, with no additional package dependencies.

```
dotnet run --project tools/patch-analysis/PatchAnalysis.csproj -- --self-test
dotnet run --project tools/patch-analysis/PatchAnalysis.csproj -- \
  /path/to/original /path/to/patched /private/path/to/diff.json
```

Only changed physical files and decompressed WAD members are reported. Byte
lengths and SHA256 identify both versions. No bytes, asset strings, instruction
listing or executable disassembly are emitted. Existing directories are required;
the analysis does not patch or execute the game. Keep original assets outside Git.

SAM comparison uses the existing parser, preserves duplicate-key values in order,
and normalizes numeric/boolean tokens for comparison. These are inferred token
types, not a verified game settings schema. Text values are represented by hashes.
`nonCommentTextEqual` separately checks whether removing `#` comments and
normalizing whitespace accounts for the entire difference. Other parser-ignored
syntax cannot be called a comment-only change from parsed entries alone.

RSE comparison reports header fields, changed string hashes and variable-name
hash inequality. A longest-common-subsequence alignment matches opcode/operand
kind shapes, then reports changed operands and removed/added instruction
addresses. The grid is limited to one million cells. Repeated shapes can make
alignment ambiguous; word addresses and total counts remain exact, while an
alignment is not a verified description of behavioral changes. No opcode
semantics or gameplay fix is inferred.

`--self-test` uses generated fixtures to exercise WAD member changes and identity,
SAM normalization and typed differences, and RSE operands/identity. Original-file
identity and actual-patch comparisons are separate evidence in
[PATCH-2](../../docs/PATCH-2.md).
