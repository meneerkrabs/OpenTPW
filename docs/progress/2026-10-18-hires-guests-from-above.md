## The hi-res guests are rendered from the original's camera (October 18, 2026)

The pools extension's Blender guests are the default look in TPW parks: the kids on land, the swimmers and the riders' heads. The kids and the swimmers are rendered again, at 45 degrees and 11.1 pixels per unit, where they used to be at 27 degrees and 11.7. The heads keep their old scale (`HEAD_PPU`), at which they match the original head sets, and their seven elevation rows do not depend on the body camera, so they are unchanged.

A new script, `tools/blender/opentpw_kid_elevation.py`, measures the camera. It renders the standing kid over a sweep of camera elevations and scales and compares its silhouettes with the original standing frames. The `.TPC` banks the game draws match best at 44 to 48 degrees. The `.FPC` banks match at 15 degrees or lower; the old calibration had been done against them, by eye.

So the two banks differ in camera, not in scale, and the generator's "TPC scale" (11.7 / 1.25) is gone. Our guests now look filmed from above, like the originals and the staff, as the player asked.

The feet hotspot is now the floor point itself (`LAND_HOTSPOT_DROP` 0). It had been one pixel lower to match the FPC's soles; from the TPC's camera, a standing kid's soles end 3 to 5 pixels below the hotspot in the originals and 2 to 5 in ours, so no correction is needed.

The frames from higher up are smaller: the eight kid atlases are now 1024 x 256 each (8 MiB of RGBA, was 15), and the shipped sprites take 8.25 MB on disk (was 8.84).
