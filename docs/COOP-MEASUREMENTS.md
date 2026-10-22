# Co-op join measurements

`[EXT:online-coop-snapshot-join]` The tables behind [COOP.md](COOP.md), "Measurements": every value of every run, as
`tools/coop_join_timings.py` prints it from the runs' evidence (the median, the range and each run's value; milliseconds
unless the row says otherwise). `scripts/measure-coop-join.sh` measures the desktop (the join smoke's host and measuring
join phases, five times, each a fresh process, silent and offscreen) and `scripts/measure-coop-join-browser.sh` the
page (the desktop's host phase once, then the page's measuring join phase in fresh headless, muted Chrome profiles).
Each measurement names its source and whether it was clean. The swaps' marks are FrameMarks sections, which nest (a
parent's time includes its children's); a construction section that spans several of the build's slices adds its
whole time to the frame it closes in (`load:entities` in a resync's swap frame, whose build's last slice runs there).
They cover each frame's update, not its drawing, and are recorded from C2c-2b-3a on, on while a park is built and for
the frame after its swap: their small cost is in those frames' timings, which earlier measurements do not carry.

## The desktop's join (C2c-2b-1)

5 runs; Release build 1.0.0+dfbf4bbc3eb6c9143ef091cfcb77e4f511862866, .NET 11.0.0-rc.2.26504.105, macOS 26.6.0 Arm64, 10 processors, silent True, advisor True

    Darwin 25.6.0 arm64
    processor Apple M5, model Mac17,2, memory 34359738368 bytes
    source dfbf4bbc3eb6c9143ef091cfcb77e4f511862866, clean

| Value | Median | Range | Runs |
|---|---|---|---|
| capture 1: cut | 10.5 | 9.4-11.7 | 11.7, 9.8, 9.4, 11.7, 10.5 |
| capture 1: publish | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| capture 1: digest | 3.9 | 3.7-4.6 | 3.9, 3.7, 3.9, 3.9, 4.6 |
| capture 1: state | 0.9 | 0.8-1.0 | 0.9, 0.8, 1.0, 0.9, 0.9 |
| capture 1: snapshot | 4.8 | 4.4-6.9 | 6.9, 5.1, 4.4, 4.5, 4.8 |
| capture 1: serializer setup | 0.0 | 0.0-2.3 | 0.0, 0.0, 0.0, 2.3, 0.0 |
| capture 1: save | 69.2 | 67.6-73.5 | 71.0, 67.6, 68.6, 69.2, 73.5 |
| capture 1: save, longest slice | 34.7 | 33.7-37.5 | 37.5, 33.7, 34.7, 34.7, 36.1 |
| capture 1: save, slices | 12 | 12-12 | 12, 12, 12, 12, 12 |
| capture 1: save copy | 0.2 | 0.1-0.3 | 0.1, 0.3, 0.2, 0.2, 0.2 |
| capture 1: writer setup | 1.3 | 1.2-1.4 | 1.2, 1.3, 1.3, 1.3, 1.4 |
| capture 1: compression | 1.5 | 1.4-1.6 | 1.6, 1.4, 1.4, 1.5, 1.5 |
| capture 1: compression, slices | 1 | 1-1 | 1, 1, 1, 1, 1 |
| capture 1: finish | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| capture 1: longest frame | 35.0 | 34.0-37.8 | 37.8, 34.0, 35.0, 34.9, 36.4 |
| capture 1: frames | 14 | 14-14 | 14, 14, 14, 14, 14 |
| capture 1: upload s | 0.016 | 0.015-0.016 | 0.016, 0.016, 0.016, 0.015, 0.016 |
| capture 1: payload KiB | 128 | 128-128 | 128, 128, 128, 128, 128 |
| capture 1: body KiB | 981 | 981-981 | 981, 981, 981, 981, 981 |
| capture 1: save KiB | 980 | 980-980 | 980, 980, 980, 980, 980 |
| capture 2: cut | 4.8 | 4.1-5.4 | 4.9, 5.4, 4.6, 4.1, 4.8 |
| capture 2: publish | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| capture 2: digest | 2.2 | 2.0-2.9 | 2.2, 2.9, 2.4, 2.0, 2.2 |
| capture 2: state | 0.1 | 0.1-0.2 | 0.1, 0.2, 0.2, 0.1, 0.1 |
| capture 2: snapshot | 2.3 | 2.0-2.6 | 2.6, 2.3, 2.0, 2.0, 2.4 |
| capture 2: serializer setup | 0.0 | 0.0-0.1 | 0.1, 0.0, 0.0, 0.0, 0.0 |
| capture 2: save | 8.6 | 8.2-14.3 | 14.3, 8.2, 9.8, 8.3, 8.6 |
| capture 2: save, longest slice | 4.1 | 3.5-9.2 | 9.2, 4.0, 4.1, 3.5, 4.1 |
| capture 2: save, slices | 4 | 3-4 | 4, 4, 4, 3, 4 |
| capture 2: save copy | 0.1 | 0.1-0.2 | 0.1, 0.1, 0.2, 0.1, 0.1 |
| capture 2: writer setup | 0.2 | 0.1-0.3 | 0.3, 0.2, 0.2, 0.1, 0.1 |
| capture 2: compression | 1.4 | 1.2-1.5 | 1.4, 1.3, 1.5, 1.2, 1.4 |
| capture 2: compression, slices | 1 | 1-1 | 1, 1, 1, 1, 1 |
| capture 2: finish | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| capture 2: longest frame | 4.8 | 4.1-9.2 | 9.2, 5.4, 4.6, 4.1, 4.8 |
| capture 2: frames | 6 | 5-6 | 6, 6, 6, 5, 6 |
| capture 2: upload s | 0.015 | 0.015-0.016 | 0.015, 0.016, 0.015, 0.015, 0.015 |
| capture 2: payload KiB | 149 | 149-149 | 149, 149, 149, 149, 149 |
| capture 2: body KiB | 1107 | 1107-1107 | 1107, 1107, 1107, 1107, 1107 |
| capture 2: save KiB | 1107 | 1107-1107 | 1107, 1107, 1107, 1107, 1107 |
| joined: process's build | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download s | 0.016 | 0.015-0.017 | 0.016, 0.016, 0.015, 0.017, 0.015 |
| joined: last attempt s | 0.016 | 0.015-0.016 | 0.016, 0.016, 0.015, 0.016, 0.015 |
| joined: payload KiB | 128 | 128-128 | 128, 128, 128, 128, 128 |
| joined: decode | 4.2 | 4.1-4.5 | 4.2, 4.1, 4.2, 4.3, 4.5 |
| joined: check | 45.2 | 40.3-46.0 | 40.6, 45.4, 45.2, 40.3, 46.0 |
| joined: swap | 13.3 | 8.2-14.6 | 13.3, 14.3, 9.0, 8.2, 14.6 |
| joined: build frames | 21 | 19-23 | 21, 21, 21, 19, 23 |
| joined: longest build frame | 439 | 395-483 | 419, 449, 439, 395, 483 |
| joined: catch-up ticks | 1 | 1-8 | 1, 1, 1, 1, 8 |
| joined: catch-up frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: catch-up s | 0.303 | 0.260-0.418 | 0.277, 0.374, 0.303, 0.260, 0.418 |
| joined: save | 167 | 153-198 | 162, 167, 177, 153, 198 |
| joined: save, longest step | 167 | 153-198 | 162, 167, 177, 153, 198 |
| joined: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| joined: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| joined: files, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: files, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: catalogue | 86.3 | 71.9-114 | 86.3, 83.5, 96.5, 71.9, 114 |
| joined: catalogue, longest step | 18.1 | 16.2-23.1 | 17.5, 18.1, 19.2, 16.2, 23.1 |
| joined: catalogue, steps | 90 | 90-90 | 90, 90, 90, 90, 90 |
| joined: catalogue, frames | 6 | 5-7 | 6, 5, 6, 5, 7 |
| joined: manifest | 55.5 | 43.2-62.7 | 53.7, 58.8, 55.5, 43.2, 62.7 |
| joined: manifest, longest step | 3.3 | 2.6-3.8 | 3.2, 3.3, 3.7, 2.6, 3.8 |
| joined: manifest, steps | 92 | 92-92 | 92, 92, 92, 92, 92 |
| joined: manifest, frames | 5 | 4-6 | 5, 5, 5, 4, 6 |
| joined: validation | 36.3 | 29.5-39.6 | 37.0, 34.7, 39.6, 29.5, 36.3 |
| joined: validation, longest step | 20.7 | 17.1-26.0 | 23.1, 19.7, 26.0, 17.1, 20.7 |
| joined: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| joined: validation, frames | 2 | 2-3 | 2, 3, 2, 2, 2 |
| joined: economy | 16.6 | 15.2-18.3 | 16.3, 16.6, 16.6, 15.2, 18.3 |
| joined: economy, longest step | 16.6 | 15.2-18.3 | 16.3, 16.6, 16.6, 15.2, 18.3 |
| joined: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: level | 891 | 762-937 | 824, 891, 913, 762, 937 |
| joined: level, longest step | 436 | 392-479 | 416, 446, 436, 392, 479 |
| joined: level, steps | 83 | 83-83 | 83, 83, 83, 83, 83 |
| joined: level, frames | 7 | 7-7 | 7, 7, 7, 7, 7 |
| joined: hud | 11.6 | 10.5-11.9 | 10.7, 11.8, 11.6, 10.5, 11.9 |
| joined: hud, longest step | 11.6 | 10.5-11.9 | 10.7, 11.8, 11.6, 10.5, 11.9 |
| joined: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor | 30.0 | 25.6-30.5 | 25.6, 30.4, 30.0, 26.1, 30.5 |
| joined: advisor, longest step | 30.0 | 25.6-30.5 | 25.6, 30.4, 30.0, 26.1, 30.5 |
| joined: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: long step, save 1 | 167 | 153-198 | 162, 167, 177, 153, 198 |
| joined: long step, catalogue 1 (1 of 5 runs) | 14.3 | 14.3-14.3 | -, -, -, -, 14.3 |
| joined: long step, catalogue 90 | 18.1 | 16.2-23.1 | 17.5, 18.1, 19.2, 16.2, 23.1 |
| joined: long step, validation 13 | 20.7 | 17.1-26.0 | 23.1, 19.7, 26.0, 17.1, 20.7 |
| joined: long step, economy 1 | 16.6 | 15.2-18.3 | 16.3, 16.6, 16.6, 15.2, 18.3 |
| joined: long step, level 76 (to 0.3) | 17.1 | 16.3-19.7 | 16.6, 17.1, 18.2, 16.3, 19.7 |
| joined: long step, level 77 (to 0.3) | 34.0 | 31.0-36.2 | 32.4, 34.0, 35.8, 31.0, 36.2 |
| joined: long step, level 79 (to 0.4) | 64.2 | 56.1-69.6 | 61.1, 65.4, 64.2, 56.1, 69.6 |
| joined: long step, level 81 (to 0.8) | 436 | 392-479 | 416, 446, 436, 392, 479 |
| joined: long step, level 82 (to 0.9) | 135 | 102-146 | 127, 135, 146, 102, 140 |
| joined: long step, level 83 (to 1) | 172 | 147-193 | 153, 175, 193, 147, 172 |
| joined: long step, advisor 1 | 30.0 | 25.6-30.5 | 25.6, 30.4, 30.0, 26.1, 30.5 |
| joined: section coasters | 5.8 | 5.2-6.4 | 5.5, 5.8, 6.0, 5.2, 6.4 |
| joined: section economy | 0.3 | 0.3-0.4 | 0.3, 0.3, 0.3, 0.3, 0.4 |
| joined: section entities | 804 | 688-850 | 746, 804, 820, 688, 850 |
| joined: section guest-sprites | 13.0 | 9.7-15.3 | 13.0, 15.3, 15.1, 9.7, 12.3 |
| joined: section guests | 135 | 102-146 | 127, 135, 146, 102, 139 |
| joined: section join-state | 2.7 | 2.3-2.8 | 2.3, 2.7, 2.8, 2.3, 2.7 |
| joined: section level-hud | 4.9 | 3.0-8.9 | 4.9, 7.9, 8.9, 3.0, 4.0 |
| joined: section model-parse | 41.5 | 37.9-46.4 | 40.4, 42.1, 41.5, 37.9, 46.4 |
| joined: section object-catalog | 4.3 | 3.5-5.4 | 3.5, 4.4, 4.0, 4.3, 5.4 |
| joined: section object-hoarding | 10.3 | 9.4-13.3 | 9.8, 10.3, 10.6, 9.4, 13.3 |
| joined: section object-models | 300 | 269-330 | 286, 309, 300, 269, 330 |
| joined: section object-runtime | 97.0 | 88.1-105 | 93.1, 98.1, 97.0, 88.1, 105 |
| joined: section object-sign | 208 | 193-232 | 200, 219, 208, 193, 232 |
| joined: section objects | 433 | 389-475 | 413, 443, 433, 389, 475 |
| joined: section original-park | 12.3 | 11.4-14.6 | 12.3, 12.2, 13.2, 11.4, 14.6 |
| joined: section paths | 4.4 | 4.0-5.2 | 4.0, 4.4, 4.8, 4.0, 5.2 |
| joined: section pipeline | 3.4 | 3.2-4.3 | 3.3, 3.4, 4.3, 3.2, 4.2 |
| joined: section resume | 62.2 | 55.9-66.2 | 56.4, 62.2, 66.2, 55.9, 63.6 |
| joined: section shader-native | 0.5 | 0.4-0.7 | 0.4, 0.5, 0.5, 0.4, 0.7 |
| joined: section shader-spirv | 2.3 | 1.0-6.4 | 2.3, 6.1, 6.4, 1.0, 2.0 |
| joined: section staff-gate-economy | 52.9 | 43.7-61.2 | 45.7, 55.5, 61.2, 43.7, 52.9 |
| joined: section staff-sprites | 15.3 | 13.3-15.8 | 13.4, 15.6, 15.8, 13.3, 15.3 |
| joined: section terrain | 50.3 | 46.5-54.9 | 48.2, 50.3, 53.2, 46.5, 54.9 |
| joined: section texture-create | 26.5 | 23.9-29.5 | 24.1, 26.5, 29.5, 23.9, 29.2 |
| joined: section texture-decode | 60.1 | 55.7-66.9 | 55.7, 60.1, 62.4, 56.6, 66.9 |
| joined: section wad-decompress | 19.6 | 17.8-21.9 | 19.5, 20.5, 19.6, 17.8, 21.9 |
| joined: section wad-index | 7.0 | 5.7-25.1 | 7.5, 5.9, 7.0, 5.7, 25.1 |
| joined: section wad-read | 15.0 | 13.7-18.1 | 14.8, 15.0, 18.1, 13.7, 17.6 |
| joined: longest update frame while building | 440 | 396-483 | 419, 450, 440, 396, 483 |
| joined: the swap's update frame | 325 | 279-442 | 301, 398, 325, 279, 442 |
| joined: first update frame after the swap | 20.3 | 18.1-35.4 | 19.3, 20.3, 20.6, 18.1, 35.4 |
| resynced: process's build | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: download s | 0.016 | 0.016-0.017 | 0.016, 0.016, 0.017, 0.017, 0.016 |
| resynced: last attempt s | 0.016 | 0.016-0.017 | 0.016, 0.016, 0.017, 0.017, 0.016 |
| resynced: payload KiB | 149 | 149-149 | 149, 149, 149, 149, 149 |
| resynced: decode | 1.5 | 1.2-2.2 | 1.5, 1.4, 1.2, 2.2, 1.5 |
| resynced: check | 2.3 | 2.1-6.9 | 2.3, 4.6, 2.3, 2.1, 6.9 |
| resynced: swap | 8.4 | 8.1-9.9 | 8.3, 9.0, 8.1, 8.4, 9.9 |
| resynced: build frames | 9 | 9-10 | 9, 9, 9, 10, 10 |
| resynced: longest build frame | 87.6 | 76.0-103 | 80.0, 89.7, 76.0, 87.6, 103 |
| resynced: catch-up ticks | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up s | 0.057 | 0.044-0.073 | 0.057, 0.057, 0.054, 0.044, 0.073 |
| resynced: save | 16.9 | 15.4-22.8 | 16.9, 16.2, 15.4, 22.8, 19.5 |
| resynced: save, longest step | 16.9 | 15.4-22.8 | 16.9, 16.2, 15.4, 22.8, 19.5 |
| resynced: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catalogue | 14.9 | 8.1-23.7 | 11.6, 16.0, 8.1, 14.9, 23.7 |
| resynced: catalogue, longest step | 14.9 | 8.1-23.7 | 11.6, 16.0, 8.1, 14.9, 23.7 |
| resynced: catalogue, steps | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: catalogue, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached) | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| resynced: manifest (cached), longest step | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| resynced: manifest (cached), steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached), frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: validation | 9.9 | 9.4-10.8 | 9.5, 9.9, 9.4, 10.8, 10.8 |
| resynced: validation, longest step | 5.6 | 5.1-5.9 | 5.4, 5.9, 5.1, 5.9, 5.6 |
| resynced: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| resynced: validation, frames | 1 | 1-2 | 2, 1, 2, 1, 1 |
| resynced: economy | 1.1 | 1.0-1.2 | 1.1, 1.1, 1.0, 1.2, 1.1 |
| resynced: economy, longest step | 1.1 | 1.0-1.2 | 1.1, 1.1, 1.0, 1.2, 1.1 |
| resynced: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: level | 218 | 212-278 | 217, 231, 212, 218, 278 |
| resynced: level, longest step | 87.5 | 75.8-102 | 79.9, 89.5, 75.8, 87.5, 102 |
| resynced: level, steps | 83 | 83-83 | 83, 83, 83, 83, 83 |
| resynced: level, frames | 6 | 6-6 | 6, 6, 6, 6, 6 |
| resynced: hud | 0.3 | 0.2-2.6 | 0.2, 2.6, 0.3, 0.2, 0.3 |
| resynced: hud, longest step | 0.3 | 0.2-2.6 | 0.2, 2.6, 0.3, 0.2, 0.3 |
| resynced: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor | 1.9 | 1.9-3.0 | 1.9, 3.0, 1.9, 1.9, 2.2 |
| resynced: advisor, longest step | 1.9 | 1.9-3.0 | 1.9, 3.0, 1.9, 1.9, 2.2 |
| resynced: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: long step, save 1 | 16.9 | 15.4-22.8 | 16.9, 16.2, 15.4, 22.8, 19.5 |
| resynced: long step, catalogue 2 (3 of 5 runs) | 16.0 | 14.9-23.7 | -, 16.0, -, 14.9, 23.7 |
| resynced: long step, level 3 (to 0.008) | 15.1 | 13.7-16.0 | 16.0, 15.2, 13.7, 15.1, 15.0 |
| resynced: long step, level 76 (to 0.3) (4 of 5 runs) | 12.9 | 12.6-13.8 | 12.8, 13.0, -, 12.6, 13.8 |
| resynced: long step, level 79 (to 0.4) | 38.2 | 35.0-39.3 | 38.7, 38.2, 38.0, 35.0, 39.3 |
| resynced: long step, level 81 (to 0.8) | 19.8 | 18.6-41.5 | 19.8, 19.4, 18.6, 23.3, 41.5 |
| resynced: long step, level 82 (to 0.9) | 87.5 | 75.8-102 | 79.9, 89.5, 75.8, 87.5, 102 |
| resynced: long step, level 83 (to 1) | 51.0 | 38.6-61.6 | 45.5, 52.0, 51.0, 38.6, 61.6 |
| resynced: section coasters | 0.1 | 0.1-0.2 | 0.1, 0.1, 0.1, 0.2, 0.2 |
| resynced: section economy | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: section entities | 196 | 189-252 | 195, 209, 189, 196, 252 |
| resynced: section guest-sprites | 3.9 | 3.8-5.6 | 3.8, 4.0, 3.9, 5.6, 3.8 |
| resynced: section guests | 87.5 | 75.8-102 | 79.9, 89.5, 75.8, 87.5, 102 |
| resynced: section join-state | 0.1 | 0.0-0.2 | 0.0, 0.1, 0.1, 0.0, 0.2 |
| resynced: section level-hud | 1.8 | 1.6-3.5 | 1.6, 2.3, 1.6, 1.8, 3.5 |
| resynced: section model-parse | 15.1 | 13.8-16.1 | 16.1, 15.2, 13.8, 15.1, 15.1 |
| resynced: section object-catalog | 1.1 | 1.0-1.2 | 1.2, 1.1, 1.0, 1.2, 1.1 |
| resynced: section object-hoarding | 1.0 | 1.0-1.1 | 1.1, 1.0, 1.0, 1.0, 1.1 |
| resynced: section object-models | 10.2 | 9.1-12.3 | 10.4, 9.5, 9.1, 10.2, 12.3 |
| resynced: section object-runtime | 4.1 | 3.5-7.2 | 3.5, 4.1, 3.9, 7.2, 4.6 |
| resynced: section object-sign | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: section objects | 19.7 | 18.6-41.4 | 19.7, 19.4, 18.6, 23.2, 41.4 |
| resynced: section original-park | 16.4 | 14.9-17.3 | 17.3, 16.4, 14.9, 16.6, 16.2 |
| resynced: section paths | 0.5 | 0.4-0.5 | 0.5, 0.5, 0.5, 0.4, 0.5 |
| resynced: section pipeline | 2.2 | 2.1-3.3 | 2.1, 2.2, 2.1, 2.3, 3.3 |
| resynced: section resume | 3.9 | 3.4-6.9 | 3.4, 3.8, 6.7, 3.9, 6.9 |
| resynced: section staff-gate-economy | 35.1 | 28.0-43.8 | 34.3, 35.1, 36.3, 28.0, 43.8 |
| resynced: section staff-sprites | 11.1 | 8.2-11.4 | 11.1, 11.3, 9.1, 8.2, 11.4 |
| resynced: section terrain | 15.6 | 13.4-17.0 | 15.3, 15.6, 13.4, 16.8, 17.0 |
| resynced: section texture-create | 7.5 | 6.8-11.2 | 7.5, 7.7, 7.4, 6.8, 11.2 |
| resynced: longest update frame while building | 87.8 | 76.1-103 | 80.2, 92.1, 76.1, 87.8, 103 |
| resynced: the swap's update frame | 63.8 | 52.9-85.1 | 63.8, 69.5, 62.7, 52.9, 85.1 |
| resynced: first update frame after the swap | 49.1 | 44.7-51.7 | 51.7, 49.7, 48.6, 44.7, 49.1 |

## The join in the browser (C2c-2b-2c)

### Compiled ahead of time, five runs

5 runs; host phase (one host phase, reused by every run): Release build 1.0.0+d710ffe8836a08b7aa69cbc3dd744a4c27c846cd, .NET 11.0.0-rc.2.26504.105, macOS 26.6.0 Arm64, 10 processors, silent True, advisor True, compiled jit; join phase: Release build 1.0.0+d710ffe8836a08b7aa69cbc3dd744a4c27c846cd, .NET 11.0.0-rc.2.26504.105, Browser Wasm, 1 processors, silent False, advisor True, compiled aot

page: Chrome/155.0.8059.85, ANGLE (Apple, ANGLE Metal Renderer: Apple M5, Unspecified Version), 1280x900, muted True, frame cap 0, pool sprites settled

    Darwin 25.6.0 arm64
    processor Apple M5, 10 cores, model Mac17,2, memory 34359738368 bytes
    source d710ffe8836a08b7aa69cbc3dd744a4c27c846cd, clean (the driver's checkout)

| Value | Median | Range | Runs |
|---|---|---|---|
| capture 1: cut | 9.4 | 9.4-9.4 | 9.4 |
| capture 1: publish | 0.1 | 0.1-0.1 | 0.1 |
| capture 1: digest | 4.0 | 4.0-4.0 | 4.0 |
| capture 1: state | 0.9 | 0.9-0.9 | 0.9 |
| capture 1: snapshot | 4.4 | 4.4-4.4 | 4.4 |
| capture 1: serializer setup | 0.0 | 0.0-0.0 | 0.0 |
| capture 1: save | 67.3 | 67.3-67.3 | 67.3 |
| capture 1: save, longest slice | 33.6 | 33.6-33.6 | 33.6 |
| capture 1: save, slices | 12 | 12-12 | 12 |
| capture 1: save copy | 0.2 | 0.2-0.2 | 0.2 |
| capture 1: writer setup | 1.5 | 1.5-1.5 | 1.5 |
| capture 1: compression | 1.6 | 1.6-1.6 | 1.6 |
| capture 1: compression, slices | 1 | 1-1 | 1 |
| capture 1: finish | 0.1 | 0.1-0.1 | 0.1 |
| capture 1: longest frame | 33.9 | 33.9-33.9 | 33.9 |
| capture 1: frames | 14 | 14-14 | 14 |
| capture 1: upload s | 0.014 | 0.014-0.014 | 0.014 |
| capture 1: payload KiB | 128 | 128-128 | 128 |
| capture 1: body KiB | 981 | 981-981 | 981 |
| capture 1: save KiB | 980 | 980-980 | 980 |
| capture 2: cut | 8.4 | 8.4-8.4 | 8.4 |
| capture 2: publish | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: digest | 4.0 | 4.0-4.0 | 4.0 |
| capture 2: state | 0.3 | 0.3-0.3 | 0.3 |
| capture 2: snapshot | 4.1 | 4.1-4.1 | 4.1 |
| capture 2: serializer setup | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: save | 10.6 | 10.6-10.6 | 10.6 |
| capture 2: save, longest slice | 5.5 | 5.5-5.5 | 5.5 |
| capture 2: save, slices | 4 | 4-4 | 4 |
| capture 2: save copy | 0.2 | 0.2-0.2 | 0.2 |
| capture 2: writer setup | 0.2 | 0.2-0.2 | 0.2 |
| capture 2: compression | 1.7 | 1.7-1.7 | 1.7 |
| capture 2: compression, slices | 1 | 1-1 | 1 |
| capture 2: finish | 0.1 | 0.1-0.1 | 0.1 |
| capture 2: longest frame | 8.4 | 8.4-8.4 | 8.4 |
| capture 2: frames | 6 | 6-6 | 6 |
| capture 2: upload s | 0.015 | 0.015-0.015 | 0.015 |
| capture 2: payload KiB | 149 | 149-149 | 149 |
| capture 2: body KiB | 1107 | 1107-1107 | 1107 |
| capture 2: save KiB | 1107 | 1107-1107 | 1107 |
| joined: process's build | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download s | 0.013 | 0.012-0.014 | 0.014, 0.013, 0.012, 0.013, 0.013 |
| joined: last attempt s | 0.013 | 0.012-0.014 | 0.014, 0.013, 0.012, 0.013, 0.013 |
| joined: payload KiB | 128 | 128-128 | 128, 128, 128, 128, 128 |
| joined: decode | 14.9 | 10.6-17.7 | 13.0, 14.9, 15.5, 17.7, 10.6 |
| joined: check | 76.0 | 72.5-84.7 | 77.4, 76.0, 84.7, 74.3, 72.5 |
| joined: swap | 9.4 | 9.2-9.8 | 9.8, 9.4, 9.7, 9.3, 9.2 |
| joined: build frames | 393 | 277-400 | 394, 389, 277, 393, 400 |
| joined: longest build frame | 375 | 366-403 | 375, 372, 366, 403, 389 |
| joined: catch-up ticks | 638 | 423-691 | 662, 638, 423, 638, 691 |
| joined: catch-up frames | 10 | 8-11 | 11, 10, 8, 10, 11 |
| joined: catch-up s | 0.587 | 0.541-0.608 | 0.608, 0.587, 0.541, 0.563, 0.587 |
| joined: save | 374 | 366-402 | 374, 371, 366, 402, 389 |
| joined: save, longest step | 374 | 366-402 | 374, 371, 366, 402, 389 |
| joined: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: files | 1958 | 1353-1980 | 1958, 1937, 1353, 1980, 1972 |
| joined: files, longest step | 1.3 | 0.6-1.4 | 0.6, 0.6, 1.3, 1.4, 1.4 |
| joined: files, steps | 10777500 | 7526527-11066327 | 10691265, 10777500, 7526527, 11066327, 11024497 |
| joined: files, frames | 375 | 258-382 | 375, 371, 258, 375, 382 |
| joined: catalogue | 79.5 | 76.4-82.3 | 79.5, 80.5, 82.3, 76.4, 79.0 |
| joined: catalogue, longest step | 17.2 | 16.2-17.6 | 16.7, 17.6, 17.5, 16.2, 17.2 |
| joined: catalogue, steps | 90 | 90-90 | 90, 90, 90, 90, 90 |
| joined: catalogue, frames | 5 | 5-6 | 6, 5, 5, 5, 5 |
| joined: manifest | 53.9 | 53.4-55.9 | 53.9, 54.2, 55.9, 53.6, 53.4 |
| joined: manifest, longest step | 6.8 | 6.7-7.0 | 6.8, 6.7, 6.9, 7.0, 6.7 |
| joined: manifest, steps | 92 | 92-92 | 92, 92, 92, 92, 92 |
| joined: manifest, frames | 5 | 5-5 | 5, 5, 5, 5, 5 |
| joined: validation | 43.6 | 43.2-45.4 | 43.2, 43.8, 45.4, 43.2, 43.6 |
| joined: validation, longest step | 25.7 | 25.1-26.8 | 25.2, 25.9, 26.8, 25.1, 25.7 |
| joined: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| joined: validation, frames | 2 | 2-3 | 2, 2, 3, 2, 2 |
| joined: economy | 17.4 | 16.2-20.3 | 17.1, 17.4, 20.3, 16.2, 18.9 |
| joined: economy, longest step | 17.4 | 16.2-20.3 | 17.1, 17.4, 20.3, 16.2, 18.9 |
| joined: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: level | 649 | 641-706 | 649, 653, 706, 644, 641 |
| joined: level, longest step | 320 | 318-354 | 320, 321, 354, 318, 318 |
| joined: level, steps | 83 | 83-83 | 83, 83, 83, 83, 83 |
| joined: level, frames | 6 | 6-6 | 6, 6, 6, 6, 6 |
| joined: hud | 8.1 | 7.6-8.8 | 8.8, 7.7, 8.1, 7.6, 8.2 |
| joined: hud, longest step | 8.1 | 7.6-8.8 | 8.8, 7.7, 8.1, 7.6, 8.2 |
| joined: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor | 34.2 | 33.3-37.2 | 34.2, 34.9, 37.2, 34.0, 33.3 |
| joined: advisor, longest step | 34.2 | 33.3-37.2 | 34.2, 34.9, 37.2, 34.0, 33.3 |
| joined: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: long step, save 1 | 374 | 366-402 | 374, 371, 366, 402, 389 |
| joined: long step, catalogue 1 | 14.4 | 13.9-16.2 | 14.0, 15.2, 16.2, 13.9, 14.4 |
| joined: long step, catalogue 90 | 17.2 | 16.2-17.6 | 16.7, 17.6, 17.5, 16.2, 17.2 |
| joined: long step, validation 13 | 25.7 | 25.1-26.8 | 25.2, 25.9, 26.8, 25.1, 25.7 |
| joined: long step, economy 1 | 17.4 | 16.2-20.3 | 17.1, 17.4, 20.3, 16.2, 18.9 |
| joined: long step, level 76 (to 0.3) | 22.3 | 21.9-27.2 | 21.9, 22.4, 27.2, 22.2, 22.3 |
| joined: long step, level 77 (to 0.3) | 30.3 | 30.1-32.3 | 30.3, 30.6, 32.3, 30.1, 30.2 |
| joined: long step, level 79 (to 0.4) | 38.0 | 36.4-38.5 | 38.3, 38.5, 36.4, 37.9, 38.0 |
| joined: long step, level 81 (to 0.8) | 320 | 318-354 | 320, 321, 354, 318, 318 |
| joined: long step, level 82 (to 0.9) | 84.5 | 80.4-94.0 | 84.8, 84.5, 94.0, 82.5, 80.4 |
| joined: long step, level 83 (to 1) | 141 | 139-151 | 141, 143, 151, 140, 139 |
| joined: long step, advisor 1 | 34.2 | 33.3-37.2 | 34.2, 34.9, 37.2, 34.0, 33.3 |
| joined: section coasters | 4.6 | 4.3-4.7 | 4.4, 4.7, 4.6, 4.3, 4.6 |
| joined: section economy | 0.1 | 0.0-0.2 | 0.1, 0.1, 0.2, 0.1, 0.0 |
| joined: section entities | 575 | 566-631 | 575, 577, 631, 569, 566 |
| joined: section guest-sprites | 13.2 | 9.0-13.6 | 13.2, 13.6, 9.0, 13.3, 12.3 |
| joined: section guests | 84.3 | 80.4-94.0 | 84.8, 84.3, 94.0, 82.5, 80.4 |
| joined: section join-state | 3.9 | 3.7-3.9 | 3.9, 3.9, 3.9, 3.8, 3.7 |
| joined: section level-hud | 7.6 | 5.8-7.7 | 5.9, 7.6, 5.8, 7.7, 7.7 |
| joined: section model-parse | 6.6 | 6.4-8.8 | 6.8, 6.4, 8.8, 6.6, 6.5 |
| joined: section object-catalog | 2.2 | 2.1-2.4 | 2.1, 2.2, 2.2, 2.3, 2.4 |
| joined: section object-hoarding | 15.5 | 15.1-16.4 | 15.6, 15.5, 16.4, 15.1, 15.5 |
| joined: section object-models | 213 | 212-234 | 214, 213, 234, 212, 213 |
| joined: section object-runtime | 66.2 | 65.7-77.0 | 66.0, 67.7, 77.0, 66.2, 65.7 |
| joined: section object-sign | 142 | 140-148 | 142, 142, 148, 141, 140 |
| joined: section objects | 318 | 316-351 | 318, 318, 351, 316, 316 |
| joined: section original-park | 8.8 | 6.7-9.1 | 8.7, 8.9, 6.7, 8.8, 9.1 |
| joined: section paths | 3.9 | 3.8-4.0 | 4.0, 3.8, 4.0, 3.9, 3.9 |
| joined: section pipeline | 8.1 | 6.8-8.9 | 8.4, 8.1, 7.9, 8.9, 6.8 |
| joined: section resume | 54.7 | 53.0-56.0 | 54.7, 54.8, 56.0, 53.8, 53.0 |
| joined: section staff-gate-economy | 48.2 | 45.9-51.7 | 48.2, 48.9, 51.7, 47.5, 45.9 |
| joined: section staff-sprites | 13.3 | 13.1-17.1 | 13.7, 13.3, 17.1, 13.1, 13.1 |
| joined: section terrain | 52.5 | 52.3-59.6 | 52.5, 53.1, 59.6, 52.3, 52.5 |
| joined: section texture-create | 20.6 | 19.9-23.3 | 20.6, 23.1, 23.3, 20.2, 19.9 |
| joined: section texture-decode | 62.5 | 61.9-71.9 | 62.7, 62.5, 71.9, 62.1, 61.9 |
| joined: section wad-decompress | 23.7 | 22.5-26.1 | 22.5, 26.1, 25.9, 23.7, 22.7 |
| joined: section wad-index | 7.3 | 6.6-10.6 | 6.7, 9.9, 10.6, 6.6, 7.3 |
| joined: section wad-read | 9.8 | 9.1-13.3 | 9.5, 9.1, 13.3, 11.5, 9.8 |
| joined: longest update frame while building | 392 | 386-426 | 392, 392, 386, 426, 404 |
| joined: the swap's update frame | 331 | 313-364 | 331, 339, 364, 313, 314 |
| joined: first update frame after the swap | 36.1 | 35.6-38.5 | 35.9, 36.1, 38.0, 38.5, 35.6 |
| joined: longest whole frame while building | 392 | 387-427 | 392, 392, 387, 427, 404 |
| joined: the swap's whole frame | 356 | 338-384 | 356, 364, 384, 338, 338 |
| joined: first whole frame after the swap | 38.1 | 37.5-45.0 | 38.1, 38.0, 45.0, 40.6, 37.5 |
| resynced: process's build | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: download s | 0.017 | 0.016-0.018 | 0.017, 0.017, 0.018, 0.016, 0.017 |
| resynced: last attempt s | 0.017 | 0.016-0.018 | 0.017, 0.017, 0.018, 0.016, 0.017 |
| resynced: payload KiB | 149 | 149-149 | 149, 149, 149, 149, 149 |
| resynced: decode | 2.4 | 1.5-4.8 | 1.5, 3.2, 1.9, 2.4, 4.8 |
| resynced: check | 8.3 | 8.1-8.4 | 8.2, 8.3, 8.3, 8.4, 8.1 |
| resynced: swap | 10.3 | 10.0-13.1 | 10.0, 10.6, 13.1, 10.3, 10.1 |
| resynced: build frames | 10 | 9-10 | 10, 10, 9, 10, 9 |
| resynced: longest build frame | 57.0 | 52.1-63.6 | 57.0, 57.0, 52.1, 57.2, 63.6 |
| resynced: catch-up ticks | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up s | 0.120 | 0.117-0.133 | 0.120, 0.126, 0.118, 0.133, 0.117 |
| resynced: save | 51.5 | 38.4-63.6 | 38.4, 57.0, 43.0, 51.5, 63.6 |
| resynced: save, longest step | 51.5 | 38.4-63.6 | 38.4, 57.0, 43.0, 51.5, 63.6 |
| resynced: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catalogue | 10.5 | 9.2-11.1 | 9.2, 11.1, 10.5, 10.5, 9.9 |
| resynced: catalogue, longest step | 10.4 | 9.2-11.1 | 9.2, 11.1, 10.4, 10.4, 9.9 |
| resynced: catalogue, steps | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: catalogue, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached) | 0.0 | 0.0-0.1 | 0.0, 0.0, 0.1, 0.0, 0.1 |
| resynced: manifest (cached), longest step | 0.0 | 0.0-0.1 | 0.0, 0.0, 0.1, 0.0, 0.1 |
| resynced: manifest (cached), steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached), frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: validation | 23.8 | 23.2-24.9 | 23.2, 23.7, 24.4, 24.9, 23.8 |
| resynced: validation, longest step | 17.4 | 17.3-17.7 | 17.4, 17.3, 17.5, 17.7, 17.3 |
| resynced: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| resynced: validation, frames | 3 | 3-3 | 3, 3, 3, 3, 3 |
| resynced: economy | 2.7 | 2.3-3.0 | 2.3, 2.7, 3.0, 2.8, 2.4 |
| resynced: economy, longest step | 2.7 | 2.3-3.0 | 2.3, 2.7, 3.0, 2.8, 2.4 |
| resynced: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: level | 207 | 204-215 | 208, 204, 207, 215, 204 |
| resynced: level, longest step | 54.2 | 51.4-56.5 | 56.2, 54.2, 51.4, 56.5, 53.1 |
| resynced: level, steps | 83 | 83-83 | 83, 83, 83, 83, 83 |
| resynced: level, frames | 6 | 5-6 | 6, 6, 5, 6, 5 |
| resynced: hud | 0.6 | 0.6-0.6 | 0.6, 0.6, 0.6, 0.6, 0.6 |
| resynced: hud, longest step | 0.6 | 0.6-0.6 | 0.6, 0.6, 0.6, 0.6, 0.6 |
| resynced: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor | 4.5 | 4.3-4.5 | 4.3, 4.5, 4.5, 4.5, 4.3 |
| resynced: advisor, longest step | 4.5 | 4.3-4.5 | 4.3, 4.5, 4.5, 4.5, 4.3 |
| resynced: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: long step, save 1 | 51.5 | 38.4-63.6 | 38.4, 57.0, 43.0, 51.5, 63.6 |
| resynced: long step, validation 13 | 17.4 | 17.3-17.7 | 17.4, 17.3, 17.5, 17.7, 17.3 |
| resynced: long step, level 76 (to 0.3) (1 of 5 runs) | 13.3 | 13.3-13.3 | -, -, -, -, 13.3 |
| resynced: long step, level 79 (to 0.4) | 29.7 | 28.7-37.0 | 29.7, 28.9, 37.0, 29.7, 28.7 |
| resynced: long step, level 81 (to 0.8) | 46.7 | 46.4-49.0 | 46.7, 46.4, 47.1, 49.0, 46.7 |
| resynced: long step, level 82 (to 0.9) | 50.1 | 47.6-50.2 | 50.2, 47.6, 50.1, 50.2, 49.6 |
| resynced: long step, level 83 (to 1) | 54.2 | 51.4-56.5 | 56.2, 54.2, 51.4, 56.5, 53.1 |
| resynced: section coasters | 0.3 | 0.3-0.4 | 0.4, 0.3, 0.3, 0.3, 0.4 |
| resynced: section economy | 0.0 | 0.0-0.1 | 0.1, 0.0, 0.0, 0.0, 0.0 |
| resynced: section entities | 184 | 177-190 | 184, 177, 190, 187, 184 |
| resynced: section guest-sprites | 2.3 | 2.3-2.4 | 2.4, 2.3, 2.3, 2.4, 2.3 |
| resynced: section guests | 50.1 | 47.6-50.2 | 50.2, 47.6, 50.1, 50.2, 49.6 |
| resynced: section join-state | 0.0 | 0.0-0.1 | 0.0, 0.0, 0.0, 0.0, 0.1 |
| resynced: section level-hud | 3.0 | 2.9-6.1 | 2.9, 3.0, 6.1, 3.0, 2.9 |
| resynced: section model-parse | 7.4 | 2.2-8.8 | 7.4, 8.4, 3.1, 8.8, 2.2 |
| resynced: section object-catalog | 2.2 | 2.1-2.6 | 2.2, 2.2, 2.1, 2.5, 2.6 |
| resynced: section object-hoarding | 2.6 | 2.2-2.7 | 2.6, 2.4, 2.2, 2.7, 2.6 |
| resynced: section object-models | 21.4 | 21.1-21.9 | 21.3, 21.4, 21.4, 21.9, 21.1 |
| resynced: section object-runtime | 5.0 | 4.6-5.1 | 4.6, 4.7, 5.0, 5.1, 5.1 |
| resynced: section object-sign | 0.0 | 0.0-0.2 | 0.0, 0.2, 0.0, 0.0, 0.1 |
| resynced: section objects | 46.4 | 46.1-48.7 | 46.4, 46.1, 46.8, 48.7, 46.3 |
| resynced: section original-park | 9.5 | 4.9-11.5 | 9.5, 10.6, 5.1, 11.5, 4.9 |
| resynced: section paths | 0.1 | 0.0-0.2 | 0.1, 0.0, 0.1, 0.2, 0.2 |
| resynced: section pipeline | 2.3 | 2.1-7.2 | 2.3, 2.2, 2.1, 2.4, 7.2 |
| resynced: section resume | 12.5 | 6.3-13.6 | 12.5, 13.3, 6.3, 13.6, 12.4 |
| resynced: section staff-gate-economy | 27.5 | 26.4-28.9 | 28.9, 26.4, 27.5, 27.8, 26.4 |
| resynced: section staff-sprites | 5.4 | 5.0-10.3 | 5.1, 5.0, 5.7, 5.4, 10.3 |
| resynced: section terrain | 16.0 | 15.5-21.0 | 15.7, 15.5, 16.0, 17.7, 21.0 |
| resynced: section texture-create | 11.1 | 9.6-13.1 | 13.1, 9.6, 11.2, 10.7, 11.1 |
| resynced: longest update frame while building | 140 | 135-151 | 138, 144, 140, 151, 135 |
| resynced: the swap's update frame | 140 | 135-151 | 138, 144, 140, 151, 135 |
| resynced: first update frame after the swap | 18.5 | 17.6-19.3 | 18.3, 18.5, 18.6, 19.3, 17.6 |
| resynced: longest whole frame while building | 142 | 138-154 | 140, 147, 142, 154, 138 |
| resynced: the swap's whole frame | 142 | 138-154 | 140, 147, 142, 154, 138 |
| resynced: first whole frame after the swap | 20.0 | 19.1-20.9 | 19.7, 20.1, 20.0, 20.9, 19.1 |
| final check: update frame | 519 | 507-540 | 507, 521, 540, 515, 519 |
| final check: whole frame | 520 | 508-540 | 508, 522, 540, 516, 520 |

### Interpreted, three runs

3 runs; host phase (one host phase, reused by every run): Release build 1.0.0+d710ffe8836a08b7aa69cbc3dd744a4c27c846cd, .NET 11.0.0-rc.2.26504.105, macOS 26.6.0 Arm64, 10 processors, silent True, advisor True, compiled jit; join phase: Release build 1.0.0+d710ffe8836a08b7aa69cbc3dd744a4c27c846cd, .NET 11.0.0-rc.2.26504.105, Browser Wasm, 1 processors, silent False, advisor True, compiled interpreter

page: Chrome/155.0.8059.85, ANGLE (Apple, ANGLE Metal Renderer: Apple M5, Unspecified Version), 1280x900, muted True, frame cap 0, pool sprites settled

    Darwin 25.6.0 arm64
    processor Apple M5, 10 cores, model Mac17,2, memory 34359738368 bytes
    source d710ffe8836a08b7aa69cbc3dd744a4c27c846cd, clean (the driver's checkout)

| Value | Median | Range | Runs |
|---|---|---|---|
| capture 1: cut | 9.1 | 9.1-9.1 | 9.1 |
| capture 1: publish | 0.1 | 0.1-0.1 | 0.1 |
| capture 1: digest | 3.6 | 3.6-3.6 | 3.6 |
| capture 1: state | 0.8 | 0.8-0.8 | 0.8 |
| capture 1: snapshot | 4.0 | 4.0-4.0 | 4.0 |
| capture 1: serializer setup | 0.6 | 0.6-0.6 | 0.6 |
| capture 1: save | 68.4 | 68.4-68.4 | 68.4 |
| capture 1: save, longest slice | 31.7 | 31.7-31.7 | 31.7 |
| capture 1: save, slices | 11 | 11-11 | 11 |
| capture 1: save copy | 0.2 | 0.2-0.2 | 0.2 |
| capture 1: writer setup | 1.5 | 1.5-1.5 | 1.5 |
| capture 1: compression | 1.6 | 1.6-1.6 | 1.6 |
| capture 1: compression, slices | 1 | 1-1 | 1 |
| capture 1: finish | 0.1 | 0.1-0.1 | 0.1 |
| capture 1: longest frame | 31.9 | 31.9-31.9 | 31.9 |
| capture 1: frames | 13 | 13-13 | 13 |
| capture 1: upload s | 0.015 | 0.015-0.015 | 0.015 |
| capture 1: payload KiB | 128 | 128-128 | 128 |
| capture 1: body KiB | 981 | 981-981 | 981 |
| capture 1: save KiB | 980 | 980-980 | 980 |
| capture 2: cut | 6.2 | 6.2-6.2 | 6.2 |
| capture 2: publish | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: digest | 3.0 | 3.0-3.0 | 3.0 |
| capture 2: state | 0.2 | 0.2-0.2 | 0.2 |
| capture 2: snapshot | 3.1 | 3.1-3.1 | 3.1 |
| capture 2: serializer setup | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: save | 12.3 | 12.3-12.3 | 12.3 |
| capture 2: save, longest slice | 5.8 | 5.8-5.8 | 5.8 |
| capture 2: save, slices | 4 | 4-4 | 4 |
| capture 2: save copy | 0.2 | 0.2-0.2 | 0.2 |
| capture 2: writer setup | 0.2 | 0.2-0.2 | 0.2 |
| capture 2: compression | 1.8 | 1.8-1.8 | 1.8 |
| capture 2: compression, slices | 1 | 1-1 | 1 |
| capture 2: finish | 0.1 | 0.1-0.1 | 0.1 |
| capture 2: longest frame | 6.2 | 6.2-6.2 | 6.2 |
| capture 2: frames | 6 | 6-6 | 6 |
| capture 2: upload s | 0.015 | 0.015-0.015 | 0.015 |
| capture 2: payload KiB | 149 | 149-149 | 149 |
| capture 2: body KiB | 1107 | 1107-1107 | 1107 |
| capture 2: save KiB | 1107 | 1107-1107 | 1107 |
| joined: process's build | 1 | 1-1 | 1, 1, 1 |
| joined: download attempts | 1 | 1-1 | 1, 1, 1 |
| joined: download s | 0.015 | 0.014-0.017 | 0.015, 0.017, 0.014 |
| joined: last attempt s | 0.015 | 0.014-0.017 | 0.015, 0.017, 0.014 |
| joined: payload KiB | 128 | 128-128 | 128, 128, 128 |
| joined: decode | 12.2 | 12.2-12.3 | 12.2, 12.2, 12.3 |
| joined: check | 101 | 91.4-102 | 101, 102, 91.4 |
| joined: swap | 9.3 | 7.9-10.5 | 9.3, 10.5, 7.9 |
| joined: build frames | 424 | 423-424 | 424, 424, 423 |
| joined: longest build frame | 1417 | 1342-1422 | 1422, 1417, 1342 |
| joined: catch-up ticks | 758 | 756-763 | 758, 756, 763 |
| joined: catch-up frames | 29 | 27-29 | 29, 29, 27 |
| joined: catch-up s | 2.024 | 1.828-2.088 | 2.024, 2.088, 1.828 |
| joined: save | 508 | 503-566 | 508, 566, 503 |
| joined: save, longest step | 508 | 503-566 | 508, 566, 503 |
| joined: save, steps | 1 | 1-1 | 1, 1, 1 |
| joined: save, frames | 1 | 1-1 | 1, 1, 1 |
| joined: files | 2315 | 2292-2315 | 2315, 2292, 2315 |
| joined: files, longest step | 0.9 | 0.7-1.8 | 0.9, 0.7, 1.8 |
| joined: files, steps | 6477933 | 6046719-6559674 | 6559674, 6046719, 6477933 |
| joined: files, frames | 383 | 381-383 | 383, 383, 381 |
| joined: catalogue | 228 | 222-228 | 222, 228, 228 |
| joined: catalogue, longest step | 59.5 | 56.1-63.5 | 59.5, 63.5, 56.1 |
| joined: catalogue, steps | 90 | 90-90 | 90, 90, 90 |
| joined: catalogue, frames | 13 | 13-14 | 13, 13, 14 |
| joined: manifest | 249 | 242-256 | 249, 256, 242 |
| joined: manifest, longest step | 41.0 | 40.5-44.5 | 41.0, 40.5, 44.5 |
| joined: manifest, steps | 92 | 92-92 | 92, 92, 92 |
| joined: manifest, frames | 16 | 16-16 | 16, 16, 16 |
| joined: validation | 435 | 428-440 | 440, 428, 435 |
| joined: validation, longest step | 237 | 230-240 | 240, 230, 237 |
| joined: validation, steps | 17 | 17-17 | 17, 17, 17 |
| joined: validation, frames | 5 | 5-5 | 5, 5, 5 |
| joined: economy | 13.8 | 13.0-14.3 | 14.3, 13.0, 13.8 |
| joined: economy, longest step | 13.8 | 13.0-14.3 | 14.3, 13.0, 13.8 |
| joined: economy, steps | 1 | 1-1 | 1, 1, 1 |
| joined: economy, frames | 1 | 1-1 | 1, 1, 1 |
| joined: level | 2470 | 2311-2480 | 2480, 2470, 2311 |
| joined: level, longest step | 1413 | 1338-1418 | 1418, 1413, 1338 |
| joined: level, steps | 83 | 83-83 | 83, 83, 83 |
| joined: level, frames | 7 | 7-7 | 7, 7, 7 |
| joined: hud | 7.9 | 7.9-8.6 | 7.9, 8.6, 7.9 |
| joined: hud, longest step | 7.9 | 7.9-8.6 | 7.9, 8.6, 7.9 |
| joined: hud, steps | 1 | 1-1 | 1, 1, 1 |
| joined: hud, frames | 1 | 1-1 | 1, 1, 1 |
| joined: advisor | 36.5 | 32.6-36.9 | 36.5, 36.9, 32.6 |
| joined: advisor, longest step | 36.5 | 32.6-36.9 | 36.5, 36.9, 32.6 |
| joined: advisor, steps | 1 | 1-1 | 1, 1, 1 |
| joined: advisor, frames | 1 | 1-1 | 1, 1, 1 |
| joined: long step, save 1 | 508 | 503-566 | 508, 566, 503 |
| joined: long step, catalogue 1 | 17.1 | 16.9-17.6 | 17.6, 16.9, 17.1 |
| joined: long step, catalogue 90 | 59.5 | 56.1-63.5 | 59.5, 63.5, 56.1 |
| joined: long step, manifest 49 (1 of 3 runs) | 12.5 | 12.5-12.5 | -, 12.5, - |
| joined: long step, manifest 54 (2 of 3 runs) | 12.0 | 12.0-12.0 | 12.0, 12.0, - |
| joined: long step, manifest 91 | 41.0 | 40.5-44.5 | 41.0, 40.5, 44.5 |
| joined: long step, validation 12 | 237 | 230-240 | 240, 230, 237 |
| joined: long step, validation 13 | 165 | 164-166 | 164, 165, 166 |
| joined: long step, validation 14 (1 of 3 runs) | 13.2 | 13.2-13.2 | 13.2, -, - |
| joined: long step, economy 1 | 13.8 | 13.0-14.3 | 14.3, 13.0, 13.8 |
| joined: long step, level 3 (to 0.008) | 16.7 | 16.1-16.9 | 16.7, 16.1, 16.9 |
| joined: long step, level 76 (to 0.3) | 73.1 | 69.2-75.0 | 75.0, 73.1, 69.2 |
| joined: long step, level 77 (to 0.3) | 195 | 181-198 | 195, 198, 181 |
| joined: long step, level 79 (to 0.4) | 194 | 182-196 | 194, 196, 182 |
| joined: long step, level 81 (to 0.8) | 1413 | 1338-1418 | 1418, 1413, 1338 |
| joined: long step, level 82 (to 0.9) | 250 | 226-251 | 251, 250, 226 |
| joined: long step, level 83 (to 1) | 307 | 281-311 | 311, 307, 281 |
| joined: long step, advisor 1 | 36.5 | 32.6-36.9 | 36.5, 36.9, 32.6 |
| joined: section coasters | 7.7 | 7.0-7.8 | 7.7, 7.8, 7.0 |
| joined: section economy | 0.2 | 0.1-0.3 | 0.2, 0.1, 0.3 |
| joined: section entities | 2354 | 2203-2357 | 2357, 2354, 2203 |
| joined: section guest-sprites | 46.5 | 41.7-46.6 | 46.5, 46.6, 41.7 |
| joined: section guests | 250 | 226-251 | 251, 250, 226 |
| joined: section join-state | 1.1 | 1.1-1.3 | 1.1, 1.3, 1.1 |
| joined: section level-hud | 9.8 | 7.0-10.6 | 9.8, 10.6, 7.0 |
| joined: section model-parse | 30.2 | 29.2-31.5 | 31.5, 30.2, 29.2 |
| joined: section object-catalog | 10.4 | 10.4-10.6 | 10.6, 10.4, 10.4 |
| joined: section object-hoarding | 19.7 | 18.5-20.4 | 20.4, 19.7, 18.5 |
| joined: section object-models | 1175 | 1119-1176 | 1175, 1176, 1119 |
| joined: section object-runtime | 118 | 109-125 | 125, 118, 109 |
| joined: section object-sign | 779 | 750-783 | 779, 783, 750 |
| joined: section objects | 1409 | 1334-1414 | 1414, 1409, 1334 |
| joined: section original-park | 29.9 | 29.0-30.4 | 30.4, 29.0, 29.9 |
| joined: section paths | 4.1 | 3.8-4.6 | 4.6, 4.1, 3.8 |
| joined: section pipeline | 18.7 | 14.2-23.7 | 23.7, 18.7, 14.2 |
| joined: section resume | 76.0 | 69.7-81.9 | 81.9, 76.0, 69.7 |
| joined: section staff-gate-economy | 117 | 109-119 | 119, 117, 109 |
| joined: section staff-sprites | 53.3 | 51.9-55.9 | 53.3, 55.9, 51.9 |
| joined: section terrain | 270 | 250-272 | 270, 272, 250 |
| joined: section texture-create | 42.8 | 40.1-50.9 | 50.9, 42.8, 40.1 |
| joined: section texture-decode | 521 | 499-525 | 521, 525, 499 |
| joined: section wad-decompress | 124 | 118-125 | 125, 124, 118 |
| joined: section wad-index | 19.1 | 18.7-19.7 | 19.7, 19.1, 18.7 |
| joined: section wad-read | 26.5 | 23.4-27.1 | 23.4, 27.1, 26.5 |
| joined: longest update frame while building | 1419 | 1343-1424 | 1424, 1419, 1343 |
| joined: the swap's update frame | 644 | 579-661 | 644, 661, 579 |
| joined: first update frame after the swap | 96.7 | 91.3-106 | 106, 96.7, 91.3 |
| joined: longest whole frame while building | 1420 | 1344-1425 | 1425, 1420, 1344 |
| joined: the swap's whole frame | 706 | 628-718 | 706, 718, 628 |
| joined: first whole frame after the swap | 102 | 95.4-111 | 111, 102, 95.4 |
| resynced: process's build | 2 | 2-2 | 2, 2, 2 |
| resynced: download attempts | 1 | 1-1 | 1, 1, 1 |
| resynced: download s | 0.017 | 0.016-0.017 | 0.017, 0.016, 0.017 |
| resynced: last attempt s | 0.017 | 0.016-0.017 | 0.017, 0.016, 0.017 |
| resynced: payload KiB | 149 | 149-149 | 149, 149, 149 |
| resynced: decode | 4.7 | 3.3-4.8 | 4.7, 4.8, 3.3 |
| resynced: check | 32.4 | 29.2-37.8 | 37.8, 32.4, 29.2 |
| resynced: swap | 29.7 | 28.8-34.4 | 34.4, 29.7, 28.8 |
| resynced: build frames | 13 | 13-13 | 13, 13, 13 |
| resynced: longest build frame | 195 | 182-198 | 195, 198, 182 |
| resynced: catch-up ticks | 1 | 1-1 | 1, 1, 1 |
| resynced: catch-up frames | 1 | 1-1 | 1, 1, 1 |
| resynced: catch-up s | 0.261 | 0.251-0.266 | 0.261, 0.266, 0.251 |
| resynced: save | 96.3 | 93.4-107 | 96.3, 107, 93.4 |
| resynced: save, longest step | 96.3 | 93.4-107 | 96.3, 107, 93.4 |
| resynced: save, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: save, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: files, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: files, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: catalogue | 36.3 | 35.2-38.9 | 36.3, 38.9, 35.2 |
| resynced: catalogue, longest step | 36.3 | 35.2-38.9 | 36.3, 38.9, 35.2 |
| resynced: catalogue, steps | 2 | 2-2 | 2, 2, 2 |
| resynced: catalogue, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: manifest (cached) | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: manifest (cached), longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: manifest (cached), steps | 1 | 1-1 | 1, 1, 1 |
| resynced: manifest (cached), frames | 1 | 1-1 | 1, 1, 1 |
| resynced: validation | 132 | 128-140 | 128, 140, 132 |
| resynced: validation, longest step | 84.1 | 77.7-87.5 | 77.7, 87.5, 84.1 |
| resynced: validation, steps | 17 | 17-17 | 17, 17, 17 |
| resynced: validation, frames | 4 | 4-4 | 4, 4, 4 |
| resynced: economy | 2.8 | 2.7-2.8 | 2.8, 2.8, 2.7 |
| resynced: economy, longest step | 2.8 | 2.7-2.8 | 2.8, 2.8, 2.7 |
| resynced: economy, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: economy, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: level | 740 | 729-784 | 729, 784, 740 |
| resynced: level, longest step | 195 | 182-197 | 195, 197, 182 |
| resynced: level, steps | 83 | 83-83 | 83, 83, 83 |
| resynced: level, frames | 7 | 7-7 | 7, 7, 7 |
| resynced: hud | 1.4 | 1.1-1.4 | 1.4, 1.4, 1.1 |
| resynced: hud, longest step | 1.4 | 1.1-1.4 | 1.4, 1.4, 1.1 |
| resynced: hud, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: hud, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: advisor | 4.9 | 4.9-5.5 | 4.9, 5.5, 4.9 |
| resynced: advisor, longest step | 4.9 | 4.9-5.5 | 4.9, 5.5, 4.9 |
| resynced: advisor, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: advisor, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: long step, save 1 | 96.3 | 93.4-107 | 96.3, 107, 93.4 |
| resynced: long step, catalogue 2 | 36.3 | 35.2-38.9 | 36.3, 38.9, 35.2 |
| resynced: long step, validation 7 | 23.9 | 21.6-24.1 | 24.1, 23.9, 21.6 |
| resynced: long step, validation 13 | 84.1 | 77.7-87.5 | 77.7, 87.5, 84.1 |
| resynced: long step, level 3 (to 0.008) | 16.8 | 16.5-16.8 | 16.8, 16.8, 16.5 |
| resynced: long step, level 76 (to 0.3) | 22.6 | 22.0-23.9 | 22.0, 22.6, 23.9 |
| resynced: long step, level 77 (to 0.3) | 30.0 | 28.5-30.4 | 28.5, 30.4, 30.0 |
| resynced: long step, level 79 (to 0.4) | 182 | 172-192 | 172, 192, 182 |
| resynced: long step, level 81 (to 0.8) | 195 | 177-197 | 195, 197, 177 |
| resynced: long step, level 82 (to 0.9) | 170 | 160-177 | 160, 177, 170 |
| resynced: long step, level 83 (to 1) | 132 | 126-139 | 126, 139, 132 |
| resynced: section coasters | 3.1 | 2.9-3.1 | 3.1, 3.1, 2.9 |
| resynced: section economy | 0.0 | 0.0-0.1 | 0.0, 0.1, 0.0 |
| resynced: section entities | 673 | 670-722 | 670, 722, 673 |
| resynced: section guest-sprites | 7.8 | 7.4-8.0 | 7.4, 8.0, 7.8 |
| resynced: section guests | 170 | 160-177 | 160, 177, 170 |
| resynced: section join-state | 0.0 | 0.0-0.1 | 0.1, 0.0, 0.0 |
| resynced: section level-hud | 5.6 | 5.3-9.5 | 5.3, 5.6, 9.5 |
| resynced: section model-parse | 17.1 | 17.1-17.1 | 17.1, 17.1, 17.1 |
| resynced: section object-catalog | 8.1 | 7.9-8.3 | 8.1, 8.3, 7.9 |
| resynced: section object-hoarding | 5.6 | 4.7-5.6 | 5.6, 5.6, 4.7 |
| resynced: section object-models | 68.9 | 64.6-69.0 | 68.9, 69.0, 64.6 |
| resynced: section object-runtime | 25.8 | 22.1-30.6 | 30.6, 25.8, 22.1 |
| resynced: section object-sign | 0.3 | 0.0-0.6 | 0.6, 0.3, 0.0 |
| resynced: section objects | 192 | 174-194 | 192, 194, 174 |
| resynced: section original-park | 25.1 | 24.7-25.4 | 25.1, 25.4, 24.7 |
| resynced: section paths | 0.1 | 0.1-0.2 | 0.2, 0.1, 0.1 |
| resynced: section pipeline | 11.5 | 11.0-14.6 | 11.5, 11.0, 14.6 |
| resynced: section resume | 32.5 | 29.5-34.4 | 29.5, 32.5, 34.4 |
| resynced: section staff-gate-economy | 58.9 | 56.3-68.0 | 56.3, 68.0, 58.9 |
| resynced: section staff-sprites | 18.2 | 16.1-25.7 | 16.1, 25.7, 18.2 |
| resynced: section terrain | 53.8 | 51.2-54.6 | 51.2, 53.8, 54.6 |
| resynced: section texture-create | 18.6 | 15.5-19.1 | 19.1, 15.5, 18.6 |
| resynced: longest update frame while building | 327 | 307-330 | 330, 327, 307 |
| resynced: the swap's update frame | 327 | 307-330 | 330, 327, 307 |
| resynced: first update frame after the swap | 49.5 | 46.6-56.8 | 56.8, 46.6, 49.5 |
| resynced: longest whole frame while building | 333 | 314-338 | 338, 333, 314 |
| resynced: the swap's whole frame | 333 | 314-338 | 338, 333, 314 |
| resynced: first whole frame after the swap | 52.7 | 50.1-60.3 | 60.3, 50.1, 52.7 |
| final check: update frame | 1632 | 1543-1643 | 1632, 1543, 1643 |
| final check: whole frame | 1633 | 1544-1644 | 1633, 1544, 1644 |

## Main after the build steps were split (the baseline of C2c-2b-3)

Main with #741 (the park start's build steps split), #799 and #802, before C2c-2b-3a.

### The desktop, five runs

5 runs; Release build 1.0.0+922fe3dab6bb3f12abba7857d95ebe69351aadc2, .NET 11.0.0-rc.2.26504.105, macOS 26.6.0 Arm64, 10 processors, silent True, advisor True, compiled jit

    Darwin 25.6.0 arm64
    processor Apple M5, model Mac17,2, memory 34359738368 bytes
    source 922fe3dab6bb3f12abba7857d95ebe69351aadc2, clean

| Value | Median | Range | Runs |
|---|---|---|---|
| capture 1: cut | 9.1 | 8.4-9.7 | 9.1, 8.4, 8.5, 9.2, 9.7 |
| capture 1: publish | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| capture 1: digest | 3.6 | 3.6-4.2 | 3.6, 3.6, 3.6, 3.9, 4.2 |
| capture 1: state | 0.8 | 0.7-0.9 | 0.9, 0.7, 0.8, 0.8, 0.8 |
| capture 1: snapshot | 4.4 | 3.9-4.5 | 4.4, 4.0, 3.9, 4.4, 4.5 |
| capture 1: serializer setup | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| capture 1: save | 63.6 | 62.4-70.9 | 70.9, 65.8, 62.5, 62.4, 63.6 |
| capture 1: save, longest slice | 32.1 | 31.1-36.1 | 36.1, 32.1, 31.6, 31.1, 32.1 |
| capture 1: save, slices | 11 | 11-13 | 13, 12, 11, 11, 11 |
| capture 1: save copy | 0.2 | 0.2-0.3 | 0.3, 0.2, 0.2, 0.2, 0.2 |
| capture 1: writer setup | 1.3 | 1.1-1.5 | 1.3, 1.1, 1.5, 1.2, 1.4 |
| capture 1: compression | 1.5 | 1.3-1.6 | 1.4, 1.5, 1.6, 1.3, 1.6 |
| capture 1: compression, slices | 1 | 1-1 | 1, 1, 1, 1, 1 |
| capture 1: finish | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| capture 1: longest frame | 32.3 | 31.4-36.4 | 36.4, 32.4, 31.9, 31.4, 32.3 |
| capture 1: frames | 13 | 13-15 | 15, 14, 13, 13, 13 |
| capture 1: upload s | 0.016 | 0.015-0.016 | 0.016, 0.016, 0.015, 0.016, 0.015 |
| capture 1: payload KiB | 128 | 128-128 | 128, 128, 128, 128, 128 |
| capture 1: body KiB | 981 | 981-981 | 981, 981, 981, 981, 981 |
| capture 1: save KiB | 980 | 980-980 | 980, 980, 980, 980, 980 |
| capture 2: cut | 5.7 | 4.3-6.6 | 4.3, 4.9, 5.7, 6.6, 5.7 |
| capture 2: publish | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| capture 2: digest | 2.7 | 2.1-2.9 | 2.1, 2.6, 2.7, 2.9, 2.8 |
| capture 2: state | 0.2 | 0.1-0.3 | 0.1, 0.1, 0.2, 0.3, 0.2 |
| capture 2: snapshot | 2.7 | 2.1-3.5 | 2.1, 2.1, 2.8, 3.5, 2.7 |
| capture 2: serializer setup | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| capture 2: save | 9.2 | 8.7-9.7 | 9.2, 8.7, 9.0, 9.2, 9.7 |
| capture 2: save, longest slice | 4.3 | 3.7-4.7 | 3.8, 3.7, 4.3, 4.5, 4.7 |
| capture 2: save, slices | 4 | 4-4 | 4, 4, 4, 4, 4 |
| capture 2: save copy | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| capture 2: writer setup | 0.2 | 0.1-0.2 | 0.2, 0.1, 0.2, 0.1, 0.2 |
| capture 2: compression | 1.3 | 1.2-1.7 | 1.3, 1.3, 1.2, 1.3, 1.7 |
| capture 2: compression, slices | 1 | 1-1 | 1, 1, 1, 1, 1 |
| capture 2: finish | 0.1 | 0.0-0.1 | 0.0, 0.1, 0.1, 0.0, 0.1 |
| capture 2: longest frame | 5.7 | 4.3-6.6 | 4.3, 4.9, 5.7, 6.6, 5.7 |
| capture 2: frames | 6 | 6-6 | 6, 6, 6, 6, 6 |
| capture 2: upload s | 0.015 | 0.015-0.016 | 0.015, 0.016, 0.015, 0.016, 0.015 |
| capture 2: payload KiB | 149 | 149-149 | 149, 149, 149, 149, 149 |
| capture 2: body KiB | 1107 | 1107-1107 | 1107, 1107, 1107, 1107, 1107 |
| capture 2: save KiB | 1107 | 1107-1107 | 1107, 1107, 1107, 1107, 1107 |
| joined: process's build | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download s | 0.016 | 0.015-0.016 | 0.016, 0.016, 0.016, 0.015, 0.015 |
| joined: last attempt s | 0.016 | 0.015-0.016 | 0.016, 0.016, 0.016, 0.015, 0.015 |
| joined: payload KiB | 128 | 128-128 | 128, 128, 128, 128, 128 |
| joined: decode | 4.2 | 3.9-4.7 | 3.9, 4.6, 4.1, 4.2, 4.7 |
| joined: check | 42.2 | 38.9-46.2 | 43.5, 46.2, 42.2, 39.9, 38.9 |
| joined: swap | 9.8 | 8.4-15.1 | 9.8, 15.1, 8.6, 8.4, 9.8 |
| joined: build frames | 34 | 32-40 | 40, 40, 34, 32, 33 |
| joined: longest build frame | 357 | 347-428 | 376, 428, 357, 355, 347 |
| joined: catch-up ticks | 8 | 8-8 | 8, 8, 8, 8, 8 |
| joined: catch-up frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: catch-up s | 0.349 | 0.266-0.410 | 0.369, 0.410, 0.349, 0.337, 0.266 |
| joined: save | 154 | 147-171 | 171, 170, 154, 149, 147 |
| joined: save, longest step | 154 | 147-171 | 171, 170, 154, 149, 147 |
| joined: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| joined: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| joined: files, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: files, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: catalogue | 71.6 | 68.6-109 | 84.8, 109, 68.6, 69.2, 71.6 |
| joined: catalogue, longest step | 16.6 | 16.0-20.4 | 18.7, 20.4, 16.5, 16.6, 16.0 |
| joined: catalogue, steps | 90 | 90-90 | 90, 90, 90, 90, 90 |
| joined: catalogue, frames | 5 | 5-7 | 5, 7, 5, 5, 5 |
| joined: manifest | 41.4 | 39.5-53.7 | 53.7, 53.1, 41.4, 39.5, 40.4 |
| joined: manifest, longest step | 3.6 | 3.0-4.8 | 4.8, 3.6, 3.6, 3.0, 3.2 |
| joined: manifest, steps | 92 | 92-92 | 92, 92, 92, 92, 92 |
| joined: manifest, frames | 4 | 4-5 | 5, 5, 4, 4, 4 |
| joined: validation | 33.8 | 32.6-36.7 | 36.7, 36.1, 33.5, 32.6, 33.8 |
| joined: validation, longest step | 20.0 | 17.6-21.5 | 19.5, 21.4, 17.6, 20.0, 21.5 |
| joined: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| joined: validation, frames | 2 | 2-3 | 3, 2, 3, 2, 2 |
| joined: economy | 15.1 | 14.6-17.6 | 16.6, 17.6, 15.1, 15.0, 14.6 |
| joined: economy, longest step | 15.1 | 14.6-17.6 | 16.6, 17.6, 15.1, 15.0, 14.6 |
| joined: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: level | 772 | 748-947 | 842, 947, 772, 767, 748 |
| joined: level, longest step | 350 | 339-419 | 367, 419, 350, 349, 339 |
| joined: level, steps | 594 | 594-594 | 594, 594, 594, 594, 594 |
| joined: level, frames | 21 | 20-26 | 26, 25, 21, 20, 21 |
| joined: hud | 10.7 | 10.3-12.2 | 11.5, 12.2, 10.7, 10.5, 10.3 |
| joined: hud, longest step | 10.7 | 10.3-12.2 | 11.5, 12.2, 10.7, 10.5, 10.3 |
| joined: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor | 23.7 | 21.6-28.2 | 24.5, 28.2, 23.7, 22.0, 21.6 |
| joined: advisor, longest step | 23.7 | 21.6-28.2 | 24.5, 28.2, 23.7, 22.0, 21.6 |
| joined: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: long step, save 1 | 154 | 147-171 | 171, 170, 154, 149, 147 |
| joined: long step, catalogue 1 (1 of 5 runs) | 13.4 | 13.4-13.4 | -, 13.4, -, -, - |
| joined: long step, catalogue 32 (1 of 5 runs) | 13.3 | 13.3-13.3 | -, 13.3, -, -, - |
| joined: long step, catalogue 90 | 16.6 | 16.0-20.4 | 18.7, 20.4, 16.5, 16.6, 16.0 |
| joined: long step, validation 13 | 20.0 | 17.6-21.5 | 19.5, 21.4, 17.6, 20.0, 21.5 |
| joined: long step, economy 1 | 15.1 | 14.6-17.6 | 16.6, 17.6, 15.1, 15.0, 14.6 |
| joined: long step, level 82 (to 0.3) (1 of 5 runs) | 12.8 | 12.8-12.8 | -, 12.8, -, -, - |
| joined: long step, level 532 (to 0.35) | 18.4 | 18.1-21.5 | 20.3, 21.5, 18.4, 18.4, 18.1 |
| joined: long step, level 544 (to 0.4) (1 of 5 runs) | 12.3 | 12.3-12.3 | -, 12.3, -, -, - |
| joined: long step, level 571 (to 0.8) | 350 | 339-419 | 367, 419, 350, 349, 339 |
| joined: long step, level 572 (to 0.85) | 20.7 | 18.1-25.4 | 22.8, 25.4, 20.7, 18.8, 18.1 |
| joined: long step, level 573 (to 0.85) (3 of 5 runs) | 12.8 | 12.7-14.3 | 14.3, -, 12.8, -, 12.7 |
| joined: long step, level 574 (to 0.85) (2 of 5 runs) | 14.8 | 14.4-15.2 | 14.4, 15.2, -, -, - |
| joined: long step, level 576 (to 0.85) | 31.7 | 29.3-46.0 | 36.3, 46.0, 29.3, 31.7, 29.6 |
| joined: long step, level 578 (to 0.85) (2 of 5 runs) | 14.0 | 13.1-15.0 | -, 13.1, -, 15.0, - |
| joined: long step, level 586 (to 0.95) | 42.0 | 41.3-58.3 | 47.3, 58.3, 41.6, 42.0, 41.3 |
| joined: long step, level 590 (to 0.95) (1 of 5 runs) | 13.0 | 13.0-13.0 | -, 13.0, -, -, - |
| joined: long step, level 592 (to 0.95) | 13.4 | 12.9-16.3 | 13.4, 15.8, 16.3, 12.9, 13.0 |
| joined: long step, level 593 (to 0.95) | 17.3 | 16.6-20.3 | 18.5, 20.3, 16.6, 17.3, 17.1 |
| joined: long step, level 594 (to 1) | 64.8 | 60.7-71.8 | 67.8, 71.8, 63.2, 64.8, 60.7 |
| joined: long step, hud 1 (1 of 5 runs) | 12.2 | 12.2-12.2 | -, 12.2, -, -, - |
| joined: long step, advisor 1 | 23.7 | 21.6-28.2 | 24.5, 28.2, 23.7, 22.0, 21.6 |
| joined: section coasters | 5.3 | 5.2-6.0 | 5.2, 6.0, 5.7, 5.3, 5.2 |
| joined: section economy | 0.3 | 0.3-0.3 | 0.3, 0.3, 0.3, 0.3, 0.3 |
| joined: section entities | 698 | 675-864 | 763, 864, 698, 692, 675 |
| joined: section guest-sprites | 12.7 | 9.5-14.2 | 14.2, 11.8, 12.8, 9.5, 12.7 |
| joined: section guests | 109 | 104-145 | 129, 145, 107, 109, 104 |
| joined: section join-state | 2.4 | 2.2-2.6 | 2.4, 2.6, 2.4, 2.3, 2.2 |
| joined: section level-hud | 4.4 | 4.1-6.4 | 5.3, 4.1, 4.4, 6.4, 4.1 |
| joined: section model-parse | 39.9 | 38.1-45.9 | 40.9, 45.9, 39.9, 38.1, 39.2 |
| joined: section object-catalog | 3.5 | 3.3-4.0 | 3.6, 4.0, 3.3, 3.3, 3.5 |
| joined: section object-hoarding | 9.2 | 8.9-12.9 | 10.3, 12.9, 9.2, 9.0, 8.9 |
| joined: section object-models | 268 | 258-318 | 281, 318, 265, 268, 258 |
| joined: section object-runtime | 49.9 | 47.5-58.3 | 50.4, 58.3, 49.9, 47.8, 47.5 |
| joined: section object-sign | 192 | 185-225 | 197, 225, 188, 192, 185 |
| joined: section objects | 347 | 336-416 | 365, 416, 347, 346, 336 |
| joined: section original-park | 12.3 | 10.8-13.1 | 12.5, 13.1, 11.5, 10.8, 12.3 |
| joined: section paths | 3.9 | 3.8-4.4 | 4.2, 4.4, 3.8, 3.9, 3.8 |
| joined: section pipeline | 3.3 | 3.0-3.9 | 3.7, 3.9, 3.3, 3.2, 3.0 |
| joined: section resume | 55.8 | 53.9-64.4 | 59.4, 64.4, 55.8, 55.6, 53.9 |
| joined: section ride-opcodes | 42.1 | 40.7-48.7 | 44.8, 48.7, 42.1, 40.7, 41.5 |
| joined: section shader-native | 0.5 | 0.4-0.6 | 0.6, 0.5, 0.4, 0.5, 0.4 |
| joined: section shader-spirv | 1.8 | 1.0-8.2 | 1.8, 2.1, 1.2, 8.2, 1.0 |
| joined: section staff-gate-economy | 46.0 | 43.2-56.3 | 49.4, 56.3, 46.0, 43.7, 43.2 |
| joined: section staff-sprites | 13.4 | 12.9-16.3 | 13.4, 15.8, 16.3, 12.9, 13.0 |
| joined: section terrain | 45.3 | 44.8-61.4 | 48.7, 61.4, 45.3, 44.9, 44.8 |
| joined: section texture-create | 23.7 | 22.9-27.7 | 24.8, 27.7, 23.3, 23.7, 22.9 |
| joined: section texture-decode | 52.5 | 50.3-73.3 | 58.4, 73.3, 52.5, 50.3, 50.7 |
| joined: section wad-decompress | 18.4 | 17.4-22.9 | 19.5, 22.9, 18.4, 17.8, 17.4 |
| joined: section wad-index | 6.7 | 5.0-18.9 | 6.7, 18.9, 5.0, 5.3, 6.8 |
| joined: section wad-read | 3.5 | 3.0-3.7 | 3.7, 3.5, 3.7, 3.0, 3.1 |
| joined: longest update frame while building | 368 | 348-433 | 390, 433, 368, 356, 348 |
| joined: the swap's update frame | 368 | 287-433 | 390, 433, 368, 356, 287 |
| joined: first update frame after the swap | 33.7 | 30.7-41.9 | 33.8, 41.9, 32.4, 30.7, 33.7 |
| resynced: process's build | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: download s | 0.017 | 0.016-0.017 | 0.016, 0.017, 0.017, 0.016, 0.017 |
| resynced: last attempt s | 0.017 | 0.016-0.017 | 0.016, 0.017, 0.017, 0.016, 0.017 |
| resynced: payload KiB | 149 | 149-149 | 149, 149, 149, 149, 149 |
| resynced: decode | 2.4 | 1.5-2.5 | 1.5, 2.5, 2.1, 2.4, 2.4 |
| resynced: check | 2.0 | 2.0-2.3 | 2.3, 2.0, 2.0, 2.0, 2.1 |
| resynced: swap | 7.5 | 7.3-9.9 | 9.9, 7.5, 7.7, 7.3, 7.4 |
| resynced: build frames | 15 | 14-16 | 15, 16, 14, 14, 15 |
| resynced: longest build frame | 34.8 | 28.7-37.3 | 34.8, 28.7, 31.4, 36.4, 37.3 |
| resynced: catch-up ticks | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up s | 0.053 | 0.050-0.083 | 0.083, 0.057, 0.053, 0.050, 0.051 |
| resynced: save | 20.7 | 16.7-24.2 | 16.7, 19.1, 20.7, 24.2, 21.9 |
| resynced: save, longest step | 20.7 | 16.7-24.2 | 16.7, 19.1, 20.7, 24.2, 21.9 |
| resynced: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catalogue | 12.8 | 10.6-17.2 | 14.3, 17.2, 10.6, 12.8, 12.8 |
| resynced: catalogue, longest step | 12.8 | 10.5-17.2 | 14.3, 17.2, 10.5, 12.8, 12.8 |
| resynced: catalogue, steps | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: catalogue, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached) | 0.1 | 0.0-0.1 | 0.0, 0.1, 0.1, 0.1, 0.1 |
| resynced: manifest (cached), longest step | 0.1 | 0.0-0.1 | 0.0, 0.1, 0.1, 0.1, 0.1 |
| resynced: manifest (cached), steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached), frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: validation | 10.2 | 9.6-10.9 | 10.6, 9.6, 10.9, 10.2, 9.6 |
| resynced: validation, longest step | 5.5 | 5.4-6.1 | 6.1, 5.5, 5.5, 5.8, 5.4 |
| resynced: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| resynced: validation, frames | 1 | 1-2 | 1, 1, 2, 1, 1 |
| resynced: economy | 1.3 | 1.1-2.3 | 1.1, 1.3, 1.4, 1.1, 2.3 |
| resynced: economy, longest step | 1.3 | 1.1-2.3 | 1.1, 1.3, 1.4, 1.1, 2.3 |
| resynced: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: level | 212 | 206-239 | 239, 228, 208, 212, 206 |
| resynced: level, longest step | 29.4 | 24.2-34.6 | 34.6, 28.5, 24.2, 29.4, 30.1 |
| resynced: level, steps | 568 | 568-568 | 568, 568, 568, 568, 568 |
| resynced: level, frames | 12 | 12-14 | 13, 14, 12, 12, 12 |
| resynced: hud | 0.3 | 0.2-0.4 | 0.4, 0.2, 0.3, 0.3, 0.3 |
| resynced: hud, longest step | 0.3 | 0.2-0.4 | 0.4, 0.2, 0.3, 0.3, 0.3 |
| resynced: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor | 1.7 | 1.6-3.6 | 3.6, 1.7, 1.7, 1.6, 1.6 |
| resynced: advisor, longest step | 1.7 | 1.6-3.6 | 3.6, 1.7, 1.7, 1.6, 1.6 |
| resynced: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: long step, save 1 | 20.7 | 16.7-24.2 | 16.7, 19.1, 20.7, 24.2, 21.9 |
| resynced: long step, catalogue 2 (4 of 5 runs) | 13.5 | 12.8-17.2 | 14.3, 17.2, -, 12.8, 12.8 |
| resynced: long step, level 3 (to 0.008) | 15.2 | 14.1-15.9 | 14.9, 15.8, 15.9, 15.2, 14.1 |
| resynced: long step, level 532 (to 0.35) | 21.9 | 21.0-24.5 | 24.5, 21.9, 21.9, 21.0, 21.3 |
| resynced: long step, level 545 (to 0.8) | 18.8 | 18.4-21.6 | 21.6, 18.9, 18.4, 18.6, 18.8 |
| resynced: long step, level 548 (to 0.85) (2 of 5 runs) | 15.5 | 15.4-15.6 | 15.6, 15.4, -, -, - |
| resynced: long step, level 550 (to 0.85) | 29.4 | 24.2-34.6 | 34.6, 28.5, 24.2, 29.4, 30.1 |
| resynced: long step, level 567 (to 0.95) | 17.6 | 15.2-20.7 | 20.7, 18.1, 17.6, 17.5, 15.2 |
| resynced: section coasters | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| resynced: section economy | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: section entities | 192 | 187-218 | 218, 207, 187, 192, 187 |
| resynced: section guest-sprites | 3.5 | 3.4-4.2 | 4.2, 3.5, 3.5, 3.5, 3.4 |
| resynced: section guests | 80.6 | 72.1-93.8 | 92.7, 93.8, 72.1, 80.6, 80.1 |
| resynced: section join-state | 0.0 | 0.0-0.1 | 0.1, 0.0, 0.0, 0.0, 0.0 |
| resynced: section level-hud | 1.6 | 1.4-3.0 | 3.0, 1.9, 1.6, 1.4, 1.5 |
| resynced: section model-parse | 15.2 | 14.1-16.0 | 15.0, 15.9, 16.0, 15.2, 14.1 |
| resynced: section object-catalog | 1.2 | 1.1-1.8 | 1.2, 1.8, 1.2, 1.1, 1.1 |
| resynced: section object-hoarding | 0.9 | 0.9-1.1 | 1.1, 0.9, 0.9, 0.9, 0.9 |
| resynced: section object-models | 10.0 | 9.1-10.7 | 10.7, 9.7, 9.1, 10.1, 10.0 |
| resynced: section object-runtime | 3.8 | 2.9-4.4 | 4.4, 3.8, 3.8, 2.9, 3.1 |
| resynced: section object-sign | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: section objects | 18.8 | 18.4-21.5 | 21.5, 18.9, 18.4, 18.6, 18.8 |
| resynced: section original-park | 16.5 | 15.3-18.0 | 16.2, 18.0, 17.3, 16.5, 15.3 |
| resynced: section paths | 0.5 | 0.5-0.5 | 0.5, 0.5, 0.5, 0.5, 0.5 |
| resynced: section pipeline | 2.2 | 2.1-2.6 | 2.6, 2.2, 2.2, 2.2, 2.1 |
| resynced: section resume | 3.4 | 3.1-3.7 | 3.7, 3.4, 3.1, 3.5, 3.2 |
| resynced: section ride-opcodes | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: section staff-gate-economy | 32.2 | 28.1-36.5 | 36.5, 32.2, 33.7, 31.8, 28.1 |
| resynced: section staff-sprites | 9.9 | 8.8-11.7 | 9.9, 9.7, 11.7, 10.0, 8.8 |
| resynced: section terrain | 15.9 | 15.0-16.7 | 16.5, 16.7, 15.9, 15.7, 15.0 |
| resynced: section texture-create | 8.0 | 7.1-10.0 | 10.0, 8.0, 9.0, 7.2, 7.1 |
| resynced: longest update frame while building | 64.3 | 61.5-97.2 | 97.2, 67.1, 64.3, 61.5, 62.3 |
| resynced: the swap's update frame | 64.3 | 61.5-97.2 | 97.2, 67.1, 64.3, 61.5, 62.3 |
| resynced: first update frame after the swap | 45.7 | 45.4-60.1 | 60.1, 58.0, 45.7, 45.4, 45.5 |

### Compiled ahead of time, five runs

5 runs; host phase (one host phase, reused by every run): Release build 1.0.0+922fe3dab6bb3f12abba7857d95ebe69351aadc2, .NET 11.0.0-rc.2.26504.105, macOS 26.6.0 Arm64, 10 processors, silent True, advisor True, compiled jit; join phase: Release build 1.0.0+922fe3dab6bb3f12abba7857d95ebe69351aadc2, .NET 11.0.0-rc.2.26504.105, Browser Wasm, 1 processors, silent False, advisor True, compiled aot

page: Chrome/155.0.8059.85, ANGLE (Apple, ANGLE Metal Renderer: Apple M5, Unspecified Version), 1280x900, muted True, frame cap 0, pool sprites settled

    Darwin 25.6.0 arm64
    processor Apple M5, 10 cores, model Mac17,2, memory 34359738368 bytes
    source 922fe3dab6bb3f12abba7857d95ebe69351aadc2, clean (the driver's checkout)

| Value | Median | Range | Runs |
|---|---|---|---|
| capture 1: cut | 9.7 | 9.7-9.7 | 9.7 |
| capture 1: publish | 0.1 | 0.1-0.1 | 0.1 |
| capture 1: digest | 4.0 | 4.0-4.0 | 4.0 |
| capture 1: state | 0.8 | 0.8-0.8 | 0.8 |
| capture 1: snapshot | 4.8 | 4.8-4.8 | 4.8 |
| capture 1: serializer setup | 0.0 | 0.0-0.0 | 0.0 |
| capture 1: save | 65.2 | 65.2-65.2 | 65.2 |
| capture 1: save, longest slice | 32.0 | 32.0-32.0 | 32.0 |
| capture 1: save, slices | 12 | 12-12 | 12 |
| capture 1: save copy | 0.2 | 0.2-0.2 | 0.2 |
| capture 1: writer setup | 1.5 | 1.5-1.5 | 1.5 |
| capture 1: compression | 1.6 | 1.6-1.6 | 1.6 |
| capture 1: compression, slices | 1 | 1-1 | 1 |
| capture 1: finish | 0.1 | 0.1-0.1 | 0.1 |
| capture 1: longest frame | 32.2 | 32.2-32.2 | 32.2 |
| capture 1: frames | 14 | 14-14 | 14 |
| capture 1: upload s | 0.016 | 0.016-0.016 | 0.016 |
| capture 1: payload KiB | 128 | 128-128 | 128 |
| capture 1: body KiB | 981 | 981-981 | 981 |
| capture 1: save KiB | 980 | 980-980 | 980 |
| capture 2: cut | 7.3 | 7.3-7.3 | 7.3 |
| capture 2: publish | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: digest | 3.6 | 3.6-3.6 | 3.6 |
| capture 2: state | 0.2 | 0.2-0.2 | 0.2 |
| capture 2: snapshot | 3.5 | 3.5-3.5 | 3.5 |
| capture 2: serializer setup | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: save | 11.1 | 11.1-11.1 | 11.1 |
| capture 2: save, longest slice | 5.8 | 5.8-5.8 | 5.8 |
| capture 2: save, slices | 4 | 4-4 | 4 |
| capture 2: save copy | 0.1 | 0.1-0.1 | 0.1 |
| capture 2: writer setup | 0.1 | 0.1-0.1 | 0.1 |
| capture 2: compression | 2.8 | 2.8-2.8 | 2.8 |
| capture 2: compression, slices | 2 | 2-2 | 2 |
| capture 2: finish | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: longest frame | 7.3 | 7.3-7.3 | 7.3 |
| capture 2: frames | 7 | 7-7 | 7 |
| capture 2: upload s | 0.017 | 0.017-0.017 | 0.017 |
| capture 2: payload KiB | 149 | 149-149 | 149 |
| capture 2: body KiB | 1107 | 1107-1107 | 1107 |
| capture 2: save KiB | 1107 | 1107-1107 | 1107 |
| joined: process's build | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download s | 0.014 | 0.011-0.015 | 0.014, 0.015, 0.013, 0.011, 0.014 |
| joined: last attempt s | 0.014 | 0.011-0.015 | 0.014, 0.015, 0.013, 0.011, 0.014 |
| joined: payload KiB | 128 | 128-128 | 128, 128, 128, 128, 128 |
| joined: decode | 13.0 | 9.4-15.1 | 9.4, 10.8, 14.2, 15.1, 13.0 |
| joined: check | 75.0 | 73.8-77.2 | 75.3, 73.8, 77.2, 75.0, 74.5 |
| joined: swap | 9.3 | 9.1-10.0 | 9.3, 9.1, 10.0, 9.7, 9.1 |
| joined: build frames | 403 | 387-409 | 403, 403, 409, 387, 409 |
| joined: longest build frame | 384 | 368-428 | 384, 368, 428, 376, 389 |
| joined: catch-up ticks | 701 | 640-709 | 701, 658, 708, 640, 709 |
| joined: catch-up frames | 11 | 10-11 | 11, 10, 11, 10, 11 |
| joined: catch-up s | 0.574 | 0.552-0.600 | 0.596, 0.552, 0.600, 0.553, 0.574 |
| joined: save | 384 | 368-428 | 384, 368, 428, 376, 388 |
| joined: save, longest step | 384 | 368-428 | 384, 368, 428, 376, 388 |
| joined: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: files | 1934 | 1894-1998 | 1930, 1934, 1998, 1894, 1948 |
| joined: files, longest step | 0.7 | 0.5-1.8 | 1.4, 1.8, 0.7, 0.6, 0.5 |
| joined: files, steps | 10730389 | 10371906-11014143 | 10454942, 11014143, 10730389, 10371906, 10888624 |
| joined: files, frames | 371 | 355-377 | 371, 371, 376, 355, 377 |
| joined: catalogue | 78.4 | 78.0-86.5 | 78.3, 78.0, 86.5, 78.5, 78.4 |
| joined: catalogue, longest step | 17.2 | 16.8-19.3 | 17.2, 16.9, 19.3, 17.2, 16.8 |
| joined: catalogue, steps | 90 | 90-90 | 90, 90, 90, 90, 90 |
| joined: catalogue, frames | 5 | 5-6 | 5, 5, 6, 5, 5 |
| joined: manifest | 54.0 | 53.7-54.6 | 53.7, 54.2, 54.0, 54.6, 53.9 |
| joined: manifest, longest step | 6.7 | 6.6-7.1 | 6.7, 6.7, 6.6, 7.1, 6.8 |
| joined: manifest, steps | 92 | 92-92 | 92, 92, 92, 92, 92 |
| joined: manifest, frames | 5 | 5-5 | 5, 5, 5, 5, 5 |
| joined: validation | 43.3 | 43.0-45.2 | 43.0, 43.3, 45.2, 43.3, 43.9 |
| joined: validation, longest step | 26.0 | 25.3-26.9 | 25.3, 25.8, 26.9, 26.0, 26.2 |
| joined: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| joined: validation, frames | 2 | 2-2 | 2, 2, 2, 2, 2 |
| joined: economy | 17.3 | 17.1-18.6 | 17.1, 17.3, 18.6, 17.1, 17.7 |
| joined: economy, longest step | 17.3 | 17.1-18.6 | 17.1, 17.3, 18.6, 17.1, 17.7 |
| joined: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: level | 664 | 650-686 | 667, 650, 686, 657, 664 |
| joined: level, longest step | 283 | 279-298 | 283, 279, 298, 283, 287 |
| joined: level, steps | 598 | 598-598 | 598, 598, 598, 598, 598 |
| joined: level, frames | 20 | 20-20 | 20, 20, 20, 20, 20 |
| joined: hud | 8.2 | 7.8-8.5 | 8.5, 7.8, 8.4, 8.0, 8.2 |
| joined: hud, longest step | 8.2 | 7.8-8.5 | 8.5, 7.8, 8.4, 8.0, 8.2 |
| joined: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor | 25.4 | 24.7-26.0 | 24.7, 24.7, 25.5, 25.4, 26.0 |
| joined: advisor, longest step | 25.4 | 24.7-26.0 | 24.7, 24.7, 25.5, 25.4, 26.0 |
| joined: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: long step, save 1 | 384 | 368-428 | 384, 368, 428, 376, 388 |
| joined: long step, catalogue 1 | 14.6 | 13.9-15.4 | 13.9, 14.1, 15.4, 14.6, 14.6 |
| joined: long step, catalogue 90 | 17.2 | 16.8-19.3 | 17.2, 16.9, 19.3, 17.2, 16.8 |
| joined: long step, validation 13 | 26.0 | 25.3-26.9 | 25.3, 25.8, 26.9, 26.0, 26.2 |
| joined: long step, economy 1 | 17.3 | 17.1-18.6 | 17.1, 17.3, 18.6, 17.1, 17.7 |
| joined: long step, level 82 (to 0.3) | 12.5 | 12.3-12.7 | 12.3, 12.5, 12.4, 12.7, 12.5 |
| joined: long step, level 544 (to 0.4) (3 of 5 runs) | 12.1 | 12.1-12.3 | 12.1, -, 12.1, -, 12.3 |
| joined: long step, level 552 (to 0.4) (3 of 5 runs) | 12.4 | 12.1-12.5 | -, -, 12.5, 12.1, 12.4 |
| joined: long step, level 575 (to 0.8) | 283 | 279-298 | 283, 279, 298, 283, 287 |
| joined: long step, level 576 (to 0.85) | 24.1 | 24.0-25.4 | 24.1, 24.0, 25.4, 24.5, 24.0 |
| joined: long step, level 577 (to 0.85) | 13.2 | 12.7-13.9 | 13.5, 12.7, 13.9, 13.2, 13.1 |
| joined: long step, level 582 (to 0.85) | 12.9 | 12.3-16.5 | 16.5, 12.6, 13.8, 12.9, 12.3 |
| joined: long step, level 590 (to 0.95) | 28.7 | 28.2-29.3 | 28.7, 28.2, 29.1, 28.4, 29.3 |
| joined: long step, level 594 (to 0.95) (1 of 5 runs) | 13.8 | 13.8-13.8 | 13.8, -, -, -, - |
| joined: long step, level 596 (to 0.95) | 13.6 | 13.3-13.6 | 13.6, 13.6, 13.6, 13.4, 13.3 |
| joined: long step, level 597 (to 0.95) | 17.7 | 17.2-19.9 | 19.9, 17.7, 18.6, 17.7, 17.2 |
| joined: long step, level 598 (to 1) | 62.3 | 60.1-64.5 | 61.4, 62.3, 64.5, 60.1, 62.9 |
| joined: long step, advisor 1 | 25.4 | 24.7-26.0 | 24.7, 24.7, 25.5, 25.4, 26.0 |
| joined: section coasters | 4.6 | 4.4-5.2 | 4.6, 4.5, 4.7, 5.2, 4.4 |
| joined: section economy | 0.1 | 0.0-0.2 | 0.2, 0.1, 0.0, 0.1, 0.2 |
| joined: section entities | 596 | 582-617 | 602, 582, 617, 592, 596 |
| joined: section guest-sprites | 13.2 | 12.7-13.9 | 13.5, 12.7, 13.9, 13.2, 13.1 |
| joined: section guests | 85.3 | 83.4-89.8 | 88.9, 83.4, 89.8, 85.3, 84.2 |
| joined: section join-state | 3.7 | 3.7-4.2 | 3.7, 3.7, 4.2, 3.7, 3.9 |
| joined: section level-hud | 5.1 | 4.8-7.4 | 7.4, 4.8, 5.3, 4.8, 5.1 |
| joined: section model-parse | 6.9 | 6.7-7.1 | 6.7, 6.7, 7.1, 7.1, 6.9 |
| joined: section object-catalog | 2.3 | 2.2-2.4 | 2.3, 2.3, 2.3, 2.2, 2.4 |
| joined: section object-hoarding | 15.7 | 15.4-18.0 | 16.0, 15.6, 18.0, 15.4, 15.7 |
| joined: section object-models | 216 | 213-226 | 216, 213, 226, 215, 219 |
| joined: section object-runtime | 25.8 | 25.0-28.3 | 25.8, 25.0, 28.3, 25.7, 26.6 |
| joined: section object-sign | 142 | 141-144 | 141, 141, 144, 142, 143 |
| joined: section objects | 281 | 276-295 | 281, 276, 295, 280, 284 |
| joined: section original-park | 9.1 | 8.8-9.6 | 9.1, 9.0, 9.6, 8.8, 9.1 |
| joined: section paths | 4.1 | 4.0-4.2 | 4.2, 4.1, 4.0, 4.1, 4.2 |
| joined: section pipeline | 8.1 | 7.0-16.5 | 16.5, 7.1, 9.1, 8.1, 7.0 |
| joined: section resume | 53.4 | 49.9-54.5 | 49.9, 53.4, 54.5, 51.2, 53.6 |
| joined: section ride-opcodes | 43.8 | 42.9-45.0 | 43.2, 42.9, 44.8, 43.8, 45.0 |
| joined: section staff-gate-economy | 52.4 | 51.9-59.9 | 59.9, 52.4, 54.6, 52.2, 51.9 |
| joined: section staff-sprites | 13.6 | 13.3-13.6 | 13.6, 13.6, 13.6, 13.4, 13.3 |
| joined: section terrain | 54.5 | 52.9-56.0 | 53.6, 52.9, 54.6, 56.0, 54.5 |
| joined: section texture-create | 20.6 | 20.2-22.9 | 21.3, 20.4, 20.6, 22.9, 20.2 |
| joined: section texture-decode | 64.1 | 63.0-69.4 | 64.1, 63.3, 69.4, 63.0, 65.5 |
| joined: section wad-decompress | 26.0 | 23.0-27.2 | 26.0, 26.2, 23.0, 23.6, 27.2 |
| joined: section wad-index | 6.2 | 5.3-8.3 | 6.8, 5.8, 6.2, 5.3, 8.3 |
| joined: section wad-read | 12.2 | 11.2-15.5 | 15.5, 12.2, 13.3, 11.2, 12.1 |
| joined: longest update frame while building | 397 | 383-446 | 397, 383, 446, 395, 405 |
| joined: the swap's update frame | 316 | 311-330 | 328, 311, 330, 316, 312 |
| joined: first update frame after the swap | 36.1 | 35.6-42.9 | 42.9, 35.6, 36.4, 35.7, 36.1 |
| joined: longest whole frame while building | 398 | 383-447 | 398, 383, 447, 396, 406 |
| joined: the swap's whole frame | 341 | 334-357 | 354, 335, 357, 341, 334 |
| joined: first whole frame after the swap | 39.1 | 37.8-45.2 | 45.2, 37.8, 39.1, 38.0, 42.7 |
| resynced: process's build | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: download s | 0.017 | 0.016-0.018 | 0.016, 0.017, 0.017, 0.017, 0.018 |
| resynced: last attempt s | 0.017 | 0.016-0.018 | 0.016, 0.017, 0.017, 0.017, 0.018 |
| resynced: payload KiB | 149 | 149-149 | 149, 149, 149, 149, 149 |
| resynced: decode | 2.4 | 2.2-4.1 | 2.4, 2.2, 2.8, 2.3, 4.1 |
| resynced: check | 7.7 | 7.6-7.7 | 7.7, 7.6, 7.6, 7.7, 7.7 |
| resynced: swap | 10.4 | 10.2-10.8 | 10.2, 10.2, 10.4, 10.5, 10.8 |
| resynced: build frames | 16 | 14-17 | 14, 16, 17, 17, 14 |
| resynced: longest build frame | 55.3 | 48.1-59.2 | 55.3, 59.2, 49.1, 48.1, 55.4 |
| resynced: catch-up ticks | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up s | 0.117 | 0.107-0.125 | 0.121, 0.117, 0.107, 0.114, 0.125 |
| resynced: save | 42.1 | 39.9-49.1 | 42.8, 39.9, 49.1, 42.1, 40.1 |
| resynced: save, longest step | 42.1 | 39.9-49.1 | 42.8, 39.9, 49.1, 42.1, 40.1 |
| resynced: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catalogue | 10.1 | 10.0-11.1 | 10.7, 10.0, 11.1, 10.0, 10.1 |
| resynced: catalogue, longest step | 10.0 | 10.0-11.0 | 10.7, 10.0, 11.0, 10.0, 10.0 |
| resynced: catalogue, steps | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: catalogue, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached) | 0.1 | 0.0-0.2 | 0.1, 0.0, 0.0, 0.2, 0.1 |
| resynced: manifest (cached), longest step | 0.1 | 0.0-0.2 | 0.1, 0.0, 0.0, 0.2, 0.1 |
| resynced: manifest (cached), steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached), frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: validation | 24.5 | 23.2-25.1 | 24.5, 24.7, 24.3, 25.1, 23.2 |
| resynced: validation, longest step | 17.9 | 17.0-18.6 | 18.0, 17.9, 17.0, 18.6, 17.1 |
| resynced: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| resynced: validation, frames | 3 | 3-3 | 3, 3, 3, 3, 3 |
| resynced: economy | 3.2 | 2.7-4.0 | 3.2, 3.0, 3.3, 4.0, 2.7 |
| resynced: economy, longest step | 3.2 | 2.7-4.0 | 3.2, 3.0, 3.3, 4.0, 2.7 |
| resynced: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: level | 208 | 204-214 | 205, 214, 208, 214, 204 |
| resynced: level, longest step | 48.3 | 47.8-55.2 | 48.3, 47.8, 48.3, 47.8, 55.2 |
| resynced: level, steps | 568 | 568-568 | 568, 568, 568, 568, 568 |
| resynced: level, frames | 12 | 11-13 | 11, 12, 13, 13, 11 |
| resynced: hud | 0.5 | 0.5-0.6 | 0.5, 0.6, 0.5, 0.5, 0.6 |
| resynced: hud, longest step | 0.5 | 0.5-0.6 | 0.5, 0.6, 0.5, 0.5, 0.6 |
| resynced: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor | 4.6 | 4.4-4.8 | 4.6, 4.8, 4.6, 4.8, 4.4 |
| resynced: advisor, longest step | 4.6 | 4.4-4.8 | 4.6, 4.8, 4.6, 4.8, 4.4 |
| resynced: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: long step, save 1 | 42.1 | 39.9-49.1 | 42.8, 39.9, 49.1, 42.1, 40.1 |
| resynced: long step, validation 13 | 17.9 | 17.0-18.6 | 18.0, 17.9, 17.0, 18.6, 17.1 |
| resynced: long step, level 3 (to 0.008) (1 of 5 runs) | 12.2 | 12.2-12.2 | -, -, -, 12.2, - |
| resynced: long step, level 545 (to 0.8) | 48.3 | 47.8-55.2 | 48.3, 47.8, 48.3, 47.8, 55.2 |
| resynced: long step, level 546 (to 0.85) | 19.0 | 18.9-19.5 | 19.0, 19.5, 19.0, 19.0, 18.9 |
| resynced: long step, level 567 (to 0.95) | 13.7 | 12.8-20.1 | 19.8, 13.7, 12.8, 13.1, 20.1 |
| resynced: long step, level 568 (to 1) (3 of 5 runs) | 15.5 | 14.7-16.3 | -, 16.3, 14.7, 15.5, - |
| resynced: section coasters | 0.3 | 0.3-0.4 | 0.4, 0.3, 0.3, 0.3, 0.3 |
| resynced: section economy | 0.0 | 0.0-0.1 | 0.0, 0.0, 0.1, 0.1, 0.0 |
| resynced: section entities | 186 | 183-197 | 197, 186, 183, 185, 191 |
| resynced: section guest-sprites | 2.5 | 2.4-2.9 | 2.4, 2.5, 2.5, 2.4, 2.9 |
| resynced: section guests | 50.2 | 49.5-52.2 | 50.2, 52.2, 49.9, 51.1, 49.5 |
| resynced: section join-state | 0.0 | 0.0-0.1 | 0.1, 0.0, 0.0, 0.0, 0.1 |
| resynced: section level-hud | 2.8 | 2.7-2.9 | 2.8, 2.8, 2.7, 2.8, 2.9 |
| resynced: section model-parse | 9.6 | 2.6-12.2 | 2.9, 11.3, 9.6, 12.2, 2.6 |
| resynced: section object-catalog | 2.2 | 1.8-2.4 | 2.2, 2.0, 2.3, 2.4, 1.8 |
| resynced: section object-hoarding | 2.8 | 2.4-2.9 | 2.8, 2.8, 2.4, 2.7, 2.9 |
| resynced: section object-models | 21.5 | 21.2-22.3 | 21.2, 21.5, 22.3, 21.5, 22.0 |
| resynced: section object-runtime | 5.2 | 4.4-5.5 | 5.5, 5.1, 5.2, 5.5, 4.4 |
| resynced: section object-sign | 0.0 | 0.0-0.1 | 0.0, 0.1, 0.0, 0.1, 0.0 |
| resynced: section objects | 47.9 | 47.5-54.9 | 47.9, 47.5, 48.0, 47.5, 54.9 |
| resynced: section original-park | 12.2 | 4.5-15.0 | 5.0, 13.7, 12.2, 15.0, 4.5 |
| resynced: section paths | 0.2 | 0.2-0.2 | 0.2, 0.2, 0.2, 0.2, 0.2 |
| resynced: section pipeline | 2.5 | 1.9-3.0 | 2.9, 2.5, 2.4, 1.9, 3.0 |
| resynced: section resume | 12.0 | 6.6-13.5 | 6.6, 13.5, 12.0, 12.5, 7.1 |
| resynced: section ride-opcodes | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: section staff-gate-economy | 27.4 | 26.6-29.4 | 29.1, 27.4, 26.6, 27.0, 29.4 |
| resynced: section staff-sprites | 5.2 | 5.1-5.8 | 5.8, 5.1, 5.1, 5.2, 5.8 |
| resynced: section terrain | 17.9 | 16.6-28.4 | 28.4, 17.4, 17.9, 17.9, 16.6 |
| resynced: section texture-create | 11.0 | 9.3-11.4 | 11.4, 11.3, 9.3, 11.0, 10.6 |
| resynced: longest update frame while building | 133 | 127-136 | 135, 136, 127, 133, 133 |
| resynced: the swap's update frame | 133 | 127-136 | 135, 136, 127, 133, 133 |
| resynced: first update frame after the swap | 18.5 | 17.1-19.2 | 19.2, 18.5, 18.7, 18.3, 17.1 |
| resynced: longest whole frame while building | 137 | 129-143 | 137, 138, 129, 136, 143 |
| resynced: the swap's whole frame | 137 | 129-143 | 137, 138, 129, 136, 143 |
| resynced: first whole frame after the swap | 20.2 | 18.6-20.7 | 20.7, 20.2, 20.2, 19.8, 18.6 |
| final check: update frame | 502 | 497-521 | 500, 521, 502, 504, 497 |
| final check: whole frame | 503 | 498-522 | 501, 522, 503, 505, 498 |

### Interpreted, three runs

3 runs; host phase (one host phase, reused by every run): Release build 1.0.0+922fe3dab6bb3f12abba7857d95ebe69351aadc2, .NET 11.0.0-rc.2.26504.105, macOS 26.6.0 Arm64, 10 processors, silent True, advisor True, compiled jit; join phase: Release build 1.0.0+922fe3dab6bb3f12abba7857d95ebe69351aadc2, .NET 11.0.0-rc.2.26504.105, Browser Wasm, 1 processors, silent False, advisor True, compiled interpreter

page: Chrome/155.0.8059.85, ANGLE (Apple, ANGLE Metal Renderer: Apple M5, Unspecified Version), 1280x900, muted True, frame cap 0, pool sprites settled

    Darwin 25.6.0 arm64
    processor Apple M5, 10 cores, model Mac17,2, memory 34359738368 bytes
    source 922fe3dab6bb3f12abba7857d95ebe69351aadc2, clean (the driver's checkout)

| Value | Median | Range | Runs |
|---|---|---|---|
| capture 1: cut | 8.7 | 8.7-8.7 | 8.7 |
| capture 1: publish | 0.2 | 0.2-0.2 | 0.2 |
| capture 1: digest | 3.7 | 3.7-3.7 | 3.7 |
| capture 1: state | 0.8 | 0.8-0.8 | 0.8 |
| capture 1: snapshot | 4.1 | 4.1-4.1 | 4.1 |
| capture 1: serializer setup | 0.0 | 0.0-0.0 | 0.0 |
| capture 1: save | 65.4 | 65.4-65.4 | 65.4 |
| capture 1: save, longest slice | 32.8 | 32.8-32.8 | 32.8 |
| capture 1: save, slices | 12 | 12-12 | 12 |
| capture 1: save copy | 0.2 | 0.2-0.2 | 0.2 |
| capture 1: writer setup | 28.3 | 28.3-28.3 | 28.3 |
| capture 1: compression | 1.3 | 1.3-1.3 | 1.3 |
| capture 1: compression, slices | 1 | 1-1 | 1 |
| capture 1: finish | 0.1 | 0.1-0.1 | 0.1 |
| capture 1: longest frame | 33.0 | 33.0-33.0 | 33.0 |
| capture 1: frames | 14 | 14-14 | 14 |
| capture 1: upload s | 0.002 | 0.002-0.002 | 0.002 |
| capture 1: payload KiB | 128 | 128-128 | 128 |
| capture 1: body KiB | 981 | 981-981 | 981 |
| capture 1: save KiB | 980 | 980-980 | 980 |
| capture 2: cut | 7.1 | 7.1-7.1 | 7.1 |
| capture 2: publish | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: digest | 3.5 | 3.5-3.5 | 3.5 |
| capture 2: state | 0.2 | 0.2-0.2 | 0.2 |
| capture 2: snapshot | 3.3 | 3.3-3.3 | 3.3 |
| capture 2: serializer setup | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: save | 11.1 | 11.1-11.1 | 11.1 |
| capture 2: save, longest slice | 5.3 | 5.3-5.3 | 5.3 |
| capture 2: save, slices | 4 | 4-4 | 4 |
| capture 2: save copy | 0.2 | 0.2-0.2 | 0.2 |
| capture 2: writer setup | 0.1 | 0.1-0.1 | 0.1 |
| capture 2: compression | 1.8 | 1.8-1.8 | 1.8 |
| capture 2: compression, slices | 1 | 1-1 | 1 |
| capture 2: finish | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: longest frame | 7.1 | 7.1-7.1 | 7.1 |
| capture 2: frames | 6 | 6-6 | 6 |
| capture 2: upload s | 0.014 | 0.014-0.014 | 0.014 |
| capture 2: payload KiB | 149 | 149-149 | 149 |
| capture 2: body KiB | 1107 | 1107-1107 | 1107 |
| capture 2: save KiB | 1107 | 1107-1107 | 1107 |
| joined: process's build | 1 | 1-1 | 1, 1, 1 |
| joined: download attempts | 1 | 1-1 | 1, 1, 1 |
| joined: download s | 0.016 | 0.015-0.017 | 0.017, 0.016, 0.015 |
| joined: last attempt s | 0.016 | 0.015-0.017 | 0.017, 0.016, 0.015 |
| joined: payload KiB | 128 | 128-128 | 128, 128, 128 |
| joined: decode | 9.5 | 9.2-12.5 | 12.5, 9.2, 9.5 |
| joined: check | 99.3 | 92.6-110 | 92.6, 99.3, 110 |
| joined: swap | 8.9 | 8.3-10.1 | 8.3, 8.9, 10.1 |
| joined: build frames | 471 | 455-482 | 455, 482, 471 |
| joined: longest build frame | 1365 | 1214-1487 | 1214, 1365, 1487 |
| joined: catch-up ticks | 872 | 819-878 | 819, 878, 872 |
| joined: catch-up frames | 33 | 29-36 | 29, 33, 36 |
| joined: catch-up s | 2.173 | 1.973-2.481 | 1.973, 2.173, 2.481 |
| joined: save | 582 | 466-613 | 466, 613, 582 |
| joined: save, longest step | 582 | 466-613 | 466, 613, 582 |
| joined: save, steps | 1 | 1-1 | 1, 1, 1 |
| joined: save, frames | 1 | 1-1 | 1, 1, 1 |
| joined: files | 2333 | 2286-2416 | 2286, 2416, 2333 |
| joined: files, longest step | 2.0 | 0.9-2.0 | 2.0, 0.9, 2.0 |
| joined: files, steps | 5909103 | 5602539-6808709 | 6808709, 5909103, 5602539 |
| joined: files, frames | 387 | 381-400 | 381, 400, 387 |
| joined: catalogue | 246 | 202-258 | 202, 246, 258 |
| joined: catalogue, longest step | 61.1 | 53.5-80.3 | 53.5, 61.1, 80.3 |
| joined: catalogue, steps | 90 | 90-90 | 90, 90, 90 |
| joined: catalogue, frames | 14 | 12-15 | 12, 15, 14 |
| joined: manifest | 272 | 235-293 | 235, 272, 293 |
| joined: manifest, longest step | 43.8 | 38.2-45.2 | 38.2, 43.8, 45.2 |
| joined: manifest, steps | 92 | 92-92 | 92, 92, 92 |
| joined: manifest, frames | 17 | 15-19 | 15, 17, 19 |
| joined: validation | 481 | 388-482 | 388, 482, 481 |
| joined: validation, longest step | 256 | 204-275 | 204, 275, 256 |
| joined: validation, steps | 17 | 17-17 | 17, 17, 17 |
| joined: validation, frames | 4 | 4-5 | 5, 4, 4 |
| joined: economy | 13.3 | 12.2-16.4 | 12.2, 13.3, 16.4 |
| joined: economy, longest step | 13.3 | 12.2-16.4 | 12.2, 13.3, 16.4 |
| joined: economy, steps | 1 | 1-1 | 1, 1, 1 |
| joined: economy, frames | 1 | 1-1 | 1, 1, 1 |
| joined: level | 2481 | 2202-2694 | 2202, 2481, 2694 |
| joined: level, longest step | 1361 | 1214-1482 | 1214, 1361, 1482 |
| joined: level, steps | 598 | 598-598 | 598, 598, 598 |
| joined: level, frames | 46 | 42-47 | 42, 46, 47 |
| joined: hud | 11.2 | 10.1-13.2 | 10.1, 11.2, 13.2 |
| joined: hud, longest step | 11.2 | 10.1-13.2 | 10.1, 11.2, 13.2 |
| joined: hud, steps | 1 | 1-1 | 1, 1, 1 |
| joined: hud, frames | 1 | 1-1 | 1, 1, 1 |
| joined: advisor | 24.8 | 21.8-27.1 | 21.8, 24.8, 27.1 |
| joined: advisor, longest step | 24.8 | 21.8-27.1 | 21.8, 24.8, 27.1 |
| joined: advisor, steps | 1 | 1-1 | 1, 1, 1 |
| joined: advisor, frames | 1 | 1-1 | 1, 1, 1 |
| joined: long step, save 1 | 582 | 466-613 | 466, 613, 582 |
| joined: long step, catalogue 1 | 16.7 | 14.5-24.7 | 14.5, 16.7, 24.7 |
| joined: long step, catalogue 90 | 61.1 | 53.5-80.3 | 53.5, 61.1, 80.3 |
| joined: long step, manifest 49 (2 of 3 runs) | 12.8 | 12.6-13.1 | -, 12.6, 13.1 |
| joined: long step, manifest 54 (2 of 3 runs) | 13.1 | 13.1-13.2 | -, 13.2, 13.1 |
| joined: long step, manifest 91 | 43.8 | 38.2-45.2 | 38.2, 43.8, 45.2 |
| joined: long step, validation 12 | 256 | 204-275 | 204, 275, 256 |
| joined: long step, validation 13 | 178 | 152-190 | 152, 178, 190 |
| joined: long step, economy 1 | 13.3 | 12.2-16.4 | 12.2, 13.3, 16.4 |
| joined: long step, level 3 (to 0.008) | 17.0 | 15.2-18.3 | 15.2, 17.0, 18.3 |
| joined: long step, level 82 (to 0.3) | 35.7 | 32.4-39.1 | 32.4, 35.7, 39.1 |
| joined: long step, level 83 (to 0.3) | 18.0 | 15.4-18.1 | 15.4, 18.0, 18.1 |
| joined: long step, level 532 (to 0.35) | 25.6 | 23.1-26.9 | 23.1, 25.6, 26.9 |
| joined: long step, level 534 (to 0.35) | 25.4 | 22.6-28.0 | 22.6, 25.4, 28.0 |
| joined: long step, level 535 (to 0.35) | 21.4 | 19.5-23.9 | 19.5, 21.4, 23.9 |
| joined: long step, level 536 (to 0.35) | 24.1 | 22.2-27.0 | 22.2, 24.1, 27.0 |
| joined: long step, level 540 (to 0.35) | 18.2 | 16.6-20.4 | 16.6, 18.2, 20.4 |
| joined: long step, level 541 (to 0.35) (1 of 3 runs) | 13.0 | 13.0-13.0 | -, -, 13.0 |
| joined: long step, level 543 (to 0.4) (1 of 3 runs) | 12.4 | 12.4-12.4 | -, -, 12.4 |
| joined: long step, level 544 (to 0.4) | 14.8 | 12.6-16.0 | 12.6, 14.8, 16.0 |
| joined: long step, level 575 (to 0.8) | 1361 | 1214-1482 | 1214, 1361, 1482 |
| joined: long step, level 576 (to 0.85) | 103 | 90.3-113 | 90.3, 103, 113 |
| joined: long step, level 577 (to 0.85) | 53.4 | 44.4-54.3 | 44.4, 54.3, 53.4 |
| joined: long step, level 578 (to 0.85) | 15.2 | 13.6-16.3 | 13.6, 15.2, 16.3 |
| joined: long step, level 582 (to 0.85) | 19.4 | 16.9-23.8 | 16.9, 23.8, 19.4 |
| joined: long step, level 584 (to 0.85) | 14.4 | 12.3-14.7 | 12.3, 14.4, 14.7 |
| joined: long step, level 587 (to 0.85) (2 of 3 runs) | 13.9 | 13.3-14.6 | -, 14.6, 13.3 |
| joined: long step, level 588 (to 0.85) | 24.5 | 21.2-25.4 | 21.2, 24.5, 25.4 |
| joined: long step, level 590 (to 0.95) | 102 | 88.0-108 | 88.0, 102, 108 |
| joined: long step, level 594 (to 0.95) | 18.2 | 17.1-20.6 | 17.1, 18.2, 20.6 |
| joined: long step, level 596 (to 0.95) | 53.1 | 50.9-60.1 | 50.9, 53.1, 60.1 |
| joined: long step, level 597 (to 0.95) | 33.0 | 29.9-38.3 | 29.9, 38.3, 33.0 |
| joined: long step, level 598 (to 1) | 84.1 | 75.0-107 | 75.0, 84.1, 107 |
| joined: long step, hud 1 (1 of 3 runs) | 13.2 | 13.2-13.2 | -, -, 13.2 |
| joined: long step, advisor 1 | 24.8 | 21.8-27.1 | 21.8, 24.8, 27.1 |
| joined: section coasters | 7.9 | 7.0-8.5 | 7.0, 7.9, 8.5 |
| joined: section economy | 0.2 | 0.1-0.2 | 0.2, 0.1, 0.2 |
| joined: section entities | 2375 | 2117-2573 | 2117, 2375, 2573 |
| joined: section guest-sprites | 48.9 | 40.9-50.2 | 40.9, 50.2, 48.9 |
| joined: section guests | 262 | 229-277 | 229, 262, 277 |
| joined: section join-state | 1.1 | 1.0-1.3 | 1.1, 1.0, 1.3 |
| joined: section level-hud | 7.3 | 6.6-8.1 | 6.6, 7.3, 8.1 |
| joined: section model-parse | 31.2 | 27.7-34.0 | 27.7, 31.2, 34.0 |
| joined: section object-catalog | 10.8 | 9.5-11.4 | 9.5, 10.8, 11.4 |
| joined: section object-hoarding | 20.6 | 17.8-22.6 | 17.8, 20.6, 22.6 |
| joined: section object-models | 1186 | 1060-1290 | 1060, 1186, 1290 |
| joined: section object-runtime | 55.3 | 48.5-60.9 | 48.5, 55.3, 60.9 |
| joined: section object-sign | 791 | 711-856 | 711, 791, 856 |
| joined: section objects | 1356 | 1210-1477 | 1210, 1356, 1477 |
| joined: section original-park | 30.6 | 27.0-32.4 | 27.0, 30.6, 32.4 |
| joined: section paths | 4.0 | 3.5-4.1 | 3.5, 4.1, 4.0 |
| joined: section pipeline | 16.1 | 14.6-16.2 | 14.6, 16.2, 16.1 |
| joined: section resume | 75.3 | 67.0-97.2 | 67.0, 75.3, 97.2 |
| joined: section ride-opcodes | 65.8 | 57.6-70.4 | 57.6, 65.8, 70.4 |
| joined: section staff-gate-economy | 116 | 108-124 | 108, 116, 124 |
| joined: section staff-sprites | 53.1 | 50.9-60.1 | 50.9, 53.1, 60.1 |
| joined: section terrain | 270 | 240-290 | 240, 270, 290 |
| joined: section texture-create | 44.1 | 38.9-46.3 | 38.9, 46.3, 44.1 |
| joined: section texture-decode | 528 | 471-576 | 471, 528, 576 |
| joined: section wad-decompress | 122 | 109-136 | 109, 122, 136 |
| joined: section wad-index | 18.2 | 15.2-19.1 | 15.2, 19.1, 18.2 |
| joined: section wad-read | 31.6 | 25.0-33.4 | 25.0, 33.4, 31.6 |
| joined: longest update frame while building | 1367 | 1215-1488 | 1215, 1367, 1488 |
| joined: the swap's update frame | 607 | 575-684 | 575, 607, 684 |
| joined: first update frame after the swap | 94.1 | 92.4-121 | 92.4, 94.1, 121 |
| joined: longest whole frame while building | 1368 | 1216-1490 | 1216, 1368, 1490 |
| joined: the swap's whole frame | 658 | 629-752 | 629, 658, 752 |
| joined: first whole frame after the swap | 98.8 | 96.7-126 | 96.7, 98.8, 126 |
| resynced: process's build | 2 | 2-2 | 2, 2, 2 |
| resynced: download attempts | 1 | 1-1 | 1, 1, 1 |
| resynced: download s | 0.017 | 0.016-0.017 | 0.016, 0.017, 0.017 |
| resynced: last attempt s | 0.017 | 0.016-0.017 | 0.016, 0.017, 0.017 |
| resynced: payload KiB | 149 | 149-149 | 149, 149, 149 |
| resynced: decode | 3.4 | 3.3-3.9 | 3.4, 3.3, 3.9 |
| resynced: check | 34.0 | 28.0-37.6 | 28.0, 37.6, 34.0 |
| resynced: swap | 30.0 | 27.5-33.8 | 27.5, 33.8, 30.0 |
| resynced: build frames | 27 | 27-27 | 27, 27, 27 |
| resynced: longest build frame | 186 | 185-199 | 186, 199, 185 |
| resynced: catch-up ticks | 1 | 1-1 | 1, 1, 1 |
| resynced: catch-up frames | 1 | 1-1 | 1, 1, 1 |
| resynced: catch-up s | 0.272 | 0.251-0.277 | 0.251, 0.277, 0.272 |
| resynced: save | 89.7 | 88.5-91.7 | 91.7, 89.7, 88.5 |
| resynced: save, longest step | 89.7 | 88.5-91.7 | 91.7, 89.7, 88.5 |
| resynced: save, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: save, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: files, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: files, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: catalogue | 35.8 | 33.9-35.8 | 35.8, 35.8, 33.9 |
| resynced: catalogue, longest step | 35.8 | 33.9-35.8 | 35.8, 35.8, 33.9 |
| resynced: catalogue, steps | 2 | 2-2 | 2, 2, 2 |
| resynced: catalogue, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: manifest (cached) | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: manifest (cached), longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: manifest (cached), steps | 1 | 1-1 | 1, 1, 1 |
| resynced: manifest (cached), frames | 1 | 1-1 | 1, 1, 1 |
| resynced: validation | 126 | 121-132 | 126, 132, 121 |
| resynced: validation, longest step | 78.7 | 76.9-84.0 | 78.7, 84.0, 76.9 |
| resynced: validation, steps | 17 | 17-17 | 17, 17, 17 |
| resynced: validation, frames | 4 | 4-4 | 4, 4, 4 |
| resynced: economy | 2.6 | 2.4-2.7 | 2.7, 2.4, 2.6 |
| resynced: economy, longest step | 2.6 | 2.4-2.7 | 2.7, 2.4, 2.6 |
| resynced: economy, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: economy, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: level | 700 | 699-727 | 699, 727, 700 |
| resynced: level, longest step | 176 | 176-189 | 176, 189, 176 |
| resynced: level, steps | 568 | 568-568 | 568, 568, 568 |
| resynced: level, frames | 21 | 21-21 | 21, 21, 21 |
| resynced: hud | 1.2 | 1.1-1.3 | 1.2, 1.1, 1.3 |
| resynced: hud, longest step | 1.2 | 1.1-1.3 | 1.2, 1.1, 1.3 |
| resynced: hud, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: hud, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: advisor | 5.3 | 5.0-10.8 | 10.8, 5.0, 5.3 |
| resynced: advisor, longest step | 5.3 | 5.0-10.8 | 10.8, 5.0, 5.3 |
| resynced: advisor, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: advisor, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: long step, save 1 | 89.7 | 88.5-91.7 | 91.7, 89.7, 88.5 |
| resynced: long step, catalogue 2 | 35.8 | 33.9-35.8 | 35.8, 35.8, 33.9 |
| resynced: long step, validation 7 | 21.1 | 19.5-21.5 | 21.5, 21.1, 19.5 |
| resynced: long step, validation 13 | 78.7 | 76.9-84.0 | 78.7, 84.0, 76.9 |
| resynced: long step, level 3 (to 0.008) | 15.7 | 15.7-16.4 | 15.7, 16.4, 15.7 |
| resynced: long step, level 82 (to 0.3) | 19.0 | 18.6-19.7 | 19.0, 19.7, 18.6 |
| resynced: long step, level 532 (to 0.35) | 19.9 | 19.0-20.3 | 19.9, 20.3, 19.0 |
| resynced: long step, level 534 (to 0.35) | 23.1 | 22.9-24.2 | 23.1, 24.2, 22.9 |
| resynced: long step, level 535 (to 0.35) | 19.8 | 19.3-21.1 | 19.3, 21.1, 19.8 |
| resynced: long step, level 536 (to 0.35) | 22.5 | 22.0-24.2 | 22.5, 24.2, 22.0 |
| resynced: long step, level 540 (to 0.35) | 16.6 | 16.5-17.9 | 16.6, 17.9, 16.5 |
| resynced: long step, level 545 (to 0.8) | 176 | 176-189 | 176, 189, 176 |
| resynced: long step, level 546 (to 0.85) | 86.5 | 85.0-92.4 | 86.5, 92.4, 85.0 |
| resynced: long step, level 548 (to 0.85) | 12.1 | 12.1-12.8 | 12.1, 12.8, 12.1 |
| resynced: long step, level 554 (to 0.85) (1 of 3 runs) | 12.1 | 12.1-12.1 | -, -, 12.1 |
| resynced: long step, level 558 (to 0.85) | 15.4 | 15.2-21.6 | 21.6, 15.4, 15.2 |
| resynced: long step, level 560 (to 0.95) | 34.6 | 28.5-37.0 | 28.5, 37.0, 34.6 |
| resynced: long step, level 566 (to 0.95) | 16.3 | 15.6-16.4 | 15.6, 16.4, 16.3 |
| resynced: long step, level 567 (to 0.95) | 30.6 | 30.0-35.1 | 30.0, 30.6, 35.1 |
| resynced: long step, level 568 (to 1) | 39.4 | 34.2-42.1 | 39.4, 34.2, 42.1 |
| resynced: section coasters | 2.9 | 2.7-3.1 | 2.9, 3.1, 2.7 |
| resynced: section economy | 0.0 | 0.0-0.1 | 0.1, 0.0, 0.0 |
| resynced: section entities | 647 | 640-674 | 647, 674, 640 |
| resynced: section guest-sprites | 7.4 | 7.4-7.6 | 7.4, 7.4, 7.6 |
| resynced: section guests | 162 | 155-163 | 162, 163, 155 |
| resynced: section join-state | 0.0 | 0.0-0.1 | 0.0, 0.0, 0.1 |
| resynced: section level-hud | 5.0 | 4.9-12.1 | 4.9, 5.0, 12.1 |
| resynced: section model-parse | 16.0 | 15.8-16.6 | 16.0, 16.6, 15.8 |
| resynced: section object-catalog | 7.9 | 7.4-8.1 | 7.9, 8.1, 7.4 |
| resynced: section object-hoarding | 5.2 | 4.8-5.3 | 4.8, 5.3, 5.2 |
| resynced: section object-models | 65.9 | 64.5-66.0 | 64.5, 65.9, 66.0 |
| resynced: section object-runtime | 23.6 | 23.3-23.7 | 23.6, 23.7, 23.3 |
| resynced: section object-sign | 0.2 | 0.0-0.2 | 0.2, 0.0, 0.2 |
| resynced: section objects | 173 | 173-186 | 173, 186, 173 |
| resynced: section original-park | 24.0 | 23.4-24.7 | 24.0, 24.7, 23.4 |
| resynced: section paths | 0.2 | 0.1-0.2 | 0.1, 0.2, 0.2 |
| resynced: section pipeline | 10.9 | 10.0-13.2 | 10.0, 10.9, 13.2 |
| resynced: section resume | 29.9 | 29.2-34.4 | 34.4, 29.2, 29.9 |
| resynced: section ride-opcodes | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: section staff-gate-economy | 55.7 | 55.0-60.3 | 55.0, 55.7, 60.3 |
| resynced: section staff-sprites | 16.3 | 15.6-16.4 | 15.6, 16.4, 16.3 |
| resynced: section terrain | 52.2 | 51.1-53.4 | 52.2, 53.4, 51.1 |
| resynced: section texture-create | 14.9 | 14.4-16.6 | 14.9, 14.4, 16.6 |
| resynced: longest update frame while building | 334 | 309-347 | 309, 347, 334 |
| resynced: the swap's update frame | 334 | 309-347 | 309, 347, 334 |
| resynced: first update frame after the swap | 53.0 | 50.4-54.7 | 54.7, 50.4, 53.0 |
| resynced: longest whole frame while building | 340 | 316-353 | 316, 353, 340 |
| resynced: the swap's whole frame | 340 | 316-353 | 316, 353, 340 |
| resynced: first whole frame after the swap | 56.1 | 53.3-58.5 | 58.5, 53.3, 56.1 |
| final check: update frame | 1568 | 1558-1731 | 1558, 1731, 1568 |
| final check: whole frame | 1570 | 1560-1733 | 1560, 1733, 1570 |

## Where a join's slice ends (C2c-2b-3a)

The wait for the level's files asks once a frame, a step that announces a long one ends the slice, the save codec's
metadata is made in steps (the stage `codec`), and the swaps' frames carry their marks.

### The desktop, five runs

5 runs; Release build 1.0.0+864ec4dc90cc4c8328a33f692bed6705501796de, .NET 11.0.0-rc.2.26504.105, macOS 26.6.0 Arm64, 10 processors, silent True, advisor True, compiled jit

    Darwin 25.6.0 arm64
    processor Apple M5, model Mac17,2, memory 34359738368 bytes
    source 864ec4dc90cc4c8328a33f692bed6705501796de, clean

| Value | Median | Range | Runs |
|---|---|---|---|
| capture 1: cut | 9.7 | 9.5-10.8 | 10.8, 9.5, 10.2, 9.5, 9.7 |
| capture 1: publish | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| capture 1: digest | 4.0 | 3.7-4.3 | 4.0, 4.0, 4.3, 3.7, 4.0 |
| capture 1: state | 1.0 | 0.8-1.1 | 1.1, 0.8, 1.0, 1.0, 0.9 |
| capture 1: snapshot | 4.7 | 4.5-5.6 | 5.6, 4.5, 4.7, 4.7, 4.7 |
| capture 1: serializer setup | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| capture 1: save | 66.0 | 64.3-75.3 | 75.3, 66.0, 64.3, 65.3, 67.5 |
| capture 1: save, longest slice | 32.2 | 32.1-40.5 | 40.5, 32.2, 32.1, 32.2, 33.1 |
| capture 1: save, slices | 12 | 11-13 | 13, 12, 11, 12, 12 |
| capture 1: save copy | 0.2 | 0.2-0.3 | 0.2, 0.3, 0.2, 0.2, 0.2 |
| capture 1: writer setup | 1.4 | 1.1-1.6 | 1.1, 1.6, 1.4, 1.4, 1.4 |
| capture 1: compression | 1.6 | 1.3-1.7 | 1.3, 1.6, 1.4, 1.7, 1.6 |
| capture 1: compression, slices | 1 | 1-1 | 1, 1, 1, 1, 1 |
| capture 1: finish | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| capture 1: longest frame | 32.6 | 32.3-41.0 | 41.0, 32.6, 32.3, 32.5, 33.4 |
| capture 1: frames | 14 | 13-15 | 15, 14, 13, 14, 14 |
| capture 1: upload s | 0.015 | 0.015-0.017 | 0.017, 0.015, 0.015, 0.015, 0.016 |
| capture 1: payload KiB | 128 | 128-128 | 128, 128, 128, 128, 128 |
| capture 1: body KiB | 981 | 981-981 | 981, 981, 981, 981, 981 |
| capture 1: save KiB | 980 | 980-980 | 980, 980, 980, 980, 980 |
| capture 2: cut | 5.9 | 5.6-8.3 | 6.7, 5.9, 5.7, 5.6, 8.3 |
| capture 2: publish | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| capture 2: digest | 3.1 | 2.6-4.2 | 3.2, 2.8, 3.1, 2.6, 4.2 |
| capture 2: state | 0.2 | 0.1-0.3 | 0.3, 0.1, 0.2, 0.2, 0.2 |
| capture 2: snapshot | 2.9 | 2.4-3.8 | 3.2, 2.9, 2.4, 2.7, 3.8 |
| capture 2: serializer setup | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| capture 2: save | 10.5 | 7.7-11.7 | 10.5, 11.7, 7.7, 9.3, 11.3 |
| capture 2: save, longest slice | 4.7 | 3.7-6.2 | 5.5, 4.7, 3.7, 4.2, 6.2 |
| capture 2: save, slices | 4 | 3-4 | 4, 4, 3, 4, 4 |
| capture 2: save copy | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| capture 2: writer setup | 0.1 | 0.1-0.2 | 0.2, 0.1, 0.2, 0.1, 0.1 |
| capture 2: compression | 1.6 | 1.3-1.7 | 1.7, 1.6, 1.3, 1.3, 1.7 |
| capture 2: compression, slices | 1 | 1-1 | 1, 1, 1, 1, 1 |
| capture 2: finish | 0.0 | 0.0-0.1 | 0.1, 0.0, 0.0, 0.0, 0.0 |
| capture 2: longest frame | 5.9 | 5.6-8.3 | 6.7, 5.9, 5.7, 5.6, 8.3 |
| capture 2: frames | 6 | 5-6 | 6, 6, 5, 6, 6 |
| capture 2: upload s | 0.015 | 0.015-0.016 | 0.016, 0.015, 0.015, 0.016, 0.015 |
| capture 2: payload KiB | 149 | 149-149 | 149, 149, 149, 149, 149 |
| capture 2: body KiB | 1107 | 1107-1107 | 1107, 1107, 1107, 1107, 1107 |
| capture 2: save KiB | 1107 | 1107-1107 | 1107, 1107, 1107, 1107, 1107 |
| joined: process's build | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download s | 0.015 | 0.015-0.016 | 0.015, 0.015, 0.016, 0.015, 0.015 |
| joined: last attempt s | 0.015 | 0.015-0.016 | 0.015, 0.015, 0.016, 0.015, 0.015 |
| joined: payload KiB | 128 | 128-128 | 128, 128, 128, 128, 128 |
| joined: decode | 4.5 | 3.8-6.7 | 6.7, 4.3, 4.5, 5.0, 3.8 |
| joined: check | 39.9 | 37.5-44.1 | 39.2, 41.1, 44.1, 39.9, 37.5 |
| joined: swap | 10.4 | 7.4-13.4 | 7.5, 10.6, 10.4, 13.4, 7.4 |
| joined: build frames | 42 | 40-43 | 42, 40, 43, 42, 40 |
| joined: longest build frame | 359 | 338-364 | 339, 360, 364, 359, 338 |
| joined: catch-up ticks | 8 | 8-8 | 8, 8, 8, 8, 8 |
| joined: catch-up frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: catch-up s | 0.335 | 0.271-0.369 | 0.335, 0.271, 0.369, 0.336, 0.325 |
| joined: codec | 69.3 | 62.5-74.0 | 74.0, 68.8, 70.8, 69.3, 62.5 |
| joined: codec, longest step | 5.0 | 4.7-5.6 | 5.6, 5.5, 4.7, 4.9, 5.0 |
| joined: codec, steps | 230 | 230-230 | 230, 230, 230, 230, 230 |
| joined: codec, frames | 6 | 6-7 | 7, 6, 6, 6, 6 |
| joined: save | 83.3 | 78.6-83.6 | 83.1, 83.6, 83.3, 83.5, 78.6 |
| joined: save, longest step | 83.3 | 78.6-83.6 | 83.1, 83.6, 83.3, 83.5, 78.6 |
| joined: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| joined: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| joined: files, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: files, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: catalogue | 77.5 | 69.8-80.8 | 77.5, 72.5, 77.5, 80.8, 69.8 |
| joined: catalogue, longest step | 16.9 | 15.5-19.9 | 15.9, 19.9, 16.9, 17.2, 15.5 |
| joined: catalogue, steps | 90 | 90-90 | 90, 90, 90, 90, 90 |
| joined: catalogue, frames | 5 | 5-5 | 5, 5, 5, 5, 5 |
| joined: manifest | 38.8 | 37.3-44.9 | 38.8, 37.8, 44.6, 44.9, 37.3 |
| joined: manifest, longest step | 2.9 | 1.9-3.1 | 2.7, 1.9, 3.1, 3.0, 2.9 |
| joined: manifest, steps | 92 | 92-92 | 92, 92, 92, 92, 92 |
| joined: manifest, frames | 4 | 4-4 | 4, 4, 4, 4, 4 |
| joined: validation | 30.8 | 29.8-32.0 | 29.8, 30.8, 30.8, 30.3, 32.0 |
| joined: validation, longest step | 17.7 | 17.3-20.5 | 17.3, 18.0, 17.6, 17.7, 20.5 |
| joined: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| joined: validation, frames | 2 | 2-3 | 2, 2, 3, 3, 2 |
| joined: economy | 14.9 | 14.1-15.3 | 14.9, 15.3, 14.8, 15.1, 14.1 |
| joined: economy, longest step | 14.9 | 14.1-15.3 | 14.9, 15.3, 14.8, 15.1, 14.1 |
| joined: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: level | 767 | 727-802 | 749, 767, 802, 793, 727 |
| joined: level, longest step | 352 | 333-357 | 339, 352, 357, 352, 333 |
| joined: level, steps | 594 | 594-594 | 594, 594, 594, 594, 594 |
| joined: level, frames | 24 | 23-25 | 24, 23, 25, 24, 23 |
| joined: hud | 10.3 | 9.7-11.5 | 10.2, 10.6, 11.5, 10.3, 9.7 |
| joined: hud, longest step | 10.3 | 9.7-11.5 | 10.2, 10.6, 11.5, 10.3, 9.7 |
| joined: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor | 22.3 | 20.8-24.9 | 21.5, 22.3, 24.9, 22.3, 20.8 |
| joined: advisor, longest step | 22.3 | 20.8-24.9 | 21.5, 22.3, 24.9, 22.3, 20.8 |
| joined: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: long step, save 1 | 83.3 | 78.6-83.6 | 83.1, 83.6, 83.3, 83.5, 78.6 |
| joined: long step, catalogue 90 | 16.9 | 15.5-19.9 | 15.9, 19.9, 16.9, 17.2, 15.5 |
| joined: long step, validation 13 | 17.7 | 17.3-20.5 | 17.3, 18.0, 17.6, 17.7, 20.5 |
| joined: long step, economy 1 | 14.9 | 14.1-15.3 | 14.9, 15.3, 14.8, 15.1, 14.1 |
| joined: long step, level 532 (to 0.35) | 18.5 | 17.7-18.8 | 18.1, 18.8, 18.5, 18.6, 17.7 |
| joined: long step, level 571 (to 0.8) | 352 | 333-357 | 339, 352, 357, 352, 333 |
| joined: long step, level 572 (to 0.85) | 18.8 | 17.2-21.4 | 18.7, 18.8, 21.4, 21.0, 17.2 |
| joined: long step, level 573 (to 0.85) (4 of 5 runs) | 12.6 | 12.5-13.9 | 12.6, 12.6, 13.9, -, 12.5 |
| joined: long step, level 574 (to 0.85) (2 of 5 runs) | 13.9 | 13.8-13.9 | -, -, 13.9, 13.8, - |
| joined: long step, level 576 (to 0.85) | 29.7 | 28.9-36.2 | 29.0, 29.7, 35.5, 36.2, 28.9 |
| joined: long step, level 586 (to 0.95) | 41.0 | 40.0-42.1 | 41.0, 42.1, 41.2, 40.0, 40.5 |
| joined: long step, level 592 (to 0.95) | 13.0 | 12.8-13.6 | 12.9, 13.0, 13.6, 13.1, 12.8 |
| joined: long step, level 593 (to 0.95) | 18.0 | 16.8-19.2 | 16.8, 18.0, 19.2, 18.0, 17.1 |
| joined: long step, level 594 (to 1) | 62.2 | 57.6-69.6 | 62.0, 62.2, 69.6, 65.3, 57.6 |
| joined: long step, advisor 1 | 22.3 | 20.8-24.9 | 21.5, 22.3, 24.9, 22.3, 20.8 |
| joined: section coasters | 5.1 | 5.0-5.3 | 5.0, 5.3, 5.1, 5.2, 5.0 |
| joined: section economy | 0.3 | 0.3-0.4 | 0.3, 0.4, 0.3, 0.3, 0.3 |
| joined: section entities | 695 | 659-722 | 675, 695, 722, 717, 659 |
| joined: section guest-sprites | 12.6 | 10.2-13.9 | 12.6, 12.6, 13.9, 10.2, 12.5 |
| joined: section guests | 106 | 99.9-121 | 104, 106, 121, 118, 99.9 |
| joined: section join-state | 2.2 | 2.2-2.4 | 2.2, 2.4, 2.4, 2.2, 2.2 |
| joined: section level-hud | 3.6 | 3.0-9.0 | 3.0, 3.2, 9.0, 7.7, 3.6 |
| joined: section model-parse | 37.7 | 37.0-39.3 | 37.7, 39.3, 37.6, 37.9, 37.0 |
| joined: section object-catalog | 3.5 | 3.1-5.5 | 5.5, 3.4, 3.9, 3.5, 3.1 |
| joined: section object-hoarding | 9.1 | 8.6-12.6 | 9.0, 9.1, 12.6, 9.3, 8.6 |
| joined: section object-models | 270 | 256-272 | 259, 270, 272, 270, 256 |
| joined: section object-runtime | 47.6 | 45.2-48.2 | 46.9, 48.2, 47.6, 47.7, 45.2 |
| joined: section object-sign | 187 | 182-192 | 184, 192, 187, 189, 182 |
| joined: section objects | 349 | 330-355 | 336, 350, 355, 349, 330 |
| joined: section original-park | 11.1 | 11.0-13.1 | 13.1, 11.0, 11.1, 11.4, 11.0 |
| joined: section paths | 3.8 | 3.6-3.9 | 3.9, 3.9, 3.8, 3.7, 3.6 |
| joined: section pipeline | 3.2 | 2.8-3.4 | 3.0, 3.2, 3.4, 3.2, 2.8 |
| joined: section resume | 56.0 | 51.2-57.6 | 56.2, 56.0, 57.6, 54.8, 51.2 |
| joined: section ride-opcodes | 41.6 | 39.3-43.2 | 39.5, 41.7, 41.6, 43.2, 39.3 |
| joined: section save-json | 44.5 | 42.8-45.1 | 44.5, 44.5, 44.8, 45.1, 42.8 |
| joined: section save-mods | 1.0 | 0.9-1.0 | 1.0, 1.0, 1.0, 0.9, 0.9 |
| joined: section save-shape | 37.1 | 34.5-37.6 | 37.1, 37.6, 37.1, 36.9, 34.5 |
| joined: section shader-native | 0.4 | 0.4-0.5 | 0.4, 0.4, 0.5, 0.4, 0.4 |
| joined: section shader-spirv | 1.1 | 1.0-6.1 | 1.1, 1.0, 6.1, 4.7, 1.0 |
| joined: section staff-gate-economy | 45.4 | 43.3-47.6 | 43.3, 45.4, 47.6, 45.4, 43.8 |
| joined: section staff-sprites | 13.0 | 12.8-13.6 | 12.9, 13.0, 13.6, 13.1, 12.8 |
| joined: section terrain | 45.0 | 43.5-50.5 | 44.1, 45.0, 45.6, 50.5, 43.5 |
| joined: section texture-create | 23.1 | 22.0-23.6 | 22.2, 23.1, 23.5, 23.6, 22.0 |
| joined: section texture-decode | 55.3 | 49.5-57.8 | 55.3, 52.2, 57.8, 56.9, 49.5 |
| joined: section wad-decompress | 17.8 | 17.2-18.0 | 17.5, 18.0, 18.0, 17.8, 17.2 |
| joined: section wad-index | 10.0 | 5.5-12.8 | 5.5, 12.8, 10.9, 10.0, 9.0 |
| joined: section wad-read | 3.5 | 2.9-3.8 | 3.2, 3.8, 3.5, 2.9, 3.6 |
| joined: longest update frame while building | 359 | 341-384 | 352, 360, 384, 359, 341 |
| joined: the swap's update frame | 352 | 293-384 | 352, 293, 384, 358, 341 |
| joined: first update frame after the swap | 30.1 | 29.3-32.1 | 30.1, 29.3, 31.0, 32.1, 29.5 |
| resynced: process's build | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: download s | 0.017 | 0.016-0.017 | 0.017, 0.016, 0.016, 0.017, 0.017 |
| resynced: last attempt s | 0.017 | 0.016-0.017 | 0.017, 0.016, 0.016, 0.017, 0.017 |
| resynced: payload KiB | 149 | 149-149 | 149, 149, 149, 149, 149 |
| resynced: decode | 1.8 | 1.2-2.4 | 2.3, 1.2, 1.8, 1.6, 2.4 |
| resynced: check | 2.0 | 1.9-2.2 | 1.9, 2.1, 2.2, 2.0, 2.0 |
| resynced: swap | 7.6 | 6.8-10.4 | 7.2, 7.7, 10.4, 7.6, 6.8 |
| resynced: build frames | 17 | 17-18 | 17, 17, 18, 17, 18 |
| resynced: longest build frame | 33.8 | 29.3-39.5 | 39.5, 33.8, 29.3, 38.8, 33.4 |
| resynced: catch-up ticks | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up s | 0.050 | 0.042-0.051 | 0.045, 0.050, 0.050, 0.051, 0.042 |
| resynced: codec | 0.7 | 0.5-1.2 | 0.9, 0.7, 0.5, 0.5, 1.2 |
| resynced: codec, longest step | 0.1 | 0.0-0.4 | 0.1, 0.4, 0.0, 0.0, 0.1 |
| resynced: codec, steps | 230 | 230-230 | 230, 230, 230, 230, 230 |
| resynced: codec, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: save | 20.7 | 14.8-27.8 | 21.8, 14.8, 20.7, 18.8, 27.8 |
| resynced: save, longest step | 20.7 | 14.8-27.8 | 21.8, 14.8, 20.7, 18.8, 27.8 |
| resynced: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catalogue | 11.6 | 9.9-19.2 | 10.4, 9.9, 19.2, 12.1, 11.6 |
| resynced: catalogue, longest step | 11.6 | 9.8-19.2 | 10.3, 9.8, 19.2, 12.0, 11.6 |
| resynced: catalogue, steps | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: catalogue, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached) | 0.1 | 0.0-0.1 | 0.1, 0.1, 0.1, 0.1, 0.0 |
| resynced: manifest (cached), longest step | 0.1 | 0.0-0.1 | 0.1, 0.1, 0.1, 0.1, 0.0 |
| resynced: manifest (cached), steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached), frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: validation | 10.2 | 8.8-12.3 | 11.9, 8.8, 10.2, 9.4, 12.3 |
| resynced: validation, longest step | 5.5 | 5.0-6.2 | 5.7, 5.0, 5.5, 5.2, 6.2 |
| resynced: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| resynced: validation, frames | 2 | 1-2 | 2, 2, 1, 1, 2 |
| resynced: economy | 1.2 | 1.0-1.5 | 1.2, 1.0, 1.2, 1.5, 1.3 |
| resynced: economy, longest step | 1.2 | 1.0-1.5 | 1.2, 1.0, 1.2, 1.5, 1.3 |
| resynced: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: level | 199 | 192-219 | 199, 192, 219, 208, 194 |
| resynced: level, longest step | 27.4 | 22.8-29.1 | 29.0, 23.5, 29.1, 27.4, 22.8 |
| resynced: level, steps | 568 | 568-568 | 568, 568, 568, 568, 568 |
| resynced: level, frames | 15 | 15-16 | 15, 15, 16, 15, 15 |
| resynced: hud | 0.2 | 0.2-0.3 | 0.3, 0.2, 0.3, 0.2, 0.2 |
| resynced: hud, longest step | 0.2 | 0.2-0.3 | 0.3, 0.2, 0.3, 0.2, 0.2 |
| resynced: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor | 1.6 | 1.5-2.0 | 1.6, 1.7, 2.0, 1.6, 1.5 |
| resynced: advisor, longest step | 1.6 | 1.5-2.0 | 1.6, 1.7, 2.0, 1.6, 1.5 |
| resynced: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: long step, save 1 | 20.7 | 14.8-27.8 | 21.8, 14.8, 20.7, 18.8, 27.8 |
| resynced: long step, catalogue 2 (2 of 5 runs) | 15.6 | 12.0-19.2 | -, -, 19.2, 12.0, - |
| resynced: long step, level 3 (to 0.008) | 15.1 | 14.7-16.2 | 15.2, 15.1, 15.1, 16.2, 14.7 |
| resynced: long step, level 82 (to 0.3) (1 of 5 runs) | 12.0 | 12.0-12.0 | -, -, -, 12.0, - |
| resynced: long step, level 532 (to 0.35) | 21.2 | 20.2-22.4 | 21.5, 21.2, 21.2, 22.4, 20.2 |
| resynced: long step, level 545 (to 0.8) | 17.9 | 16.0-19.4 | 18.1, 17.2, 17.9, 19.4, 16.0 |
| resynced: long step, level 550 (to 0.85) | 27.4 | 22.8-29.1 | 29.0, 23.5, 29.1, 27.4, 22.8 |
| resynced: long step, level 567 (to 0.95) | 16.1 | 15.9-18.5 | 16.1, 16.8, 18.5, 16.1, 15.9 |
| resynced: section coasters | 0.1 | 0.1-0.1 | 0.1, 0.1, 0.1, 0.1, 0.1 |
| resynced: section economy | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: section entities | 179 | 173-199 | 179, 173, 199, 188, 175 |
| resynced: section guest-sprites | 3.6 | 3.5-4.1 | 3.5, 3.5, 3.6, 3.7, 4.1 |
| resynced: section guests | 70.0 | 66.4-83.9 | 70.0, 67.2, 83.9, 73.5, 66.4 |
| resynced: section join-state | 0.0 | 0.0-0.1 | 0.1, 0.0, 0.0, 0.0, 0.0 |
| resynced: section level-hud | 1.5 | 1.4-2.4 | 1.5, 1.4, 2.4, 1.5, 1.4 |
| resynced: section model-parse | 15.2 | 14.8-16.3 | 15.2, 15.1, 15.2, 16.3, 14.8 |
| resynced: section object-catalog | 1.1 | 1.0-1.3 | 1.2, 1.0, 1.1, 1.3, 1.1 |
| resynced: section object-hoarding | 1.0 | 0.9-1.6 | 1.0, 0.9, 0.9, 1.1, 1.6 |
| resynced: section object-models | 8.8 | 7.4-10.3 | 9.6, 8.2, 8.8, 10.3, 7.4 |
| resynced: section object-runtime | 3.3 | 2.8-3.7 | 3.0, 3.5, 3.7, 3.3, 2.8 |
| resynced: section object-sign | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: section objects | 17.9 | 16.0-19.3 | 18.1, 17.1, 17.9, 19.3, 16.0 |
| resynced: section original-park | 16.4 | 16.0-17.7 | 16.6, 16.2, 16.4, 17.7, 16.0 |
| resynced: section paths | 0.4 | 0.3-0.5 | 0.3, 0.4, 0.5, 0.5, 0.3 |
| resynced: section pipeline | 2.2 | 2.1-2.5 | 2.2, 2.1, 2.3, 2.5, 2.1 |
| resynced: section resume | 3.1 | 3.0-3.1 | 3.1, 3.0, 3.1, 3.1, 3.0 |
| resynced: section ride-opcodes | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: section save-json | 16.7 | 12.2-23.7 | 18.1, 12.2, 16.7, 15.7, 23.7 |
| resynced: section save-mods | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: section save-shape | 3.6 | 2.6-4.0 | 3.6, 2.6, 3.9, 3.0, 4.0 |
| resynced: section staff-gate-economy | 30.2 | 28.1-32.4 | 30.2, 30.5, 32.4, 30.0, 28.1 |
| resynced: section staff-sprites | 9.6 | 8.2-9.9 | 9.8, 9.6, 9.3, 9.9, 8.2 |
| resynced: section terrain | 17.2 | 13.9-21.2 | 15.5, 13.9, 17.3, 17.2, 21.2 |
| resynced: section texture-create | 7.1 | 6.2-8.4 | 6.8, 7.1, 8.4, 6.2, 7.7 |
| resynced: longest update frame while building | 62.0 | 53.3-63.2 | 55.1, 62.5, 63.2, 62.0, 53.3 |
| resynced: the swap's update frame | 62.0 | 53.3-63.2 | 55.1, 62.5, 63.2, 62.0, 53.3 |
| resynced: first update frame after the swap | 43.9 | 43.3-51.1 | 43.9, 43.8, 51.1, 45.2, 43.3 |
| joined: the swap's frame, mark advisor.costume | 1.6 | 1.3-4.6 | 1.6, 1.3, 1.6, 2.5, 4.6 |
| joined: the swap's frame, mark advisor.decodeMp2 | 77.2 | 23.5-78.4 | 77.2, 23.5, 78.4, 78.4, 76.9 |
| joined: the swap's frame, mark advisor.loadResponse | 78.7 | 24.9-80.4 | 78.7, 24.9, 80.4, 80.1, 78.2 |
| joined: the swap's frame, mark advisor.play | 11.5 | 10.9-13.2 | 11.4, 10.9, 13.2, 11.5, 12.8 |
| joined: the swap's frame, mark advisor.selectRig | 5.5 | 5.0-7.9 | 5.2, 6.0, 7.9, 5.5, 5.0 |
| joined: the swap's frame, mark advisor.themeCostume | 0.3 | 0.2-1.2 | 1.2, 0.3, 0.3, 0.3, 0.2 |
| joined: the swap's frame, mark coop.session | 54.0 | 47.0-56.9 | 48.8, 54.0, 56.9, 55.5, 47.0 |
| joined: the swap's frame, mark level.entities | 65.3 | 62.4-75.1 | 68.3, 63.9, 75.1, 65.3, 62.4 |
| joined: the swap's frame, mark level.evening | 1.2 | 1.1-1.3 | 1.1, 1.2, 1.3, 1.2, 1.1 |
| joined: the swap's frame, mark level.pools | 18.5 | 16.6-20.7 | 18.5, 16.6, 20.7, 19.6, 17.4 |
| joined: the swap's frame, mark level.simulation | 29.0 | 28.4-32.9 | 29.0, 29.5, 32.9, 29.0, 28.4 |
| joined: the swap's frame, mark load:image-decode | 4.2 | 4.0-6.4 | 4.2, 4.2, 6.4, 4.8, 4.0 |
| joined: the swap's frame, mark load:model-parse | 3.9 | 3.5-6.3 | 4.1, 3.7, 6.3, 3.9, 3.5 |
| joined: the swap's frame, mark load:pipeline | 1.7 | 1.5-4.4 | 1.8, 1.7, 4.4, 1.7, 1.5 |
| joined: the swap's frame, mark load:shader-native | 1.8 | 1.7-1.9 | 1.8, 1.7, 1.9, 1.8, 1.8 |
| joined: the swap's frame, mark load:shader-spirv | 56.6 | 53.5-60.7 | 56.6, 55.4, 60.7, 57.3, 53.5 |
| joined: the swap's frame, mark load:texture-create | 13.9 | 12.5-15.1 | 12.5, 15.1, 15.0, 13.9, 13.4 |
| joined: the swap's frame, mark load:texture-decode | 14.0 | 13.5-15.9 | 15.9, 14.0, 15.0, 13.5, 13.5 |
| joined: the swap's frame, mark load:wad-decompress | 1.0 | 1.0-1.0 | 1.0, 1.0, 1.0, 1.0, 1.0 |
| joined: the swap's frame, mark park.advisor | 166 | 111-176 | 166, 111, 176, 168, 166 |
| joined: the swap's frame, mark park.audio | 2.5 | 2.2-2.6 | 2.5, 2.5, 2.6, 2.4, 2.2 |
| joined: the swap's frame, mark park.hud | 14.3 | 11.9-15.4 | 14.8, 11.9, 15.4, 14.3, 13.7 |
| joined: the swap's frame, mark park.level | 116 | 110-131 | 118, 112, 131, 116, 110 |
| joined: the swap's frame, mark sim.guests | 3.0 | 2.9-3.4 | 2.9, 3.0, 3.4, 3.0, 2.9 |
| joined: the swap's frame, mark sim.objects | 4.2 | 4.2-4.5 | 4.2, 4.3, 4.5, 4.2, 4.2 |
| joined: the swap's frame, mark sim.sweptTurn | 14.3 | 13.8-16.3 | 14.3, 14.6, 16.3, 14.2, 13.8 |
| joined: the frame after the swap, mark audio.decodeSync | 1.5 | 0.5-1.8 | 1.5, 0.5, 1.7, 1.8, 1.5 |
| joined: the frame after the swap, mark level.entities | 2.7 | 2.6-3.0 | 2.7, 2.7, 2.9, 3.0, 2.6 |
| joined: the frame after the swap, mark level.simulation | 24.8 | 24.1-26.3 | 24.8, 24.1, 25.1, 26.3, 24.3 |
| joined: the frame after the swap, mark load:object-models | 7.9 | 7.8-8.3 | 7.8, 8.2, 7.9, 8.3, 7.8 |
| joined: the frame after the swap, mark load:place-cue | 1.8 | 0.9-2.1 | 1.8, 0.9, 2.0, 2.1, 1.8 |
| joined: the frame after the swap, mark load:place-object | 10.5 | 10.0-10.8 | 10.1, 10.5, 10.5, 10.8, 10.0 |
| joined: the frame after the swap, mark load:texture-create | 1.2 | 1.2-2.1 | 1.2, 2.1, 1.2, 1.3, 1.2 |
| joined: the frame after the swap, mark load:texture-decode | 4.6 | 3.9-4.8 | 4.7, 3.9, 4.6, 4.8, 4.6 |
| joined: the frame after the swap, mark park.hud | 2.1 | 2.1-2.3 | 2.1, 2.1, 2.3, 2.2, 2.1 |
| joined: the frame after the swap, mark park.level | 27.5 | 26.8-29.4 | 27.5, 26.8, 28.0, 29.4, 26.9 |
| resynced: the swap's frame, mark audio.decodeSync | 2.0 | 2.0-2.3 | 2.0, 2.0, 2.3, 2.2, 2.0 |
| resynced: the swap's frame, mark coop.session | 16.2 | 15.2-20.8 | 15.8, 16.5, 20.8, 16.2, 15.2 |
| resynced: the swap's frame, mark level.entities | 18.6 | 16.0-21.4 | 16.1, 19.4, 18.6, 21.4, 16.0 |
| resynced: the swap's frame, mark level.pools | 12.8 | 10.2-14.1 | 12.2, 14.1, 12.8, 13.8, 10.2 |
| resynced: the swap's frame, mark level.simulation | 10.7 | 10.3-12.3 | 10.6, 12.3, 10.7, 10.3, 11.6 |
| resynced: the swap's frame, mark load:entities | 179 | 173-199 | 179, 173, 199, 188, 175 |
| resynced: the swap's frame, mark load:level-hud | 1.4 | 1.4-2.4 | 1.5, 1.4, 2.4, 1.4, 1.4 |
| resynced: the swap's frame, mark load:resume | 3.1 | 3.0-3.1 | 3.1, 3.0, 3.1, 3.1, 3.0 |
| resynced: the swap's frame, mark load:shader-native | 1.4 | 1.2-2.5 | 1.3, 2.5, 1.5, 1.4, 1.2 |
| resynced: the swap's frame, mark load:staff-gate-economy | 30.2 | 28.1-32.4 | 30.2, 30.6, 32.4, 30.0, 28.1 |
| resynced: the swap's frame, mark load:texture-create | 8.8 | 6.9-10.1 | 8.8, 8.7, 9.0, 10.1, 6.9 |
| resynced: the swap's frame, mark park.level | 42.1 | 37.9-45.8 | 38.9, 45.8, 42.1, 45.5, 37.9 |
| resynced: the swap's frame, mark sim.rendering | 1.9 | 1.8-2.0 | 1.9, 1.9, 1.8, 1.8, 2.0 |
| resynced: the frame after the swap, mark advisor.clipDurations | 5.6 | 5.2-7.9 | 5.3, 6.5, 5.6, 7.9, 5.2 |
| resynced: the frame after the swap, mark advisor.decodeMp2 | 23.2 | 23.0-23.3 | 23.3, 23.2, 23.2, 23.3, 23.0 |
| resynced: the frame after the swap, mark advisor.loadResponse | 23.7 | 23.3-24.0 | 23.7, 23.5, 24.0, 23.7, 23.3 |
| resynced: the frame after the swap, mark advisor.play | 16.3 | 14.6-17.5 | 14.9, 16.3, 17.0, 17.5, 14.6 |
| resynced: the frame after the swap, mark advisor.selectRig | 9.4 | 9.3-11.2 | 9.4, 9.6, 11.2, 9.4, 9.3 |
| resynced: the frame after the swap, mark load:model-parse | 15.7 | 14.0-20.9 | 14.3, 15.7, 20.9, 16.9, 14.0 |
| resynced: the frame after the swap, mark load:shader-native | 0.8 | 0.8-1.3 | 1.0, 0.8, 1.3, 0.8, 0.8 |
| resynced: the frame after the swap, mark park.advisor | 42.8 | 41.8-49.9 | 41.8, 42.8, 49.9, 44.1, 42.1 |
| resynced: the frame after the swap, mark park.level | 1.0 | 0.8-1.1 | 0.8, 0.9, 1.1, 1.0, 1.1 |

### Compiled ahead of time, five runs

5 runs; host phase (one host phase, reused by every run): Release build 1.0.0+864ec4dc90cc4c8328a33f692bed6705501796de, .NET 11.0.0-rc.2.26504.105, macOS 26.6.0 Arm64, 10 processors, silent True, advisor True, compiled jit; join phase: Release build 1.0.0+864ec4dc90cc4c8328a33f692bed6705501796de, .NET 11.0.0-rc.2.26504.105, Browser Wasm, 1 processors, silent False, advisor True, compiled aot

page: Chrome/155.0.8059.85, ANGLE (Apple, ANGLE Metal Renderer: Apple M5, Unspecified Version), 1280x900, muted True, frame cap 0, pool sprites settled

    Darwin 25.6.0 arm64
    processor Apple M5, 10 cores, model Mac17,2, memory 34359738368 bytes
    source 864ec4dc90cc4c8328a33f692bed6705501796de, clean (the driver's checkout)

| Value | Median | Range | Runs |
|---|---|---|---|
| capture 1: cut | 9.5 | 9.5-9.5 | 9.5 |
| capture 1: publish | 0.1 | 0.1-0.1 | 0.1 |
| capture 1: digest | 3.7 | 3.7-3.7 | 3.7 |
| capture 1: state | 1.2 | 1.2-1.2 | 1.2 |
| capture 1: snapshot | 4.5 | 4.5-4.5 | 4.5 |
| capture 1: serializer setup | 0.0 | 0.0-0.0 | 0.0 |
| capture 1: save | 69.8 | 69.8-69.8 | 69.8 |
| capture 1: save, longest slice | 36.4 | 36.4-36.4 | 36.4 |
| capture 1: save, slices | 12 | 12-12 | 12 |
| capture 1: save copy | 0.2 | 0.2-0.2 | 0.2 |
| capture 1: writer setup | 1.4 | 1.4-1.4 | 1.4 |
| capture 1: compression | 1.4 | 1.4-1.4 | 1.4 |
| capture 1: compression, slices | 1 | 1-1 | 1 |
| capture 1: finish | 0.1 | 0.1-0.1 | 0.1 |
| capture 1: longest frame | 36.9 | 36.9-36.9 | 36.9 |
| capture 1: frames | 14 | 14-14 | 14 |
| capture 1: upload s | 0.015 | 0.015-0.015 | 0.015 |
| capture 1: payload KiB | 128 | 128-128 | 128 |
| capture 1: body KiB | 981 | 981-981 | 981 |
| capture 1: save KiB | 980 | 980-980 | 980 |
| capture 2: cut | 7.1 | 7.1-7.1 | 7.1 |
| capture 2: publish | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: digest | 3.5 | 3.5-3.5 | 3.5 |
| capture 2: state | 0.2 | 0.2-0.2 | 0.2 |
| capture 2: snapshot | 3.3 | 3.3-3.3 | 3.3 |
| capture 2: serializer setup | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: save | 10.3 | 10.3-10.3 | 10.3 |
| capture 2: save, longest slice | 5.3 | 5.3-5.3 | 5.3 |
| capture 2: save, slices | 4 | 4-4 | 4 |
| capture 2: save copy | 0.1 | 0.1-0.1 | 0.1 |
| capture 2: writer setup | 0.1 | 0.1-0.1 | 0.1 |
| capture 2: compression | 1.8 | 1.8-1.8 | 1.8 |
| capture 2: compression, slices | 1 | 1-1 | 1 |
| capture 2: finish | 0.2 | 0.2-0.2 | 0.2 |
| capture 2: longest frame | 7.1 | 7.1-7.1 | 7.1 |
| capture 2: frames | 6 | 6-6 | 6 |
| capture 2: upload s | 0.015 | 0.015-0.015 | 0.015 |
| capture 2: payload KiB | 149 | 149-149 | 149 |
| capture 2: body KiB | 1107 | 1107-1107 | 1107 |
| capture 2: save KiB | 1107 | 1107-1107 | 1107 |
| joined: process's build | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: download s | 0.014 | 0.012-0.014 | 0.013, 0.012, 0.014, 0.014, 0.014 |
| joined: last attempt s | 0.014 | 0.012-0.014 | 0.013, 0.012, 0.014, 0.014, 0.014 |
| joined: payload KiB | 128 | 128-128 | 128, 128, 128, 128, 128 |
| joined: decode | 14.3 | 12.5-15.5 | 13.5, 15.1, 12.5, 15.5, 14.3 |
| joined: check | 75.2 | 74.2-77.6 | 74.7, 77.6, 75.2, 74.2, 76.4 |
| joined: swap | 8.9 | 8.9-9.5 | 8.9, 9.5, 9.1, 8.9, 8.9 |
| joined: build frames | 64 | 63-68 | 64, 63, 63, 65, 68 |
| joined: longest build frame | 288 | 288-293 | 288, 291, 288, 288, 293 |
| joined: catch-up ticks | 15 | 15-15 | 15, 15, 15, 15, 15 |
| joined: catch-up frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: catch-up s | 0.260 | 0.255-0.267 | 0.267, 0.259, 0.260, 0.255, 0.261 |
| joined: codec | 298 | 291-348 | 297, 298, 291, 299, 348 |
| joined: codec, longest step | 19.3 | 18.3-28.8 | 20.1, 18.6, 18.3, 19.3, 28.8 |
| joined: codec, steps | 230 | 230-230 | 230, 230, 230, 230, 230 |
| joined: codec, frames | 21 | 21-23 | 21, 21, 21, 21, 23 |
| joined: save | 87.9 | 86.8-96.4 | 89.1, 87.9, 87.6, 86.8, 96.4 |
| joined: save, longest step | 87.9 | 86.8-96.4 | 89.1, 87.9, 87.6, 86.8, 96.4 |
| joined: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: files | 0.4 | 0.4-0.5 | 0.5, 0.5, 0.4, 0.4, 0.4 |
| joined: files, longest step | 0.3 | 0.3-0.4 | 0.4, 0.4, 0.3, 0.3, 0.3 |
| joined: files, steps | 9 | 8-10 | 9, 8, 8, 9, 10 |
| joined: files, frames | 9 | 8-10 | 9, 8, 8, 9, 10 |
| joined: catalogue | 80.7 | 79.5-98.3 | 80.7, 79.5, 80.5, 82.4, 98.3 |
| joined: catalogue, longest step | 18.1 | 17.2-24.6 | 18.1, 17.2, 17.9, 18.6, 24.6 |
| joined: catalogue, steps | 90 | 90-90 | 90, 90, 90, 90, 90 |
| joined: catalogue, frames | 5 | 5-6 | 5, 5, 5, 6, 6 |
| joined: manifest | 56.6 | 55.7-58.5 | 56.5, 55.7, 56.7, 56.6, 58.5 |
| joined: manifest, longest step | 6.8 | 6.8-7.0 | 6.9, 6.8, 6.8, 6.8, 7.0 |
| joined: manifest, steps | 92 | 92-92 | 92, 92, 92, 92, 92 |
| joined: manifest, frames | 5 | 5-5 | 5, 5, 5, 5, 5 |
| joined: validation | 55.0 | 54.9-58.3 | 54.9, 55.0, 58.3, 54.9, 58.1 |
| joined: validation, longest step | 24.6 | 24.4-24.9 | 24.4, 24.4, 24.7, 24.9, 24.6 |
| joined: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| joined: validation, frames | 2 | 2-2 | 2, 2, 2, 2, 2 |
| joined: economy | 17.3 | 17.1-20.4 | 17.6, 17.3, 17.1, 17.3, 20.4 |
| joined: economy, longest step | 17.3 | 17.1-20.4 | 17.6, 17.3, 17.1, 17.3, 20.4 |
| joined: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: level | 668 | 658-680 | 668, 680, 658, 663, 671 |
| joined: level, longest step | 288 | 286-293 | 287, 291, 286, 288, 293 |
| joined: level, steps | 598 | 598-598 | 598, 598, 598, 598, 598 |
| joined: level, frames | 23 | 23-23 | 23, 23, 23, 23, 23 |
| joined: hud | 7.9 | 7.4-8.8 | 8.8, 7.7, 8.3, 7.4, 7.9 |
| joined: hud, longest step | 7.9 | 7.4-8.8 | 8.8, 7.7, 8.3, 7.4, 7.9 |
| joined: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor | 26.2 | 24.7-28.7 | 28.7, 26.2, 25.3, 24.7, 27.0 |
| joined: advisor, longest step | 26.2 | 24.7-28.7 | 28.7, 26.2, 25.3, 24.7, 27.0 |
| joined: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| joined: long step, codec 84 | 12.7 | 12.6-14.4 | 12.7, 12.9, 12.6, 12.6, 14.4 |
| joined: long step, codec 114 | 19.3 | 18.3-28.8 | 20.1, 18.6, 18.3, 19.3, 28.8 |
| joined: long step, codec 128 (1 of 5 runs) | 12.4 | 12.4-12.4 | -, -, -, -, 12.4 |
| joined: long step, codec 229 | 14.7 | 14.5-16.5 | 14.5, 14.8, 14.6, 14.7, 16.5 |
| joined: long step, save 1 | 87.9 | 86.8-96.4 | 89.1, 87.9, 87.6, 86.8, 96.4 |
| joined: long step, catalogue 1 | 14.6 | 14.1-24.6 | 14.6, 14.1, 14.6, 14.7, 24.6 |
| joined: long step, catalogue 90 | 18.1 | 17.2-18.9 | 18.1, 17.2, 17.9, 18.6, 18.9 |
| joined: long step, validation 13 | 24.6 | 24.4-24.9 | 24.4, 24.4, 24.7, 24.9, 24.6 |
| joined: long step, validation 17 | 14.4 | 13.3-16.7 | 13.6, 14.4, 16.7, 13.3, 16.4 |
| joined: long step, economy 1 | 17.3 | 17.1-20.4 | 17.6, 17.3, 17.1, 17.3, 20.4 |
| joined: long step, level 82 (to 0.3) | 12.3 | 12.0-12.6 | 12.3, 12.4, 12.2, 12.6, 12.0 |
| joined: long step, level 544 (to 0.4) (2 of 5 runs) | 12.1 | 12.0-12.2 | -, 12.2, -, 12.0, - |
| joined: long step, level 575 (to 0.8) | 288 | 286-293 | 287, 291, 286, 288, 293 |
| joined: long step, level 576 (to 0.85) | 23.6 | 23.5-24.4 | 23.5, 24.4, 23.6, 23.6, 24.3 |
| joined: long step, level 577 (to 0.85) | 13.2 | 12.9-13.6 | 13.2, 13.6, 12.9, 12.9, 13.5 |
| joined: long step, level 582 (to 0.85) | 13.1 | 12.7-14.6 | 14.6, 12.7, 13.1, 13.6, 13.0 |
| joined: long step, level 590 (to 0.95) | 27.8 | 27.6-29.6 | 28.3, 29.6, 27.6, 27.6, 27.8 |
| joined: long step, level 596 (to 0.95) | 13.5 | 13.2-13.7 | 13.2, 13.7, 13.6, 13.5, 13.5 |
| joined: long step, level 597 (to 0.95) | 17.5 | 17.1-19.3 | 19.3, 17.8, 17.3, 17.1, 17.5 |
| joined: long step, level 598 (to 1) | 73.5 | 67.9-76.6 | 73.9, 76.6, 67.9, 72.5, 73.5 |
| joined: long step, advisor 1 | 26.2 | 24.7-28.7 | 28.7, 26.2, 25.3, 24.7, 27.0 |
| joined: section coasters | 4.4 | 4.4-4.8 | 4.4, 4.8, 4.4, 4.4, 4.5 |
| joined: section economy | 0.1 | 0.1-0.2 | 0.2, 0.2, 0.1, 0.1, 0.1 |
| joined: section entities | 591 | 586-599 | 591, 599, 586, 587, 594 |
| joined: section guest-sprites | 13.2 | 12.8-13.6 | 13.2, 13.6, 12.8, 12.9, 13.5 |
| joined: section guests | 86.7 | 86.1-88.3 | 87.4, 88.3, 86.1, 86.1, 86.7 |
| joined: section join-state | 4.0 | 3.9-4.6 | 4.4, 4.6, 3.9, 4.0, 4.0 |
| joined: section level-hud | 5.3 | 5.1-8.3 | 8.3, 5.3, 5.4, 5.1, 5.2 |
| joined: section model-parse | 7.0 | 6.4-7.4 | 6.4, 7.4, 7.1, 6.4, 7.0 |
| joined: section object-catalog | 2.3 | 2.2-2.4 | 2.3, 2.3, 2.4, 2.4, 2.2 |
| joined: section object-hoarding | 15.3 | 12.0-18.7 | 15.4, 15.3, 12.0, 15.2, 18.7 |
| joined: section object-models | 222 | 222-223 | 222, 223, 223, 222, 222 |
| joined: section object-runtime | 25.8 | 25.1-26.5 | 25.4, 26.4, 25.8, 25.1, 26.5 |
| joined: section object-sign | 142 | 141-143 | 141, 143, 142, 141, 142 |
| joined: section objects | 285 | 283-290 | 285, 288, 283, 285, 290 |
| joined: section original-park | 8.8 | 8.6-9.2 | 8.6, 8.7, 9.2, 8.8, 8.9 |
| joined: section paths | 3.3 | 3.2-3.4 | 3.4, 3.4, 3.3, 3.2, 3.2 |
| joined: section pipeline | 7.7 | 7.0-9.4 | 9.4, 7.0, 9.3, 7.7, 7.3 |
| joined: section resume | 63.1 | 58.2-66.2 | 60.8, 66.2, 58.2, 63.1, 64.0 |
| joined: section ride-opcodes | 37.7 | 36.7-38.6 | 36.7, 38.6, 37.7, 37.2, 37.7 |
| joined: section save-json | 61.1 | 60.2-67.4 | 61.9, 61.1, 60.7, 60.2, 67.4 |
| joined: section save-mods | 0.9 | 0.9-1.0 | 0.9, 0.9, 0.9, 1.0, 0.9 |
| joined: section save-shape | 25.6 | 25.3-27.5 | 25.8, 25.4, 25.6, 25.3, 27.5 |
| joined: section staff-gate-economy | 52.4 | 51.3-54.3 | 54.3, 53.9, 52.4, 51.3, 52.3 |
| joined: section staff-sprites | 13.5 | 13.2-13.7 | 13.2, 13.7, 13.6, 13.4, 13.5 |
| joined: section terrain | 53.1 | 52.2-54.0 | 53.3, 54.0, 52.5, 53.1, 52.2 |
| joined: section texture-create | 23.0 | 20.1-23.3 | 23.3, 20.1, 21.0, 23.0, 23.0 |
| joined: section texture-decode | 62.4 | 61.2-65.0 | 61.2, 65.0, 61.7, 62.4, 64.0 |
| joined: section wad-decompress | 22.9 | 22.5-25.4 | 25.4, 22.9, 22.9, 22.5, 22.9 |
| joined: section wad-index | 5.8 | 5.6-8.5 | 5.6, 5.6, 8.5, 5.8, 5.8 |
| joined: section wad-read | 12.6 | 12.4-17.3 | 12.6, 14.8, 12.4, 12.4, 17.3 |
| joined: longest update frame while building | 323 | 311-324 | 323, 323, 317, 311, 324 |
| joined: the swap's update frame | 323 | 311-324 | 323, 323, 317, 311, 324 |
| joined: first update frame after the swap | 36.2 | 36.0-37.1 | 36.2, 37.1, 36.2, 36.0, 36.2 |
| joined: longest whole frame while building | 345 | 337-351 | 351, 345, 343, 337, 346 |
| joined: the swap's whole frame | 345 | 337-351 | 351, 345, 343, 337, 346 |
| joined: first whole frame after the swap | 38.6 | 38.2-42.9 | 38.6, 39.6, 38.4, 38.2, 42.9 |
| resynced: process's build | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: download attempts | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: download s | 0.017 | 0.016-0.017 | 0.016, 0.017, 0.017, 0.017, 0.017 |
| resynced: last attempt s | 0.017 | 0.016-0.017 | 0.016, 0.017, 0.017, 0.017, 0.017 |
| resynced: payload KiB | 149 | 149-149 | 149, 149, 149, 149, 149 |
| resynced: decode | 2.5 | 1.3-2.5 | 2.5, 2.5, 2.2, 2.5, 1.3 |
| resynced: check | 8.4 | 8.2-8.7 | 8.6, 8.4, 8.7, 8.4, 8.2 |
| resynced: swap | 15.8 | 10.7-30.5 | 30.5, 15.8, 19.5, 15.2, 10.7 |
| resynced: build frames | 18 | 17-19 | 18, 17, 17, 18, 19 |
| resynced: longest build frame | 57.2 | 48.3-61.2 | 61.2, 48.3, 60.8, 57.2, 49.1 |
| resynced: catch-up ticks | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catch-up s | 0.121 | 0.117-0.130 | 0.117, 0.122, 0.130, 0.117, 0.121 |
| resynced: codec | 1.7 | 1.2-2.1 | 2.0, 2.1, 1.7, 1.7, 1.2 |
| resynced: codec, longest step | 0.2 | 0.1-0.3 | 0.2, 0.3, 0.2, 0.1, 0.1 |
| resynced: codec, steps | 230 | 230-230 | 230, 230, 230, 230, 230 |
| resynced: codec, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: save | 47.8 | 37.5-54.1 | 47.8, 45.4, 50.9, 54.1, 37.5 |
| resynced: save, longest step | 47.8 | 37.5-54.1 | 47.8, 45.4, 50.9, 54.1, 37.5 |
| resynced: save, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: save, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: files, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: files, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: catalogue | 10.2 | 9.4-10.4 | 10.2, 10.4, 10.0, 10.3, 9.4 |
| resynced: catalogue, longest step | 10.1 | 9.3-10.4 | 10.1, 10.4, 10.0, 10.3, 9.3 |
| resynced: catalogue, steps | 2 | 2-2 | 2, 2, 2, 2, 2 |
| resynced: catalogue, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached) | 0.0 | 0.0-0.2 | 0.0, 0.0, 0.0, 0.2, 0.0 |
| resynced: manifest (cached), longest step | 0.0 | 0.0-0.2 | 0.0, 0.0, 0.0, 0.2, 0.0 |
| resynced: manifest (cached), steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: manifest (cached), frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: validation | 24.7 | 23.6-30.2 | 30.2, 24.7, 24.3, 24.9, 23.6 |
| resynced: validation, longest step | 17.5 | 17.4-17.9 | 17.9, 17.4, 17.4, 17.9, 17.5 |
| resynced: validation, steps | 17 | 17-17 | 17, 17, 17, 17, 17 |
| resynced: validation, frames | 3 | 3-3 | 3, 3, 3, 3, 3 |
| resynced: economy | 2.9 | 2.2-3.1 | 2.9, 3.1, 2.7, 3.0, 2.2 |
| resynced: economy, longest step | 2.9 | 2.2-3.1 | 2.9, 3.1, 2.7, 3.0, 2.2 |
| resynced: economy, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: economy, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: level | 207 | 201-210 | 210, 203, 201, 207, 207 |
| resynced: level, longest step | 49.7 | 48.0-61.0 | 61.0, 48.0, 49.7, 56.9, 48.8 |
| resynced: level, steps | 568 | 568-568 | 568, 568, 568, 568, 568 |
| resynced: level, frames | 15 | 14-15 | 15, 14, 14, 15, 15 |
| resynced: hud | 0.6 | 0.6-0.9 | 0.7, 0.6, 0.9, 0.6, 0.6 |
| resynced: hud, longest step | 0.6 | 0.6-0.9 | 0.7, 0.6, 0.9, 0.6, 0.6 |
| resynced: hud, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: hud, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor | 4.8 | 4.7-5.1 | 4.7, 4.8, 5.0, 5.1, 4.8 |
| resynced: advisor, longest step | 4.8 | 4.7-5.1 | 4.7, 4.8, 5.0, 5.1, 4.8 |
| resynced: advisor, steps | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: advisor, frames | 1 | 1-1 | 1, 1, 1, 1, 1 |
| resynced: long step, save 1 | 47.8 | 37.5-54.1 | 47.8, 45.4, 50.9, 54.1, 37.5 |
| resynced: long step, validation 13 | 17.5 | 17.4-17.9 | 17.9, 17.4, 17.4, 17.9, 17.5 |
| resynced: long step, level 82 (to 0.3) (1 of 5 runs) | 13.9 | 13.9-13.9 | -, 13.9, -, -, - |
| resynced: long step, level 545 (to 0.8) | 49.7 | 48.0-61.0 | 61.0, 48.0, 49.7, 56.9, 48.8 |
| resynced: long step, level 546 (to 0.85) | 18.7 | 18.5-19.0 | 18.7, 18.5, 19.0, 18.7, 19.0 |
| resynced: long step, level 567 (to 0.95) | 17.7 | 12.7-18.5 | 17.7, 18.1, 12.8, 18.5, 12.7 |
| resynced: long step, level 568 (to 1) (1 of 5 runs) | 16.1 | 16.1-16.1 | -, -, -, -, 16.1 |
| resynced: section coasters | 0.3 | 0.2-0.4 | 0.3, 0.2, 0.4, 0.4, 0.3 |
| resynced: section economy | 0.0 | 0.0-0.1 | 0.0, 0.0, 0.0, 0.1, 0.1 |
| resynced: section entities | 189 | 186-198 | 198, 189, 186, 194, 188 |
| resynced: section guest-sprites | 2.5 | 2.3-2.8 | 2.3, 2.3, 2.7, 2.8, 2.5 |
| resynced: section guests | 49.9 | 48.9-50.2 | 49.3, 48.9, 50.1, 49.9, 50.2 |
| resynced: section join-state | 0.0 | 0.0-0.1 | 0.0, 0.1, 0.0, 0.0, 0.1 |
| resynced: section level-hud | 3.1 | 3.0-3.2 | 3.0, 3.1, 3.2, 3.2, 3.1 |
| resynced: section model-parse | 3.1 | 2.4-8.9 | 3.1, 3.2, 8.9, 2.9, 2.4 |
| resynced: section object-catalog | 2.3 | 2.1-2.5 | 2.4, 2.1, 2.3, 2.1, 2.5 |
| resynced: section object-hoarding | 3.0 | 2.8-3.1 | 3.0, 3.0, 2.8, 2.8, 3.1 |
| resynced: section object-models | 23.4 | 21.2-33.7 | 33.7, 21.2, 23.4, 31.1, 22.4 |
| resynced: section object-runtime | 5.6 | 5.0-6.0 | 5.6, 6.0, 5.7, 5.3, 5.0 |
| resynced: section object-sign | 0.0 | 0.0-0.1 | 0.1, 0.0, 0.0, 0.0, 0.1 |
| resynced: section objects | 49.5 | 47.8-60.8 | 60.8, 47.8, 49.5, 56.7, 48.6 |
| resynced: section original-park | 5.3 | 4.8-11.4 | 5.5, 5.3, 11.4, 5.2, 4.8 |
| resynced: section paths | 0.1 | 0.0-0.2 | 0.0, 0.0, 0.2, 0.1, 0.2 |
| resynced: section pipeline | 7.7 | 1.5-7.9 | 7.7, 1.5, 3.0, 7.9, 7.9 |
| resynced: section resume | 7.0 | 6.7-12.9 | 6.7, 6.9, 7.3, 7.0, 12.9 |
| resynced: section ride-opcodes | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0, 0.0, 0.0 |
| resynced: section save-json | 42.8 | 33.2-49.7 | 42.8, 40.9, 45.5, 49.7, 33.2 |
| resynced: section save-mods | 0.0 | 0.0-0.1 | 0.0, 0.1, 0.0, 0.0, 0.1 |
| resynced: section save-shape | 4.4 | 4.2-5.4 | 4.9, 4.4, 5.4, 4.3, 4.2 |
| resynced: section staff-gate-economy | 27.3 | 26.5-28.1 | 27.1, 27.3, 28.1, 27.6, 26.5 |
| resynced: section staff-sprites | 5.6 | 5.0-5.8 | 5.6, 5.5, 5.7, 5.8, 5.0 |
| resynced: section terrain | 19.2 | 17.5-24.1 | 19.2, 24.1, 17.5, 18.5, 21.7 |
| resynced: section texture-create | 8.9 | 8.6-9.5 | 8.8, 8.9, 8.6, 9.3, 9.5 |
| resynced: longest update frame while building | 141 | 135-152 | 152, 141, 152, 135, 140 |
| resynced: the swap's update frame | 141 | 135-152 | 152, 141, 152, 135, 140 |
| resynced: first update frame after the swap | 19.6 | 18.9-20.3 | 20.3, 18.9, 19.6, 19.2, 19.9 |
| resynced: longest whole frame while building | 144 | 138-155 | 154, 144, 155, 138, 142 |
| resynced: the swap's whole frame | 144 | 138-155 | 154, 144, 155, 138, 142 |
| resynced: first whole frame after the swap | 21.1 | 20.5-22.1 | 22.1, 20.5, 21.1, 20.9, 21.7 |
| joined: the swap's frame, mark advisor.costume | 1.3 | 1.2-1.3 | 1.3, 1.3, 1.2, 1.3, 1.3 |
| joined: the swap's frame, mark advisor.decodeSlice | 5.0 | 5.0-5.1 | 5.0, 5.0, 5.1, 5.1, 5.0 |
| joined: the swap's frame, mark advisor.firstUpdate | 5.2 | 5.1-5.2 | 5.2, 5.2, 5.2, 5.2, 5.1 |
| joined: the swap's frame, mark advisor.loadResponse | 1.1 | 1.1-1.7 | 1.2, 1.1, 1.7, 1.1, 1.1 |
| joined: the swap's frame, mark advisor.play | 13.1 | 12.8-13.8 | 12.8, 13.8, 13.0, 13.1, 13.6 |
| joined: the swap's frame, mark advisor.selectRig | 4.7 | 4.6-5.4 | 4.7, 5.3, 4.6, 4.6, 5.4 |
| joined: the swap's frame, mark audio.decodeSync | 1.2 | 1.0-1.2 | 1.2, 1.2, 1.2, 1.0, 1.1 |
| joined: the swap's frame, mark coop.session | 87.9 | 86.6-90.7 | 87.2, 90.7, 87.9, 86.6, 88.9 |
| joined: the swap's frame, mark level.entities | 66.7 | 65.0-70.8 | 70.8, 66.5, 65.0, 68.5, 66.7 |
| joined: the swap's frame, mark level.pools | 71.8 | 69.8-75.5 | 75.5, 71.8, 70.4, 69.8, 75.3 |
| joined: the swap's frame, mark level.simulation | 45.4 | 44.0-46.1 | 46.0, 45.4, 46.1, 44.0, 44.7 |
| joined: the swap's frame, mark load:model-parse | 7.4 | 2.8-11.9 | 7.6, 2.8, 11.9, 7.4, 3.5 |
| joined: the swap's frame, mark load:object-models | 11.1 | 10.4-14.5 | 10.6, 14.5, 11.1, 10.4, 14.5 |
| joined: the swap's frame, mark load:object-runtime | 4.7 | 0.6-5.4 | 5.4, 0.7, 5.1, 4.7, 0.6 |
| joined: the swap's frame, mark load:pipeline | 11.4 | 9.5-13.8 | 13.8, 11.2, 11.8, 9.5, 11.4 |
| joined: the swap's frame, mark load:place-cue | 1.4 | 1.3-1.5 | 1.4, 1.4, 1.5, 1.5, 1.3 |
| joined: the swap's frame, mark load:place-object | 16.8 | 16.4-17.9 | 17.5, 16.8, 17.9, 16.5, 16.4 |
| joined: the swap's frame, mark load:texture-create | 19.1 | 17.8-21.6 | 21.6, 17.8, 18.5, 19.1, 21.2 |
| joined: the swap's frame, mark load:texture-decode | 15.0 | 13.0-26.6 | 14.3, 25.6, 13.0, 15.0, 26.6 |
| joined: the swap's frame, mark load:wad-decompress | 1.0 | 0.6-1.1 | 1.0, 0.9, 1.1, 0.6, 1.1 |
| joined: the swap's frame, mark load:wad-read | 1.0 | 0.7-1.6 | 1.0, 0.8, 0.7, 1.3, 1.6 |
| joined: the swap's frame, mark park.advisor | 39.4 | 34.8-40.8 | 35.7, 40.5, 39.4, 34.8, 40.8 |
| joined: the swap's frame, mark park.hud | 5.4 | 5.2-5.4 | 5.2, 5.4, 5.4, 5.2, 5.4 |
| joined: the swap's frame, mark park.level | 185 | 182-193 | 193, 185, 182, 183, 188 |
| joined: the swap's frame, mark sim.guests | 2.6 | 2.4-2.9 | 2.6, 2.9, 2.6, 2.4, 2.6 |
| joined: the swap's frame, mark sim.objects | 4.2 | 4.1-4.8 | 4.8, 4.2, 4.6, 4.1, 4.2 |
| joined: the swap's frame, mark sim.sweptTurn | 6.5 | 6.3-6.8 | 6.3, 6.8, 6.7, 6.4, 6.5 |
| joined: the frame after the swap, mark advisor.decodeSlice | 5.0 | 5.0-5.0 | 5.0, 5.0, 5.0, 5.0, 5.0 |
| joined: the frame after the swap, mark level.cells | 1.3 | 1.3-1.4 | 1.4, 1.4, 1.3, 1.3, 1.3 |
| joined: the frame after the swap, mark level.simulation | 24.2 | 24.0-24.6 | 24.0, 24.6, 24.2, 24.1, 24.3 |
| joined: the frame after the swap, mark park.advisor | 8.7 | 8.7-8.9 | 8.7, 8.9, 8.7, 8.8, 8.7 |
| joined: the frame after the swap, mark park.hud | 1.1 | 1.0-1.2 | 1.1, 1.2, 1.1, 1.0, 1.0 |
| joined: the frame after the swap, mark park.level | 26.0 | 25.7-26.4 | 25.9, 26.4, 26.0, 25.7, 26.2 |
| joined: the frame after the swap, mark sim.objects | 7.8 | 7.5-8.4 | 7.5, 7.8, 8.4, 7.8, 8.0 |
| joined: the frame after the swap, mark sim.sweptTurn | 4.3 | 4.2-4.8 | 4.8, 4.5, 4.2, 4.3, 4.2 |
| resynced: the swap's frame, mark audio.decodeSync | 2.6 | 2.6-2.7 | 2.6, 2.6, 2.7, 2.6, 2.6 |
| resynced: the swap's frame, mark coop.session | 24.5 | 24.0-39.5 | 39.5, 24.5, 28.6, 24.0, 24.2 |
| resynced: the swap's frame, mark level.entities | 37.6 | 35.1-39.5 | 37.6, 35.1, 39.5, 37.3, 39.0 |
| resynced: the swap's frame, mark level.pools | 65.3 | 63.4-72.9 | 64.0, 67.6, 72.9, 63.4, 65.3 |
| resynced: the swap's frame, mark level.simulation | 10.0 | 9.9-10.8 | 9.9, 10.0, 10.6, 9.9, 10.8 |
| resynced: the swap's frame, mark load:pipeline | 0.7 | 0.6-1.2 | 1.1, 1.2, 0.7, 0.6, 0.7 |
| resynced: the swap's frame, mark load:texture-create | 15.1 | 15.0-19.7 | 15.1, 15.1, 19.7, 15.0, 17.3 |
| resynced: the swap's frame, mark park.audio | 0.1 | 0.0-3.6 | 0.0, 3.6, 0.1, 0.1, 0.0 |
| resynced: the swap's frame, mark park.level | 113 | 111-123 | 112, 113, 123, 111, 115 |
| resynced: the swap's frame, mark sim.rendering | 2.8 | 2.6-2.9 | 2.8, 2.6, 2.9, 2.6, 2.9 |
| resynced: the frame after the swap, mark advisor.decodeSlice | 5.0 | 5.0-5.0 | 5.0, 5.0, 5.0, 5.0, 5.0 |
| resynced: the frame after the swap, mark advisor.firstUpdate | 5.0 | 5.0-5.0 | 5.0, 5.0, 5.0, 5.0, 5.0 |
| resynced: the frame after the swap, mark advisor.play | 7.6 | 7.4-7.8 | 7.7, 7.6, 7.4, 7.6, 7.8 |
| resynced: the frame after the swap, mark advisor.selectRig | 2.1 | 2.0-2.2 | 2.2, 2.1, 2.0, 2.2, 2.1 |
| resynced: the frame after the swap, mark level.cells | 1.6 | 1.4-1.7 | 1.6, 1.4, 1.6, 1.6, 1.7 |
| resynced: the frame after the swap, mark level.simulation | 4.7 | 4.4-5.4 | 5.4, 4.6, 5.0, 4.4, 4.7 |
| resynced: the frame after the swap, mark load:model-parse | 1.1 | 0.8-1.3 | 1.1, 0.8, 1.1, 1.0, 1.3 |
| resynced: the frame after the swap, mark load:pipeline | 0.5 | 0.0-1.1 | 0.5, 0.4, 1.1, 0.5, 0.0 |
| resynced: the frame after the swap, mark park.advisor | 12.5 | 12.1-12.7 | 12.6, 12.1, 12.1, 12.5, 12.7 |
| resynced: the frame after the swap, mark park.level | 6.6 | 6.1-7.1 | 7.1, 6.2, 6.8, 6.1, 6.6 |
| resynced: the frame after the swap, mark sim.guests | 0.4 | 0.0-1.1 | 1.1, 0.4, 0.0, 0.3, 1.0 |
| resynced: the frame after the swap, mark sim.objects | 1.7 | 1.5-2.4 | 1.6, 1.5, 1.7, 2.4, 1.9 |
| resynced: the frame after the swap, mark sim.sweptTurn | 1.1 | 1.0-1.6 | 1.6, 1.1, 1.4, 1.0, 1.0 |
| final check: update frame | 507 | 506-522 | 511, 507, 522, 507, 506 |
| final check: whole frame | 508 | 507-523 | 512, 508, 523, 508, 507 |

### Interpreted, three runs

3 runs; host phase (one host phase, reused by every run): Release build 1.0.0+864ec4dc90cc4c8328a33f692bed6705501796de, .NET 11.0.0-rc.2.26504.105, macOS 26.6.0 Arm64, 10 processors, silent True, advisor True, compiled jit; join phase: Release build 1.0.0+864ec4dc90cc4c8328a33f692bed6705501796de, .NET 11.0.0-rc.2.26504.105, Browser Wasm, 1 processors, silent False, advisor True, compiled interpreter

page: Chrome/155.0.8059.85, ANGLE (Apple, ANGLE Metal Renderer: Apple M5, Unspecified Version), 1280x900, muted True, frame cap 0, pool sprites settled

    Darwin 25.6.0 arm64
    processor Apple M5, 10 cores, model Mac17,2, memory 34359738368 bytes
    source 864ec4dc90cc4c8328a33f692bed6705501796de, clean (the driver's checkout)

| Value | Median | Range | Runs |
|---|---|---|---|
| capture 1: cut | 9.7 | 9.7-9.7 | 9.7 |
| capture 1: publish | 0.1 | 0.1-0.1 | 0.1 |
| capture 1: digest | 4.1 | 4.1-4.1 | 4.1 |
| capture 1: state | 0.9 | 0.9-0.9 | 0.9 |
| capture 1: snapshot | 4.6 | 4.6-4.6 | 4.6 |
| capture 1: serializer setup | 0.0 | 0.0-0.0 | 0.0 |
| capture 1: save | 67.2 | 67.2-67.2 | 67.2 |
| capture 1: save, longest slice | 32.0 | 32.0-32.0 | 32.0 |
| capture 1: save, slices | 13 | 13-13 | 13 |
| capture 1: save copy | 0.3 | 0.3-0.3 | 0.3 |
| capture 1: writer setup | 1.6 | 1.6-1.6 | 1.6 |
| capture 1: compression | 1.7 | 1.7-1.7 | 1.7 |
| capture 1: compression, slices | 1 | 1-1 | 1 |
| capture 1: finish | 0.1 | 0.1-0.1 | 0.1 |
| capture 1: longest frame | 32.3 | 32.3-32.3 | 32.3 |
| capture 1: frames | 15 | 15-15 | 15 |
| capture 1: upload s | 0.015 | 0.015-0.015 | 0.015 |
| capture 1: payload KiB | 128 | 128-128 | 128 |
| capture 1: body KiB | 981 | 981-981 | 981 |
| capture 1: save KiB | 980 | 980-980 | 980 |
| capture 2: cut | 7.6 | 7.6-7.6 | 7.6 |
| capture 2: publish | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: digest | 3.9 | 3.9-3.9 | 3.9 |
| capture 2: state | 0.2 | 0.2-0.2 | 0.2 |
| capture 2: snapshot | 3.4 | 3.4-3.4 | 3.4 |
| capture 2: serializer setup | 0.0 | 0.0-0.0 | 0.0 |
| capture 2: save | 10.8 | 10.8-10.8 | 10.8 |
| capture 2: save, longest slice | 5.9 | 5.9-5.9 | 5.9 |
| capture 2: save, slices | 4 | 4-4 | 4 |
| capture 2: save copy | 0.1 | 0.1-0.1 | 0.1 |
| capture 2: writer setup | 0.1 | 0.1-0.1 | 0.1 |
| capture 2: compression | 1.8 | 1.8-1.8 | 1.8 |
| capture 2: compression, slices | 1 | 1-1 | 1 |
| capture 2: finish | 0.1 | 0.1-0.1 | 0.1 |
| capture 2: longest frame | 7.6 | 7.6-7.6 | 7.6 |
| capture 2: frames | 6 | 6-6 | 6 |
| capture 2: upload s | 0.015 | 0.015-0.015 | 0.015 |
| capture 2: payload KiB | 149 | 149-149 | 149 |
| capture 2: body KiB | 1107 | 1107-1107 | 1107 |
| capture 2: save KiB | 1107 | 1107-1107 | 1107 |
| joined: process's build | 1 | 1-1 | 1, 1, 1 |
| joined: download attempts | 1 | 1-1 | 1, 1, 1 |
| joined: download s | 0.016 | 0.016-0.016 | 0.016, 0.016, 0.016 |
| joined: last attempt s | 0.016 | 0.016-0.016 | 0.016, 0.016, 0.016 |
| joined: payload KiB | 128 | 128-128 | 128, 128, 128 |
| joined: decode | 11.1 | 9.8-12.2 | 12.2, 9.8, 11.1 |
| joined: check | 95.8 | 95.2-95.9 | 95.8, 95.2, 95.9 |
| joined: swap | 8.2 | 8.0-8.3 | 8.0, 8.2, 8.3 |
| joined: build frames | 107 | 106-109 | 106, 107, 109 |
| joined: longest build frame | 1230 | 1230-1247 | 1230, 1230, 1247 |
| joined: catch-up ticks | 59 | 59-86 | 59, 59, 86 |
| joined: catch-up frames | 4 | 4-5 | 4, 4, 5 |
| joined: catch-up s | 0.747 | 0.734-0.782 | 0.747, 0.734, 0.782 |
| joined: codec | 281 | 278-306 | 281, 278, 306 |
| joined: codec, longest step | 13.4 | 12.8-15.6 | 12.8, 13.4, 15.6 |
| joined: codec, steps | 230 | 230-230 | 230, 230, 230 |
| joined: codec, frames | 22 | 21-24 | 21, 22, 24 |
| joined: save | 191 | 190-194 | 194, 190, 191 |
| joined: save, longest step | 191 | 190-194 | 194, 190, 191 |
| joined: save, steps | 1 | 1-1 | 1, 1, 1 |
| joined: save, frames | 1 | 1-1 | 1, 1, 1 |
| joined: files | 0.5 | 0.4-0.6 | 0.6, 0.5, 0.4 |
| joined: files, longest step | 0.4 | 0.3-0.5 | 0.4, 0.5, 0.3 |
| joined: files, steps | 8 | 8-10 | 10, 8, 8 |
| joined: files, frames | 8 | 8-10 | 10, 8, 8 |
| joined: catalogue | 200 | 198-202 | 198, 200, 202 |
| joined: catalogue, longest step | 54.7 | 54.6-55.0 | 54.6, 54.7, 55.0 |
| joined: catalogue, steps | 90 | 90-90 | 90, 90, 90 |
| joined: catalogue, frames | 12 | 11-12 | 11, 12, 12 |
| joined: manifest | 237 | 230-238 | 230, 237, 238 |
| joined: manifest, longest step | 38.7 | 37.5-39.2 | 37.5, 38.7, 39.2 |
| joined: manifest, steps | 92 | 92-92 | 92, 92, 92 |
| joined: manifest, frames | 16 | 15-16 | 15, 16, 16 |
| joined: validation | 379 | 379-381 | 381, 379, 379 |
| joined: validation, longest step | 205 | 203-205 | 203, 205, 205 |
| joined: validation, steps | 17 | 17-17 | 17, 17, 17 |
| joined: validation, frames | 4 | 4-4 | 4, 4, 4 |
| joined: economy | 16.4 | 16.3-17.2 | 17.2, 16.3, 16.4 |
| joined: economy, longest step | 16.4 | 16.3-17.2 | 17.2, 16.3, 16.4 |
| joined: economy, steps | 1 | 1-1 | 1, 1, 1 |
| joined: economy, frames | 1 | 1-1 | 1, 1, 1 |
| joined: level | 2233 | 2226-2249 | 2226, 2233, 2249 |
| joined: level, longest step | 1230 | 1230-1247 | 1230, 1230, 1247 |
| joined: level, steps | 598 | 598-598 | 598, 598, 598 |
| joined: level, frames | 46 | 46-46 | 46, 46, 46 |
| joined: hud | 10.5 | 10.1-10.6 | 10.6, 10.1, 10.5 |
| joined: hud, longest step | 10.5 | 10.1-10.6 | 10.6, 10.1, 10.5 |
| joined: hud, steps | 1 | 1-1 | 1, 1, 1 |
| joined: hud, frames | 1 | 1-1 | 1, 1, 1 |
| joined: advisor | 23.2 | 22.8-23.2 | 23.2, 23.2, 22.8 |
| joined: advisor, longest step | 23.2 | 22.8-23.2 | 23.2, 23.2, 22.8 |
| joined: advisor, steps | 1 | 1-1 | 1, 1, 1 |
| joined: advisor, frames | 1 | 1-1 | 1, 1, 1 |
| joined: long step, codec 114 | 13.0 | 12.8-15.6 | 12.8, 13.0, 15.6 |
| joined: long step, codec 229 (2 of 3 runs) | 13.7 | 13.4-14.0 | -, 13.4, 14.0 |
| joined: long step, save 1 | 191 | 190-194 | 194, 190, 191 |
| joined: long step, catalogue 1 | 14.1 | 13.9-14.3 | 14.1, 14.3, 13.9 |
| joined: long step, catalogue 90 | 54.7 | 54.6-55.0 | 54.6, 54.7, 55.0 |
| joined: long step, manifest 91 | 38.7 | 37.5-39.2 | 37.5, 38.7, 39.2 |
| joined: long step, validation 12 | 205 | 203-205 | 203, 205, 205 |
| joined: long step, validation 13 | 148 | 148-152 | 152, 148, 148 |
| joined: long step, economy 1 | 16.4 | 16.3-17.2 | 17.2, 16.3, 16.4 |
| joined: long step, level 3 (to 0.008) | 15.0 | 15.0-15.0 | 15.0, 15.0, 15.0 |
| joined: long step, level 82 (to 0.3) | 32.4 | 32.0-32.7 | 32.7, 32.4, 32.0 |
| joined: long step, level 83 (to 0.3) | 15.4 | 15.3-15.7 | 15.7, 15.4, 15.3 |
| joined: long step, level 532 (to 0.35) | 22.4 | 22.4-22.9 | 22.4, 22.4, 22.9 |
| joined: long step, level 534 (to 0.35) | 22.3 | 22.2-22.6 | 22.3, 22.2, 22.6 |
| joined: long step, level 535 (to 0.35) | 19.3 | 19.0-19.4 | 19.3, 19.0, 19.4 |
| joined: long step, level 536 (to 0.35) | 21.9 | 21.1-22.1 | 21.9, 21.1, 22.1 |
| joined: long step, level 540 (to 0.35) | 16.2 | 16.0-16.7 | 16.0, 16.2, 16.7 |
| joined: long step, level 544 (to 0.4) | 12.7 | 12.5-13.3 | 13.3, 12.5, 12.7 |
| joined: long step, level 575 (to 0.8) | 1230 | 1230-1247 | 1230, 1230, 1247 |
| joined: long step, level 576 (to 0.85) | 91.8 | 91.6-92.1 | 91.6, 92.1, 91.8 |
| joined: long step, level 577 (to 0.85) | 45.8 | 45.0-47.3 | 45.0, 47.3, 45.8 |
| joined: long step, level 578 (to 0.85) | 17.4 | 17.3-17.8 | 17.3, 17.8, 17.4 |
| joined: long step, level 582 (to 0.85) | 17.3 | 16.5-17.6 | 17.3, 17.6, 16.5 |
| joined: long step, level 584 (to 0.85) | 12.8 | 12.7-13.1 | 13.1, 12.7, 12.8 |
| joined: long step, level 587 (to 0.85) (1 of 3 runs) | 12.2 | 12.2-12.2 | -, -, 12.2 |
| joined: long step, level 588 (to 0.85) | 25.9 | 21.5-26.9 | 21.5, 25.9, 26.9 |
| joined: long step, level 590 (to 0.95) | 90.7 | 90.2-93.9 | 93.9, 90.7, 90.2 |
| joined: long step, level 594 (to 0.95) | 16.3 | 16.3-16.6 | 16.3, 16.6, 16.3 |
| joined: long step, level 596 (to 0.95) | 49.5 | 48.4-50.0 | 48.4, 50.0, 49.5 |
| joined: long step, level 597 (to 0.95) | 29.6 | 29.6-33.8 | 33.8, 29.6, 29.6 |
| joined: long step, level 598 (to 1) | 73.9 | 69.6-77.7 | 69.6, 77.7, 73.9 |
| joined: long step, advisor 1 | 23.2 | 22.8-23.2 | 23.2, 23.2, 22.8 |
| joined: section coasters | 7.3 | 7.2-7.4 | 7.2, 7.3, 7.4 |
| joined: section economy | 0.2 | 0.1-0.2 | 0.2, 0.1, 0.2 |
| joined: section entities | 2141 | 2137-2160 | 2137, 2141, 2160 |
| joined: section guest-sprites | 42.0 | 41.3-43.4 | 41.3, 43.4, 42.0 |
| joined: section guests | 236 | 228-237 | 228, 237, 236 |
| joined: section join-state | 1.1 | 1.0-1.1 | 1.1, 1.1, 1.0 |
| joined: section level-hud | 6.4 | 6.3-6.6 | 6.4, 6.3, 6.6 |
| joined: section model-parse | 28.0 | 27.3-28.7 | 28.0, 27.3, 28.7 |
| joined: section object-catalog | 10.3 | 10.1-10.6 | 10.6, 10.1, 10.3 |
| joined: section object-hoarding | 18.8 | 18.3-18.9 | 18.8, 18.3, 18.9 |
| joined: section object-models | 1076 | 1072-1088 | 1072, 1076, 1088 |
| joined: section object-runtime | 49.3 | 48.4-51.1 | 49.3, 48.4, 51.1 |
| joined: section object-sign | 718 | 713-718 | 713, 718, 718 |
| joined: section objects | 1226 | 1225-1242 | 1226, 1225, 1242 |
| joined: section original-park | 27.7 | 27.4-28.0 | 28.0, 27.4, 27.7 |
| joined: section paths | 3.8 | 3.6-3.8 | 3.8, 3.6, 3.8 |
| joined: section pipeline | 15.3 | 13.8-16.0 | 15.3, 16.0, 13.8 |
| joined: section resume | 66.0 | 61.8-69.7 | 61.8, 69.7, 66.0 |
| joined: section ride-opcodes | 57.7 | 57.3-58.4 | 58.4, 57.3, 57.7 |
| joined: section save-json | 141 | 140-144 | 144, 140, 141 |
| joined: section save-mods | 1.2 | 1.1-1.2 | 1.2, 1.2, 1.1 |
| joined: section save-shape | 48.0 | 48.0-48.3 | 48.0, 48.0, 48.3 |
| joined: section staff-gate-economy | 105 | 105-106 | 105, 106, 105 |
| joined: section staff-sprites | 49.5 | 48.4-50.0 | 48.4, 50.0, 49.5 |
| joined: section terrain | 243 | 242-244 | 243, 242, 244 |
| joined: section texture-create | 39.3 | 36.8-41.8 | 39.3, 36.8, 41.8 |
| joined: section texture-decode | 478 | 478-481 | 478, 478, 481 |
| joined: section wad-decompress | 113 | 110-114 | 110, 113, 114 |
| joined: section wad-index | 15.6 | 15.1-15.9 | 15.1, 15.9, 15.6 |
| joined: section wad-read | 24.4 | 22.1-24.7 | 24.7, 24.4, 22.1 |
| joined: longest update frame while building | 1232 | 1232-1248 | 1232, 1232, 1248 |
| joined: the swap's update frame | 573 | 573-586 | 586, 573, 573 |
| joined: first update frame after the swap | 88.9 | 88.4-90.2 | 90.2, 88.9, 88.4 |
| joined: longest whole frame while building | 1233 | 1233-1249 | 1233, 1233, 1249 |
| joined: the swap's whole frame | 627 | 626-639 | 639, 626, 627 |
| joined: first whole frame after the swap | 94.2 | 93.2-95.2 | 95.2, 94.2, 93.2 |
| resynced: process's build | 2 | 2-2 | 2, 2, 2 |
| resynced: download attempts | 1 | 1-1 | 1, 1, 1 |
| resynced: download s | 0.016 | 0.016-0.017 | 0.016, 0.017, 0.016 |
| resynced: last attempt s | 0.016 | 0.016-0.017 | 0.016, 0.017, 0.016 |
| resynced: payload KiB | 149 | 149-149 | 149, 149, 149 |
| resynced: decode | 4.9 | 4.7-5.4 | 5.4, 4.7, 4.9 |
| resynced: check | 27.6 | 27.5-29.0 | 27.5, 29.0, 27.6 |
| resynced: swap | 26.0 | 25.5-26.4 | 25.5, 26.4, 26.0 |
| resynced: build frames | 30 | 30-30 | 30, 30, 30 |
| resynced: longest build frame | 179 | 177-180 | 180, 177, 179 |
| resynced: catch-up ticks | 1 | 1-1 | 1, 1, 1 |
| resynced: catch-up frames | 1 | 1-1 | 1, 1, 1 |
| resynced: catch-up s | 0.235 | 0.235-0.242 | 0.235, 0.242, 0.235 |
| resynced: codec | 4.4 | 4.4-4.7 | 4.7, 4.4, 4.4 |
| resynced: codec, longest step | 0.6 | 0.5-0.6 | 0.6, 0.6, 0.5 |
| resynced: codec, steps | 230 | 230-230 | 230, 230, 230 |
| resynced: codec, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: save | 92.2 | 89.8-93.0 | 92.2, 93.0, 89.8 |
| resynced: save, longest step | 92.2 | 89.8-93.0 | 92.2, 93.0, 89.8 |
| resynced: save, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: save, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: files | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: files, longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: files, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: files, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: catalogue | 33.6 | 33.5-33.8 | 33.5, 33.8, 33.6 |
| resynced: catalogue, longest step | 33.5 | 33.5-33.8 | 33.5, 33.8, 33.5 |
| resynced: catalogue, steps | 2 | 2-2 | 2, 2, 2 |
| resynced: catalogue, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: manifest (cached) | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: manifest (cached), longest step | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: manifest (cached), steps | 1 | 1-1 | 1, 1, 1 |
| resynced: manifest (cached), frames | 1 | 1-1 | 1, 1, 1 |
| resynced: validation | 121 | 119-121 | 121, 119, 121 |
| resynced: validation, longest step | 76.5 | 75.4-77.1 | 77.1, 75.4, 76.5 |
| resynced: validation, steps | 17 | 17-17 | 17, 17, 17 |
| resynced: validation, frames | 4 | 4-4 | 4, 4, 4 |
| resynced: economy | 2.4 | 2.3-2.6 | 2.4, 2.6, 2.3 |
| resynced: economy, longest step | 2.4 | 2.3-2.6 | 2.4, 2.6, 2.3 |
| resynced: economy, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: economy, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: level | 676 | 675-680 | 680, 675, 676 |
| resynced: level, longest step | 169 | 168-170 | 170, 168, 169 |
| resynced: level, steps | 568 | 568-568 | 568, 568, 568 |
| resynced: level, frames | 24 | 24-24 | 24, 24, 24 |
| resynced: hud | 1.1 | 1.0-1.1 | 1.1, 1.0, 1.1 |
| resynced: hud, longest step | 1.1 | 1.0-1.1 | 1.1, 1.0, 1.1 |
| resynced: hud, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: hud, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: advisor | 4.8 | 4.7-4.9 | 4.7, 4.8, 4.9 |
| resynced: advisor, longest step | 4.8 | 4.7-4.9 | 4.7, 4.8, 4.9 |
| resynced: advisor, steps | 1 | 1-1 | 1, 1, 1 |
| resynced: advisor, frames | 1 | 1-1 | 1, 1, 1 |
| resynced: long step, save 1 | 92.2 | 89.8-93.0 | 92.2, 93.0, 89.8 |
| resynced: long step, catalogue 2 | 33.5 | 33.5-33.8 | 33.5, 33.8, 33.5 |
| resynced: long step, validation 7 | 19.7 | 19.4-19.9 | 19.7, 19.4, 19.9 |
| resynced: long step, validation 13 | 76.5 | 75.4-77.1 | 77.1, 75.4, 76.5 |
| resynced: long step, level 3 (to 0.008) | 15.0 | 14.9-15.0 | 14.9, 15.0, 15.0 |
| resynced: long step, level 82 (to 0.3) | 18.2 | 18.1-18.5 | 18.1, 18.2, 18.5 |
| resynced: long step, level 532 (to 0.35) | 24.1 | 23.8-24.4 | 23.8, 24.1, 24.4 |
| resynced: long step, level 534 (to 0.35) | 22.6 | 22.1-23.3 | 23.3, 22.1, 22.6 |
| resynced: long step, level 535 (to 0.35) | 19.5 | 19.4-20.3 | 20.3, 19.4, 19.5 |
| resynced: long step, level 536 (to 0.35) | 22.3 | 21.6-22.4 | 22.4, 21.6, 22.3 |
| resynced: long step, level 540 (to 0.35) | 16.3 | 16.3-16.8 | 16.3, 16.3, 16.8 |
| resynced: long step, level 545 (to 0.8) | 169 | 168-170 | 170, 168, 169 |
| resynced: long step, level 546 (to 0.85) | 84.5 | 84.0-85.7 | 85.7, 84.0, 84.5 |
| resynced: long step, level 548 (to 0.85) | 12.5 | 12.2-12.5 | 12.5, 12.5, 12.2 |
| resynced: long step, level 554 (to 0.85) (2 of 3 runs) | 17.1 | 17.0-17.3 | -, 17.0, 17.3 |
| resynced: long step, level 558 (to 0.85) | 15.0 | 14.9-15.0 | 15.0, 15.0, 14.9 |
| resynced: long step, level 560 (to 0.95) | 28.2 | 27.8-28.8 | 27.8, 28.2, 28.8 |
| resynced: long step, level 566 (to 0.95) | 15.8 | 15.7-15.9 | 15.9, 15.7, 15.8 |
| resynced: long step, level 567 (to 0.95) | 27.9 | 27.9-29.4 | 27.9, 29.4, 27.9 |
| resynced: long step, level 568 (to 1) | 30.8 | 30.7-31.2 | 30.7, 31.2, 30.8 |
| resynced: section coasters | 2.8 | 2.7-2.8 | 2.8, 2.7, 2.8 |
| resynced: section economy | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: section entities | 634 | 633-638 | 638, 633, 634 |
| resynced: section guest-sprites | 6.9 | 6.8-7.0 | 7.0, 6.8, 6.9 |
| resynced: section guests | 158 | 158-161 | 161, 158, 158 |
| resynced: section join-state | 0.1 | 0.0-0.1 | 0.1, 0.1, 0.0 |
| resynced: section level-hud | 4.5 | 4.4-4.5 | 4.5, 4.4, 4.5 |
| resynced: section model-parse | 15.2 | 15.2-15.3 | 15.2, 15.2, 15.3 |
| resynced: section object-catalog | 7.9 | 7.9-8.0 | 7.9, 8.0, 7.9 |
| resynced: section object-hoarding | 4.7 | 4.3-4.9 | 4.7, 4.9, 4.3 |
| resynced: section object-models | 61.2 | 60.6-61.6 | 61.2, 60.6, 61.6 |
| resynced: section object-runtime | 21.3 | 21.2-21.7 | 21.2, 21.3, 21.7 |
| resynced: section object-sign | 0.2 | 0.1-0.2 | 0.1, 0.2, 0.2 |
| resynced: section objects | 167 | 165-168 | 168, 165, 167 |
| resynced: section original-park | 23.2 | 23.1-23.3 | 23.2, 23.3, 23.1 |
| resynced: section paths | 0.1 | 0.1-0.2 | 0.1, 0.1, 0.2 |
| resynced: section pipeline | 8.6 | 8.2-10.3 | 8.2, 8.6, 10.3 |
| resynced: section resume | 26.2 | 26.1-26.7 | 26.1, 26.7, 26.2 |
| resynced: section ride-opcodes | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: section save-json | 81.9 | 78.7-82.7 | 81.9, 82.7, 78.7 |
| resynced: section save-mods | 0.0 | 0.0-0.0 | 0.0, 0.0, 0.0 |
| resynced: section save-shape | 10.3 | 10.2-11.1 | 10.2, 10.3, 11.1 |
| resynced: section staff-gate-economy | 58.3 | 58.0-59.7 | 58.3, 59.7, 58.0 |
| resynced: section staff-sprites | 15.8 | 15.7-15.9 | 15.9, 15.7, 15.8 |
| resynced: section terrain | 51.0 | 50.5-51.2 | 50.5, 51.2, 51.0 |
| resynced: section texture-create | 13.7 | 13.0-14.5 | 13.7, 14.5, 13.0 |
| resynced: longest update frame while building | 287 | 286-295 | 286, 295, 287 |
| resynced: the swap's update frame | 287 | 286-295 | 286, 295, 287 |
| resynced: first update frame after the swap | 48.7 | 48.6-50.0 | 50.0, 48.7, 48.6 |
| resynced: longest whole frame while building | 293 | 292-301 | 292, 301, 293 |
| resynced: the swap's whole frame | 293 | 292-301 | 292, 301, 293 |
| resynced: first whole frame after the swap | 51.8 | 51.7-53.3 | 53.3, 51.8, 51.7 |
| joined: the swap's frame, mark advisor.clipDurations | 2.9 | 2.9-3.2 | 2.9, 2.9, 3.2 |
| joined: the swap's frame, mark advisor.costume | 3.5 | 3.4-3.6 | 3.5, 3.6, 3.4 |
| joined: the swap's frame, mark advisor.decodeSlice | 5.3 | 5.0-5.4 | 5.0, 5.3, 5.4 |
| joined: the swap's frame, mark advisor.firstUpdate | 5.7 | 5.2-5.8 | 5.2, 5.7, 5.8 |
| joined: the swap's frame, mark advisor.loadResponse | 1.6 | 1.6-1.7 | 1.6, 1.6, 1.7 |
| joined: the swap's frame, mark advisor.play | 38.0 | 37.9-39.1 | 38.0, 37.9, 39.1 |
| joined: the swap's frame, mark advisor.selectRig | 24.6 | 24.4-25.0 | 24.6, 24.4, 25.0 |
| joined: the swap's frame, mark coop.session | 107 | 107-108 | 107, 107, 108 |
| joined: the swap's frame, mark level.entities | 173 | 172-176 | 176, 173, 172 |
| joined: the swap's frame, mark level.pools | 145 | 144-152 | 152, 145, 144 |
| joined: the swap's frame, mark level.simulation | 24.4 | 24.3-24.8 | 24.3, 24.4, 24.8 |
| joined: the swap's frame, mark load:image-decode | 3.7 | 3.7-3.8 | 3.7, 3.8, 3.7 |
| joined: the swap's frame, mark load:model-parse | 16.8 | 16.4-18.1 | 16.8, 16.4, 18.1 |
| joined: the swap's frame, mark load:pipeline | 15.4 | 14.6-16.1 | 16.1, 14.6, 15.4 |
| joined: the swap's frame, mark load:texture-create | 29.7 | 27.2-32.3 | 32.3, 29.7, 27.2 |
| joined: the swap's frame, mark load:texture-decode | 88.1 | 88.0-94.4 | 94.4, 88.0, 88.1 |
| joined: the swap's frame, mark load:wad-decompress | 4.7 | 4.2-4.7 | 4.2, 4.7, 4.7 |
| joined: the swap's frame, mark load:wad-read | 2.6 | 2.4-7.5 | 7.5, 2.4, 2.6 |
| joined: the swap's frame, mark park.advisor | 116 | 115-117 | 117, 115, 116 |
| joined: the swap's frame, mark park.hud | 5.9 | 5.6-5.9 | 5.9, 5.9, 5.6 |
| joined: the swap's frame, mark park.level | 344 | 342-354 | 354, 344, 342 |
| joined: the swap's frame, mark sim.guests | 2.0 | 1.7-2.3 | 1.7, 2.0, 2.3 |
| joined: the swap's frame, mark sim.objects | 6.2 | 6.0-7.0 | 6.2, 6.0, 7.0 |
| joined: the swap's frame, mark sim.sweptTurn | 9.0 | 8.8-9.3 | 9.0, 9.3, 8.8 |
| joined: the frame after the swap, mark advisor.decodeSlice | 5.1 | 5.1-5.2 | 5.1, 5.1, 5.2 |
| joined: the frame after the swap, mark audio.decodeSync | 4.2 | 4.2-4.4 | 4.2, 4.2, 4.4 |
| joined: the frame after the swap, mark level.entities | 12.1 | 12.0-12.1 | 12.1, 12.1, 12.0 |
| joined: the frame after the swap, mark level.simulation | 69.4 | 68.6-70.4 | 70.4, 69.4, 68.6 |
| joined: the frame after the swap, mark load:model-parse | 1.1 | 1.0-1.4 | 1.4, 1.1, 1.0 |
| joined: the frame after the swap, mark load:object-models | 52.8 | 52.7-53.7 | 53.7, 52.8, 52.7 |
| joined: the frame after the swap, mark load:pipeline | 1.8 | 1.8-2.1 | 1.8, 1.8, 2.1 |
| joined: the frame after the swap, mark load:place-cue | 4.6 | 4.5-4.7 | 4.5, 4.6, 4.7 |
| joined: the frame after the swap, mark load:place-object | 55.1 | 55.0-56.0 | 56.0, 55.1, 55.0 |
| joined: the frame after the swap, mark load:texture-create | 1.9 | 1.9-2.1 | 1.9, 2.1, 1.9 |
| joined: the frame after the swap, mark load:texture-decode | 41.6 | 41.5-42.8 | 42.8, 41.5, 41.6 |
| joined: the frame after the swap, mark load:wad-read | 0.5 | 0.4-4.8 | 0.5, 4.8, 0.4 |
| joined: the frame after the swap, mark park.advisor | 5.1 | 5.1-5.2 | 5.1, 5.1, 5.2 |
| joined: the frame after the swap, mark park.hud | 2.1 | 1.9-2.1 | 2.1, 1.9, 2.1 |
| joined: the frame after the swap, mark park.level | 81.6 | 80.7-82.6 | 82.6, 81.6, 80.7 |
| joined: the frame after the swap, mark sim.objects | 2.6 | 2.2-2.7 | 2.6, 2.7, 2.2 |
| resynced: the swap's frame, mark audio.decodeSync | 15.3 | 15.2-15.4 | 15.3, 15.2, 15.4 |
| resynced: the swap's frame, mark coop.session | 59.6 | 59.2-61.5 | 59.2, 61.5, 59.6 |
| resynced: the swap's frame, mark level.entities | 78.3 | 78.0-80.9 | 78.0, 80.9, 78.3 |
| resynced: the swap's frame, mark level.pools | 124 | 124-128 | 124, 128, 124 |
| resynced: the swap's frame, mark level.simulation | 23.7 | 23.6-23.8 | 23.8, 23.6, 23.7 |
| resynced: the swap's frame, mark load:pipeline | 2.1 | 1.9-2.2 | 2.1, 2.2, 1.9 |
| resynced: the swap's frame, mark load:texture-create | 20.8 | 20.1-21.6 | 20.8, 21.6, 20.1 |
| resynced: the swap's frame, mark park.level | 226 | 226-232 | 226, 232, 226 |
| resynced: the swap's frame, mark sim.rendering | 1.6 | 1.5-1.7 | 1.7, 1.5, 1.6 |
| resynced: the swap's frame, mark sim.sweptTurn | 2.7 | 2.7-2.8 | 2.8, 2.7, 2.7 |
| resynced: the frame after the swap, mark advisor.clipDurations | 1.9 | 1.9-2.0 | 2.0, 1.9, 1.9 |
| resynced: the frame after the swap, mark advisor.decodeSlice | 5.2 | 5.1-5.3 | 5.2, 5.3, 5.1 |
| resynced: the frame after the swap, mark advisor.firstUpdate | 5.2 | 5.1-5.3 | 5.2, 5.3, 5.1 |
| resynced: the frame after the swap, mark advisor.loadResponse | 1.2 | 1.2-1.3 | 1.2, 1.2, 1.3 |
| resynced: the frame after the swap, mark advisor.play | 16.3 | 16.1-16.8 | 16.8, 16.3, 16.1 |
| resynced: the frame after the swap, mark advisor.selectRig | 8.8 | 8.7-9.2 | 9.2, 8.7, 8.8 |
| resynced: the frame after the swap, mark level.cells | 3.5 | 3.5-3.6 | 3.6, 3.5, 3.5 |
| resynced: the frame after the swap, mark level.simulation | 9.0 | 8.6-9.1 | 9.0, 9.1, 8.6 |
| resynced: the frame after the swap, mark load:model-parse | 8.3 | 8.1-9.4 | 9.4, 8.1, 8.3 |
| resynced: the frame after the swap, mark load:pipeline | 2.1 | 2.0-2.5 | 2.1, 2.0, 2.5 |
| resynced: the frame after the swap, mark park.advisor | 35.3 | 35.2-36.0 | 36.0, 35.2, 35.3 |
| resynced: the frame after the swap, mark park.level | 13.0 | 12.7-13.2 | 13.2, 13.0, 12.7 |
| resynced: the frame after the swap, mark sim.objects | 3.7 | 3.6-3.9 | 3.6, 3.9, 3.7 |
| resynced: the frame after the swap, mark sim.sweptTurn | 1.1 | 1.0-1.1 | 1.1, 1.1, 1.0 |
| final check: update frame | 1547 | 1525-1556 | 1556, 1525, 1547 |
| final check: whole frame | 1548 | 1527-1558 | 1558, 1527, 1548 |
