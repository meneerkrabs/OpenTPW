using OpenTPW.Online.Packages;

namespace OpenTPW;

/// <summary>
/// The version 2 park layout: capture of the live park's built state (cell map, placed objects with their
/// published state, coaster tracks) into a <see cref="MinimalParkPayload"/> and the application of that
/// layout for a read-only visit. The capture is the exact state the owner built — no economy is simulated
/// on either side — and both sides digest it with <see cref="ParkLayoutDigest"/> so a visit can check it
/// rebuilt the same park (docs/ONLINE.md "Local files and visits").
/// </summary>
public static class SharedParkLayout
{
	/// <summary>Every non-empty cell of the cell map, in row order (the <see cref="WorldStateHash"/> cell digest's order).</summary>
	public static IReadOnlyList<ParkCellRecord> CaptureCells( ParkCellMap cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		if ( cells.HasQueuePassResidues || cells.HasExceptionalQueueMetadata )
			throw new NotSupportedException( "This park contains native queue-pass metadata that the shared layout format cannot preserve. Save a resumable park instead." );
		var records = new List<ParkCellRecord>();
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				var type = cells.RawTypeAt( x, y );
				var links = cells.LinksAt( x, y );
				var queueLink = cells.QueueLinkAt( x, y );
				// a visit is read-only and buys no land, so a park's land is not published (it would add a record for most cells)
				var flags = cells.Land != null ? cells.FlagsAt( x, y ) & ~ParkCellFlags.Unowned : cells.FlagsAt( x, y );
				var placements = cells.PlacementCountAt( x, y );
				if ( type == 0 && links == 0 && queueLink == 0 && flags == 0 && placements == 0 )
					continue;
				records.Add( new ParkCellRecord( x, y, type, links, queueLink, (ushort)flags, placements ) );
			}
		}
		return records;
	}

	/// <summary>An empty cell map of the level's dimensions with every payload cell written into it.</summary>
	public static ParkCellMap BuildCellMap( int width, int height, IReadOnlyList<ParkCellRecord> cells )
	{
		var built = new ParkCellMap( width, height );
		foreach ( var cell in cells )
		{
			if ( !built.InBounds( cell.X, cell.Y ) )
				throw new InvalidDataException( $"Shared park cell ({cell.X}, {cell.Y}) is outside the level map." );
			built.SetType( cell.X, cell.Y, cell.Type );
			built.SetLinks( cell.X, cell.Y, cell.Links );
			if ( cell.Type == (byte)ParkCellType.Queue )
				built.SetQueueLink( cell.X, cell.Y, cell.QueueLink );
			built.SetFlags( cell.X, cell.Y, (ParkCellFlags)cell.Flags );
			built.SetPlacementCount( cell.X, cell.Y, cell.Placements );
		}
		return built;
	}

	/// <summary>
	/// The level's placed objects with their published state: footprint and rotation from the placement, open state
	/// and price from the economy record the level links to the object, when it has an economy.
	/// </summary>
	public static IReadOnlyList<ParkObjectRecord> CaptureObjects( Level level )
	{
		ArgumentNullException.ThrowIfNull( level );
		var records = new List<ParkObjectRecord>();
		foreach ( var item in level.Objects.Objects )
		{
			var (minX, minY, maxX, maxY) = ObjectFootprint.GetBounds( item.Entry.Shape, 0, 0, item.Placement.Rotation );
			int? price = null;
			double? repair = null;
			if ( level.GetEconomyInstance( item ) is int instance && level.Park?.Economy.TryGetObject( instance, out var state ) == true )
			{
				price = state.Price;
				repair = state.Repair;
			}
			records.Add( new ParkObjectRecord( item.Entry.InfoId, item.Placement.X, item.Placement.Y,
				maxX - minX + 1, maxY - minY + 1, item.Placement.Rotation, item.Entry.IsFixedItem, item.IsOpen, price, repair ) );
		}
		return records;
	}

	/// <summary>Every coaster track with pylons, by the index of its station in <paramref name="objects"/>.</summary>
	public static IReadOnlyList<CoasterTrackRecord> CaptureCoasters( Level level, IReadOnlyList<ParkObjectRecord> objects )
	{
		ArgumentNullException.ThrowIfNull( level );
		ArgumentNullException.ThrowIfNull( objects );
		var coasters = new List<CoasterTrackRecord>();
		for ( var index = 0; index < level.Objects.Objects.Count; index++ )
		{
			if ( level.GetCoasterBuilder( level.Objects.Objects[index] ) is not { } builder )
				continue;
			if ( CaptureCoaster( index, builder ) is { } record )
				coasters.Add( record );
		}
		return coasters;
	}

	/// <summary>One track's record (its pylons and circuit state), or null for a station without a shaped track.</summary>
	internal static CoasterTrackRecord? CaptureCoaster( int station, CoasterBuilder builder )
	{
		ArgumentNullException.ThrowIfNull( builder );
		if ( builder.Track.PylonCount == 0 )
			return null;
		var nodes = builder.Track.Nodes.Where( node => node.IsPlayerPylon )
			.Select( node => new CoasterNodeRecord( node.X, node.Y, node.Height, node.LoopPylon ) ).ToList();
		// A circuit cut open in the middle is published as the open track its pylons make (the layout record has no gap).
		return new CoasterTrackRecord( station, nodes, builder.Track.IsClosed, builder.Track.IsComplete && !builder.Track.IsCut );
	}

	/// <summary>
	/// Places every payload object without build rules (the owner validated them when building), at its published anchor,
	/// rotation and open state. Returns one entry per record — the placed object, or null when the local catalog lacks it
	/// or its footprint lies outside the level map (reported, like the original import rejects such records) — so payload
	/// indices (a coaster's station) stay aligned.
	/// </summary>
	public static (IReadOnlyList<OriginalObject?> Placed, IReadOnlyList<string> Skipped) PlaceObjects( ParkObjects objects, IReadOnlyList<ParkObjectRecord> records )
	{
		ArgumentNullException.ThrowIfNull( objects );
		ArgumentNullException.ThrowIfNull( records );
		var placed = new List<OriginalObject?>( capacity: records.Count );
		var skipped = new List<string>();
		foreach ( var record in records )
		{
			var entry = objects.Catalog.Find( record.InfoId );
			if ( entry == null || entry.IsTool )
			{
				placed.Add( null );
				skipped.Add( $"object {record.InfoId}" );
				continue;
			}
			if ( !FitsLevel( entry, record, objects.Grid.Width, objects.Grid.Height ) )
			{
				placed.Add( null );
				skipped.Add( $"object {record.InfoId} at ({record.X}, {record.Y}) outside the level map" );
				continue;
			}
			placed.Add( objects.Add( entry, record.X, record.Y, record.Rotation, record.Open ?? true ) );
		}
		return (placed, skipped);
	}

	/// <summary>Checks the local catalog footprint, including rotation, without creating graphics resources.</summary>
	internal static bool FitsLevel( ObjectCatalogEntry entry, ParkObjectRecord record, int width, int height ) =>
		ObjectFootprint.GetCells( entry.Shape, record.X, record.Y, record.Rotation )
			.All( cell => cell.X >= 0 && cell.Y >= 0 && cell.X < width && cell.Y < height );

	/// <summary>
	/// Rebuilds a published coaster on its placed station: each pylon is appended at its published height (the
	/// clamp is idempotent, so the stored heights reproduce the track), then the circuit is closed and completed
	/// as published, which fits the curve, rates the track and places the trains exactly as the owner's build did.
	/// </summary>
	public static bool RestoreCoaster( Level level, OriginalObject station, IReadOnlyList<CoasterNodeRecord> nodes, bool closed, bool complete )
	{
		ArgumentNullException.ThrowIfNull( level );
		ArgumentNullException.ThrowIfNull( station );
		return level.GetCoasterBuilder( station ) is { } builder && RestoreCoaster( builder, nodes, closed, complete );
	}

	/// <summary>The headless core of the restore (the visit's Level path calls it with the station's builder).</summary>
	internal static bool RestoreCoaster( CoasterBuilder builder, IReadOnlyList<CoasterNodeRecord> nodes, bool closed, bool complete )
	{
		ArgumentNullException.ThrowIfNull( builder );
		ArgumentNullException.ThrowIfNull( nodes );
		foreach ( var node in nodes )
			builder.Track.Append( builder.Ride.World, builder.Ride, node.X, node.Y, node.Height, node.LoopPylon );
		if ( closed && !builder.Track.Close() )
			return false;
		if ( complete )
		{
			if ( builder.Track.Complete() != CoasterPlaceResult.Ok )
				return false;
			builder.Ride.Rebuild();
		}
		builder.Track.Revalidate( builder.Site, builder.Ride.World, builder.Ride );
		return true;
	}

	/// <summary>Recomputes the payload's layout digest from the live visit state, to verify the built park matches the published one.</summary>
	public static string DigestOf( Level level )
	{
		ArgumentNullException.ThrowIfNull( level );
		var cells = CaptureCells( level.Guests?.Grid.Cells ?? EmptyCellMap );
		var objects = CaptureObjects( level );
		var coasters = CaptureCoasters( level, objects );
		return ParkLayoutDigest.Compute( cells, objects, coasters );
	}

	private static ParkCellMap? emptyCellMap;

	private static ParkCellMap EmptyCellMap => emptyCellMap ??= new ParkCellMap( 1, 1 );
}
