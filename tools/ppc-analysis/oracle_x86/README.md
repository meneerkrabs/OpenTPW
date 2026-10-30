# x86 differential oracle (dev-only)

Runs isolated routines of the two identified Windows executables in the Unicorn emulator and
compares them with the plans. Policy, harness, targets and findings: `docs/reverse/ORACLE.md`
section 7. Unicorn is a **dev-only, optional** dependency (owner decision 2026-10-12): the game
never imports it and every test that needs it skips without it.

## Set-up

Python 3.12+ (tested with 3.14). Use a venv outside the checkout:

```
python3 -m venv /tmp/oracle-venv
/tmp/oracle-venv/bin/python -m pip install unicorn==2.1.4
```

Pinned version: `unicorn==2.1.4` (Unicorn is GPLv2 upstream; keep it out of any shipped artifact).

The private executables stay outside the checkout; the oracle refuses any file whose SHA-256 is not
the identified one:

| Variable | File |
| --- | --- |
| `OPENTPW_TPW_EXE` | the decrypted Sim Theme Park World v2.0 no-CD `tp.exe` (3,734,528 bytes) |
| `OPENTPW_TPI_EXE` | the Theme Park Inc no-CD `Game.exe` (6,742,016 bytes) |

Never the SafeDisc disc-root `Game.exe`, `TP.ICD` or any other file.

## Running

```
export OPENTPW_TPW_EXE=/path/to/tp.exe
/tmp/oracle-venv/bin/python -I -B -m unittest discover -s tools/ppc-analysis/oracle_x86 -t tools/ppc-analysis/oracle_x86
/tmp/oracle-venv/bin/python -I tools/ppc-analysis/oracle_x86/x86_differential.py        # prints the comparisons
/tmp/oracle-venv/bin/python -I -B tools/ppc-analysis/run_evidence_checks.py --tpw-exe /path/to/tp.exe
```

With the system `python3` (no Unicorn) or without the variables the PE reader tests still run and
the rest reports skips; nothing fails. CI has neither.

## Park fence, demolition and cell validator (round 9, package H)

`fence_oracle.py` runs the fence set-up `0x5323F0`, the Buy Land validator `0x531A50` and purchase `0x5327E0` on a synthetic 128 x 128 map; `fence_golden.py` writes `source/OpenTPW.Tests/ParkFenceGolden.cs` from 20 cases and `validator_golden.py` writes `QueueCellValidatorGolden.cs` from the cell validator `0x535670` (both accept `--check FILE`). Findings X35 and X36 in `docs/reverse/ORACLE.md` section 7.24.

## Demolition's record clear and the placed station (round 10, package B)

`demolition_oracle.py` runs the object demolition `0x527EE0` with the second layer's record clear `0x53B280` on synthetic cells; `demolition_golden.py` writes `ParkDemolitionTrackGolden.cs` from 17 cases (both accept `--check FILE`). `station_oracle.py` runs a kart or water station's placement (`0x528A70`, the station line, the exit and entry tiles) and the net worth `0x4C7DB0`; `station_golden.py` writes `StationFootprintGolden.cs`. Findings X37 and X38 in `docs/reverse/ORACLE.md` section 7.25.

## Track records on the fence's cells (round 10, package B)

`fence_track_oracle.py` lays synthetic second-layer track records on the fence sandbox's cells and runs purchases over them; `fence_track_golden.py` writes `ParkFenceTrackGolden.cs` from 16 cases (`--check FILE`, about eight minutes). Finding X39 in `docs/reverse/ORACLE.md` section 7.26.

## The sky objects' quad (round 10, package D2)

`moon_oracle.py` runs the sky object draw `0x5860D0` with its point transform `0x578D20` and sprite rotation `0x591430` on a synthetic sky object and prints the four corner offsets for a few camera rolls; `SkyTests` pins them (finding X41).

## Theme Park Inc's shop cost of goods (round 10, package K)

`tpi_goods_cost_oracle.py` runs the routine `0x5E6260` of the Theme Park Inc no-CD `Game.exe` (`OPENTPW_TPI_EXE`) with a synthetic thing and type record on random inputs and compares it with the formula `ThemeParkIncGoodsCost` ports (400 cases at the control words `0x27F` and `0x7F`, no mismatch).

## The coaster's run table and bend tables (round 10, package N)

`coaster_x87_oracle.py` runs the run builder `0x4402D0` over a sampled synthetic chain (`sampler(..., extrusion=...)`) and the bend car's tables `0x438AB0` (`bend_tables`); `coaster_runs_golden.py` writes `CoasterRunsGolden.cs` from 60 chains and `coaster_bend_golden.py` writes `CoasterBendGolden.cs` from 120 cars (both accept `--check FILE`). Findings X42 and X43 in `docs/reverse/ORACLE.md` section 7.29.
