namespace OpenTPW;

/// <summary>Optional host arrival policy for newly dispatched mechanic jobs; no native movement/timing claim.</summary>
public interface IMechanicJobWorld
{
	bool HasArrived( StaffMember member );

	/// <summary>Whether the member can start a new job: false for a mechanic standing off the path network (a drop on grass or a queue), who could never arrive.</summary>
	// [APPROX:ECON-054] a mechanic put down off the path network (a drop on grass or a queue) is not dispatched, and the staff routes run over walkable path-grid cells only; the original searches from any cell (0x4DA5B0), routes with the step test 0x4D8750, which lets a walker go grass to grass and grass to path (onto a queue only along its links; type 4, types 2/5/7 and the map edge block), claims the ride only after the route succeeds (0x4FA530, 0x4E01F0) and otherwise patrols or idles (0x4F9490) — evidence needed: the walker 0x511470's outcome on particular layouts and the mechanic walker's mode (staff +0x188)
	bool CanStartJobs( StaffMember member ) => true;
}
