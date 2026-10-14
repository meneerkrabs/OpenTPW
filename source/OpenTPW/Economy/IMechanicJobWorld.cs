namespace OpenTPW;

/// <summary>Optional host arrival policy for newly dispatched mechanic jobs; no native movement/timing claim.</summary>
public interface IMechanicJobWorld
{
	bool HasArrived( StaffMember member );
}
