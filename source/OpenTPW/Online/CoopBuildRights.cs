namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-permissions] A shared park's rights (docs/COOP.md, "Building by every player"; the user's decision Q7: the host sets rights per player). The host's seat 0 may issue
/// every command; a guest's seat may issue the building the host granted it (<see cref="SetBuildGrantCommand"/>, kept in the park on every peer) and nothing else. The building of C3a is an
/// allowlist over a command's kind, its fields and its target: path and queue lines, the queue's edit, an object placed (not a moving placement, not a track add-on), single removals of a
/// cell's object, queue or path (not a coaster's or a track ride's station, whose edits need claims) and land. Everything else stays the host's: speed and pause (Q8), cheats (Q17), the
/// developer kinds, money and park management (C4), undo lines, Move, staff, pools, tracks and coasters (C3b and C3c) and the grants themselves. Asked at execution on every peer, of the same
/// park, the answer is the same everywhere.
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
		EditQueueCommand => true,
		// a moving placement and a track add-on belong to the Move tool and the track tools, which come later
		PlaceObjectCommand place => !place.Moving && place.AddOnRide < 0,
		RemoveAtCommand remove => !Claimed( remove.X, remove.Y ),
		RemoveObjectAtCommand remove => !Claimed( remove.X, remove.Y ),
		RemovePathCellCommand => true,
		RemoveQueueCellCommand => true,
		BuyLandCommand => true,
		_ => false
	};

	/// <summary>Whether the object on the cell is a coaster's or a track ride's station, whose edits need a claim (C3c): a guest may not remove it yet.</summary>
	private bool Claimed( int x, int y ) => level.Objects?.FindAt( x, y ) is { } item && (item.Runtime?.Coaster != null || level.TrackStations.ContainsKey( item ));
}
