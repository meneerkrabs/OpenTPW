namespace OpenTPW;

/// <summary>
/// The tour-ride slice's approximation register (docs/TOURS.md). Each entry is tagged <c>[APPROX:TOUR-NNN]</c> at its code site; the words before the
/// colon name the topic. Everything else of the slice is traced from the Windows binary (BIN labels at the code sites).
/// </summary>
public static class TourApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("TOUR-005", "TOUR-GROUND: the cars clear the height map (.hmp) of the object on a cell, its byte x 100/255 plus the object's base height, found by the cell's footprint as tp.exe finds it by the owner word of a type-4, 9 or 10 cell and the class-3 thing there (0x527E80, 0x527D60); the original asks the cell's second-layer mesh instance first (0x53BF30: a track piece of a kart or water ride, or a park fence post, read through its own map +0xCC and height +0x44) and rebuilds a missing or mis-sized map from the mesh (0x00451880), which are not modelled, so those cells give the terrain; the map is read at the object's rotation, taken as in TOUR-003"),
		("TOUR-011", "TOUR-WALKON: the walking body is not drawn between the nodes; the walk table itself is the original's (RideVisitorBridge: WALKON 0x00556F40 takes the first free entry of the header walk size and sets state 1 for ftol(the distance between its nodes u1, class 0x800, and u2, class 0x80 for the head action 4, else 0x800) x 100 ms, 100 ms for 0; the walk update 0x00557AB0 that the main loop pass 0x0054E360 runs at 0x0054FA08, once per rendered frame after the park's substeps,, then 0x00557D80, moves 1 to 2 when the end has passed (the head action 4 hangs the head on head id u2, 0x0044B410; action 2 creates effect 0x13 at u2) and a WALKOFF 0x005571A0 entry from 3 to 4 after ftol(the distance between u3 and u4) x 100 ms, and WALKGET 0x00557110 hands out the first entry in state 4 of the table), but the update draws the body along its nodes (0x005580A0: the nodes' positions by the elapsed share, the height from the nodes when bit 0 of the last WALKON operand is set, which every shipped script does, else from the terrain 0x004511A0) and runs once per main loop pass, where OpenTPW runs it in the ride's host step; a node missing from the model makes the original log (0x00556D2C) and leave its stack words as they were, where OpenTPW takes no walk-on time and the ride's default walk-off time (1000 ms for a tour ride, else none)"),
	};
}
