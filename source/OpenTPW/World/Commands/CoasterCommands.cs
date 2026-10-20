namespace OpenTPW;

/// <summary>Which value of a pylon a drag of the coaster tool's panel changes (<see cref="EditCoasterNodeCommand"/>).</summary>
public enum CoasterNodeField : byte
{
	/// <summary>The height button: the loft parameter (and the height it gives).</summary>
	Loft,
	/// <summary>The rotate button: the yaw.</summary>
	Yaw,
	/// <summary>The tilt button: the bank.</summary>
	Bank,
	/// <summary>The tilt button: the incline.</summary>
	Incline,
	/// <summary>The tilt button on a loop pylon: the loop rotation.</summary>
	LoopRotate,
	/// <summary>The move button: the cell.</summary>
	Move
}

/// <summary>
/// What a click of the coaster tool did (<see cref="CoasterClickCommand"/>): the validator's verdict, whether the click completed the track (the tool then ends), the action message the park shows and
/// the tool state the placement left (the loop edit mode counts its pylons down), which the issuer's builder takes.
/// </summary>
public sealed record CoasterClickDone( CoasterPlaceResult Result, bool Completed, string Message, CoasterToolState Next );

/// <summary>
/// [EXT:online-coop-commands] The coaster tool's entry (<see cref="Level.EnterCoasterTool"/>) on the station whose attraction id is <paramref name="Station"/>: the ride closes, its controller
/// takes the tool's state, a closed circuit whose closing link fails opens again and the coaster is marked as being edited, so no guest boards it. The panel is the issuer's.
/// </summary>
public sealed record BeginCoasterToolCommand( int Station ) : ParkToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.BeginCoasterTool;
	internal override bool IsWellFormed => Station >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Station );
	internal override ParkCommandResult Execute( Level level ) => level.ExecuteBeginCoasterTool( Station );
}

/// <summary>
/// [EXT:online-coop-commands] The coaster tool's exit (<see cref="Level.ExitCoasterTool"/>) from the station whose attraction id is <paramref name="Station"/>: the issuing player's pylon edit there
/// ends where it stands, the coaster is no longer being edited and a ride the tool took out of service opens again.
/// </summary>
public sealed record EndCoasterToolCommand( int Station ) : ParkToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.EndCoasterTool;
	internal override bool IsWellFormed => Station >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Station );
	internal override ParkCommandResult Execute( Level level ) => level.ExecuteEndCoasterTool( Station, Player );
}

/// <summary>
/// [EXT:online-coop-commands] A command of the coaster tool's panel (<see cref="CoasterToolPanel"/>) and its Backspace: it acts on the builder of the coaster station whose attraction id is
/// <see cref="Station"/> and on the pylon edit the issuing player holds (<see cref="CoasterEdits"/>). The coaster must be in the tool (<see cref="CoasterRide.BeingEdited"/>), as it is whenever the
/// panel is open: its entry has let the riders off, which the cut and the edits rely on.
/// </summary>
public abstract record CoasterPanelCommand( int Station ) : ParkToolCommand
{
	internal override bool IsWellFormed => Station >= 0;

	internal sealed override ParkCommandResult Execute( Level level )
	{
		if ( level.ResolveCommandObject( Station ) is not { } station || level.GetCoasterBuilder( station ) is not { } builder )
			return ParkCommandResult.Rejected( $"Attraction {Station} is no coaster station." );
		if ( station.Runtime.Coaster is not { BeingEdited: true } )
			return ParkCommandResult.Rejected( $"The coaster of attraction {Station} is not in the coaster tool." );
		var result = Apply( builder, level.CoasterEdits );
		AfterApply( level, station, result );
		return result;
	}

	/// <summary>Runs the command on the station's builder and the players' edits: accepted, or rejected without a change.</summary>
	internal abstract ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits );

	/// <summary>What the level does after the builder's part (the click's message and a completion's exit), on every peer.</summary>
	private protected virtual void AfterApply( Level level, OriginalObject station, ParkCommandResult result ) { }

	/// <summary>Applies a panel command straight to a builder and its edits, outside a level's command funnel (the panel's tests); a malformed one or any other command is refused.</summary>
	internal static ParkCommandResult ApplyTo( ParkCommand command, CoasterBuilder builder, CoasterEdits edits ) =>
		command is not CoasterPanelCommand panel ? ParkCommandResult.Rejected( $"{command.Kind} is no coaster panel command." )
		: !panel.IsWellFormed ? ParkCommandResult.Rejected( $"The {command.Kind} command has fields out of range." )
		: panel.Apply( builder, edits );

	/// <summary>The edit the issuing player holds on this station, when its pylon can still be edited.</summary>
	private protected bool TryEdit( CoasterBuilder builder, CoasterEdits edits, out CoasterEdits.Held held )
	{
		held = null!;
		if ( edits.Of( Player ) is not { } mine || mine.Station != Station || !ReferenceEquals( mine.Builder, builder ) || !builder.CanEdit( mine.Edit.Index ) )
			return false;
		held = mine;
		return true;
	}
}

/// <summary>
/// [EXT:online-coop-commands] A click of the coaster tool's build buttons on cell (<paramref name="X"/>, <paramref name="Y"/>) for the station whose attraction id is <paramref name="Station"/>
/// (<see cref="Level.CoasterClick"/>): a click on the closed circuit's station end completes the track (it is rated and its trains start; the coaster is then no longer being edited and the ride
/// opens), any other places a pylon at the tool's <paramref name="Height"/> or closes the circuit on its open start (<see cref="CoasterBuilder.Place"/>). The loop edit mode and its pylons still to
/// place (<paramref name="LoopMode"/>, <paramref name="LoopPylonsLeft"/>) are the issuer's panel state the placement reads: the loop mode needs the loop button (<see cref="CoasterBuilder.LoopUnlocked"/>),
/// and a pylon after the first of a loop pair must be its second, as the panel's buttons allow. The builder of every peer keeps its own tool state. The value is <see cref="CoasterClickDone"/>.
/// </summary>
public sealed record CoasterClickCommand( int Station, int X, int Y, float Height, bool LoopMode, int LoopPylonsLeft ) : CoasterPanelCommand( Station )
{
	public override ParkCommandKind Kind => ParkCommandKind.CoasterClick;
	internal override bool IsWellFormed => Station >= 0 && IsCell( X, Y ) && float.IsFinite( Height ) && LoopPylonsLeft is >= 0 and <= CoasterBuilder.LoopPair && (LoopMode || LoopPylonsLeft == 0);
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Station );
		writer.Int( X );
		writer.Int( Y );
		writer.Float( Height );
		writer.Bool( LoopMode );
		writer.Int( LoopPylonsLeft );
	}

	internal override ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits )
	{
		var issued = new CoasterToolState( Height, LoopMode, LoopPylonsLeft );
		var track = builder.Track;
		if ( track.IsChainStart( X, Y ) && track.IsClosed )
		{
			var completed = builder.Complete();
			if ( completed != CoasterPlaceResult.Ok )
			{
				var refusal = $"Cannot complete the track: {completed}.";
				return new ParkCommandResult( false, refusal, new CoasterClickDone( completed, false, refusal, issued ) );
			}
			var rating = builder.Ride.Rating!.Value;
			return ParkCommandResult.Ok( new CoasterClickDone( completed, true,
				$"Coaster complete (track ${builder.Ride.PaidCost}): excitement {rating.Excitement:F0}, sickness {rating.Sickness:F0}, {rating.Trains} train(s).", issued ) );
		}
		var closing = track.IsChainStart( X, Y );
		// the panel's buttons: the loop button exists only with the loop upgrade, and the build button is off while a loop pair waits for its second pylon (the loop button resumes it at its second)
		if ( !closing && LoopMode && !builder.LoopUnlocked )
			return ParkCommandResult.Rejected( "The coaster takes no loop pylons." );
		if ( !closing && builder.LoopPairPending && !(LoopMode && LoopPylonsLeft == 1) )
			return ParkCommandResult.Rejected( "A loop pair waits for its second pylon." );
		var version = track.Version;
		var count = track.Nodes.Count;
		// the placement reads the issuer's tool state; this peer's builder keeps its own (its own panel's), and the issuer takes the state the placement left from the value
		var kept = builder.ToolState;
		CoasterPlaceResult result;
		CoasterToolState next;
		builder.SetToolState( issued );
		try
		{
			result = builder.Place( X, Y );
			next = builder.ToolState;
		}
		finally
		{
			builder.SetToolState( kept );
		}
		var message = closing && track.IsClosed ? "Closed the circuit; click the station end again to complete the coaster."
			: closing ? $"The circuit stays open: {result}."
			: track.Nodes.Count == count ? $"Cannot build a pylon here: {result}."
			: result == CoasterPlaceResult.Ok ? $"Placed a pylon (track cost so far ${builder.BuildCost})."
			: $"Placed a pylon marked invalid: {result}.";
		// a pylon marked invalid is placed (and charged) all the same: only a click that changed nothing is refused
		var changed = track.Version != version;
		return new ParkCommandResult( changed, changed ? null : message, new CoasterClickDone( result, false, message, next ) );
	}

	private protected override void AfterApply( Level level, OriginalObject station, ParkCommandResult result )
	{
		if ( result.Value is CoasterClickDone done )
			level.AfterCoasterClick( station, done );
	}
}

/// <summary>Backspace in the coaster tool: the last pylon of the open chain goes, with no refund (<see cref="CoasterBuilder.RemoveLast"/>); every pylon edit on the coaster is released first.</summary>
public sealed record RemoveLastCoasterPylonCommand( int Station ) : CoasterPanelCommand( Station )
{
	public override ParkCommandKind Kind => ParkCommandKind.RemoveLastCoasterPylon;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Station );

	internal override ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits )
	{
		if ( !builder.CanRemoveLast )
			return ParkCommandResult.Rejected( "No pylon to take back." );
		// a pylon list that changes under a drag would settle the drag against another pylon: every edit ends first
		edits.ReleaseAll( Station );
		return builder.RemoveLast() ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "No pylon to take back." );
	}
}

/// <summary>
/// The press of a drag in the coaster tool's panel on pylon <paramref name="Node"/> (<see cref="CoasterBuilder.BeginEdit"/>): the pylon must take the drag (<see cref="CoasterBuilder.DragRange"/>);
/// its cost (with the next pylon's on a finished track, for a move) is noted in the issuing player's edit with the drag and its range, which the drag's steps keep to and its release settles. An edit
/// the player still holds is released first. The loop tilt of the first pylon of a pair whose loop rotation is above 0.5 takes 1 off it, as the press does. The value is the edit.
/// </summary>
public sealed record BeginCoasterNodeEditCommand( int Station, int Node, CoasterDrag Drag ) : CoasterPanelCommand( Station )
{
	public override ParkCommandKind Kind => ParkCommandKind.BeginCoasterNodeEdit;
	internal override bool IsWellFormed => Station >= 0 && Node >= 0 && Drag <= CoasterDrag.LoopTilt;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Station );
		writer.Int( Node );
		writer.Byte( (byte)Drag );
	}

	internal override ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits )
	{
		if ( builder.DragRange( Node, Drag ) is not { } range )
			return ParkCommandResult.Rejected( $"Pylon {Node} takes no {Drag} drag." );
		// a drag never starts over another one: the player's edit still held ends where it stands (its pylon may share this one's stack, so the range is taken again)
		edits.Release( Player );
		range = builder.DragRange( Node, Drag ) ?? range;
		if ( builder.BeginEdit( Node, includeNext: Drag == CoasterDrag.Move ) is not { } edit )
			return ParkCommandResult.Rejected( $"Pylon {Node} cannot be edited." );
		// [BIN:TPW-EXE:0x441cd0 loop tilt] the first pylon of a pair whose loop rotation (+0xF8) is above 0.5 loses 1 at the press
		var node = builder.Track.Nodes[Node];
		if ( Drag == CoasterDrag.LoopTilt && (node.Flags & CoasterNodeFlags.LoopFirst) != 0 && node.LoopRotate > 0.5f )
			builder.EditLoopRotate( edit, node.LoopRotate - 1f );
		edits.Begin( Player, new CoasterEdits.Held( Station, builder, edit, Drag, range ) );
		return ParkCommandResult.Ok( edit );
	}
}

/// <summary>
/// One step of a drag in the coaster tool's panel: the issuing player's edited pylon takes <paramref name="Value"/> for <paramref name="Field"/> (the loft, yaw, bank, incline or loop rotation), or
/// for <see cref="CoasterNodeField.Move"/> stands on cell (<paramref name="X"/>, <paramref name="Y"/>). The field must be the drag's and the value within the range its press gave, as the panel clamps
/// it. No money moves until the release. The value of a move is the validator's verdict.
/// </summary>
public sealed record EditCoasterNodeCommand( int Station, CoasterNodeField Field, float Value, int X, int Y ) : CoasterPanelCommand( Station )
{
	public override ParkCommandKind Kind => ParkCommandKind.EditCoasterNode;
	internal override bool IsWellFormed => Station >= 0 && Field <= CoasterNodeField.Move && float.IsFinite( Value ) && IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Station );
		writer.Byte( (byte)Field );
		writer.Float( Value );
		writer.Int( X );
		writer.Int( Y );
	}

	internal override ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits )
	{
		if ( !TryEdit( builder, edits, out var held ) )
			return ParkCommandResult.Rejected( $"Player {Player} edits no pylon of attraction {Station}." );
		var range = held.Range;
		// the drag's own field, within its press's range; a tilt drags only a value whose control the pylon has (an open range)
		var allowed = (held.Drag, Field) switch
		{
			(CoasterDrag.Height, CoasterNodeField.Loft) => range.Holds( Value ),
			(CoasterDrag.Rotate, CoasterNodeField.Yaw) => range.Holds( Value ),
			(CoasterDrag.LoopTilt, CoasterNodeField.LoopRotate) => range.Holds( Value ),
			(CoasterDrag.Tilt, CoasterNodeField.Bank) => range.High > range.Low && range.Holds( Value ),
			(CoasterDrag.Tilt, CoasterNodeField.Incline) => range.HighSecond > range.LowSecond && range.HoldsSecond( Value ),
			(CoasterDrag.Move, CoasterNodeField.Move) => true,
			_ => false
		};
		if ( !allowed )
			return ParkCommandResult.Rejected( $"The {held.Drag} drag of pylon {held.Edit.Index} takes no {Field} of {Value}." );
		var edit = held.Edit;
		switch ( Field )
		{
			case CoasterNodeField.Loft:
				builder.EditLoft( edit, Value );
				break;
			case CoasterNodeField.Yaw:
				builder.EditYaw( edit, Value );
				break;
			case CoasterNodeField.Bank:
				builder.EditBank( edit, Value );
				break;
			case CoasterNodeField.Incline:
				builder.EditIncline( edit, Value );
				break;
			case CoasterNodeField.LoopRotate:
				builder.EditLoopRotate( edit, Value );
				break;
			default:
				return ParkCommandResult.Ok( builder.EditMove( edit, X, Y ) );
		}
		return ParkCommandResult.Ok();
	}
}

/// <summary>
/// The release of a drag in the coaster tool's panel: the issuing player's edit ends as its drag does (<see cref="CoasterEdits.End"/>: a height drag settles its money, a move when the pylon stands on
/// another cell, any other drag only fits the track again).
/// </summary>
public sealed record EndCoasterNodeEditCommand( int Station ) : CoasterPanelCommand( Station )
{
	public override ParkCommandKind Kind => ParkCommandKind.EndCoasterNodeEdit;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Station );

	internal override ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits )
	{
		if ( edits.Of( Player ) is not { } held || held.Station != Station )
			return ParkCommandResult.Rejected( $"Player {Player} edits no pylon of attraction {Station}." );
		// a pylon that went during the drag ends the drag where it stands, as the panel's drag does
		edits.End( Player );
		return ParkCommandResult.Ok();
	}
}

/// <summary>
/// The stack buttons' release over pylon <paramref name="Node"/>: it swaps places with the piece above (<paramref name="Up"/>) or below it in its cell's stack (<see cref="CoasterBuilder.SwapStack"/>);
/// every pylon edit on the coaster is released first.
/// </summary>
public sealed record SwapCoasterStackCommand( int Station, int Node, bool Up ) : CoasterPanelCommand( Station )
{
	public override ParkCommandKind Kind => ParkCommandKind.SwapCoasterStack;
	internal override bool IsWellFormed => Station >= 0 && Node >= 0;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Station );
		writer.Int( Node );
		writer.Bool( Up );
	}

	internal override ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits )
	{
		if ( !builder.CanSwapStack( Node, Up ) )
			return ParkCommandResult.Rejected( $"Pylon {Node} has no stack piece that way." );
		edits.ReleaseAll( Station );
		return builder.SwapStack( Node, Up ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( $"Pylon {Node} has no stack piece that way." );
	}
}

/// <summary>
/// The demolish button's second release: the nodes between pylons <paramref name="First"/> and <paramref name="Second"/> are cut out of the finished circuit (<see cref="CoasterBuilder.Cut"/>); every
/// pylon edit on the coaster is released first. The value is the cut's outcome.
/// </summary>
public sealed record CutCoasterCommand( int Station, int First, int Second ) : CoasterPanelCommand( Station )
{
	public override ParkCommandKind Kind => ParkCommandKind.CutCoaster;
	internal override bool IsWellFormed => Station >= 0 && First >= 0 && Second >= 0;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Station );
		writer.Int( First );
		writer.Int( Second );
	}

	internal override ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits )
	{
		if ( !builder.CanCut( First, Second ) )
			return new ParkCommandResult( false, "The circuit cannot be cut there.", CoasterTrack.CutResult.Refused );
		edits.ReleaseAll( Station );
		var result = builder.Cut( First, Second );
		return new ParkCommandResult( result != CoasterTrack.CutResult.Refused, result == CoasterTrack.CutResult.Refused ? "The circuit cannot be cut there." : null, result );
	}
}
