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

`fence_oracle.py` runs the fence set-up `0x5323F0`, the Buy Land validator `0x531A50` and purchase `0x5327E0` on a synthetic 128 x 128 map; `fence_golden.py` writes `source/OpenTPW.Tests/ParkFenceGolden.cs` from 18 cases and `validator_golden.py` writes `QueueCellValidatorGolden.cs` from the cell validator `0x535670` (both accept `--check FILE`). Findings X35 and X36 in `docs/reverse/ORACLE.md` section 7.24.
