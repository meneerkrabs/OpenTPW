namespace OpenTPW;

/// <summary>
/// The tour-ride slice's approximation register (docs/TOURS.md). Each entry is tagged <c>[APPROX:TOUR-NNN]</c> at its code site; the words before the
/// colon name the topic. Everything else of the slice is traced from the Windows binary (BIN labels at the code sites).
/// </summary>
public static class TourApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("TOUR-002", "TOUR-EJECT: riders a severe breakdown (TOUR 9 4) threw off a flying car are handed out at the station through TOUR 4 like unloaded riders, which also lowers VAR_ONRIDE; the original removes their head sprites and drops the ids (the loop in 0x0055DA70 discards what 0x0055D720 returns), the script never releases them and VAR_ONRIDE keeps counting them, so every thrown-off rider inflates the ride's rider count; what the guest system does with a boarded guest that nothing releases is not traced"),
		("TOUR-005", "TOUR-GROUND: the cars clear the height map (.hmp) of the object on a cell, its byte x 100/255 plus the object's base height; the original also takes the cell's owner from 0x0053BF30 (track cells redirected), reads the object of a type-3 thing on the cell and rebuilds a missing or mis-sized map from the mesh (0x00451880), which are not modelled, so those cells give the terrain; the map is read at the object's rotation, taken as in TOUR-003"),
		("TOUR-008", "TOUR-SHADOW: no shadow is drawn under the tour cars; the car draw 0x0055CE60 places only the instance and its emitter, the MESHSHADOW flag (0x200 of 0x0079D86C) has no reader (none of the 26 references to that word tests it) and the instance's vertex writer 0x00580530 holds no shadow code, but the draw that consumes its vertices (the batcher 0x00581DB0) was not read for a shadow pass; the clips, the pose (the engine's sine table), the model axis, the angle formula, the per-frame smoothing and the riders' head nodes are the original's"),
	};
}
