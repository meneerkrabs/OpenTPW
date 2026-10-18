## Guests and staff are drawn from the bank the game draws, seen from above (October 18, 2026)

Every original sprite set (kids, costumes, staff and entertainers) is now drawn from its `.TPC` bank, as tp.exe does. Before this change, OpenTPW drew the `.FPC` bank of the 27 sets that ship one.

The FPC is the same art rendered from a lower camera. The widths match and the frames are taller: a standing handyman is 58 to 62 px tall in the FPC and 49 to 53 in the TPC. That made OpenTPW's people look filmed from the side, where the original's look filmed from above, as the player noticed.

tp.exe's ESP loader opens the FPC only with a sprite-detail flag. The only writer of that flag is passed 0 by both of its calls, and TPI's Game.exe does the same. This closes `GUEST-022`, and the staff tools' dangling person needs no special case any more.

A texture pack built before this change holds upscaled atlases of the FPC frames, so the atlases of the sets that ship an FPC now use a `.tpc.atlas` key. Those guests show unupscaled until the pack is rebuilt.

The hi-res Blender guests are not re-rendered yet. They were made at 27 degrees and at 1.25 times the TPC scale, on the earlier reading.
