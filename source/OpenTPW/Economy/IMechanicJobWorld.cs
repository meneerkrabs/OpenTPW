namespace OpenTPW;

/// <summary>Optional host arrival policy for newly dispatched mechanic jobs; no native movement/timing claim.</summary>
public interface IMechanicJobWorld
{
	bool HasArrived( StaffMember member );

	/// <summary>Whether the member can start a new job: false for a mechanic standing off the path network (a drop on grass or a queue), who could never arrive.</summary>
	// [BIN:TPW-EXE:0x00511420 staff route] in a level the staff route with the original's search and edge test from any cell (0x00511420, 0x004D8750; the walk mask +0x188 is 0 but for a strike walk and a guard's second route to the booth), claim a ride only after the route succeeds (0x004FA530, 0x004E01F0) and otherwise patrol or idle (0x004F9490)
	// [APPROX:ECON-054] a headless economy (no staff world) does not dispatch a mechanic put down off the path network (a drop on grass or a queue), its timed job having no walk — evidence needed: none: a headless economy keeps no map to walk (a level's staff world runs the original's route and ride claim)
	bool CanStartJobs( StaffMember member ) => true;
}
