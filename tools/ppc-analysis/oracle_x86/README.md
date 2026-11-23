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

## Theme Park Inc guest profile

`tpi_guest_profile_oracle.py` runs the profile fragment at `0x60C58F` through `0x60C711` and the subsequent meter and prank fragment up to `0x60C830`, including the original world generator at `0x6493E0`. It uses synthetic guest and settings records and does not execute the rest of the constructor. It also checks the appearance reseed and the base-speed conversion at `0x60C712`. That conversion multiplies by the selected profile's binary32 coefficient and truncates; some retail inputs differ at x87 control words `0x7F` and `0x27F`/`0x37F` (for example, speed 60 × `0.95f` gives 57 or 56). The original changes DirectDraw cooperative level with `DDSCL_FPUSETUP` at `0x6BF33F` and `0x6BF5A8`, then calls Direct3D device creation at `0x6D62AD`. This is not a capture of the active control word on a particular device. Check the 25 fixtures with `--check source/OpenTPW.Tests/ThemeParkIncGuestStartTests.cs`.

## Theme Park Inc precipitation

`tpi_precipitation_oracle.py` runs the drop update at `0x642230` up to the lightning branch and the drop target calculation at the start of `0x642A80`. The update executes the renderer count setter and mode-2 reset on synthetic records. It records the raw volume request rather than playing audio. The spell-start replay also records mode and fall speed. Separate fragments verify the wind angle and force passed to the renderer (`0x642C9B onward`) and the normalized fall vector at `0x6EA9A0` under two x87 precision settings. Check the 40 fixtures with `--check source/OpenTPW.Tests/ThemeParkIncPrecipitationTests.cs`.

## Theme Park Inc advisor weather forecasts

`tpi_advisor_forecast_oracle.py` checks the four forecast descriptors in the identified TPI executable (`OPENTPW_TPI_EXE`). It runs the descriptor initializer at `0x729530`, the forecast response slice at `0x70F59E`, 28 score lookups across the mode word, the game-object flags and the animation word, the 72 class admissions of the eligibility matrix at `0x71BDC0`, caption formatter handoffs, seven admission cases for rain at `0x718820`, and five playback ticks at `0x718190`. It also reads the bank's three red-month advice rows (`0x1A1`–`0x1A3`), their response rows built at `0x7104A0` and the tag-94 caption entry. History and clock values are synthetic; the playback replay captures speech and tag submissions instead of sending them to the actual systems. The oracle does not launch the game, render text or play audio. See [the forecast trace](../../../docs/reverse/TPI-ADVISOR-FORECAST.md) and [the bank mail trace](../../../docs/reverse/TPI-BANK-MAIL.md).

From the repository root, with Unicorn installed in the selected Python environment:

```bash
python3 -I -B -c 'import runpy, sys; sys.path.insert(0, "tools/ppc-analysis/oracle_x86"); runpy.run_module("tpi_advisor_forecast_oracle", run_name="__main__")'
```

## Theme Park Inc lightning and thunder

`tpi_storm_timer_oracle.py` checks the storm timer branch at `0x6422FE` and strike geometry at `0x642550`. The timer replay supplies synthetic strike coordinates and captures strike, thunder and RNG calls; the geometry replay supplies four draws and compares coordinate bits at x87 control words `0x7F` and `0x27F`. Neither replay claims to identify the live device precision. Verify the fixtures with `--check source/OpenTPW.Tests/ThemeParkIncStormTests.cs`.

## Theme Park Inc's shop cost of goods (round 10, package K)

`tpi_goods_cost_oracle.py` runs the routine `0x5E6260` of the Theme Park Inc no-CD `Game.exe` (`OPENTPW_TPI_EXE`) with a synthetic thing and type record on random inputs and compares it with the formula `ThemeParkIncGoodsCost` ports (400 cases at the control words `0x27F` and `0x7F`, no mismatch).

`tpi_staff_formula_oracle.py` runs Theme Park Inc's staff interpolation helper `0x62C900`, its expected wage `0x62CD30`, its work duration `0x62E4C0`, its wage review `0x62CDA0` (with a scripted `rand()`), the step `0x62B3E0`, rest `0x62A1C0` and daily experience gain `0x629650` (a synthetic staff thing, with the settings words it reads written into the image) on random inputs and compares them with the formulas `ThemeParkIncStaffSettings` and `ThemeParkIncStaffMember` port (2,000 cases each, no mismatch); `--golden` prints native cases for the C# tests.

`tpi_staff_candidate_oracle.py` runs Theme Park Inc's staff candidate constructor `0x6302C0` with the real expected wage and name draw, the settings words it reads written into the image and its outside calls stubbed (scripted world and `rand()` words, name-taken answers), and compares the record with the port `ThemeParkIncStaffCandidates.Generate` reads (500 cases over the six roles, random agency rows and keys, no mismatch); `--golden` prints native cases.

## Theme Park Inc's ride wear and the mechanic's repair timer

`tpi_ride_wear_oracle.py` runs the wear amount `0x5E21A0`, the wear tick `0x5E25B0` and the mechanic's setter `0x5DA150` (state 0x16, the repair timer, with the real work duration `0x62E4C0`) of the Theme Park Inc no-CD `Game.exe` (`OPENTPW_TPI_EXE`) on a synthetic ride thing, type record and mechanic, with the script reads, the type lookup and the advice chain stubbed, and compares the repair and life singles, the `VAR_WORN` write and the timer with the reading `ParkEconomy.ThemeParkIncRides` and `ThemeParkIncStaffWorld` port at the control words `0x7F` and `0x27F` (2000 cases by default, no mismatch). `--golden` prints native wear-amount and wear-tick cases (the first ten, then the first clamped repair, clamped life, worn ride and wearing special ride), which `ThemeParkIncMechanicTests` pins as numbers. See [TPI-STAFF.md](../../../docs/reverse/TPI-STAFF.md), "Mechanics and ride wear".

## Theme Park Inc's plants and gardener

`tpi_gardener_oracle.py` runs the object state dispatch `0x5E4CB0` on a plant in state 3 (the plant block `0x5E4CDE`: the weather's wither into `VAR_SCALE`, the condition's gauge `+0x54` and the RegionFX row moves) and the gardener's plant chooser `0x5D2DC0` over a synthetic object list of the Theme Park Inc no-CD `Game.exe` (`OPENTPW_TPI_EXE`). The script reads and writes, the row calls, the advisor's notice clearing, the drop's fallback `0x5E9320` and the route `0x62EAC0` are stubbed and recorded; the weather thing lookup, the claim reader `0x5E37F0` with its validator, the area test, the lerp, the request, on-fire and dry tests and the claim run natively. It compares the writes, the gauge, the rows, the chosen plant, the routes tried, the lapsed claimants' setter calls and the claim words with the reading `ParkEconomy.ThemeParkIncPlants` and `ThemeParkIncStaffWorld.Gardeners` port, at the control words `0x7F` and `0x27F` (2000 cases by default, no mismatch). `--golden` prints the first native cases. See [TPI-STAFF.md](../../../docs/reverse/TPI-STAFF.md), "Gardener and plants".

## Theme Park Inc's entertainer and guard

`tpi_performer_oracle.py` runs the pre-updates `0x5D1960` (entertainer, row 0) and `0x5D3B40` (guard, row 3) over a synthetic cell manager of the Theme Park Inc no-CD `Game.exe` (`OPENTPW_TPI_EXE`): the stored cell word `0x609E20`, the lerp `0x62C900` and the region writer `0x5D7770` with its wrappers run natively, and the grid's halfwords and `+0xE4` are compared with `ParkEnvironment.ApplyRadius` and `ThemeParkIncStaffWorld.MoveEffect`. It also runs the entertainer's search `0x5D1A30` (the gate, the tired test, the draw, the finder `0x5B1B20`, the destination and the setter stubbed and recorded; the draws taken, the finder's radius and the setter's states are compared with `SearchEntertainer`), the entertainer's setter `0x5D1D30` (the draw's remainder by the bank's performance count, the theme record's index and the variant: the template, state and stamp are compared with `PerformanceTemplateOf`) and the guard's decision `0x5D3C10` (compared with `DecideGuard`). 1000 cases, no mismatch. `--golden` prints native cases. See [TPI-STAFF.md](../../../docs/reverse/TPI-STAFF.md), "Entertainer and guard".

`tpi_guest_states_oracle.py` runs three routines of Theme Park Inc's native guest states in the no-CD `Game.exe` (`OPENTPW_TPI_EXE`) at the game thread's control word `0x7F`: the catch test `0x614720` over the state words -1..40; the state setter `0x61A540` for every state phase P1 enters (the world generator, the booth cell `0x5D79B0`, the mode store, the speed term and the stay turns run natively, the analyser's stay ring is stubbed and recorded; the state, mode, wait, heading, stamps, counters, speed term and stay are compared with `GuestSimulation.SetThemeParkIncState`); and the pre-update `0x619B50` over random meters, cell words, turns, ids, umbrella states, balloon countdowns and states (the region record, the clamped adder, `_ftol`, the speed term, the world generator and the history shift run natively; the speed average, the weather rule, the park-cell test, the thought pick, the thought object and the balloon release are stubbed and recorded; compared with `ThemeParkIncPreUpdate`). 40 setter rounds and 1000 pre-update cases, no mismatch; it found the history shift's overlapping copy (`0x611D20`) and the balloon countdown's place under the park-cell test. See [TPI-GUESTS.md](../../../docs/reverse/TPI-GUESTS.md).

## Theme Park Inc's weather spell roll (round 10, package K)

`tpi_weather_roll_oracle.py` runs the spell roll `0x642770` of the Theme Park Inc no-CD `Game.exe` (`OPENTPW_TPI_EXE`) with the calendar reads and the CRT `rand()` stubbed and synthetic table and quality words, and compares the spell record with the model `ThemeParkIncWeatherSpells` ports (3000 random cases, no mismatch; ARRIVAL-008, `[EXT:tpi-weather-spell-seed]`).

## The coaster's run table and bend tables (round 10, package N)

`coaster_x87_oracle.py` runs the run builder `0x4402D0` over a sampled synthetic chain (`sampler(..., extrusion=...)`) and the bend car's tables `0x438AB0` (`bend_tables`); `coaster_runs_golden.py` writes `CoasterRunsGolden.cs` from 60 chains and `coaster_bend_golden.py` writes `CoasterBendGolden.cs` from 120 cars (both accept `--check FILE`). Findings X42 and X43 in `docs/reverse/ORACLE.md` section 7.29.

## The queue tool's corner walk and a ride's queue demolition (round 11, package D)

`queue_oracle.py` runs the corner walk `0x530120` and the ride demolition `0x527EE0` of a ride with a queue (the executable's own Back, click handler, queue line walker and cell remover) on synthetic queue layouts; `queue_golden.py` writes `QueueCornerWalkGolden.cs` from 82 cases and `queue_demolition_golden.py` writes `QueueDemolitionGolden.cs` from 42 (both accept `--check FILE`). Findings X44 and X45 in `docs/reverse/ORACLE.md` section 7.30.

## The bridge walks (round 11, package E)

`bridge_walk_oracle.py` lays synthetic second-layer records beside a placed kart or water ride and runs the op 0x86 pair (forward walk `0x52C6F0`, reverse walk `0x52CCB0`); `bridge_walk_golden.py` writes `TrackBridgeWalkGolden.cs` from 700 random layouts (`--check FILE`, about 25 minutes, layouts whose walk reads past the structure array are left out). Finding X47 in `docs/reverse/ORACLE.md` section 7.32.

## The camera loader's yaw snap (round 11, package C)

`camera_snap_golden.py` runs the tail of the camera loader `0x42CEC0` (`0x42D03F`, with the CRT fmod `0x67B24A`) on 585 yaws and writes `CameraSnapGolden.cs`; `CameraSnapGoldenTests` pins `ParkCameraMode.SnapToQuarterTurn` to it bit for bit (finding X49 in ORACLE.md).

## The head placement routine (round 11, package W)

`head_oracle.py` runs the head placement `0x44B510` over a synthetic head node and camera (the sprite register writers recorded, not run, the PC sine table built as the message pump builds it, control word 0x7F); `head_golden.py` writes `HeadPlacementGolden.cs` from 700 cases, which `HeadPlacementGoldenTests` replays on `OriginalHeadMath` bit for bit (finding X48 in docs/reverse/ORACLE.md).

## The build tools' ghost lines and tiles (round 11, package F)

`ghost_oracle.py` runs tp.exe's own line walker `0x536100` with the ghost cell routine `0x532FC0` (the path and queue tools' preview), the track tile chooser `0x539760` and the tile line walker `0x53AAC0` with the ghost tile routine `0x5370E0` (the kart and water line tools' and the fence tool's preview) on synthetic maps, with its own stubs for the face queue `0x53C8D0`, the height lookups and the money. `ghost_line_golden.py`, `ghost_tile_golden.py` and `ghost_tile_line_golden.py` write `GhostLineGolden.cs`, `GhostTileGolden.cs` and `GhostTileLineGolden.cs` (zlib + base64; 300 random lines, 600 random tile cases and 240 random tile lines; `--check <file>` compares) which `GhostLineGoldenTests`, `GhostTileGoldenTests` and `GhostTileLineGoldenTests` replay on `BlueprintLineGhost`, `TrackTileChooser` and `TileLineGhost`.

## The camcorder walk's frame step (round 11, package Q)

`camcorder_walk_oracle.py` runs the whole camera update `0x42B1C0` in the walk mode (camera flags 2) over a synthetic terrain object with its real helpers and the clock, key state, walk test, ride entrance lookup and ride-it routine answering from the case; `camcorder_walk_golden.py` writes `CamcorderWalkGolden.cs` from 1500 random frames (`--check FILE`, about 40 seconds); `CamcorderWalkGoldenTests` pins `CamcorderWalk.Step` to it bit for bit (finding X58 in ORACLE.md).

## Theme Park Inc's guest price test (round 11, package T)

`tpi_price_oracle.py` runs the routine `0x6120C0` of the Theme Park Inc no-CD `Game.exe` (`OPENTPW_TPI_EXE`) with a synthetic guest, attraction thing, type record and rating byte on random inputs and compares verdict, statistics and the three ring counters with the reading `ThemeParkIncPriceTest` ports (490 cases, no mismatch; finding X62). `--golden` writes `source/OpenTPW.Tests/ThemeParkIncPriceGolden.cs`.

## The UI draw sorts (round 11, package K)

`ui_sort_oracle.py` runs the record sort `0x5829E0` and the triangle sort `0x565490` on synthetic keys; `ui_sort_golden.py` writes `UiDepthSortGolden.cs` (64 tie orders and 60 key sets; `--check FILE`). Finding X70 in `docs/reverse/ORACLE.md` section 7.39.

`ui_batch_oracle.py` runs the UI part batcher `0x581DB0` with stubbed primitive builder and prints the order it emits material runs in for chosen texture page keys (finding X70).

`ui_scene_oracle.py` runs the scene list insertion `0x576270` (the order instances reach the part list: newest shown first) and the batcher on the resulting list (finding X79, `UiSceneOrder` in `test_x86_oracle.py`).

`sky_oracle.py` runs the sky draw `0x5863C0` (constructor `0x584EF0`) with a stubbed device and prints the records it makes for the normal render flags and the globe's `0x2000000` (finding X78, docs/reverse/ORACLE.md 7.40). `sky_golden.py` turns its records into the golden numbers `source/OpenTPW.Tests/SkyGolden.cs`.
