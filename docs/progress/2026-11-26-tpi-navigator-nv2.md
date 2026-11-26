## Theme Park Inc: the navigator object (November 26, 2026)

The second phase of the Theme Park Inc navigator port (NV2): the walk object now holds every member of the original's navigator, shared by Theme Park World and Theme Park Inc.

- **The members.** OpenTPW's walk gains the navigator members it lacked: mass, the radius as a member, the speed caps (moved into the walk object, the guest and the staff member forward to them), the local axes the steering step writes after each commit, the formation position and force (no simulation writer; passed through), the five subpath distances of the set-up, which follow_path now reads as the original does, the written waypoint slots and the whole progress history word.
- **The record.** The navigator's 177-byte serializer (Theme Park Inc `0x63AD30`, Theme Park World `0x50D150`) has one reader and writer: every member in alphabetical order with its exact width. The walk maps onto it both ways, and this is the record the native park writer will store.
- **Theme Park Inc's arithmetic.** Theme Park Inc's walk takes its square root as the game thread's 24-bit x87 rounds it, which differs by one unit on some lengths and axes. Theme Park World's walk keeps its exact root for now, so its results, digests and saves do not change; its oracle ran at a wider precision than the game thread's, and reading it at 24 bits is a recorded follow-up.
- **The switch.** Under the hidden `--tpi-native-walk` the new members join the digest and the guest and staff saves carry the navigator's record in an optional member, also while a guest stands or rides, since the original saves its one navigator object whole; a record that disagrees with the walk, or one in a save without the switch, is refused. Off, nothing changes.
- **The oracle.** New x86 targets run Game.exe's navigator constructor, set-up and steering step through whole walks (every byte of the record after each step), avoid_walls, the square root and both halves of the serializer; all agree with the port. The C# meets three of Game.exe's walks directly, and Theme Park World's steering oracle now compares every member too.

No new approximation or label is added, and no C runtime `rand()` is reached.

Verified with unit tests that hold Game.exe's records for three walks, load and save each record unchanged, continue a loaded walk, load and write back the navigators of the shipped Jungle save and eleven more original saves byte for byte, and resume native guests (mid-walk and standing) and a native handyman with identical digests; the new and existing oracle targets; the Theme Park World walk, guest and pin suites; and the silent, offscreen Science smoke with the switch off and on.

The original game was not launched, and no binary or data is included.
