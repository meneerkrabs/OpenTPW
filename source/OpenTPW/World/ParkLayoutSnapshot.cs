using OpenTPW.Mods;

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
		[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
		public int QueuePassSchemaVersion { get; init; }
		[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
		public List<QueuePassResidue>? QueuePassResidues { get; init; }
		/// <summary>
		/// <see cref="ParkCellMap.BlockStamp"/> of every 16 × 16-cell block, row by row (the walk's changed-ground reroute, WALK-007); null
		/// when no block is stamped, and in saves made before the stamps existed, which restore every block unstamped.
		/// </summary>
		[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
		public int[]? BlockStamps { get; init; }
		/// <summary><see cref="ParkCellMap.StampCounter"/>; absent (0) in saves made before the stamps existed.</summary>
		[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
		public int StampCounter { get; init; }
		/// <summary>
		/// The park's land (Buy Land): its fence and which cells it owns. Null in saves written before land existed and for
		/// maps without land; the loader then sets the level's land up afresh. The cell records' flags omit the unowned bit,
		/// which this record carries for the whole map.
		/// </summary>
		public LandData? Land { get; init; }
	}

	/// <summary>The land of a park: the fence's gate posts and ring, and the unowned cells as a row-major bit map.</summary>
	public sealed record LandData
	{
		public required int PostAX { get; init; }
		public required int PostAY { get; init; }
		public required int PostBX { get; init; }
		public required int PostBY { get; init; }
		/// <summary>Every fence post from post A to post B in walking order, as (x, y) pairs.</summary>
		public required List<int[]> Ring { get; init; }
		/// <summary>Base64 of one bit per cell in row order (bit <c>i &amp; 7</c> of byte <c>i &gt;&gt; 3</c>): set where the cell is not owned.</summary>
		public required string Unowned { get; init; }
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
		/// <summary>The queue link (map cell <c>+13</c>): 0 or one cardinal bit, toward a queue cell's predecessor or the way an entrance or exit cell opens, and only on those cells.</summary>
		public required byte QueueLink { get; init; }
		/// <summary>
		/// The owner word (map cell <c>+0x10</c>, <see cref="ParkCellMap.OwnerAt"/>): 0 on a cell nothing owns and in saves made before the owner word existed, whose object cells then
		/// take their owner again when the objects are placed back.
		/// </summary>
		[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
		public ushort Owner { get; init; }
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
		/// <summary>
		/// How <see cref="BaseHeight"/> was taken: 1 = the midpoint of the footprint's lowest and highest terrain vertex (<see cref="ParkObjects.GetBaseHeight"/>); absent in a save from before that rule,
		/// which took the mean ground height of the footprint's cells and restores with it.
		/// </summary>
		[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
		public int? BaseHeightRule { get; init; }
		/// <summary>Whether the entry is a fixed item (<c>Info.DontApplyOffset</c>); must match the catalog at load.</summary>
		public required bool IsFixedItem { get; init; }
		/// <summary>
		/// The object's thing id, a slot of the park's thing pool (<see cref="ThingPool"/>; the resume envelope keeps the pool). 0 in a save from before the pool: the object then takes the next free
		/// slot when it is placed back.
		/// </summary>
		[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
		public int ThingId { get; init; }
	}

	/// <summary>The base height rule a placement is saved with: 1 (vertex midpoint), or none for a placement restored from a save that predates it.</summary>
	internal static int? BaseHeightRuleOf( ObjectPlacement placement ) => placement.LegacyBaseHeight ? null : 1;

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
				// with land, the land record carries the unowned bit of every cell, so a cell is not recorded for it alone
				var flags = (ushort)(cells.Land != null ? cells.FlagsAt( x, y ) & ~ParkCellFlags.Unowned : cells.FlagsAt( x, y ));
				var placements = cells.PlacementCountAt( x, y );
				var links = cells.LinksAt( x, y );
				var queueLink = cells.QueueLinkAt( x, y );
				var residue = cells.HasQueuePassResidue( x, y );
				if ( !residue && (type == 0 && cells.OwnerAt( x, y ) != 0 || type is 0 or 1 && queueLink != 0) )
					throw new InvalidDataException( "Unclassified queue metadata cannot be saved as an ordinary cell." );
				if ( !residue && type == 0 && flags == 0 && placements == 0 && links == 0 && queueLink == 0 )
					continue;
				cellRecords.Add( new CellData { X = x, Y = y, Type = type, Flags = flags, Placements = placements, Links = links, QueueLink = residue ? (byte)0 : queueLink, Owner = residue ? (ushort)0 : cells.OwnerAt( x, y ) } );
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
				BaseHeightRule = BaseHeightRuleOf( item.Placement ),
				IsFixedItem = item.Entry.IsFixedItem,
				ThingId = item.ThingId
			} );
		}
		var stamps = cells.CaptureBlockStamps();
		return new Data
		{
			Cells = new CellMapData { Width = cells.Width, Height = cells.Height, Version = cells.Version, Cells = cellRecords, QueuePassSchemaVersion = cells.HasQueuePassResidues ? 1 : 0, QueuePassResidues = cells.HasQueuePassResidues ? cells.CaptureQueuePassResidues().ToList() : null, BlockStamps = stamps, StampCounter = stamps == null ? 0 : cells.StampCounter, Land = CaptureLand( cells ) },
			Objects = objectRecords,
			NextSeed = objects.NextPlacementSeed,
			NextAttractionId = objects.NextAttractionId,
			ObjectScriptKey = objects.ObjectScriptKey
		};
	}

	/// <summary>The land record of <paramref name="cells"/>, null when the map has no land.</summary>
	public static LandData? CaptureLand( ParkCellMap cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		if ( cells.Land is not { } land )
			return null;
		var bits = new byte[(cells.Width * cells.Height + 7) / 8];
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				var index = y * cells.Width + x;
				if ( cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) )
					bits[index >> 3] |= (byte)(1 << (index & 7));
			}
		}
		return new LandData
		{
			PostAX = land.PostA.X, PostAY = land.PostA.Y, PostBX = land.PostB.X, PostBY = land.PostB.Y,
			Ring = land.Ring.Select( post => new[] { post.X, post.Y } ).ToList(),
			Unowned = Convert.ToBase64String( bits )
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
	public static void ValidateSnapshot( Data snapshot, ObjectCatalog catalog, ModSet? activeMods = null )
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
		ValidateCells( snapshot, activeMods );
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
			Require( record.BaseHeightRule is null or 1, $"object {index} has an unknown base height rule {record.BaseHeightRule}" );
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
		Require( cellMap.QueuePassResidues == null || !cellMap.QueuePassResidues.Any( residue => footprints.Contains( (residue.X, residue.Y) ) ),
			"queue pass residues overlap an unsupported object footprint" );
		var previousAttraction = 0;
		var thingIds = new HashSet<int>();
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
			Require( record.ThingId is >= 0 and <= ThingPool.Capacity, $"object {index}'s thing id {record.ThingId} is not a slot of the thing pool (1..{ThingPool.Capacity})" );
			Require( record.ThingId == 0 || thingIds.Add( record.ThingId ), $"two objects share thing id {record.ThingId}" );
		}
	}

	private static void ValidateQueuePassResidues( CellMapData map )
	{
		if ( map.QueuePassSchemaVersion == 0 && map.QueuePassResidues == null ) return;
		if ( map.QueuePassSchemaVersion != 1 || map.QueuePassResidues is not { Count: > 0 } residues || residues.Count > (long)map.Width * map.Height )
			throw new InvalidDataException( "Unsupported queue pass residue schema." );
		var seen = new HashSet<(int, int)>();
		var ordinary = new Dictionary<(int, int), CellData>();
		foreach ( var cell in map.Cells )
			if ( cell == null || !ordinary.TryAdd( (cell.X, cell.Y), cell ) )
				throw new InvalidDataException( "Invalid ordinary cell records beside queue residues." );
		foreach ( var residue in residues )
		{
			if ( residue == null || residue.X < 0 || residue.Y < 0 || residue.X >= map.Width || residue.Y >= map.Height
				|| !seen.Add( (residue.X, residue.Y) ) || residue.Type is not (0 or 1)
				|| residue.QueueLink is not (0 or 1 or 4 or 16 or 64) || residue.Owner > 128 * 128 )
				throw new InvalidDataException( "Invalid queue pass residue." );
			if ( !ordinary.TryGetValue( (residue.X, residue.Y), out var cell ) )
				throw new InvalidDataException( "Queue residue must name exactly one ordinary cell record." );
			if ( cell.Type != residue.Type || cell.Owner != 0 || cell.QueueLink != 0
				|| (cell.Type == 0 && (cell.Placements != 0 || (cell.Flags & ~(ushort)ParkCellFlags.Unowned) != 0))
				|| (cell.Type == 1 ? residue.QueueLink == 0 : cell.Links == 0 && residue.QueueLink == 0 && residue.Owner == 0) )
				throw new InvalidDataException( "Queue residue contradicts its ordinary cell record." );
		}
	}

	private static void ValidateCells( Data snapshot, ModSet? activeMods = null )
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
		ValidateQueuePassResidues( cells );
		var seen = new HashSet<(int, int)>();
		for ( var index = 0; index < cells.Cells.Count; index++ )
		{
			var cell = cells.Cells[index];
			Require( cell != null, $"cell record {index} is missing" );
			Require( cell!.X >= 0 && cell.Y >= 0 && cell.X < cells.Width && cell.Y < cells.Height, $"cell ({cell.X}, {cell.Y}) is outside the map" );
			Require( seen.Add( (cell.X, cell.Y) ), $"cell ({cell.X}, {cell.Y}) is recorded twice" );
			// [EXT:pools-save] a pool cell is a plain water cell: no links, queue link, owner or placement count, and it exists only while the pools extension is on
			if ( cell.Type == ParkCellTypes.PoolWater )
			{
				Require( (activeMods ?? OpenTpwExtensions.Active).IsEnabled( OfficialExtensions.Pools.Id ), $"cell ({cell.X}, {cell.Y}) is a pool cell, which needs the {OfficialExtensions.Pools.Id} extension" );
				Require( cell.Links == 0 && cell.QueueLink == 0 && cell.Owner == 0 && cell.Placements == 0, $"pool cell ({cell.X}, {cell.Y}) carries links, an owner or a placement count" );
				// [EXT:pools-river] [EXT:pools-waves] a pool cell's flags are the river bit and the wave settings' bits, nothing else; the wave bits are a stored height (1 to 3) and an interval (0 to 2), on or off
				var poolBits = (ParkCellFlags)cell.Flags;
				Require( (poolBits & ~(ParkCellFlags.PoolRiver | ParkCellFlags.PoolWaveBits)) == 0, $"pool cell ({cell.X}, {cell.Y}) carries flags {cell.Flags:X4} besides the river and wave bits" );
				Require( (poolBits & ParkCellFlags.PoolWaveBits) == 0 || PoolWaveSettings.Has( poolBits ), $"pool cell ({cell.X}, {cell.Y}) has wave bits but no wave height" );
				Require( ((int)(poolBits & ParkCellFlags.PoolWaveIntervalMask) >> 14) <= 2, $"pool cell ({cell.X}, {cell.Y}) has the unused wave interval 3" );
			}
			// [EXT:pools-river] [EXT:pools-waves] the river and wave bits exist on pool cells only; a coaster's marker cell keeps the original's station head bit, which a wave height bit shares
			if ( cell.Type != ParkCellTypes.PoolWater )
			{
				var original = cell.Type == CoasterCellMarkers.MarkerType ? CoasterCellMarkers.StationHeadMark : 0;
				Require( (cell.Flags & (ushort)(ParkCellFlags.PoolRiver | ParkCellFlags.PoolWaveBits) & ~original) == 0, $"cell ({cell.X}, {cell.Y}) carries pool river or wave flags but is no pool cell" );
			}
			Require( cell.QueueLink is 0 or 1 or 4 or 16 or 64, $"cell ({cell.X}, {cell.Y}) has queue link {cell.QueueLink}" );
			Require( cell.QueueLink == 0 || ParkCellTypes.CarriesQueueLink( cell.Type ), $"cell ({cell.X}, {cell.Y}) has a queue link but type {cell.Type}" );
			Require( cell.Placements >= 0, $"cell ({cell.X}, {cell.Y}) has placement count {cell.Placements}" );
			Require( cell.Owner == 0 || cell.Type != 0, $"cell ({cell.X}, {cell.Y}) has owner word {cell.Owner} but is empty" );
			// an owner word is the cell word y * 128 + x + 1 of a cell of the 128 x 128 array (0x4D0AF0 decodes it as cell - 1)
			Require( cell.Owner <= 128 * 128, $"cell ({cell.X}, {cell.Y}) has owner word {cell.Owner} beyond the cell array" );
			// with a land record the unowned flag lives in the record's bits, never in a cell record (see CaptureSnapshot)
			Require( cells.Land == null || (cell.Flags & (ushort)ParkCellFlags.Unowned) == 0,
				$"cell ({cell.X}, {cell.Y}) carries the unowned flag beside a land record" );
		}
		if ( cells.BlockStamps is { } stamps )
		{
			var blocks = (long)((cells.Width + ParkCellMap.StampBlock - 1) / ParkCellMap.StampBlock) * ((cells.Height + ParkCellMap.StampBlock - 1) / ParkCellMap.StampBlock);
			Require( stamps.Length == blocks, $"the cell map has {stamps.Length} block stamps for {blocks} blocks" );
			Require( Array.TrueForAll( stamps, stamp => stamp >= 0 && stamp <= cells.StampCounter ), "a block stamp is negative or above the stamp counter" );
		}
		else
			Require( cells.StampCounter == 0, "the cell map has a stamp counter but no block stamps" );
		if ( cells.Land is { } land )
			ReadLand( land, cells.Width, cells.Height );
	}

	/// <summary>The land and unowned-cell bits of a land record; throws <see cref="InvalidDataException"/> for an inconsistent one.</summary>
	private static (ParkLand Land, byte[] Unowned) ReadLand( LandData data, int width, int height )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"Layout snapshot is inconsistent: {message}." );
		}
		Require( data.Ring != null && data.Unowned != null, "the land record is incomplete" );
		Require( data.Ring!.Count is >= 2 and <= ParkLand.MaximumPosts, $"the fence has {data.Ring.Count} posts" );
		Require( data.Ring.All( post => post is { Length: 2 } && post[0] >= 0 && post[1] >= 0 && post[0] < width && post[1] < height ),
			"a fence post is malformed or outside the map" );
		byte[] bits;
		try
		{
			bits = Convert.FromBase64String( data.Unowned! );
		}
		catch ( FormatException )
		{
			throw new InvalidDataException( "Layout snapshot is inconsistent: the unowned cells are not Base64." );
		}
		Require( bits.Length == (width * height + 7) / 8, $"the unowned cells cover {bits.Length} bytes, not {(width * height + 7) / 8}" );
		try
		{
			return (new ParkLand( (data.PostAX, data.PostAY), (data.PostBX, data.PostBY), data.Ring.Select( post => (post[0], post[1]) ).ToArray() ), bits);
		}
		catch ( InvalidDataException exception )
		{
			throw new InvalidDataException( $"Layout snapshot is inconsistent: {exception.Message}", exception );
		}
	}

	/// <summary>A fresh cell map with every snapshot cell written and the saved version restored (an empty map otherwise).</summary>
	public static ParkCellMap RestoreCellMap( Data snapshot ) => RestoreCellMap( snapshot, null );

	/// <summary>
	/// <see cref="RestoreCellMap(Data)"/> with the saved land; a snapshot without a land record (a save from before land
	/// existed) gets <paramref name="newLand"/> instead, applied to the restored cells before the saved version is restored.
	/// </summary>
	public static ParkCellMap RestoreCellMap( Data snapshot, Action<ParkCellMap>? newLand )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		ValidateCells( snapshot );
		var cells = new ParkCellMap( snapshot.Cells.Width, snapshot.Cells.Height );
		foreach ( var cell in snapshot.Cells.Cells )
		{
			cells.SetType( cell.X, cell.Y, cell.Type );
			cells.SetLinks( cell.X, cell.Y, cell.Links );
			if ( ParkCellTypes.CarriesQueueLink( cell.Type ) )
				cells.SetQueueLink( cell.X, cell.Y, cell.QueueLink );
			cells.SetFlags( cell.X, cell.Y, (ParkCellFlags)cell.Flags );
			cells.SetPlacementCount( cell.X, cell.Y, cell.Placements );
			if ( cell.Owner != 0 )
				cells.SetOwner( cell.X, cell.Y, cell.Owner );
		}
		if ( snapshot.Cells.Land is { } saved )
		{
			var (land, unowned) = ReadLand( saved, cells.Width, cells.Height );
			for ( var index = 0; index < cells.Width * cells.Height; index++ )
			{
				if ( (unowned[index >> 3] & (1 << (index & 7))) == 0 )
					continue;
				var (x, y) = (index % cells.Width, index / cells.Width);
				cells.SetFlags( x, y, cells.FlagsAt( x, y ) | ParkCellFlags.Unowned );
			}
			cells.SetLand( land );
		}
		else
			newLand?.Invoke( cells );
		foreach ( var residue in snapshot.Cells.QueuePassResidues ?? new List<QueuePassResidue>() )
			cells.ApplyQueuePassMetadata( residue.X, residue.Y, residue.QueueLink, residue.Owner );
		cells.RestoreVersion( snapshot.Cells.Version );
		// The writes above stamped the blocks with restore-time versions; the saved stamps (none before WALK-007) replace them.
		cells.RestoreBlockStamps( snapshot.Cells.StampCounter, snapshot.Cells.BlockStamps );
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
		var placed = new List<OriginalObject>();
		foreach ( var _ in RestoreObjectSteps( snapshot, target, placed ) )
		{
		}
		return placed;
	}

	/// <summary>
	/// <see cref="RestoreObjects"/> one object a step, for a park built over frames (a co-op join or resync: docs/COOP.md, "The game's join"). The first step checks the whole
	/// snapshot before it places anything; each step places one object in the saved order into <paramref name="placed"/>, the last restores the allocators, and every step gives the
	/// share done (1 only in the last). A failure clears the target, as the call does. This is no rollback: a restore given up between its steps leaves its objects in the target and the
	/// script world advanced, so it is for a candidate its owner drops whole when it gives up (a join's build disposes its scene and level), as an original save's import given up
	/// between its objects is (<see cref="ParkObjects.ImportOriginalSteps"/>).
	/// </summary>
	internal static IEnumerable<float> RestoreObjectSteps( Data snapshot, ParkObjects target, List<OriginalObject> placed )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		ArgumentNullException.ThrowIfNull( target );
		ArgumentNullException.ThrowIfNull( placed );
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
		// a step places its object and checks it under the clean-up a failure needs; the step ends outside it (no yield inside a try with a catch)
		for ( var index = 0; index < snapshot.Objects.Count; index++ )
		{
			try
			{
				placed.Add( RestoreObject( snapshot, target, index ) );
			}
			catch
			{
				target.Clear();
				throw;
			}
			// the share reaches 1 only with the allocators restored, in the last step
			yield return (index + 1) / (float)(snapshot.Objects.Count + 1);
		}
		try
		{
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
		yield return 1;
	}

	/// <summary>Places the snapshot's object of <paramref name="index"/> and checks that it stands where, how high and under the id it was saved with.</summary>
	private static OriginalObject RestoreObject( Data snapshot, ParkObjects target, int index )
	{
		var record = snapshot.Objects[index];
		var entry = target.Catalog.Find( record.InfoId )!;
		// The runtime adopts the record's saved attraction id; validation has already proven the ids increase
		// with placement order, so no id is taken twice and no dead id is ever replayed.
		var item = target.AddRestored( entry, record.X, record.Y, record.Rotation, record.Open, record.CreationSeed, record.AttractionId, legacyBaseHeight: record.BaseHeightRule == null, thingId: record.ThingId );
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
		return item;
	}
}
