namespace OpenTPW;

/// <summary>
/// The tour-ride slice's approximation register (docs/TOURS.md). Each entry is tagged <c>[APPROX:TOUR-NNN]</c> at its code site; the words before the
/// colon name the topic. Everything else of the slice is traced from the Windows binary (BIN labels at the code sites).
/// </summary>
public static class TourApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("TOUR-005", "TOUR-GROUND: the cars clear the height map (.hmp) of the object on a cell, its byte x 100/255 plus the object's base height; the original also takes the cell's owner from 0x0053BF30 (track cells redirected), reads the object of a type-3 thing on the cell and rebuilds a missing or mis-sized map from the mesh (0x00451880), which are not modelled, so those cells give the terrain; the map is read at the object's rotation, taken as in TOUR-003"),
		("TOUR-008", "TOUR-SHADOW: no shadow is drawn under the tour cars; the car draw 0x0055CE60 places only the instance and its emitter, the MESHSHADOW flag (0x200 of 0x0079D86C) has no reader (none of the 26 references to that word tests it) and the instance's vertex writer 0x00580530 holds no shadow code, but the draw that consumes its vertices (the batcher 0x00581DB0) was not read for a shadow pass; the clips, the pose (the engine's sine table), the model axis, the angle formula, the per-frame smoothing and the riders' head nodes are the original's"),
		("TOUR-011", "TOUR-WALKON: WALKON boards a tour rider at once; the original WALKON takes ftol(the 3-D distance between its nodes u1 and u2) x 100 ms (100 ms for 0, the start node of class 0x80 for the head action 4; 0x00556F40 stores the walk table entry's start and end times at +0x08 and +0x0C, state 1); the WALKOFF action 2 effect (type 0x13 at the start node, each coordinate through ftol and shifted left by 10, 0x00521E60 at 0x005572EB) is built (RideVisitorBridge.WalkOffEffect); what reads the entry's end time and moves its state to 4 (the state WALKGET 0x00557110 hands out) is not read, so what the walk-on time gates is unknown"),
	};
}
