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
		set => selection = value;
	}

	/// <summary>
	/// The issuing player's track tool globals (<see cref="TrackToolGlobals"/>): client-side tool state, in no save, hash or digest. A game keeps one for the whole process run (the executable's globals
	/// persist across park loads, so <see cref="GameFlow"/> hands every level the same), a level built elsewhere has its own. The demolishing and selling commands carry its tiles (<see cref="ToolTiles"/>).
	/// </summary>
	internal TrackToolGlobals ClientToolGlobals
	{
		get => clientToolGlobals ??= new();
		set => clientToolGlobals = value;
	}

	private TrackToolGlobals? clientToolGlobals;

	/// <summary>The client's globals as a command carries them.</summary>
	internal ToolTiles ClientTiles => new( ClientToolGlobals.Exit.X, ClientToolGlobals.Exit.Y, ClientToolGlobals.Entry.X, ClientToolGlobals.Entry.Y );

	/// <summary>
	/// The select <c>0x5276D0</c> as the park view's click on an object, the sell and erase buttons, the Move pick-up and the track button run it: the exit and entry tiles of the ride the object stands for
	/// (an add-on stands for its ride) go into the globals, mapped from its anchor and rotation as <c>0x52A3A0</c> and <c>0x52A420</c> do, whatever kind of object it is. During Buy Land the select takes the
	/// gate-post branch instead (<see cref="NoteFenceSelection"/>). A list row's info arm, the window's next and previous arrows and a placement's own selection of the new object are no select.
	/// </summary>
	// [BIN:TPW-EXE:0x005278B9 ride select writes the tool globals] the select 0x5276D0 stores the selected thing's anchor (+5, +7) in 0x7639E0/E4 and 0x7639E8/EC and maps them with 0x52A3A0 (exit) and 0x52A420 (entry) by its rotation (+0x10); an add-on selects its owner ride first (0x5277A6..0x5277F3); callers: the park view's click 0x487DC9/0x487F84/0x488096 (0x4879D0), the erase button 0x48CD95 (0x48CD10), the Move pick-up 0x48CFCF (0x48CFA0), the track button 0x4AF233 (0x4AF200), the click handler's modes 0x524AF6/0x524B6C and the placement commit 0x528F36/0x5296EA; the list-row arm and the arrows call only the selection list's setter 0x4815B0
	internal void NoteToolSelection( OriginalObject? item )
	{
		if ( BuyLandTool is { IsActive: true } && Guests?.Grid.Cells.Land is { } land )
		{
			NoteFenceSelection( land.PostA );
			return;
		}
		// an object built without its runtime (a layout rig) is no placed thing the select could map
		// the developer prototype (PlacedRide) is no placed thing of the park's catalogue: it has no entry the select could map
		if ( item is not { Runtime: not null } || item is PrototypeRide || WindowTarget( item ) is not { } target )
			return;
		var station = trackStations != null && trackStations.TryGetValue( target, out var laid ) ? laid : TrackStation.FromPlacement( target.Placement.X, target.Placement.Y, target.Placement.Rotation );
		ClientToolGlobals.Exit = station.Exit;
		ClientToolGlobals.Entry = station.Entry;
	}

	/// <summary>The select in the fence modes (Buy Land): the gate post A is the exit, and the entry is the post mapped by rotation 0 (<c>0x52A420</c>).</summary>
	// [BIN:TPW-EXE:0x0052773B fence select writes the tool globals] in the modes 0x19 and 0x1A the select 0x5276D0 stores the gate post 0x7639F0/F4 in 0x7639E0/E4 and 0x7639E8/EC (0x52772A..0x527776), pushes rotation 0 (0x527786) and jumps to 0x5278CD, where 0x52A420 maps the entry pair: (x - 2, y + 1); only the exit stays the raw post
	private void NoteFenceSelection( (int X, int Y) post )
	{
		ClientToolGlobals.Exit = post;
		ClientToolGlobals.Entry = (post.X - 2, post.Y + 1);
	}

	/// <summary>
	/// A placement commit's writes of the track tool globals (the placing player's): <c>0x528A70</c> resets the exit pair to (-1, -1) (<c>0x528ACE</c>), then selects the selection it finds (<c>0x528F36</c>,
	/// <c>0x5296EA</c>) unless the build tool of a track add-on is armed (<c>0x8187F8</c>, the type's <c>+0x9C</c> when <c>+0x288</c> is 1: <c>0x46D605..0x46D61D</c>); a kart or water ride's station line then stores its exit
	/// (<c>0x52A31C</c>), which the track tool's start writes here (<see cref="TrackEditor.UseLineToolExit"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x00528ACE placement resets the exit] 0x7639E0/E4 := (-1, -1) on every commit; the entry pair is left
	private void NoteToolPlacement( ObjectCatalogEntry entry )
	{
		ClientToolGlobals.Exit = (-1, -1);
		if ( TrackAddOns.IsTrackAddOn( entry ) && entry.Settings.GetInt( "Bumper.WhichTrackType", 0 ) != 0 )
			return;
		NoteToolSelection( Selection );
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
