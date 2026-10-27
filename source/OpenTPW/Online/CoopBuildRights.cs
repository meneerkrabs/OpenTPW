namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-permissions] A shared park's rights (docs/COOP.md, "Building by every player"; the user's decision Q7: the host sets rights per player). The host's seat 0 may issue
/// every command but an edit of a ride another player claims; a guest's seat may issue the building the host granted it (<see cref="SetBuildGrantCommand"/>, kept in the park on every
/// peer) and nothing else. The building is an allowlist over a command's kind, its fields and its target, the commands of the guests' tools that wait for the host or send and go on:
/// <list type="bullet">
/// <item>C3a: path and queue lines; an object placed (not a moving placement, not a track add-on, whatever ride the command names, not a ride whose own tool follows its placement: a
/// coaster, a kart or water ride, until C3c); the remove click on an ordinary object (not a fixed item of the park);</item>
/// <item>C3b: land; the area clear, which leaves standing what the guest may not remove (<see cref="AllowsRemoval"/>); the remove click on a path or queue cell; Backspace over a
/// path or queue cell; the sale of an ordinary object; the queue's edit; the Move tool's pick-up of an object it may remove and place again, its placement, and its end, which every
/// guest seat may issue, granted or not;</item>
/// <item>C3b-3: the pool tools' batches of a stroke (<see cref="PoolCellsCommand"/>, <see cref="LayPoolSlidePiecesCommand"/>), a bridge, the last piece of an unfinished slide back,
/// Backspace over a bridge or a pool cell, and slide towers, which a guest now places and removes as any object;</item>
/// <item>C3c-1: a track ride's claim (<see cref="ClaimRideCommand"/>) and the track commands of the ride whose claim the guest holds; the release of its own claim
/// (<see cref="ReleaseRideCommand"/>), which every guest seat may issue, granted or not; bumper rides, which a guest now places (their queue tool follows); and since C3c-1b kart and
/// water rides, whose track tool follows through the claim;</item>
/// <item>C3c-2: a coaster's claim and the coaster tool's commands of the coaster whose claim the guest holds (its entry and end, <see cref="CoasterPanelCommand"/>); coasters, which a guest now
/// places (the coaster tool follows through the claim); and a kart or water ride's track add-on on its own ride (<see cref="GuestMayPlaceAddOn"/>).</item>
/// </list>
/// [EXT:online-coop-claims] A claim guards its ride against every other player, the host included (<see cref="LeavesClaimsAlone"/>): its track (a coaster's tool and pylons), its sale, its
/// removal and its pick-up, its demolition by a placement, an add-on on it, its queue's lines, edit and cells, the path cell its back joins, and a path cell linked into its entrance, whose
/// clear re-evaluates it. The host's cleanup leaves the claims alone by itself: its release of any claim, a departed seat's release and the grants.
/// Everything else stays the host's: speed and pause (Q8), cheats (Q17), the developer kinds, money, staff and the park's management (C4) with a wave machine's settings, undo lines and
/// pool strokes (C5), the pools' single-cell kinds no guest's tool issues, the rights, the grants and the release of a departed seat. Every command kind is of one class
/// (<see cref="ClassOf"/>, C4a), which says who may issue it; the management rights (<see cref="CoopRights"/>) take their classes once a part offers them (<see cref="CoopRightsOffer"/>).
/// Asked at execution on every peer, of the same park, the answer is the same everywhere.
/// </summary>
internal sealed class CoopBuildRights( Level level ) : IParkCommandRights
{
	public bool Allows( int player, ParkCommandKind kind ) => player == ParkCommands.LocalPlayer;

	public bool Allows( int player, ParkCommand command )
	{
		// [EXT:online-coop-claims] the host issues everything but an edit of a ride another player claims, and its cleanup always (C3c-1)
		if ( player == ParkCommands.LocalPlayer )
			return IsHostCleanup( command ) || LeavesClaimsAlone( player, command );
		if ( player <= 0 )
			return false;
		return ClassOf( command.Kind ) switch
		{
			// [EXT:online-coop-commands] a Move tool's end only drops its own seat's carry, and a Move must end even when the grant went meanwhile (C3b-2); [EXT:online-coop-claims] a release
			// only drops its own seat's claim, which a track tool lets go once it left its ride, the grant taken back or not (C3c-1)
			CoopCommandClass.SeatCleanup => true,
			CoopCommandClass.GuestBuilding => level.HasBuildGrant( player ) && Builds( command ) && HoldsClaimFor( player, command ) && LeavesClaimsAlone( player, command ),
			// [EXT:online-coop-permissions] a management right this build does not offer is refused whatever the seat's rights hold (C4a)
			var management when RightOf( management ) is { } right => CoopRightsOffer.Offered.HasFlag( right ) && level.RightsOf( player ).HasFlag( right ),
			_ => false
		};
	}

	/// <summary>
	/// [EXT:online-coop-permissions] Who may issue a command kind in a shared park (docs/COOP.md, "Money and rights (C4)"; C4a): every kind is of exactly one class, a test holds them to it.
	/// The C3 building's own fields, targets and claims stay asked as they were (<see cref="Builds"/>, <see cref="HoldsClaimFor"/>, <see cref="LeavesClaimsAlone"/>).
	/// </summary>
	internal static CoopCommandClass ClassOf( ParkCommandKind kind ) => kind switch
	{
		ParkCommandKind.PlaceObject or ParkCommandKind.RemoveAt or ParkCommandKind.RemoveAttraction or ParkCommandKind.PickUpObject or ParkCommandKind.LayLine or ParkCommandKind.Backtrack
			or ParkCommandKind.EditQueue or ParkCommandKind.QueueSegment or ParkCommandKind.ClearArea or ParkCommandKind.BuyLand or ParkCommandKind.UndoPoolSlidePiece
			or ParkCommandKind.PlacePoolBridge or ParkCommandKind.PoolCells or ParkCommandKind.LayPoolSlidePieces or ParkCommandKind.LayTrackLine or ParkCommandKind.UndoTrackLine
			or ParkCommandKind.CutTrack or ParkCommandKind.OpenTrackLineTool or ParkCommandKind.BeginCoasterTool or ParkCommandKind.EndCoasterTool or ParkCommandKind.CoasterClick
			or ParkCommandKind.RemoveLastCoasterPylon or ParkCommandKind.BeginCoasterNodeEdit or ParkCommandKind.EditCoasterNode or ParkCommandKind.EndCoasterNodeEdit
			or ParkCommandKind.SwapCoasterStack or ParkCommandKind.CutCoaster or ParkCommandKind.ClaimRide => CoopCommandClass.GuestBuilding,
		ParkCommandKind.EndMove or ParkCommandKind.ReleaseRide => CoopCommandClass.SeatCleanup,
		ParkCommandKind.SetAttractionOpen or ParkCommandKind.SetRideSettings or ParkCommandKind.SetPrice or ParkCommandKind.SetGamePrize or ParkCommandKind.SetShopSettings
			or ParkCommandKind.BuyUpgrade or ParkCommandKind.CancelUpgrade or ParkCommandKind.RequestMaintenance => CoopCommandClass.Attractions,
		ParkCommandKind.SetTrainingBudget or ParkCommandKind.RenameStaff or ParkCommandKind.FireStaff or ParkCommandKind.SetPatrolArea or ParkCommandKind.BeginStaffHire
			or ParkCommandKind.CancelStaffHire or ParkCommandKind.PlaceStaff or ParkCommandKind.PickUpStaff or ParkCommandKind.DropStaff => CoopCommandClass.Staff,
		ParkCommandKind.SetResearchEffort or ParkCommandKind.SetResearchWorkload or ParkCommandKind.MoveResearch => CoopCommandClass.Research,
		ParkCommandKind.SetParkOpen or ParkCommandKind.SetEntranceFee or ParkCommandKind.TakeLoan or ParkCommandKind.RepayLoan or ParkCommandKind.RenamePark
			or ParkCommandKind.RefreshLoanOffers => CoopCommandClass.Finance,
		// what the host's tools issue and no guest's tool does: single cells, the line and queue undo, the pool strokes' undo and their single cells, a bridge's removal
		ParkCommandKind.RemoveObjectAt or ParkCommandKind.UndoLine or ParkCommandKind.RemovePathCell or ParkCommandKind.RemoveQueueCell or ParkCommandKind.UndoQueueSegment
			or ParkCommandKind.PaintPoolCell or ParkCommandKind.ErasePoolCell or ParkCommandKind.UndoPoolStroke or ParkCommandKind.LayPoolSlidePiece
			or ParkCommandKind.RemovePoolBridge => CoopCommandClass.HostTools,
		// the host's alone: the developer prototype's placement and removal (a session's host may issue them, so an older log replays as it did), the finale's dismissal and a challenge's
		// answer (they bind the whole park), the speed (Q8), the wave machine (C5), and the session's own kinds
		ParkCommandKind.PlacePrototype or ParkCommandKind.RemovePrototype or ParkCommandKind.DismissFinale or ParkCommandKind.AnswerChallenge or ParkCommandKind.SetGameSpeed
			or ParkCommandKind.SetPoolWaves or ParkCommandKind.SetSessionClock or ParkCommandKind.SetBuildGrant or ParkCommandKind.ReleaseSeat
			or ParkCommandKind.SetSeatRights => CoopCommandClass.HostOnly,
		// refused in a session for every seat, the host included, by the session's rules (Q17 and C2b-2)
		ParkCommandKind.SetPrototypeRunning or ParkCommandKind.SetAllObjectsOpen or ParkCommandKind.QueueCheat => CoopCommandClass.RefusedInSession,
		_ => throw new ArgumentOutOfRangeException( nameof( kind ), kind, "Every command kind has a class of the shared park's rights." )
	};

	/// <summary>[EXT:online-coop-permissions] The right a management class needs (C4a), or null for a class that is no right's.</summary>
	internal static CoopRights? RightOf( CoopCommandClass @class ) => @class switch
	{
		CoopCommandClass.Attractions => CoopRights.Attractions,
		CoopCommandClass.Staff => CoopRights.Staff,
		CoopCommandClass.Research => CoopRights.Research,
		CoopCommandClass.Finance => CoopRights.Finance,
		_ => null
	};

	/// <summary>[EXT:online-coop-permissions] The reason these rights give for a guest's building the host has not granted it (<see cref="RefusalReason"/>).</summary>
	internal const string NotGrantedReason = "The host has not let this player build.";

	/// <summary>
	/// [EXT:online-coop-claims] The reason these rights give for a guest's track or coaster command on a ride whose claim it does not hold and no other seat holds (<see cref="RefusalReason"/>): the
	/// host let it go (the guest left for 30 seconds, or sent nothing on the ride for 5 minutes), and the guest's tool ends (C3c-1b, C3c-2).
	/// </summary>
	internal const string ClaimLostReason = "This player no longer edits this ride.";

	/// <summary>
	/// The reason these rights give for a refusal of <paramref name="command"/> of <paramref name="player"/>, read where and when it is refused: [EXT:online-coop-claims] another player's claim
	/// (<see cref="Level.ClaimedReason"/>), a guest's track command on a ride whose claim it no longer holds (<see cref="ClaimLostReason"/>, C3c-1b), or [EXT:online-coop-permissions] a guest's
	/// building the host has not granted it (<see cref="NotGrantedReason"/>), which the HUD says in its own words; null for what only the host does, which keeps the generic reason.
	/// </summary>
	public string? RefusalReason( int player, ParkCommand command ) => player == ParkCommands.LocalPlayer
		? !IsHostCleanup( command ) && !LeavesClaimsAlone( player, command ) ? Level.ClaimedReason : null
		: player <= 0 || !Builds( command ) ? null
		: !level.HasBuildGrant( player ) ? NotGrantedReason
		: !LeavesClaimsAlone( player, command ) ? Level.ClaimedReason
		: !HoldsClaimFor( player, command ) ? ClaimLostReason : null;

	/// <summary>
	/// [EXT:online-coop-claims] What a refusal by these rights says to its player, from the refusal's <paramref name="reason"/> as the rights gave it where and when the command was refused
	/// (<see cref="RefusalReason"/>): another player's claim, a claim the player no longer holds, building the host has not granted, or what only the host does.
	/// </summary>
	internal static CoopRefusal RefusalOf( string? reason ) => reason switch
	{
		Level.ClaimedReason => CoopRefusal.Claimed,
		ClaimLostReason => CoopRefusal.ClaimLost,
		NotGrantedReason => CoopRefusal.NotGranted,
		_ => CoopRefusal.HostOnly
	};

	/// <summary>[EXT:online-coop-claims] The host's cleanup, which no claim refuses: its release of any claim, a departed seat's release, the grants and the rights.</summary>
	private static bool IsHostCleanup( ParkCommand command ) => command is ReleaseRideCommand or ReleaseSeatCommand or SetBuildGrantCommand or SetSeatRightsCommand;

	/// <summary>
	/// Whether <paramref name="command"/> is building a granted guest may do (C3a to C3c-2): its kind, its fields and its target, before the claims (<see cref="LeavesClaimsAlone"/>) and a
	/// track or coaster command's claim (<see cref="HoldsClaimFor"/>), which depend on the player.
	/// </summary>
	internal bool Builds( ParkCommand command ) => command switch
	{
		LayLineCommand or QueueSegmentCommand => true,
		// an object placed, or the Move tool's placement (C3b-2: the command itself checks that its player carries the type); a track add-on only onto its own ride's track (C3c-2), so a
		// request without its ride cannot place it as an ordinary object
		PlaceObjectCommand place => level.ResolveCommandEntry( place.Entry ) is { } entry && (place.AddOnRide < 0 ? GuestMayPlace( entry ) : GuestMayPlaceAddOn( place, entry )),
		// the remove click takes an ordinary object, else the queue or path cell under it (C3b)
		RemoveAtCommand remove => level.Objects?.FindAt( remove.X, remove.Y ) is not { IsDeleted: false } item || GuestMayRemove( item.Entry ),
		// C3b: land, the area clear (which asks AllowsRemoval of each object it would take), Backspace over a path, queue, pool bridge or pool water cell, and the queue's edit
		BuyLandCommand or ClearAreaCommand or EditQueueCommand => true,
		BacktrackCommand => level.Paths != null,
		// the sale of an ordinary placed object (never the developer prototype; a sale of an object a later record removed is the park's to refuse, as the host's is)
		RemoveAttractionCommand sale => level.ResolveCommandObject( sale.Attraction ) is not { IsDeleted: false } item
			|| (!ReferenceEquals( item, level.PlacedRide ) && GuestMayRemove( item.Entry )),
		// C3b-2: the Move tool's pick-up of an object the guest may remove and place again (never the developer prototype); a missing object is the park's to refuse
		PickUpObjectCommand pick => level.ResolveCommandObject( pick.Attraction ) is not { IsDeleted: false } item
			|| (!ReferenceEquals( item, level.PlacedRide ) && GuestMayRemove( item.Entry ) && GuestMayPlace( item.Entry )),
		// C3b-3: a guest's pool strokes go in batches (the pool builders refuse every cell that is not theirs: a path, a queue, an object), its bridges, and the last piece of a slide that is
		// not finished (a finished slide takes riders: its pieces are the host's, or go with its tower; a slide with no piece to take back is the park's to refuse, quietly, as the host's
		// Backspace is)
		PoolCellsCommand or LayPoolSlidePiecesCommand or PlacePoolBridgeCommand => true,
		UndoPoolSlidePieceCommand undo => level.PoolSlides?.Find( PoolSlide.KeyOf( undo.TowerX, undo.TowerY ) ) is not { IsComplete: true },
		// [EXT:online-coop-claims] C3c-1: a ride's claim (never the developer prototype; a missing ride or one that is no track ride or coaster is the park's to refuse), and the track
		// commands, of the ride whose claim the guest holds; C3c-2: a coaster's claim too, and its coaster tool's commands
		ClaimRideCommand claim => level.ResolveCommandObject( claim.Ride ) is not { IsDeleted: false } item || !ReferenceEquals( item, level.PlacedRide ),
		OpenTrackLineToolCommand or LayTrackLineCommand or UndoTrackLineCommand or CutTrackCommand => true,
		BeginCoasterToolCommand or EndCoasterToolCommand or CoasterPanelCommand => true,
		_ => false
	};

	/// <summary>
	/// [EXT:online-coop-claims] Whether <paramref name="player"/> holds the claim a track or coaster command needs: a guest edits the track of the ride whose claim it took (C3c-1), and the coaster
	/// whose claim it took (C3c-2), the tool's end included (a late end after the host let the claim go is refused: the claim's end left the coaster's tool state already).
	/// </summary>
	private bool HoldsClaimFor( int player, ParkCommand command ) => TrackRide( command ) is not { } ride || level.ClaimOf( ride ) == player;

	/// <summary>The ride a track command or a coaster tool's command edits (its attraction id), or null for any other command.</summary>
	private static int? TrackRide( ParkCommand command ) => command switch
	{
		OpenTrackLineToolCommand open => open.Ride,
		LayTrackLineCommand line => line.Ride,
		UndoTrackLineCommand undo => undo.Ride,
		CutTrackCommand cut => cut.Ride,
		// [EXT:online-coop-claims] the coaster tool's commands (C3c-2)
		BeginCoasterToolCommand begin => begin.Station,
		EndCoasterToolCommand end => end.Station,
		CoasterPanelCommand panel => panel.Station,
		_ => null
	};

	/// <summary>
	/// [EXT:online-coop-claims] Whether <paramref name="command"/> of <paramref name="player"/> leaves alone every ride claimed against that player (<see cref="ClaimedAgainst"/>): the ride itself
	/// (its track, its sale, its removal, its pick-up, its demolition by a placement, an add-on on it), its queue's lines, their undo, its edit and its cells, the path cell its back
	/// joins and a path cell linked into its entrance (<see cref="PathClearTouchesClaim"/>), which no removal, area clear, line or placement takes. Asked of every player, the host included,
	/// so a claim's holder edits its ride alone (C3c-1); while nothing is claimed against the player it asks nothing.
	/// </summary>
	internal bool LeavesClaimsAlone( int player, ParkCommand command ) => !CanBeClaimedAgainst( player ) || command switch
	{
		// a claimed ride's queue lines and their undo; no line crosses its cells (a path line paves a queue end), and no queue line takes the path cell its back joins (the queue writers
		// lay queue over path); an undo takes the cells its line built off again, which may take a queue's link off, and a path line's undo the link into a claimed ride's entrance; a
		// path line's undo only counts down the ones it crossed (ParkPathBuilder.Undo), which takes no link off, so those are asked for themselves alone, and a queue line's undo leaves
		// them as they are (QueueLineWriter.Undo)
		LayLineCommand line => (line.Mode != CellToolMode.Queue || !ClaimedRide( player, line.Ride ))
			&& !CrossesClaimedQueue( player, line.StartX, line.StartY, line.EndX, line.EndY, joins: line.Mode == CellToolMode.Queue, endOnPathLinks: line.Mode == CellToolMode.Queue ),
		UndoLineCommand undo => (undo.Mode != CellToolMode.Queue || !ClaimedRide( player, undo.Ride ))
			&& !TouchesClaimedQueue( player, undo.Built.ToArray(), joins: true )
			&& (undo.Mode == CellToolMode.Queue || !TouchesClaimedQueue( player, undo.Existing.ToArray(), joins: false ))
			&& (undo.Mode != CellToolMode.Path || !ClearsBesideClaimedEntrance( player, undo.Built )),
		// a native segment lays queue over every path cell of its line but a last one, where it stops (QueueSegmentReducer.Commit), and its undo clears the path cells on its line, so
		// none of those may be linked into a claimed ride's entrance; the queue tool's per-cell line above lays no queue over path (QueuePaths.CheckExtend) and only links a last path
		// cell (QueuePaths.Connect), so it takes no such link off
		QueueSegmentCommand line => !ClaimedRide( player, line.Ride ) && !CrossesClaimedQueue( player, line.StartX, line.StartY, line.EndX, line.EndY, joins: true )
			&& !ClearsBesideClaimedEntrance( player, LineCells( line.StartX, line.StartY, line.EndX, line.EndY )[..^1] ),
		UndoQueueSegmentCommand undo => !ClaimedRide( player, undo.Ride ) && !CrossesClaimedQueue( player, undo.StartX, undo.StartY, undo.EndX, undo.EndY, joins: true )
			&& !ClearsBesideClaimedEntrance( player, LineCells( undo.StartX, undo.StartY, undo.EndX, undo.EndY ) ),
		// no add-on on a claimed ride, nor a placement that demolishes what is claimed or writes over a claimed ride's queue, the path cell its back joins or one linked into its entrance (a
		// track add-on's as its ride's add-on rule places it)
		PlaceObjectCommand place => (place.AddOnRide < 0 || !ClaimedRide( player, place.AddOnRide ))
			&& (level.ResolveCommandEntry( place.Entry ) is not { } entry || PlacementLeavesClaimsAlone( player, entry, place.X, place.Y, place.Rotation,
				place.AddOnRide >= 0 ? level.ResolveCommandObject( place.AddOnRide ) : null )),
		RemoveAtCommand remove => level.Objects?.FindAt( remove.X, remove.Y ) is { IsDeleted: false } item ? !ClaimedAgainst( player, item )
			: !ClaimedQueueAt( player, remove.X, remove.Y ) && !PathClearTouchesClaim( player, remove.X, remove.Y ),
		RemoveObjectAtCommand remove => level.Objects?.FindAt( remove.X, remove.Y ) is not { IsDeleted: false } item || !ClaimedAgainst( player, item ),
		RemovePathCellCommand cell => !PathClearTouchesClaim( player, cell.X, cell.Y ),
		RemoveQueueCellCommand cell => !ClaimedQueueAt( player, cell.X, cell.Y ),
		BacktrackCommand back => level.Paths is not { } paths || Level.BacktrackTargetAt( paths.Cells, back.X, back.Y, level.PoolBridges ) switch
		{
			// C3b-3: a pool bridge, unless an end of it is a claimed queue's or the path cell it joins; a pool water cell
			Level.BacktrackTarget.PoolBridge => level.PoolBridges?.At( back.X, back.Y ) is not { } standing || !BridgeTouchesClaimedQueue( player, standing ),
			Level.BacktrackTarget.PoolWater => true,
			Level.BacktrackTarget.Queue => !ClaimedQueueAt( player, back.X, back.Y ),
			// a path cell laid more than once only loses a placement (ParkPathBuilder.RemoveOnce), which takes no link off; the press that clears it is asked
			_ => paths.Cells.PlacementCountAt( back.X, back.Y ) > 0 || !PathClearTouchesClaim( player, back.X, back.Y )
		},
		RemoveAttractionCommand sale => level.ResolveCommandObject( sale.Attraction ) is not { IsDeleted: false } item || !ClaimedAgainst( player, item ),
		EditQueueCommand edit => !ClaimedQueueAt( player, edit.X, edit.Y ),
		PickUpObjectCommand pick => level.ResolveCommandObject( pick.Attraction ) is not { IsDeleted: false } item || !ClaimedAgainst( player, item ),
		PlacePoolBridgeCommand bridge => !BridgeTouchesClaimedQueue( player, new PoolBridge( bridge.X, bridge.Y, bridge.Direction, 0 ) ),
		RemovePoolBridgeCommand bridge => level.PoolBridges?.At( bridge.X, bridge.Y ) is not { } standing || !BridgeTouchesClaimedQueue( player, standing ),
		RemovePrototypeCommand => level.PlacedRide is not { IsDeleted: false } prototype || !ClaimedAgainst( player, prototype ),
		// a track line of a ride another seat claims, and one whose tiles lie over the path cell a claimed queue joins or one linked into a claimed ride's entrance: a tile laid over path
		// clears it (TrackBuilder.ClearPath), which takes the link of the queue's back cell off or re-evaluates the ride of the entrance, as a path removal does; the line is asked as it is
		// drawn, also past a tile its writer would stop at
		LayTrackLineCommand line => !level.ClaimedAgainst( player, line.Ride ) && !TrackLineTouchesClaim( player, line ),
		// the track commands and the coaster tool's commands of a ride another seat claims
		_ => TrackRide( command ) is not { } ride || !level.ClaimedAgainst( player, ride )
	};

	/// <summary>
	/// Whether <paramref name="line"/> of <paramref name="player"/> lays a tile over a path cell whose clear touches a ride claimed against that player (<see cref="PathClearTouchesClaim"/>): its
	/// tiles as the line writer lays them (<see cref="TrackLineWriter.LayLine"/> to its snapped end), each over its four cells.
	/// </summary>
	private bool TrackLineTouchesClaim( int player, LayTrackLineCommand line )
	{
		var start = (line.StartX, line.StartY);
		return TrackLineWriter.LayLine( start, TrackLineWriter.SnapEnd( start, (line.EndX, line.EndY) ) )
			.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ).Any( cell => PathClearTouchesClaim( player, cell.X, cell.Y ) );
	}

	/// <summary>
	/// Whether <paramref name="player"/> may remove <paramref name="item"/> inside a command that removes what it finds (the area clear): the host what no other player claims, a granted
	/// guest an ordinary object no other player claims (<see cref="MayRemove"/>), any other seat nothing.
	/// </summary>
	public bool AllowsRemoval( int player, OriginalObject item ) => player == ParkCommands.LocalPlayer ? !ClaimedAgainst( player, item )
		: player > 0 && level.HasBuildGrant( player ) && MayRemove( player, item );

	/// <summary>
	/// Whether <paramref name="player"/> may clear the path cell (<paramref name="x"/>, <paramref name="y"/>) inside a command that clears what it finds (the area clear): the host and a
	/// granted guest any but one whose clear touches a ride claimed against them (<see cref="PathClearTouchesClaim"/>: the one its queue joins, one linked into its entrance), any other seat
	/// none.
	/// </summary>
	public bool AllowsPathRemoval( int player, int x, int y ) => (player == ParkCommands.LocalPlayer || (player > 0 && level.HasBuildGrant( player ))) && !PathClearTouchesClaim( player, x, y );

	/// <summary>Whether a granted guest (<paramref name="player"/>) may remove <paramref name="item"/>: not a fixed item of the park, nor a ride claimed against it.</summary>
	private bool MayRemove( int player, OriginalObject item ) => GuestMayRemove( item.Entry ) && !ClaimedAgainst( player, item );

	/// <summary>
	/// Whether a granted guest may remove an object of <paramref name="entry"/>: not a fixed item of the park (its gates, its lights, its bus: <see cref="ObjectCatalogEntry.IsFixedItem"/>).
	/// A slide tower is a guest's to remove since its slide is (C3b-3): its removal takes the slide with it and refunds it.
	/// </summary>
	internal static bool GuestMayRemove( ObjectCatalogEntry entry ) => !entry.IsFixedItem;

	/// <summary>
	/// Whether a granted guest may place <paramref name="entry"/> as an object of its own: not a track add-on, which goes onto its ride's track (<see cref="GuestMayPlaceAddOn"/>). Every ride whose
	/// own tool follows its placement is a guest's since C3c-2: a kart or water ride's track tool (C3c-1b) and a coaster's tool (C3c-2) follow through the ride's claim, a bumper ride's queue
	/// tool follows (C3c-1), and a slide tower's slide mode lays its pieces in batches (C3b-3).
	/// </summary>
	internal static bool GuestMayPlace( ObjectCatalogEntry entry ) => !TrackAddOns.IsTrackAddOn( entry );

	/// <summary>
	/// [EXT:online-coop-claims] Whether a granted guest's <paramref name="place"/> of <paramref name="entry"/> onto the track of the ride it names (<see cref="PlaceObjectCommand.AddOnRide"/>) is
	/// building (C3c-2): not a moving placement, the entry a track add-on (<see cref="TrackAddOns.IsTrackAddOn"/>) of that ride's type (its <c>AddOn.UpgradesId</c>, as the ride's upgrade list
	/// offers it), and the ride a kart or water ride's standing station (a coaster takes no track add-on). A ride claimed against the guest is refused by the claims
	/// (<see cref="LeavesClaimsAlone"/>), the add-on's cells by the park's add-on rule (<see cref="Level.CheckAddOn(ObjectCatalogEntry, int, int, int, OriginalObject)"/>).
	/// </summary>
	private bool GuestMayPlaceAddOn( PlaceObjectCommand place, ObjectCatalogEntry entry ) => !place.Moving && TrackAddOns.IsTrackAddOn( entry )
		&& level.ResolveCommandObject( place.AddOnRide ) is { IsDeleted: false } ride && level.TrackStations.ContainsKey( ride )
		&& entry.Settings.GetInt( "AddOn.UpgradesId" ) == ride.Entry.InfoId;

	/// <summary>Whether anything can be claimed against <paramref name="player"/>: while another seat holds a claim.</summary>
	private bool CanBeClaimedAgainst( int player ) => level.HasClaimsAgainst( player );

	/// <summary>Whether the ride with attraction id <paramref name="ride"/> is claimed against <paramref name="player"/>.</summary>
	private bool ClaimedRide( int player, int ride ) => level.ResolveCommandObject( ride ) is { IsDeleted: false } item && ClaimedAgainst( player, item );

	/// <summary>Whether the queue cell (<paramref name="x"/>, <paramref name="y"/>) belongs to the queue of a ride claimed against <paramref name="player"/>.</summary>
	private bool ClaimedQueueAt( int player, int x, int y ) => level.QueueOwnerAt( x, y ) is { } owner && ClaimedAgainst( player, owner );

	/// <summary>
	/// Whether a line from (<paramref name="startX"/>, <paramref name="startY"/>) toward (<paramref name="endX"/>, <paramref name="endY"/>), snapped as the line writers snap it, crosses the queue
	/// cell of a ride claimed against <paramref name="player"/> (a path line paves a queue end; a queue line of another ride may take over a cell no owner word marks yet), or, with
	/// <paramref name="joins"/> (a queue line, which may lay queue over path), the path cell such a queue joins. With <paramref name="endOnPathLinks"/> (the queue tool's line) a last cell that
	/// is a path cell only links the queue to that path (<see cref="QueuePaths.Connect"/>), so it takes no joined queue's link off: it is asked for itself alone. The cells' owners are resolved
	/// together (<see cref="Level.QueueOwnersAt"/>).
	/// </summary>
	private bool CrossesClaimedQueue( int player, int startX, int startY, int endX, int endY, bool joins, bool endOnPathLinks = false )
	{
		var cells = LineCells( startX, startY, endX, endY );
		if ( endOnPathLinks && joins && cells.Length > 1 && level.Guests?.Grid is { } grid && grid.IsWalkable( cells[^1].X, cells[^1].Y ) )
			return TouchesClaimedQueue( player, cells[..^1], joins: true ) || TouchesClaimedQueue( player, cells[^1..], joins: false );
		return TouchesClaimedQueue( player, cells, joins );
	}

	/// <summary>The cells of a line from (<paramref name="startX"/>, <paramref name="startY"/>) toward (<paramref name="endX"/>, <paramref name="endY"/>), snapped as the line writers snap it.</summary>
	private static (int X, int Y)[] LineCells( int startX, int startY, int endX, int endY )
	{
		var start = (startX, startY);
		return ParkPathBuilder.LayLine( start, ParkPathBuilder.SnapEnd( start, (endX, endY) ) ).ToArray();
	}

	/// <summary>
	/// Whether <paramref name="player"/>'s placement of <paramref name="entry"/> on (<paramref name="x"/>, <paramref name="y"/>) turned <paramref name="rotation"/> leaves alone what is claimed
	/// against that player, as the placement's own check plans it (<see cref="ParkObjects.Check(ObjectCatalogEntry, int, int, int, out ObjectPlacementPlan)"/>, centred as
	/// <see cref="Level.PlaceObject"/> centres it): no object it would demolish (one of a lower overwrite priority) is a ride claimed against the player, nor, for a guest, one it may not
	/// remove (<see cref="MayRemove"/>), and no cell it writes is a claimed ride's queue cell, the path cell its back joins or a path cell linked into its entrance
	/// (<see cref="BesideClaimedEntrance"/>): its footprint, the path cells it clears (beyond a track end too) and the cell outside a queue ride's entrance, which it lays as queue (taking the
	/// links of a path cell there off). The cell outside any other entrance and a queue ride's exit it lays as path, over a path too, which takes no link off: they are asked for themselves
	/// alone (a path paves a queue's end). The exit of an object without a queue lays no cell: it only
	/// links a path cell already outside it (that cell's links and its path and access neighbours', <see cref="PathLinkWriter.LinkPath"/>), which takes no queue's link off, so it is not
	/// asked. The placement lays only its last entrance's and its last exit's (<see cref="ObjectAccessCells.Lay"/>), so only they are asked. A track add-on onto <paramref name="addOnRide"/>'s
	/// track is planned as its ride's add-on rule places it (<see cref="Level.CheckAddOn(ObjectCatalogEntry, int, int, int, OriginalObject, out ObjectPlacementPlan)"/>, C3c-2). A placement
	/// the park refuses is the park's to refuse.
	/// </summary>
	internal bool PlacementLeavesClaimsAlone( int player, ObjectCatalogEntry entry, int x, int y, int rotation, OriginalObject? addOnRide = null )
	{
		var anchor = Level.GetCentredAnchor( entry, x, y, rotation );
		if ( level.Objects is not { } objects )
			return true;
		ObjectPlacementPlan plan;
		var result = addOnRide != null && TrackAddOns.IsTrackAddOn( entry ) ? level.CheckAddOn( entry, x, y, rotation, addOnRide, out plan )
			: objects.Check( entry, anchor.X, anchor.Y, rotation, out plan );
		if ( result != OriginalPlacementResult.Allowed )
			return true;
		if ( plan.Overwritten.Any( item => player == ParkCommands.LocalPlayer ? ClaimedAgainst( player, item ) : !MayRemove( player, item ) ) )
			return false;
		if ( !InitialStationLeavesClaimsAlone( player, entry, anchor.X, anchor.Y, rotation ) )
			return false;
		var (entrance, exit) = ObjectAccessCells.Find( ObjectFootprint.GetAccessPoints( entry.Shape, anchor.X, anchor.Y, rotation ) );
		var queueEntrance = entry.HasQueue && entrance is { } enter ? new[] { (enter.OutsideX, enter.OutsideY) } : Array.Empty<(int, int)>();
		var written = ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, rotation ).Select( cell => (cell.X, cell.Y) ).Concat( plan.ClearedPaths ).Concat( queueEntrance )
			.Distinct().ToArray();
		var paths = new[] { entry.HasQueue ? exit : entrance }.OfType<ObjectAccessPoint>().Select( point => (point.OutsideX, point.OutsideY) ).ToArray();
		return !TouchesClaimedQueue( player, written, joins: true ) && !TouchesClaimedQueue( player, paths, joins: false )
			&& !ClearsBesideClaimedEntrance( player, written );
	}

	/// <summary>
	/// A fresh kart/water station's exit and entry ops lie outside its ordinary footprint plan. Ask before the command buys or registers the station, including demolition before a
	/// refused tile's type op: objects, the owners of offset track records cleared by GiveWay, and path cells whose clearing could unlink another ride's queue or re-evaluate a claimed ride
	/// beside them (<see cref="BesideClaimedEntrance"/>). Never omit a forbidden removal inside the writer after the station has already been bought. Existing stations keep the writer's skip
	/// gates, so a harmless adjacent placement stays possible.
	/// </summary>
	private bool InitialStationLeavesClaimsAlone( int player, ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation )
	{
		if ( level.KartTracks == null || level.WaterTracks == null
			|| BumperTemplates.ForRide( entry.Settings.GetInt( "Bumper.WhichTrackType" ), entry.Settings.GetInt( "Bumper.BumperType" ) ) is not { IsBumper: false } template )
			return true;
		var builder = template.Family == TrackFamily.Kart ? level.KartTracks : level.WaterTracks;
		var station = TrackStation.FromPlacement( anchorX, anchorY, rotation );
		var demolished = builder.InitialStationGiveWayCells( station ).ToArray();
		if ( demolished.Select( cell => level.Objects.FindAt( cell.X, cell.Y ) ).OfType<OriginalObject>().Distinct()
			.Any( item => player == ParkCommands.LocalPlayer ? ClaimedAgainst( player, item ) : !MayRemove( player, item ) ) )
			return false;
		// A track cell outside its station has no OriginalObject in Objects.FindAt. Its owner is the station's first-cell index plus one, not its attraction id.
		var owners = demolished.Select( cell => builder.Tracks.Get( cell.X, cell.Y ).Owner ).Where( owner => owner != 0 ).ToHashSet();
		if ( level.TrackStations.Any( pair => owners.Contains( builder.OwnerOf( pair.Value ) ) && ClaimedAgainst( player, pair.Key ) ) )
			return false;
		var paths = new[] { station.Exit, station.Entry }.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) )
			.Where( cell => builder.Cells.TypeAt( cell.X, cell.Y ) == ParkCellType.Path ).ToArray();
		return !TouchesClaimedQueue( player, paths, joins: true ) && !ClearsBesideClaimedEntrance( player, paths );
	}

	/// <summary>
	/// Whether <paramref name="cells"/> hold the queue cell of a ride claimed against <paramref name="player"/>, or, with <paramref name="joins"/>, the path cell such a queue joins. The cells'
	/// owners are resolved together (<see cref="Level.QueueOwnersAt"/>).
	/// </summary>
	private bool TouchesClaimedQueue( int player, IReadOnlyList<(int X, int Y)> cells, bool joins )
	{
		if ( level.Guests?.Grid is not { } grid )
			return false;
		var asked = joins ? cells.Concat( cells.SelectMany( cell => JoinedQueueCells( grid, cell.X, cell.Y ) ) ) : cells;
		var claimed = level.QueueOwnersAt( asked ).Where( cell => cell.Owner is { } owner && ClaimedAgainst( player, owner ) ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
		return claimed.Count > 0 && (cells.Any( claimed.Contains ) || (joins && cells.Any( cell => JoinedQueueCells( grid, cell.X, cell.Y ).Any( claimed.Contains ) )));
	}

	/// <summary>
	/// Whether (<paramref name="x"/>, <paramref name="y"/>) is a path cell the queue of a ride claimed against <paramref name="player"/> joins: a removal of it, or a queue laid over it, would take
	/// the link of the queue's back cell off (<see cref="PathLinkWriter.Unlink"/>).
	/// </summary>
	private bool JoinsClaimedQueue( int player, int x, int y ) => level.Guests?.Grid is { } grid
		&& level.QueueOwnersAt( JoinedQueueCells( grid, x, y ) ).Any( cell => cell.Owner is { } owner && ClaimedAgainst( player, owner ) );

	/// <summary>
	/// [EXT:online-coop-claims] Whether a clear of the path cell (<paramref name="x"/>, <paramref name="y"/>) touches a ride claimed against <paramref name="player"/>: it takes the link of
	/// that ride's queue's back cell off (<see cref="JoinsClaimedQueue"/>), or the link into its entrance, which re-evaluates the ride (<see cref="BesideClaimedEntrance"/>).
	/// </summary>
	private bool PathClearTouchesClaim( int player, int x, int y ) => JoinsClaimedQueue( player, x, y ) || BesideClaimedEntrance( player, x, y );

	/// <summary>
	/// [EXT:online-coop-claims] Whether a clear of the path cell (<paramref name="x"/>, <paramref name="y"/>) re-evaluates a ride claimed against <paramref name="player"/>: for each cardinal
	/// link of the cell into an entrance cell (type 9) that carries an owner word, the clear runs the re-evaluation of that word's ride as it takes the link off (tp.exe <c>0x53694B</c>,
	/// <see cref="PathLinkWriter.Unlink"/> and <c>TrackBuilder.ClearPath</c>, <c>Level.ReevaluateRideOfOwner</c>), which recomputes the ride's queue, stamps a block and may open the ride when
	/// it is closed. A cell that is no path cell, or not linked into the entrance beside it, re-evaluates nothing.
	/// </summary>
	private bool BesideClaimedEntrance( int player, int x, int y )
	{
		if ( level.Guests is not { } guests )
			return false;
		var cells = guests.Grid.Cells;
		if ( !cells.InBounds( x, y ) || cells.TypeAt( x, y ) != ParkCellType.Path )
			return false;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			var (nx, ny) = (x + dx, y + dy);
			if ( (cells.LinksAt( x, y ) & GuestPathGrid.LinkValue( direction )) == 0 || !cells.InBounds( nx, ny ) || cells.RawTypeAt( nx, ny ) != ParkCellTypes.Entrance
				|| cells.OwnerAt( nx, ny ) is not (> 0 and var owner) )
				continue;
			// the ride the re-evaluation looks up by the word
			if ( guests.Attractions.OfType<RideVisitorBridge>().FirstOrDefault( ride => ride.OwnerWord == owner && ride.HasCells ) is { } ride && level.ClaimedAgainst( player, ride.AttractionId ) )
				return true;
		}
		return false;
	}

	/// <summary>Whether a clear of a path cell among <paramref name="cells"/> re-evaluates a ride claimed against <paramref name="player"/> (<see cref="BesideClaimedEntrance"/>).</summary>
	private bool ClearsBesideClaimedEntrance( int player, IEnumerable<(int X, int Y)> cells ) => cells.Any( cell => BesideClaimedEntrance( player, cell.X, cell.Y ) );

	/// <summary>The queue cells a path cell (<paramref name="x"/>, <paramref name="y"/>) is joined to: its four neighbours that are queue cells, linked to it on either side.</summary>
	private static IEnumerable<(int X, int Y)> JoinedQueueCells( GuestPathGrid grid, int x, int y )
	{
		if ( !grid.Cells.InBounds( x, y ) || grid.Cells.TypeAt( x, y ) != ParkCellType.Path )
			yield break;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			var (nx, ny) = (x + dx, y + dy);
			if ( !grid.Cells.InBounds( nx, ny ) || !grid.IsQueue( nx, ny ) )
				continue;
			if ( (grid.Cells.LinksAt( x, y ) & GuestPathGrid.LinkValue( direction )) != 0 || (grid.Cells.LinksAt( nx, ny ) & GuestPathGrid.LinkValue( (direction + 2) % 4 )) != 0 )
				yield return (nx, ny);
		}
	}

	/// <summary>
	/// Whether a bridge's cells (its seven and the two outside ends, <see cref="PoolTool.HoverBridgeCells"/>) hold the queue cell of a ride claimed against <paramref name="player"/> or the path
	/// cell its back joins.
	/// </summary>
	private bool BridgeTouchesClaimedQueue( int player, PoolBridge bridge ) =>
		TouchesClaimedQueue( player, Enumerable.Range( -1, PoolBridge.Length + 2 ).Select( bridge.Cell ).ToArray(), joins: true );

	/// <summary>
	/// Whether <paramref name="item"/> is claimed against <paramref name="player"/>: the object window offers neither its track tool nor its queue's tool to that player, and its edits are
	/// refused (<see cref="LeavesClaimsAlone"/>).
	/// </summary>
	internal bool IsClaimedAgainst( int player, OriginalObject item ) => ClaimedAgainst( player, item );

	/// <summary>
	/// [EXT:online-coop-claims] Whether <paramref name="item"/> is claimed against <paramref name="player"/>: a track ride (C3c-1) or a coaster (C3c-2) whose claim another seat holds.
	/// </summary>
	private bool ClaimedAgainst( int player, OriginalObject item ) => (level.TrackStations.ContainsKey( item ) || item.Runtime?.Coaster != null) && item.Runtime is { } runtime
		&& level.ClaimedAgainst( player, runtime.AttractionId );
}

/// <summary>[EXT:online-coop-permissions] What a refusal by a shared park's rights says to its player (<see cref="CoopBuildRights.RefusalOf"/>).</summary>
internal enum CoopRefusal
{
	/// <summary>A command only the host may issue.</summary>
	HostOnly,
	/// <summary>Building the host has not granted this player yet.</summary>
	NotGranted,
	/// <summary>[EXT:online-coop-claims] An edit of a ride another player claims (C3c-1).</summary>
	Claimed,
	/// <summary>[EXT:online-coop-claims] A guest's edit of a ride whose claim the host let go: its track tool (C3c-1b) or coaster tool (C3c-2) ends.</summary>
	ClaimLost,
}

/// <summary>
/// [EXT:online-coop-permissions] The classes of the shared park's command kinds (<see cref="CoopBuildRights.ClassOf"/>, C4a): the guests' building (C3, with its grant and claims), what every
/// seat may issue to end its own tools, the four management rights, the host's tools' primitives and the host's alone, and what a session refuses for every seat.
/// </summary>
internal enum CoopCommandClass
{
	GuestBuilding,
	SeatCleanup,
	Attractions,
	Staff,
	Research,
	Finance,
	HostTools,
	HostOnly,
	RefusedInSession
}
