namespace OpenTPW;

/// <summary>Optional host arrival policy for newly dispatched mechanic jobs; no native movement/timing claim.</summary>
public interface IMechanicJobWorld
{
	bool HasArrived( StaffMember member );

	/// <summary>Whether the member can start a new job: false for a mechanic standing off the path network (a drop on grass or a queue), who could never arrive.</summary>
	// [APPROX:ECON-054] a mechanic put down off the path network is not dispatched: the original's walk from such a cell to the network is untraced, and a reserved ride would wait for an arrival that cannot happen — evidence needed: what the mechanic state machine (0x4DA490) does from a cell off the paths
	bool CanStartJobs( StaffMember member ) => true;
}
