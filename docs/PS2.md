# PS2 version data

October 9, 2026. Status: **read-only viewing support** (`[EXT:ps2-data]`). OpenTPW still runs on
the PC/Mac data; the PS2 data can be listed and its textures exported for viewing. Nothing from the
disc is in the repository.

Source examined: the PAL disc *Theme Park World (Europe) (En,Fr,De)*, `SYSTEM.CNF`
`BOOT2 = cdrom0:\SLES_500.32`, a single MODE2/2352 CD track. The executable `SLES_500.32` is a
stripped MIPS (Emotion Engine) ELF: no symbols, unlike the Mac PEF libraries.

## Disc layout

- `DATA/*.WAD`: 16 archives in the FKNL format below — one per theme (`JUNGLE`, `HALLOW`, `SPACE`,
  `FANTASY`), their ride scripts (`JRSE`, `HRSE`, `SRSE`, `FRSE`), and `DATA`, `FRONTEND`,
  `LOBBY`, `UI`, `MENUS`, `PARTICLE`, `ICONS`, `LIPS`.
- `AUDIO/<theme>/{PARK1,PARK2,MUSIC,LOBBY}`: `.SDT` banks with `.MAP` catalogues. Each theme has two
  park banks with different ride sounds.
- `MOVIES/*.MPC`: movies (format not examined).

## FKNL archive (`FknlArchive`)

Little-endian. Header: `FKNL`, 0, data start, an unknown word, name-pool start, root record offset.

| Record | Bytes | Fields |
| --- | --- | --- |
| directory | 16 | file-entry table offset, subdirectory-entry table offset, file count, subdirectory count |
| file entry | 16 | name offset, data offset, stored size, size |
| subdirectory entry | 8 | name offset, directory record offset |

Names are NUL-terminated Latin-1 strings in the pool between the name-pool start and the data
start. A member whose stored size is smaller than its size is RefPack-compressed (`10 FB`), as in the
PC WADs; otherwise it is stored. Three `Text/translations/eur/*.dup` entries in `DATA.WAD` record a
size but store no bytes (empty placeholders). All 16 archives (14,678 files) parse and every member
decompresses to its recorded size (`FknlArchiveTests`, private, `OPENTPW_PS2_DATA`).

## Formats inside

| Extension | Meaning | Status |
| --- | --- | --- |
| `.ssh` | EA `SHPS` shape container, id `GIMX`; one image record per texture plus a `0x70` name block | container read (`SshFile`); pixel payload not decoded |
| `.tga` | truecolor TGA of every texture, same size as the SSH | decoded for viewing |
| `.mps` / `.aps` | models and animations (PS2 counterparts of MD2), e.g. `Rides/dizzyd/dizzyd.mps`, `terrain/terrain_1.mps` | not decoded |
| `.rse` / `.rss` / `.sam` | ride scripts (same RSE virtual machine: the executable contains the PC opcode names and `RSSE:` diagnostics), settings | RSE/SAM readers apply in principle; not run yet |
| `.lip`, `.sce`, `.plb`, `.dat`, `.bff` | lip sync, menu scenes, particles, misc | not examined |

SSH image records use type `0x84` (24-bit) and `0x85` (32-bit) with the 0x80 bit set. Their payload
starts with `GM`, the number of 16×16 blocks across and down, and the payload length: a lossy block
format (similar in spirit to the TQI video) that the PS2 decodes at load time. It is not decoded
yet; the accompanying TGA files make every texture viewable without it.

## Compared with the PC version

- Textures are mostly half the PC resolution: of 3,771 textures present in both, 3,019 are smaller on
  PS2 (typically 64×64 instead of 128×128), 684 equal and 68 larger (tiny 8×8 ones). The largest PS2
  images are 512×512 menu backgrounds (the per-theme "laptop" screens, front-end art per language).
- PS2-only side games under `Sideshow/`: `pong`, `sgpuzzle`, `sgrace`, `sgsquark`, `sgwhack`, `sgshy`,
  `sgstrtst`, `sgfortun` (names from the archives; the executable also names `ratrace`).
- Each theme has two terrains (`terrain/terrain_1`, `terrain/terrain_2`) and two park sound banks:
  the PS2 splits each theme into two parks.
- Ride signs are pre-rendered textures (`<ride>/sign/_name1.ssh`, `_name2.ssh`) instead of text drawn
  with TrueType fonts at run time.
- Several rides that are official bonus objects on the PC (e.g. Dizzy Dinos, Manic Mechanic, Devil,
  G-Force, Whirlwind) are part of the PS2 game.

## Viewing

```sh
bash scripts/run.sh --export-ps2 /path/to/ps2/DATA /path/to/output
```

Writes every texture as `<output>/<archive>/<path>.png` (from the TGA) and `index.tsv` listing every
member with its size and, for SSH files, the image records (size, type, `GM`, name). On the PAL disc:
14,678 files, 5,695 textures, in about five seconds on an Apple silicon Mac.

## Next steps

`.mps`/`.aps` models (compare with the PC MD2 of the same object, e.g. `dizzyd`), the PS2 terrains,
the `GM` texture payload, and running PS2 ride scripts on the existing RideVM. Playing the PS2 side
games needs their logic from the stripped executable and is out of scope for now.
