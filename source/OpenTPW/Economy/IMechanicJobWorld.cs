namespace OpenTPW;

/// <summary>Optional host arrival policy for newly dispatched mechanic jobs; no native movement/timing claim.</summary>
public interface IMechanicJobWorld
{
	bool HasArrived( StaffMember member );

	/// <summary>Whether the member can start a new job: false for a mechanic standing off the path network (a drop on grass or a queue), who could never arrive.</summary>
	// [APPROX:ECON-054] a headless economy (no staff world) does not dispatch a mechanic put down off the path network (a drop on grass or a queue), its timed job having no walk; in a level the staff route with the original's search and edge test from any cell (0x511420, 0x4D8750; the walk mask +0x188 is 0 but for a strike walk and a guard's second route to the booth), claim a ride only after the route succeeds (0x4FA530, 0x4E01F0) and otherwise patrol or idle (0x4F9490) — evidence needed: none for a level; a headless economy keeps no map to walk
	bool CanStartJobs( StaffMember member ) => true;
}
