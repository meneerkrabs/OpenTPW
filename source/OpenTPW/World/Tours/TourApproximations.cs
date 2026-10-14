namespace OpenTPW;

/// <summary>
/// The tour-ride slice's approximation register (docs/TOURS.md). Each entry is tagged <c>[APPROX:TOUR-NNN]</c> at its code site; the words before the
/// colon name the topic. Everything else of the slice is traced from the Windows binary (BIN labels at the code sites).
/// </summary>
public static class TourApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("TOUR-001", "TOUR-TICK: the controller ticks at 1000/31 steps per second from the game clock; the original's world step is a 31 ms catch-up loop on the wall clock (0x0054E360), whose phase against the script loop is not modelled"),
		("TOUR-002", "TOUR-EJECT: riders a severe breakdown (TOUR 9 4) threw off a flying car are handed out at the station through TOUR 4 like unloaded riders; the original drops them from the car without any hand-over and what returns them to the park is not traced"),
		("TOUR-003", "TOUR-PAD: the launch pad is read from the model's rest pose (node class 0x800, number 99) and the placement rotation is taken as the original's angle word (degrees x 4096 / 360); the sign of that angle against OpenTPW's rotation direction is not confirmed on a placed ride"),
		("TOUR-004", "TOUR-FX: the cars' engine sounds, the breakdown smoke (events 0x27 and 0x28), the repair sound and the spawn sound of the original are not played; the car mesh animations are tracked (car.Animation) but not played, see TOUR-008"),
		("TOUR-005", "TOUR-GROUND: the ground the cars avoid is the terrain's cell-corner height truncated to whole model units; the original's lookup also returns the top of a building standing on the cell (thing type 3)"),
		("TOUR-006", "TOUR-LEGACY: a park-resume save from before the tour controllers restores each live tour ride with a fresh controller (one car at the station, ready) as if its script had just run TOUR 1; nobody was aboard in such a save"),
		("TOUR-007", "TOUR-WALKOFF: a tour ride's riders take one second to walk off after WALKOFF; the original's walk-off nodes and their duration are not traced, and a walker that completes at once makes the unload loops of the Star and Tweety Tours scripts end after one rider"),
		("TOUR-008", "TOUR-DRAW: the cars are drawn from the ride's supplemental mesh 0 at the controller's pose, riders as head sprites at the mesh's head dummies; the mesh's forward axis (taken as +X), the signs of pitch and roll, the car animations (ids 0, 3, 4, 5, 6, 9, 10) and the car shadow are assumptions or not drawn"),
	};
}
