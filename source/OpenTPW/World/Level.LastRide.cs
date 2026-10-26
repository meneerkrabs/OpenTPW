namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// The object a placement or a queue edit last left for the path tool's Back to re-evaluate: its attraction id. tp.exe keeps it in the global 0x8186AC, which only the level's start clears
	/// (0x52F16F) and no save writes. Session state, as the ride claims are: in the session digest once set and in a join snapshot, never in the world hash or a save. Null until the first
	/// placement or queue edit.
	/// </summary>
	// [BIN:TPW-EXE:0x008186AC last ride] the writers are the placement's setup (0x524C3D: the placed thing), the pick tool's selection (0x5277A1) and its owner substitution (0x527831); Back reads it when its argument is 0, as the path tool's Back passes (0x40BEB3, read at 0x52FFE2), and runs the queue re-evaluation 0x4DE1F0 on it after a pop of the modes 1, 3 and 0x34 (0x52FF04, 0x52FFEC); no save routine reads it
	private int? lastRide;

	/// <summary>The attraction id of the last placed or edited object (<see cref="lastRide"/>), or null.</summary>
	internal int? LastRide => lastRide;

	/// <summary>Records <paramref name="item"/> as the last ride (0x524C3D, 0x5277A1).</summary>
	private void NoteLastRide( OriginalObject? item )
	{
		if ( item?.Runtime != null )
			lastRide = item.Runtime.AttractionId;
	}

	/// <summary>
	/// The re-evaluation Back runs after it pops a point of the path tool (0x52FFE2..0x52FFEC): the queue of the last ride is recomputed and its opening tests run, as for the cell clear's
	/// re-evaluation. An object that is no longer there, or holds no queue, is left as it is.
	/// </summary>
	private void ReevaluateLastRide()
	{
		if ( lastRide is not { } id || Guests == null || ResolveCommandRide( id ) is not { } ride || !ride.HasCells )
			return;
		ride.RecomputeQueue( Guests.Grid );
		ride.NotifyQueueEdited( Guests.Grid );
	}
}
