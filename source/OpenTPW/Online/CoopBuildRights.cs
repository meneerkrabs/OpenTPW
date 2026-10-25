namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-permissions] A shared park's rights (docs/COOP.md, "Building by every player"; the user's decision Q7: the host sets rights per player). The host's seat 0 may issue
/// every command; a guest's seat may issue the building the host granted it (<see cref="SetBuildGrantCommand"/>, kept in the park on every peer) and nothing else. The building of C3a is an
/// allowlist over a command's kind, its fields and its target, the commands of the guests' tools that wait for the host (C3a-2): path and queue lines, an object placed (not a moving
/// placement, not a track add-on, whatever ride the command names, not a ride whose own tool follows its placement: a coaster, a kart, water or bumper ride, a slide tower) and the remove click (not on a coaster's or a
/// track ride's station, whose edits need claims). Everything else stays the host's: speed and pause (Q8), cheats (Q17), the developer kinds, money and park management (C4), undo lines,
/// land, the queue's edit, the area clear, Move, staff, pools, tracks and coasters (C3b and C3c) and the grants themselves. Asked at execution on every peer, of the same park, the answer
/// is the same everywhere.
/// </summary>
internal sealed class CoopBuildRights( Level level ) : IParkCommandRights
{
	public bool Allows( int player, ParkCommandKind kind ) => player == ParkCommands.LocalPlayer;

	public bool Allows( int player, ParkCommand command )
	{
		if ( player == ParkCommands.LocalPlayer )
			return true;
		return player > 0 && level.HasBuildGrant( player ) && Builds( command );
	}

	/// <summary>Whether <paramref name="command"/> is building a granted guest may do (C3a): its kind, its fields and its target.</summary>
	internal bool Builds( ParkCommand command ) => command switch
	{
		LayLineCommand => true,
		QueueSegmentCommand => true,
		// a moving placement and a track add-on belong to the Move tool and the track tools, which come later, as do the rides whose own tool follows their placement; a track add-on's
		// entry is refused whatever ride the command names, so a request without one cannot place it as an ordinary object
		PlaceObjectCommand place => !place.Moving && place.AddOnRide < 0 && level.ResolveCommandEntry( place.Entry ) is { } entry && GuestMayPlace( entry ),
		RemoveAtCommand remove => !Claimed( remove.X, remove.Y ),
		_ => false
	};

	/// <summary>Whether a granted guest may place <paramref name="entry"/>: not a track add-on (the track tools are the host's) and not a ride whose own tool follows its placement.</summary>
	internal static bool GuestMayPlace( ObjectCatalogEntry entry ) => !TrackAddOns.IsTrackAddOn( entry ) && !OwnToolFollows( entry );

	/// <summary>
	/// Whether a placed <paramref name="entry"/> opens a tool of its own (<c>ParkHud.FinishPlacement</c>): a coaster's track, a kart, water or bumper ride's track or arena, a slide tower's
	/// slide. Those tools hold state no guest's request carries yet (C3b, C3c).
	/// </summary>
	internal static bool OwnToolFollows( ObjectCatalogEntry entry ) => CoasterDefinition.IsCoaster( entry.Settings )
		|| BumperTemplates.ForRide( entry.Settings.GetInt( "Bumper.WhichTrackType" ), entry.Settings.GetInt( "Bumper.BumperType" ) ) != null
		|| PoolSlideTowers.IsSlideTower( entry );

	/// <summary>Whether the object on the cell is a coaster's or a track ride's station, whose edits need a claim (C3c): a guest may not remove it yet.</summary>
	private bool Claimed( int x, int y ) => level.Objects?.FindAt( x, y ) is { } item && (item.Runtime?.Coaster != null || level.TrackStations.ContainsKey( item ));
}
