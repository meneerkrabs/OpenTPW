namespace OpenTPW;

/// <summary>
/// Deep typed layout snapshot of a park (save/load milestone): every non-empty cell of the <see cref="ParkCellMap"/>
/// with its raw type byte, flags, placement counter, cardinal links and queue link plus the map's version, and the
/// placed objects of a <see cref="ParkObjects"/> in iteration order with catalog key and archive, effective anchor,
/// rotation, open state, explicit creation seed, attraction id and base height — plus the placement-seed allocator
/// and the world seed's object-script key. DTOs are plain deep copies (no live object graphs, no runtime internals);
/// capture only reads, validation is pure, and restoration writes into fresh candidates (a new cell map; an unused
/// <see cref="ParkObjects"/> whose script world never allocated) through the same placement path as the game's own
/// import, so it never charges, never duplicates fixed items and fires <see cref="ParkObjects.ObjectPlaced"/> exactly
/// once per object.
/// </summary>
/// <remarks>
/// Caller ordering (the envelope/integration lane owns the transaction): validate the complete snapshot, restore the
/// cell map first (the guests' walk grid derives from it), then restore the objects into the fresh candidate level's
/// <see cref="ParkObjects"/> — which must be an unused park whose script world never allocated (build it without the
/// shipped/imported/default objects; clearing those after construction is not enough, because their construction has
/// already advanced the script world's id allocators, and <see cref="RestoreObjects"/> rejects such a target before
/// placing anything), then let the guest/VM/runtime lanes restore their state into the returned objects (list index
/// and <c>AttractionId</c> are the stable references; the VM world restore runs after the objects exist and re-sets
/// the world counters), and only then commit the new level and retire the old one. A failed restore throws
/// <see cref="InvalidDataException"/> and leaves the candidate empty; the live park is never touched here. Prices,
/// repair and other economy state are the economy snapshot's truth: the envelope stores its attraction-to-instance
/// associations directly and re-links them by the restored <c>AttractionId</c> (first-match by <c>InfoId</c> would
/// swap duplicates — the Easymode park alone has three small toilets and two bins) — nothing in this module charges
/// or sells.
/// </remarks>
public static class ParkLayoutSnapshot
{
	/// <summary>The layout of one park: its cell map, its placed objects in iteration order, and the seed and id allocators.</summary>
	public sealed record Data
	{
		public required CellMapData Cells { get; init; }
		public required List<PlacedObjectData> Objects { get; init; }
		/// <summary>The next seed <see cref="ParkObjects.Add"/> draws; objects placed after a load continue the saved sequence.</summary>
		public required int NextSeed { get; init; }
		/// <summary>
		/// The script world's next attraction id after the last object (and anything else that drew one): removals leave
		/// holes below it, and restoring reproduces the saved ids exactly — objects are never renumbered to 1..N.
		/// </summary>
		public required int NextAttractionId { get; init; }
		/// <summary>The saved park's <see cref="WorldSeed.ObjectScriptKey"/>; seeds and allocator are meaningless under another key.</summary>
		public required int ObjectScriptKey { get; init; }
	}

	/// <summary>The cell map: its dimensions, its change counter, and every non-empty cell in row order.</summary>
	public sealed record CellMapData
	{
		public required int Width { get; init; }
		public required int Height { get; init; }
		/// <summary><see cref="ParkCellMap.Version"/>: bumped by every write; restored exactly so cached walk state keeps the saved epoch.</summary>
		public required int Version { get; init; }
		public required List<CellData> Cells { get; init; }
	}

	/// <summary>One non-empty cell: the raw bytes and counters of <see cref="ParkCellMap"/> at (X, Y).</summary>
	public sealed record CellData
	{
		public required int X { get; init; }
		public required int Y { get; init; }
		/// <summary>The original type byte (not just the named <see cref="ParkCellType"/> values).</summary>
		public required byte Type { get; init; }
		/// <summary>Raw flag bits (map cell <c>+8</c>/<c>+9</c>).</summary>
		public required ushort Flags { get; init; }
		/// <summary>Same-type placement counter (map cell <c>+32</c>); never negative in a live map.</summary>
		public required short Placements { get; init; }
		/// <summary>Cardinal link bits 1 (−Y), 4 (+X), 16 (+Y), 64 (−X) (map cell <c>+12</c>).</summary>
		public required byte Links { get; init; }
		/// <summary>The queue link toward the queue's predecessor (map cell <c>+13</c>): 0 or one cardinal bit, and only on a queue cell.</summary>
		public required byte QueueLink { get; init; }
	}

	/// <summary>
	/// One placed object in <see cref="Data.Objects"/> iteration order: what <see cref="ParkObjects.Add"/> placed and
	/// what recreating it needs. X/Y/Rotation are the effective placement (non-TPI fixed items stand at (0, 0, 0) in
	/// park space whatever anchor the import record carried). The list index is the stable cross-lane reference, and
	/// <see cref="AttractionId"/> is the stable identity guests, scripts and the economy were linked by — including for
	/// duplicate InfoIds, where first-match reassociation would be wrong.
	/// </summary>
	public sealed record PlacedObjectData
	{
		/// <summary>Catalog key (<c>Info.Id</c>) of the placed object.</summary>
		public required int InfoId { get; init; }
		/// <summary>
		/// Data-root-relative archive path of the catalog entry — identity metadata: the saved InfoId must still resolve
		/// to this archive. It is not a content fingerprint; the save envelope owns content digests.
		/// </summary>
		public required string Archive { get; init; }
		public required int X { get; init; }
		public required int Y { get; init; }
		public required int Rotation { get; init; }
		/// <summary>Live open state (<c>VAR_RIDECLOSED</c>) at capture; prices and repair belong to the economy snapshot.</summary>
		public required bool Open { get; init; }
		/// <summary>The explicit seed the object's script was created with (<c>ordinal ^ ObjectScriptKey</c>, ordinal &lt; NextSeed).</summary>
		public required int CreationSeed { get; init; }
		/// <summary>
		/// The object's attraction id in the park's script world: strictly increasing with placement order, with holes where
		/// removed objects' ids fell (the allocator never rewinds). Restored exactly; guests and the economy key by it.
		/// </summary>
		public required int AttractionId { get; init; }
		/// <summary>Base height the terrain gave the footprint at capture; a different terrain under the same anchor fails the restore.</summary>
		public required float BaseHeight { get; init; }
		/// <summary>Whether the entry is a fixed item (<c>Info.DontApplyOffset</c>); must match the catalog at load.</summary>
		public required bool IsFixedItem { get; init; }
	}

	/// <summary>The cell map and placed objects of a live park, in the state right now. Throws for state this format cannot save.</summary>
	public static Data CaptureSnapshot( ParkCellMap cells, ParkObjects objects )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( objects );
		var cellRecords = new List<CellData>();
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				var type = cells.RawTypeAt( x, y );
				var flags = (ushort)cells.FlagsAt( x, y );
				var placements = cells.PlacementCountAt( x, y );
				var links = cells.LinksAt( x, y );
				var queueLink = cells.QueueLinkAt( x, y );
				if ( type == 0 && flags == 0 && placements == 0 && links == 0 && queueLink == 0 )
					continue;
				cellRecords.Add( new CellData { X = x, Y = y, Type = type, Flags = flags, Placements = placements, Links = links, QueueLink = queueLink } );
			}
		}
		var objectRecords = new List<PlacedObjectData>( objects.Objects.Count );
		foreach ( var item in objects.Objects )
		{
			if ( item.IsDeleted )
				throw new InvalidDataException( $"Placed object {item.Entry.DisplayName} is deleted but still registered." );
			if ( item.CreationSeed is not int seed )
				throw new NotSupportedException( $"{item.Entry.DisplayName} was placed without an explicit creation seed; its layout cannot be saved." );
			objectRecords.Add( new PlacedObjectData
			{
				InfoId = item.Entry.InfoId,
				Archive = item.Entry.ArchivePath,
				X = item.Placement.X,
				Y = item.Placement.Y,
				Rotation = item.Placement.Rotation,
				Open = item.IsOpen,
				CreationSeed = seed,
				AttractionId = item.Visitors.AttractionId,
				BaseHeight = item.Placement.BaseHeight,
				IsFixedItem = item.Entry.IsFixedItem
			} );
		}
		return new Data
		{
			Cells = new CellMapData { Width = cells.Width, Height = cells.Height, Version = cells.Version, Cells = cellRecords },
			Objects = objectRecords,
			NextSeed = objects.NextPlacementSeed,
			NextAttractionId = objects.NextAttractionId,
			ObjectScriptKey = objects.ObjectScriptKey
		};
	}

	/// <summary>
	/// Pure validation against a catalog: completeness (no missing cell map, object list or records), dimensions and
	/// bounds, duplicate or illegal cell records, catalog keys and archive identity, rotations, footprint overlaps,
	/// duplicate fixed items, and that every seed and attraction id is one the saved allocator could have produced —
	/// with both allocators far enough from <see cref="int.MaxValue"/> that restoring them cannot overflow. Footprint
	/// bounds use the cell map's dimensions (an object grid is never larger); <see cref="RestoreObjects"/> re-checks
	/// against the actual target grid. Throws <see cref="InvalidDataException"/> naming the first inconsistency.
	/// </summary>
	public static void ValidateSnapshot( Data snapshot, ObjectCatalog catalog )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		ArgumentNullException.ThrowIfNull( catalog );
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"Layout snapshot is inconsistent: {message}." );
		}
		Require( snapshot.Cells != null, "the snapshot has no cell map" );
		Require( snapshot.Objects != null, "the snapshot has no object list" );
		var cellMap = snapshot.Cells!;
		var objects = snapshot.Objects!;
		ValidateCells( snapshot );
		Require( snapshot.NextSeed >= 1, "next seed is before the first placement" );
		Require( snapshot.NextSeed < int.MaxValue, "the placement-seed allocator is exhausted" );
		Require( snapshot.NextAttractionId >= 1, "the attraction-id allocator is before the first allocation" );
		Require( snapshot.NextAttractionId < int.MaxValue, "the attraction-id allocator is exhausted" );
		var seeds = new HashSet<int>();
		var footprints = new HashSet<(int, int)>();
		var fixedItems = new HashSet<int>();
		for ( var index = 0; index < objects.Count; index++ )
		{
			var record = objects[index];
			Require( record != null, $"object record {index} is missing" );
			var entry = catalog.Find( record!.InfoId );
			Require( entry != null, $"object {index} has unknown Info.Id {record.InfoId}" );
			Require( !entry!.IsTool, $"object {index} ({entry.DisplayName}) is a tool, not a placed object" );
			Require( string.Equals( entry.ArchivePath, record.Archive, StringComparison.OrdinalIgnoreCase ), $"object {index} resolved to {entry.ArchivePath}, not the saved {record.Archive}" );
			Require( entry.IsFixedItem == record.IsFixedItem, $"object {index} ({entry.DisplayName}) is fixed-item {entry.IsFixedItem}, not the saved {record.IsFixedItem}" );
			Require( ObjectFootprint.IsValidRotation( record.Rotation ), $"object {index} has rotation {record.Rotation}" );
			Require( float.IsFinite( record.BaseHeight ), $"object {index} has a non-finite base height" );
			if ( entry.IsFixedItem )
			{
				Require( fixedItems.Add( entry.InfoId ), $"fixed item {entry.DisplayName} is placed more than once" );
				continue;
			}
			foreach ( var (x, y, _) in ObjectFootprint.GetCells( entry.Shape, record.X, record.Y, record.Rotation ) )
			{
				Require( x >= 0 && y >= 0 && x < cellMap.Width && y < cellMap.Height,
					$"object {index} ({entry.DisplayName}) reaches outside the cell map at ({x}, {y})" );
				Require( footprints.Add( (x, y) ), $"objects overlap at ({x}, {y})" );
			}
		}
		var previousAttraction = 0;
		for ( var index = 0; index < objects.Count; index++ )
		{
			var record = objects[index]!;
			var ordinal = record.CreationSeed ^ snapshot.ObjectScriptKey;
			Require( ordinal >= 1 && ordinal < snapshot.NextSeed,
				$"object {index}'s creation seed is not the {snapshot.ObjectScriptKey}-keyed ordinal of a placement before {snapshot.NextSeed}" );
			Require( seeds.Add( ordinal ), "two objects share one placement ordinal" );
			Require( record.AttractionId >= 1 && record.AttractionId < snapshot.NextAttractionId,
				$"object {index}'s attraction id {record.AttractionId} is outside the saved allocator (1..{snapshot.NextAttractionId})" );
			Require( record.AttractionId > previousAttraction,
				$"object {index}'s attraction id {record.AttractionId} does not follow {previousAttraction} in placement order" );
			previousAttraction = record.AttractionId;
		}
	}

	private static void ValidateCells( Data snapshot )
	{
		var cells = snapshot.Cells;
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"Layout snapshot is inconsistent: {message}." );
		}
		Require( cells.Width > 0 && cells.Height > 0 && cells.Width <= 1024 && cells.Height <= 1024, $"cell map is {cells.Width}×{cells.Height}" );
		Require( cells.Version >= 0, "cell map version is negative" );
		Require( cells.Cells != null, "the cell map has no cell records" );
		Require( cells.Cells!.Count <= (long)cells.Width * cells.Height, "more cell records than cells" );
		var seen = new HashSet<(int, int)>();
		for ( var index = 0; index < cells.Cells.Count; index++ )
		{
			var cell = cells.Cells[index];
			Require( cell != null, $"cell record {index} is missing" );
			Require( cell!.X >= 0 && cell.Y >= 0 && cell.X < cells.Width && cell.Y < cells.Height, $"cell ({cell.X}, {cell.Y}) is outside the map" );
			Require( seen.Add( (cell.X, cell.Y) ), $"cell ({cell.X}, {cell.Y}) is recorded twice" );
			Require( cell.QueueLink is 0 or 1 or 4 or 16 or 64, $"cell ({cell.X}, {cell.Y}) has queue link {cell.QueueLink}" );
			Require( cell.QueueLink == 0 || cell.Type == (byte)ParkCellType.Queue, $"cell ({cell.X}, {cell.Y}) has a queue link but type {cell.Type}" );
			Require( cell.Placements >= 0, $"cell ({cell.X}, {cell.Y}) has placement count {cell.Placements}" );
		}
	}

	/// <summary>A fresh cell map with every snapshot cell written and the saved version restored (an empty map otherwise).</summary>
	public static ParkCellMap RestoreCellMap( Data snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		ValidateCells( snapshot );
		var cells = new ParkCellMap( snapshot.Cells.Width, snapshot.Cells.Height );
		foreach ( var cell in snapshot.Cells.Cells )
		{
			cells.SetType( cell.X, cell.Y, cell.Type );
			cells.SetLinks( cell.X, cell.Y, cell.Links );
			if ( cell.Type == (byte)ParkCellType.Queue )
				cells.SetQueueLink( cell.X, cell.Y, cell.QueueLink );
			cells.SetFlags( cell.X, cell.Y, (ParkCellFlags)cell.Flags );
			cells.SetPlacementCount( cell.X, cell.Y, cell.Placements );
		}
		cells.RestoreVersion( snapshot.Cells.Version );
		return cells;
	}

	/// <summary>
	/// Places every snapshot object into the candidate <paramref name="target"/> — which must be an unused park (no
	/// objects, and a script world that never allocated: build the resume candidate without the shipped/imported/
	/// default objects, because clearing those afterwards leaves the allocators advanced and is rejected here before
	/// anything is placed) and built with the saved park's world seed, which the script-key check proves. Each object
	/// goes through the import placement path with its own saved seed and its saved attraction id — the runtime adopts
	/// the id, so nothing is charged, <see cref="ParkObjects.ObjectPlaced"/> fires exactly once, and removed objects'
	/// identity holes are reproduced in constant time (dead ids are skipped by the frontier, never replayed — the
	/// saved next id is adopted at the end, so a park that removed thousands of objects restores no slower than one
	/// that removed none; survivors are never renumbered). Every object's effective placement, base height and
	/// attraction id is checked against the record, so a diverging catalog, edition or terrain fails the load. On
	/// failure anything already placed is removed again (its GPU resources disposed) and the exception propagates; the
	/// live level is not touched. A list entry's index equals its snapshot index, and its <c>Visitors.AttractionId</c>
	/// equals the record's — the keys the guest, VM and economy restores join by.
	/// </summary>
	public static IReadOnlyList<OriginalObject> RestoreObjects( Data snapshot, ParkObjects target )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		ArgumentNullException.ThrowIfNull( target );
		ValidateSnapshot( snapshot, target.Catalog );
		if ( target.Objects.Count != 0 )
			throw new InvalidDataException( "Layout objects restore into an unused ParkObjects (the fresh candidate level); this one already placed objects." );
		if ( target.NextAttractionId != 1 )
			throw new InvalidDataException(
				$"Layout objects restore into a candidate whose script world never allocated; this one's next attraction id is already {target.NextAttractionId}. " +
				"Build the resume candidate without its shipped/imported/default objects — clearing them afterwards leaves the allocators advanced." );
		if ( target.ScriptWorld.Scripts.Count != 0 )
			throw new InvalidDataException(
				$"Layout objects restore into a candidate whose script world holds no scripts; this one already holds {target.ScriptWorld.Scripts.Count}." );
		if ( snapshot.ObjectScriptKey != target.ObjectScriptKey )
			throw new InvalidDataException( $"Layout snapshot was saved with object-script key {snapshot.ObjectScriptKey}, but this park's is {target.ObjectScriptKey} (another world seed)." );
		for ( var index = 0; index < snapshot.Objects.Count; index++ )
		{
			var record = snapshot.Objects[index];
			var entry = target.Catalog.Find( record.InfoId )!;
			if ( entry.IsFixedItem )
				continue;
			foreach ( var (x, y, _) in ObjectFootprint.GetCells( entry.Shape, record.X, record.Y, record.Rotation ) )
				if ( x < 0 || y < 0 || x >= target.Grid.Width || y >= target.Grid.Height )
					throw new InvalidDataException( $"object {index} ({entry.DisplayName}) reaches outside this level's grid at ({x}, {y})." );
		}
		var placed = new List<OriginalObject>( snapshot.Objects.Count );
		try
		{
			for ( var index = 0; index < snapshot.Objects.Count; index++ )
			{
				var record = snapshot.Objects[index];
				var entry = target.Catalog.Find( record.InfoId )!;
				// The runtime adopts the record's saved attraction id; validation has already proven the ids increase
				// with placement order, so no id is taken twice and no dead id is ever replayed.
				var item = target.AddRestored( entry, record.X, record.Y, record.Rotation, record.Open, record.CreationSeed, record.AttractionId );
				var placement = item.Placement;
				if ( placement.X != record.X || placement.Y != record.Y || placement.Rotation != record.Rotation )
					throw new InvalidDataException(
						$"object {index} ({entry.DisplayName}) restored at ({placement.X}, {placement.Y}, {placement.Rotation}), not the saved ({record.X}, {record.Y}, {record.Rotation})." );
				if ( placement.BaseHeight != record.BaseHeight )
					throw new InvalidDataException(
						$"object {index} ({entry.DisplayName}) stands at base height {placement.BaseHeight}, not the saved {record.BaseHeight}: the terrain under it changed." );
				if ( item.Visitors.AttractionId != record.AttractionId )
					throw new InvalidDataException(
						$"object {index} ({entry.DisplayName}) got attraction id {item.Visitors.AttractionId}, not the saved {record.AttractionId}: the restore path stopped preserving ids." );
				placed.Add( item );
			}
			// Ids the park burned after its last surviving object: adopt the saved frontier, no per-id work.
			target.RestoreAttractionIdAllocator( snapshot.NextAttractionId );
			if ( target.NextAttractionId != snapshot.NextAttractionId )
				throw new InvalidDataException( $"Restored attraction-id allocator is at {target.NextAttractionId}, not the saved {snapshot.NextAttractionId}." );
		}
		catch
		{
			target.Clear();
			throw;
		}
		target.RestoreSeedAllocator( snapshot.NextSeed );
		return placed;
	}
}
