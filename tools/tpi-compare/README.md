# TPI comparison probes

Static metadata only. These tools do not execute games/installers, remove
protection, decode a new TPI format, or implement a TPI game target. Source assets
remain outside Git; output consists of identities, header fields, counts, names,
and existing-reader outcomes. Serial/license files and the known fix directories
are excluded before contents are read.

The C# tool references the existing `OpenTPW.Files` project. It adds no packages.
Recognized signatures take priority over extensions. Candidate extensions can
request a reader, but a successful signature/reader result is needed for a
format claim. Existing readers cover DWFB/WAD, M3D2/MD2, RSSEQ/RSE, TP2M/MAP,
SAM text, and SDT entries. COS uses `SaveReader.Inspect` solely to test the known
save envelope; there is no COS payload decoder. SHPI/FSH is recorded without
inventing an image decoder. `--version-info` reads standard file metadata and
must not be presented as an engine-version proof.

The WAD probe preflights directory/name/payload bounds before using `WadArchive`.
It skips members over 16 MiB or compressed inputs over 64 KiB; skipped members
are not reader failures. Sampling is deterministic: terrain/standard settings
first, then features, then other files. Physical probes are capped at twelve per
family, member probes at twenty-four per family. Full physical-file hashes and
signature counts are collected separately from those bounded parse samples.
The maximum inventory is 50,000 files and maximum physical parse input 128 MiB.

From the repository root, using an existing .NET 8 SDK and the existing reader
dependencies:

```sh
dotnet run --project tools/tpi-compare/TpiCompare.csproj --configuration Release -- --self-test
python3 -m unittest discover -s tools/tpi-compare -p 'test_*.py' -v
dotnet run --project tools/tpi-compare/TpiCompare.csproj --configuration Release -- /Users/sander/server/game-assets/theme-park-world TPW-Windows-baseline /tmp/tpi-compare-tpw-baseline.json
dotnet run --project tools/tpi-compare/TpiCompare.csproj --configuration Release -- /Users/sander/server/game-assets/theme-park-world-patch2 TPW-Windows-official-Patch2 /tmp/tpi-compare-tpw-patch2.json
dotnet run --project tools/tpi-compare/TpiCompare.csproj --configuration Release -- /Users/sander/server/game-assets/theme-park-inc/retail TPI-retail-InstallShield-payload /tmp/tpi-compare-tpi-retail.json
dotnet run --project tools/tpi-compare/TpiCompare.csproj --configuration Release -- /Users/sander/server/game-assets/theme-park-inc/cd/THEME_PARK/CoasterData TPI-retail-CD-coasters /tmp/tpi-compare-cos.json
python3 tools/tpi-compare/pe_metadata.py /Users/sander/server/game-assets/theme-park-inc/cd/THEME_PARK/Game.exe /Users/sander/server/game-assets/theme-park-inc/cd/THEME_PARK/IOSYS002.dll /Users/sander/server/game-assets/theme-park-inc/cd/THEME_PARK/drvmgt.dll /Users/sander/server/game-assets/theme-park-inc/cd/THEME_PARK/secdrv.sys /Users/sander/server/game-assets/theme-park-world-patch2/TP.EXE /Users/sander/server/game-assets/theme-park-world-patch2/TP.ICD --output /tmp/tpi-compare-pe.json
python3 tools/tpi-compare/feral_metadata.py /Users/sander/server/game-assets/mac-feral/bin/SimThemePark.data /Users/sander/server/game-assets/mac-feral/bin/libraries/engine_shared.data --output /tmp/tpi-compare-feral.json
python3 tools/tpi-compare/summarize.py --tpw /tmp/tpi-compare-tpw-baseline.json --patch2 /tmp/tpi-compare-tpw-patch2.json --tpi /tmp/tpi-compare-tpi-retail.json --cos /tmp/tpi-compare-cos.json --pe /tmp/tpi-compare-pe.json --feral /tmp/tpi-compare-feral.json --output tools/tpi-compare/evidence.json
```

The retail payload was extracted with the already installed `unshield`; this is
archive extraction, not installer execution:

```sh
unshield -d /Users/sander/server/game-assets/theme-park-inc/retail x /Users/sander/server/game-assets/theme-park-inc/cd/THEME_PARK/data1.cab
```

`pe_metadata.py` reads bounded standard PE headers/imports and candidate names
using [Microsoft's PE specification](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format).
RVA resolution requires one file-backed span; malformed imports are reported
without unwrapping. Candidate text names are not validated RTTI. PEF metadata
reuses the repository's tested `pef.py` and remains a separate Mac baseline.

See [TPI-COMPARISON](../../docs/TPI-COMPARISON.md) for results and limits.

## Full corpus mode

`--full` removes sampling limits, hashes every WAD member, probes every recognized
family, adds BF4 decoding through `FontFile`, and records only value hashes for
SAM tokens and variable-name hashes for RSE. It retains size/count limits;
compressed member input is capped at 2 MiB and decoded member size at 16 MiB.
Unknown COS/SHPI header/table observations remain metadata, not new decoders.
The actual corpora had no WAD member exclusions at these bounds.

```sh
dotnet run --project tools/tpi-compare/TpiCompare.csproj --configuration Release -- --full /Users/sander/server/game-assets/theme-park-world TPW-Windows-full /tmp/tpi-full-tpw.json
dotnet run --project tools/tpi-compare/TpiCompare.csproj --configuration Release -- --full /Users/sander/server/game-assets/theme-park-world-patch2 TPW-Patch2-full /tmp/tpi-full-patch2.json
dotnet run --project tools/tpi-compare/TpiCompare.csproj --configuration Release -- --full /Users/sander/server/game-assets/theme-park-inc/retail TPI-retail-full /tmp/tpi-full-retail.json
dotnet run --project tools/tpi-compare/TpiCompare.csproj --configuration Release -- --full /Users/sander/server/game-assets/theme-park-inc/cd/THEME_PARK/CoasterData TPI-CD-coasters-full /tmp/tpi-full-cos.json
python3 tools/tpi-compare/readme_metadata.py /Users/sander/server/game-assets/theme-park-inc/cd/THEME_PARK/Readme.txt /Users/sander/server/game-assets/theme-park-inc/cd/THEME_PARK/ReadMe.htm --output /tmp/tpi-full-readme.json
python3 tools/tpi-compare/full_summary.py --tpw /tmp/tpi-full-tpw.json --patch2 /tmp/tpi-full-patch2.json --tpi /tmp/tpi-full-retail.json --source-root . --cos /tmp/tpi-full-cos.json --readme /tmp/tpi-full-readme.json --output /Users/sander/server/game-assets/theme-park-inc/analysis/tpi-full-evidence.json
```

The full artifact remains outside Git at the path above. Generate the checked-in
compact review artifact separately:

```sh
python3 tools/tpi-compare/compact_audit.py --full /Users/sander/server/game-assets/theme-park-inc/analysis/tpi-full-evidence.json --output tools/tpi-compare/full-evidence.json --regeneration-command "python3 tools/tpi-compare/full_summary.py --tpw /tmp/tpi-full-tpw.json --patch2 /tmp/tpi-full-patch2.json --tpi /tmp/tpi-full-retail.json --source-root . --cos /tmp/tpi-full-cos.json --readme /tmp/tpi-full-readme.json --output /Users/sander/server/game-assets/theme-park-inc/analysis/tpi-full-evidence.json"
```

The compact artifact retains all partial-reader records, diagnostic group counts
and examples, corpus manifests, opcode proofs and format limits. It pins exact
full-report bytes/SHA-256 plus fingerprints of the detailed arrays.

The full artifact records manifests, every exact shared payload hash, duplicate
occurrence counts, reader outcomes, missing-handler opcode IDs with source/script
hashes, standard-settings key/value-hash differences, and training/direction key
searches. `--readme` can additionally consume a metadata-only readme report.
Enum/handler status is a static source declaration audit, not VM execution or a
claim that Hooked effects are implemented for TPI.

## Remaining-format metadata

After producing an updated full retail report with the current C# tool:

```sh
python3 tools/tpi-compare/format_questions.py --cos-root /Users/sander/server/game-assets/theme-park-inc/cd/THEME_PARK/CoasterData --sdt /Users/sander/server/game-assets/theme-park-inc/retail/Data/levels/arabian/Music/MusicHD.sdt /Users/sander/server/game-assets/theme-park-inc/retail/Data/levels/water/Music/MusicHD.sdt --shpi-report /tmp/tpi-full-retail.json --speech /Users/sander/server/game-assets/theme-park-inc/retail/Data/global/Speech/speechHD.SDT --output tools/tpi-compare/format-evidence.json
```

The prototype enforces corpus-checked COS bounds, hashes the observed 128-byte
header region and body, classifies SHPI entry/attachment header observations,
and inspects SDT boundary candidates. It compares complete speech blocks by
SHA-256 without decoding audio. No binary string names or payload arrays are
stored; candidate semantics and recovery/repair remain outside the prototype.
Primary format-implementation references and controller memory/file distinctions
are documented in TPI-COMPARISON.
