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

/// <summary>How a drag of the coaster tool's panel ends (<see cref="EndCoasterNodeEditCommand"/>).</summary>
public enum CoasterEditEnd : byte
{
	/// <summary>Without money (rotate, tilt): a finished track is fitted again (<see cref="CoasterBuilder.FinishEdit"/>).</summary>
	Finish,
	/// <summary>The height drag: the cost noted at the press is refunded and the new cost charged (<see cref="CoasterBuilder.EndEdit"/>).</summary>
	Settle,
	/// <summary>The move drag: settled when the pylon stands on another cell than the press found it on, else only fitted again.</summary>
	SettleIfMoved
}

/// <summary>
/// What a click of the coaster tool did (<see cref="CoasterClickCommand"/>): the validator's verdict, whether the click completed the track (the tool then ends) and the action message the park
/// shows.
/// </summary>
public sealed record CoasterClickDone( CoasterPlaceResult Result, bool Completed, string Message );

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
/// [EXT:online-coop-commands] The coaster tool's exit (<see cref="Level.ExitCoasterTool"/>) from the station whose attraction id is <paramref name="Station"/>: the coaster is no longer being
/// edited and a ride the tool took out of service opens again.
/// </summary>
public sealed record EndCoasterToolCommand( int Station ) : ParkToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.EndCoasterTool;
	internal override bool IsWellFormed => Station >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Station );
	internal override ParkCommandResult Execute( Level level ) => level.ExecuteEndCoasterTool( Station );
}

/// <summary>
/// [EXT:online-coop-commands] A command of the coaster tool's panel (<see cref="CoasterToolPanel"/>) and its Backspace: it acts on the builder of the coaster station whose attraction id is
/// <see cref="Station"/> and on the pylon edit the issuing player holds (<see cref="CoasterEdits"/>).
/// </summary>
public abstract record CoasterPanelCommand( int Station ) : ParkToolCommand
{
	internal override bool IsWellFormed => Station >= 0;

	internal sealed override ParkCommandResult Execute( Level level )
	{
		if ( level.ResolveCommandObject( Station ) is not { } station || level.GetCoasterBuilder( station ) is not { } builder )
			return ParkCommandResult.Rejected( $"Attraction {Station} is no coaster station." );
		var result = Apply( builder, level.CoasterEdits );
		AfterApply( level, station, result );
		return result;
	}

	/// <summary>Runs the command on the station's builder and the players' edits: accepted, or rejected without a change.</summary>
	internal abstract ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits );

	/// <summary>What the level does after the builder's part (the click's message and a completion's exit), on every peer.</summary>
	private protected virtual void AfterApply( Level level, OriginalObject station, ParkCommandResult result ) { }

	/// <summary>Applies a panel command straight to a builder and its edits, outside a level's command funnel (the panel's tests); any other command is refused.</summary>
	public static ParkCommandResult ApplyTo( ParkCommand command, CoasterBuilder builder, CoasterEdits edits ) =>
		command is CoasterPanelCommand panel ? panel.Apply( builder, edits ) : ParkCommandResult.Rejected( $"{command.Kind} is no coaster panel command." );

	/// <summary>The edit the issuing player holds on this station, when its pylon can still be edited.</summary>
	private protected bool TryEdit( CoasterBuilder builder, CoasterEdits edits, out CoasterBuilder.NodeEdit edit )
	{
		edit = null!;
		if ( edits.Of( Player ) is not { } held || held.Station != Station || !builder.CanEdit( held.Edit.Index ) )
			return false;
		edit = held.Edit;
		return true;
	}
}

/// <summary>
/// [EXT:online-coop-commands] A click of the coaster tool's build buttons on cell (<paramref name="X"/>, <paramref name="Y"/>) for the station whose attraction id is <paramref name="Station"/>
/// (<see cref="Level.CoasterClick"/>): a click on the closed circuit's station end completes the track (it is rated and its trains start; the coaster is then no longer being edited and the ride
/// opens), any other places a pylon at the tool's <paramref name="Height"/> or closes the circuit on its open start (<see cref="CoasterBuilder.Place"/>). The loop edit mode and its pylons still to
/// place (<paramref name="LoopMode"/>, <paramref name="LoopPylonsLeft"/>) are the issuer's panel state the placement reads. The value is <see cref="CoasterClickDone"/>.
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
		builder.SetToolState( Height, LoopMode, LoopPylonsLeft );
		var track = builder.Track;
		if ( track.IsChainStart( X, Y ) && track.IsClosed )
		{
			var completed = builder.Complete();
			if ( completed != CoasterPlaceResult.Ok )
			{
				var refusal = $"Cannot complete the track: {completed}.";
				return new ParkCommandResult( false, refusal, new CoasterClickDone( completed, false, refusal ) );
			}
			var rating = builder.Ride.Rating!.Value;
			return ParkCommandResult.Ok( new CoasterClickDone( completed, true,
				$"Coaster complete (track ${builder.Ride.PaidCost}): excitement {rating.Excitement:F0}, sickness {rating.Sickness:F0}, {rating.Trains} train(s)." ) );
		}
		var closing = track.IsChainStart( X, Y );
		var version = track.Version;
		var count = track.Nodes.Count;
		var result = builder.Place( X, Y );
		var message = closing && track.IsClosed ? "Closed the circuit; click the station end again to complete the coaster."
			: closing ? $"The circuit stays open: {result}."
			: track.Nodes.Count == count ? $"Cannot build a pylon here: {result}."
			: result == CoasterPlaceResult.Ok ? $"Placed a pylon (track cost so far ${builder.BuildCost})."
			: $"Placed a pylon marked invalid: {result}.";
		// a pylon marked invalid is placed (and charged) all the same: only a click that changed nothing is refused
		var changed = track.Version != version;
		return new ParkCommandResult( changed, changed ? null : message, new CoasterClickDone( result, false, message ) );
	}

	private protected override void AfterApply( Level level, OriginalObject station, ParkCommandResult result )
	{
		if ( result.Value is CoasterClickDone done )
			level.AfterCoasterClick( station, done );
	}
}

/// <summary>Backspace in the coaster tool: the last pylon of the open chain goes, with no refund (<see cref="CoasterBuilder.RemoveLast"/>).</summary>
public sealed record RemoveLastCoasterPylonCommand( int Station ) : CoasterPanelCommand( Station )
{
	public override ParkCommandKind Kind => ParkCommandKind.RemoveLastCoasterPylon;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Station );
	internal override ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits ) =>
		builder.RemoveLast() ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "No pylon to take back." );
}

/// <summary>
/// The press of a drag in the coaster tool's panel on pylon <paramref name="Node"/> (<see cref="CoasterBuilder.BeginEdit"/>): the pylon and its cost (with the next one's on a finished track,
/// <paramref name="IncludeNext"/>, for a move) are noted in the issuing player's edit, which the drag's commands change and its release settles.
/// </summary>
public sealed record BeginCoasterNodeEditCommand( int Station, int Node, bool IncludeNext ) : CoasterPanelCommand( Station )
{
	public override ParkCommandKind Kind => ParkCommandKind.BeginCoasterNodeEdit;
	internal override bool IsWellFormed => Station >= 0 && Node >= 0;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Station );
		writer.Int( Node );
		writer.Bool( IncludeNext );
	}

	internal override ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits )
	{
		if ( builder.BeginEdit( Node, IncludeNext ) is not { } edit )
			return ParkCommandResult.Rejected( $"Pylon {Node} cannot be edited." );
		edits.Begin( Player, Station, edit );
		return ParkCommandResult.Ok( edit );
	}
}

/// <summary>
/// One step of a drag in the coaster tool's panel: the issuing player's edited pylon takes <paramref name="Value"/> for <paramref name="Field"/> (the loft, yaw, bank, incline or loop rotation), or
/// for <see cref="CoasterNodeField.Move"/> stands on cell (<paramref name="X"/>, <paramref name="Y"/>). No money moves until the release. The value of a move is the validator's verdict.
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
		if ( !TryEdit( builder, edits, out var edit ) )
			return ParkCommandResult.Rejected( $"Player {Player} edits no pylon of attraction {Station}." );
		// the pylon's controls decide which values a drag may change, as the panel's press does
		var node = builder.Track.Nodes[edit.Index];
		var capabilities = builder.Track.Definition.Pylon( node.LoopPylon ).Capabilities;
		var loop = (node.Flags & CoasterNodeFlags.Loop) != 0;
		var allowed = Field switch
		{
			CoasterNodeField.Loft => (capabilities & CoasterPylonCapabilities.Loft) != 0,
			CoasterNodeField.Yaw => (capabilities & CoasterPylonCapabilities.Rotate) != 0,
			CoasterNodeField.Bank => !loop && (capabilities & CoasterPylonCapabilities.Bank) != 0,
			CoasterNodeField.Incline => !loop && (capabilities & CoasterPylonCapabilities.Incline) != 0,
			CoasterNodeField.LoopRotate => loop && (capabilities & CoasterPylonCapabilities.LoopTilt) != 0,
			_ => true
		};
		if ( !allowed )
			return ParkCommandResult.Rejected( $"Pylon {edit.Index} takes no {Field} drag." );
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

/// <summary>The release of a drag in the coaster tool's panel: the issuing player's edit ends as <paramref name="End"/> says (the money of a height or move drag settles) and is dropped.</summary>
public sealed record EndCoasterNodeEditCommand( int Station, CoasterEditEnd End ) : CoasterPanelCommand( Station )
{
	public override ParkCommandKind Kind => ParkCommandKind.EndCoasterNodeEdit;
	internal override bool IsWellFormed => Station >= 0 && End <= CoasterEditEnd.SettleIfMoved;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Station );
		writer.Byte( (byte)End );
	}

	internal override ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits )
	{
		if ( edits.Of( Player ) is not { } held || held.Station != Station )
			return ParkCommandResult.Rejected( $"Player {Player} edits no pylon of attraction {Station}." );
		edits.End( Player );
		// a pylon that went during the drag (deleted meanwhile) ends the drag where it stands, as the panel's drag does
		if ( !builder.CanEdit( held.Edit.Index ) )
			return ParkCommandResult.Ok();
		var edit = held.Edit;
		var node = builder.Track.Nodes[edit.Index];
		var settle = End == CoasterEditEnd.Settle || (End == CoasterEditEnd.SettleIfMoved && (node.X, node.Y) != (edit.Start.X, edit.Start.Y));
		if ( settle )
			builder.EndEdit( edit );
		else
			builder.FinishEdit();
		return ParkCommandResult.Ok();
	}
}

/// <summary>The stack buttons' release over pylon <paramref name="Node"/>: it swaps places with the piece above (<paramref name="Up"/>) or below it in its cell's stack (<see cref="CoasterBuilder.SwapStack"/>).</summary>
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

	internal override ParkCommandResult Apply( CoasterBuilder builder, CoasterEdits edits ) =>
		builder.SwapStack( Node, Up ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( $"Pylon {Node} has no stack piece that way." );
}

/// <summary>The demolish button's second release: the nodes between pylons <paramref name="First"/> and <paramref name="Second"/> are cut out of the finished circuit (<see cref="CoasterBuilder.Cut"/>). The value is the cut's outcome.</summary>
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
		var result = builder.Cut( First, Second );
		return new ParkCommandResult( result != CoasterTrack.CutResult.Refused, result == CoasterTrack.CutResult.Refused ? "The circuit cannot be cut there." : null, result );
	}
}
