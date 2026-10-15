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
		("TOUR-003", "TOUR-PAD: the launch pad is the pad node's position in the model's rest pose (the original reads the live node instance at TOUR 1 time) and the placement rotation is taken as equal to the original's instance angle (+0x10, degrees; the angle formula 0x00554385 is the original's); evidence needed: an original save with a placed tour ride, to set its rotation against a car's take-off direction"),
		("TOUR-005", "TOUR-GROUND: the cars clear the height map (.hmp) of the object on a cell, its byte x 100/255 plus the object's base height; the original also takes the cell's owner from 0x0053BF30 (track cells redirected), reads the object of a type-3 thing on the cell and rebuilds a missing or mis-sized map from the mesh (0x00451880), which are not modelled, so those cells give the terrain; the map is read at the object's rotation, taken as in TOUR-003"),
		("TOUR-007", "TOUR-WALKOFF: a tour ride's riders take one second to walk off after WALKOFF; the original takes ftol(the 3-D distance between the walker's two slot nodes) x 100 ms, 100 ms for a zero distance (0x005571A0; the start node is class 0x80 when the slot's +0x16 is 4, else 0x800, the end node class 0x800), and the Star and Tweety Tours unload loops depend on the walk not finishing at once; OpenTPW's WALKON keeps no walk nodes, so the walk owner has to supply them (evidence needed: the nodes a WALKON slot holds when WALKOFF runs)"),
		("TOUR-008", "TOUR-SHADOW: no shadow is drawn under the tour cars; whether the original draws one is not read (the car instance is created with flags 0x101 by 0x00463060, and the car draw 0x0055CE60 places only the instance and its emitter); the clips, the pose, the model axis, the angle formula, the per-frame smoothing and the riders' head nodes are the original's, with exact sines where the original reads its angle table"),
	};
}
