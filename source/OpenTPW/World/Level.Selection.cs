namespace OpenTPW;

public partial class Level
{
	private OriginalObject? selection;

	/// <summary>
	/// The object this client has selected (tp.exe's selection <c>0x7C2654</c>): the object whose window is open, and the object a placement just made. It is the issuing player's own UI state, in no
	/// save, hash, digest or join state; the level's start clears it (<c>0x52F16F</c>) and the removal of the selected object does (<c>0x4DD2D6..0x4DD2E6</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x008186AC selected ride] the global 0x8186AC is the selection 0x7C2654 (0x4815A0 → 0x48C930), stored at 0x5277A1 between the test and the je, so it is 0 when nothing is selected; a selected add-on (+0x288 == 1) is replaced by its owner ride (0x527831); placement selects at creation (0x52A02E) and the object windows select through 0x5276D0 (0x487EE2, 0x48CD8B, 0x48CFC5, 0x4988EE, 0x4AF229, the script command 0x404E79); the click handler runs 0x5276D0(0) whenever [0x816D48] is 0 (0x524A51 → 0x524AF5), which Back clears itself (0x52FEE5) and replays (0x52FF3A, 0x52FFB2), so the value is the selection at the time of the Back; no save routine reads it
	internal OriginalObject? Selection
	{
		get => selection is { IsDeleted: false } ? selection : null;
		set
		{
			selection = value;
			NoteToolSelection( value );
		}
	}

	/// <summary>
	/// A select writes the track tool's globals: the exit and entry tiles of the ride the selection stands for (an add-on stands for its ride), mapped from its anchor and rotation as <c>0x52A3A0</c> and
	/// <c>0x52A420</c> do, whatever kind of object it is. Nothing resets them, so the bulldozer that comes later works with the last select's.
	/// </summary>
	// [BIN:TPW-EXE:0x005278B9 ride select writes the tool globals] the select 0x5276D0 stores the selected thing's anchor (+5, +7) in 0x7639E0/E4 and 0x7639E8/EC and maps them with 0x52A3A0 (exit) and 0x52A420 (entry) by its rotation (+0x10); an add-on selects its owner ride first (0x5277A6..0x5277F3)
	private void NoteToolSelection( OriginalObject? item )
	{
		if ( WindowTarget( item ) is not { } target || CoopSession != null )
			return;
		var station = trackStations != null && trackStations.TryGetValue( target, out var laid ) ? laid : TrackStation.FromPlacement( target.Placement.X, target.Placement.Y, target.Placement.Rotation );
		trackToolGlobals.Exit = station.Exit;
		trackToolGlobals.Entry = station.Entry;
	}

	/// <summary>The Buy Land tool's select: the gate post A goes into both pairs of the track tool's globals, so a track tile is no longer named by them (a fence post has none).</summary>
	// [BIN:TPW-EXE:0x0052773B fence select writes the tool globals] in the modes 0x19 and 0x1A the select 0x5276D0 stores the gate post 0x7639F0/F4 in 0x7639E0/E4 and 0x7639E8/EC (0x52772A..0x527776) before it reads any thing
	private void NoteFenceSelection( (int X, int Y) post )
	{
		if ( CoopSession != null )
			return;
		trackToolGlobals.Exit = post;
		trackToolGlobals.Entry = post;
	}

	/// <summary>The attraction id of the ride the selection stands for (an add-on stands for its ride, <see cref="WindowTarget"/>), or -1 when nothing, or no attraction, is selected.</summary>
	internal int SelectedRideId => WindowTarget( Selection ) is { Runtime.IsAttraction: true } ride ? ride.Runtime.AttractionId : -1;

	/// <summary>
	/// The re-evaluation Back runs after it pops a point of the path tool (<c>0x52FFE2..0x52FFEC</c>) on the ride the issuer had selected, which the command carries (<see cref="UndoLineCommand.Ride"/>):
	/// the queue is recomputed and its opening tests run. A ride that is gone, or holds no queue, is left as it is.
	/// </summary>
	private void ReevaluateSelectedRide( int attraction )
	{
		if ( attraction < 0 || Guests == null || ResolveCommandRide( attraction ) is not { } ride || !ride.HasCells )
			return;
		ride.RecomputeQueue( Guests.Grid );
		ride.NotifyQueueEdited( Guests.Grid );
	}
}
