namespace OpenTPW;

/// <summary>
/// Original map cell types that the build tools write (docs/reverse/PATH-plan.md §2). Other original
/// types (objects, tracks, terrain) are kept as raw bytes and named only when a tool needs them.
/// </summary>
public enum ParkCellType : byte
{
	Empty = 0,
	Path = 1,
	Queue = 3,
	/// <summary>
	/// [EXT:pools-cell-type] A pool water cell of the <c>opentpw.pools</c> extension (docs/POOLS.md): <see cref="ParkCellTypes.PoolWater"/>. No original type uses
	/// the value, and it is not walkable.
	/// </summary>
	PoolWater = ParkCellTypes.PoolWater
}

/// <summary>The map cell types the world init and the object placement write besides <see cref="ParkCellType"/> (tp.exe numbering).</summary>
public static class ParkCellTypes
{
	/// <summary>Water outside the terrain (MAP bit 0x02).</summary>
	public const byte Water = 2;
	/// <summary>An object's occupied cell ('*', and the shape types 11, 16 and 23, which the placement stores as 4).</summary>
	public const byte Object = 4;
	/// <summary>No terrain: a heightfield hole or a cell beyond the heightfield.</summary>
	public const byte NoTerrain = 7;
	/// <summary>An object's entrance cell (shape '8', '6', '2', '4', 'O').</summary>
	public const byte Entrance = 9;
	/// <summary>An object's exit cell (shape 'N', 'E', 'S', 'W', 'X').</summary>
	public const byte Exit = 10;
	/// <summary>A fixed walkway outside the terrain (MAP bit 0x80: the bridge deck, the entrance crossings).</summary>
	public const byte FixedWalkway = 30;
	/// <summary>
	/// The coaster module's cell marker (<c>SetCell(0x15)</c> of <c>0x452D30</c>, called by the coaster tool code <c>0x434412</c>, <c>0x443057</c>, <c>0x444EE7</c> and <c>0x445B50</c>), which the
	/// placement also writes on the cell beyond a track end (<c>&lt;</c>, <c>&gt;</c>, <c>D</c> of a coaster station) with the ride's owner word; the validator's modes 0x15 and 0x16
	/// accept it (<c>0x535B53</c>). OpenTPW's coaster cell markers write it (<see cref="CoasterCellMarkers"/>).
	/// </summary>
	public const byte TrackConnection = 0x15;
	/// <summary>A park fence post on the structure layer.</summary>
	public const byte Hoarding = 0x19;
	/// <summary>
	/// [EXT:pools-cell-type] The pool water cell of the <c>opentpw.pools</c> extension (docs/POOLS.md). The original's cell types stop at 30 (<see cref="FixedWalkway"/>); the
	/// placement writes only the shape types 0 to 23 and the walks accept 1, 3, 9, 10 and the track types, so 0x40 (64) collides with no original value, with no byte of
	/// the original's cell record (the saves hold the type in one byte) and with the types the engine keeps as raw bytes. It is not <see cref="ParkCellType.Path"/>, so it is not walkable.
	/// </summary>
	// [EXT:pools-cell-type] OpenTPW's own cell type; the original game has no pools
	public const byte PoolWater = 0x40;

	/// <summary>The types of a track cell on the second layer (kart and water origins and stations, and the fence post): tp.exe's cell predicate <c>0x53AD30</c>.</summary>
	// [BIN:TPW-EXE:0x0053AD30 track cell] true for the cell types 0xB, 0xD, 0x10, 0x12 and 0x19
	public static bool IsTrackCell( byte type ) => type is TrackCellTypes.KartOrigin or TrackCellTypes.KartStart or TrackCellTypes.WaterOrigin or TrackCellTypes.WaterStart or Hoarding;

	/// <summary>The types of a track tile's extension cells, whose head the cell lookups follow through the owner word: <c>0x53AD90</c>.</summary>
	// [BIN:TPW-EXE:0x0053AD90 track extension] true for the cell types 0xC and 0x11
	public static bool IsTrackExtension( byte type ) => type is TrackCellTypes.Extension or 0x11;

	/// <summary>The cell types a ride object's footprint writes and the area clear and the owner lookup treat as the object's cells: 4, 9 and 10.</summary>
	// [BIN:TPW-EXE:0x0052BE74 object cell types] the area clear tests the type with 4 and the range 9..10 (0x52BE74..0x52BE85); the owner lookup 0x527D60 tests 4, 9 and 10 (0x527DB3..0x527DC7)
	public static bool IsObjectCell( byte type ) => type is Object or Entrance or Exit;

	/// <summary>The cell types whose queue link (<c>+0xD</c>) means something: a queue cell's link to its predecessor, and the direction an entrance or exit cell opens.</summary>
	// [BIN:TPW-EXE:0x004D8750 queue link readers] the edge test and the link writer read +0xD of types 9 and 10 (0x534A18..0x534A74), the queue walk and the cell models of type 3 (0x4DE670, 0x535DD0); Easymode's saved entrance and exit cells carry the rotated direction byte there
	public static bool CarriesQueueLink( byte type ) => type is (byte)ParkCellType.Queue or Entrance or Exit;
}

/// <summary>Map cell flag bits the build tools read (PATH-plan §5.2, §5.4).</summary>
[Flags]
public enum ParkCellFlags : ushort
{
	None = 0,
	/// <summary>
	/// The flag word is set to exactly this by the placement on the cells of a '+' or '#' footprint cell (an add-on over its ride's track; <c>0x53A510(0x10)</c>)
	/// and on the track layer's cell beneath; the area clear refuses an object whose run meets it (<c>0x535DB0(0x10)</c>), and a flagged track tile gets no piece instance.
	/// </summary>
	AddOn = 0x10,
	NoModify = 0x20,
	Unowned = 0x40,
	/// <summary>The world init's copy of MAP bit 0x10 (the entrance complex).</summary>
	EntranceArea = 0x80,
	/// <summary>The world init's copy of MAP bit 0x04 (high byte bit 0x04); its readers are not traced.</summary>
	MapBit04 = 0x400,
	/// <summary>
	/// [EXT:pools-river] On a pool water cell: the cell is part of a lazy river's ring (docs/POOLS.md, "Lazy river"). The original's flag word has no use of this bit, and only pool cells carry it.
	/// </summary>
	PoolRiver = 0x1000,
	/// <summary>[EXT:pools-waves] On a pool water cell: the pool's wave machine is switched on (<see cref="PoolWaveSettings"/>).</summary>
	PoolWavesOn = 0x2000,
	/// <summary>
	/// [EXT:pools-waves] The two wave-height bits (0 means the pool has no wave settings yet, 1 to 3 is the height). A coaster station head's marker cell (type 21) carries 0x100 as the
	/// original's marker bit (<see cref="CoasterCellMarkers.StationHeadMark"/>), so these bits are a wave height on pool cells only: a reader must never scan other cells for them.
	/// </summary>
	PoolWaveHeightMask = 0x0300,
	/// <summary>[EXT:pools-waves] The two wave-interval bits (0 to 2 is the interval; 3 is unused).</summary>
	PoolWaveIntervalMask = 0xC000,
	/// <summary>[EXT:pools-waves] Every pool flag a pool cell may carry besides the river bit: the wave settings' bits.</summary>
	PoolWaveBits = PoolWavesOn | PoolWaveHeightMask | PoolWaveIntervalMask
}

/// <summary>
/// The CPU-only park cell map shared by the path and queue tools (PATH-plan §9.1/§9.6): per cell the
/// original type byte, flags, the same-type placement counter, the cardinal links (1, 4, 16, 64) and the
/// queue link. It is the source of truth for path and queue cells: <see cref="GuestPathGrid"/> is a view
/// that derives the guests' walk graph from it (path cells are walkable, their links are the connections).
/// </summary>
public sealed partial class ParkCellMap
{
	/// <summary>[EXT:pools-bridges] Optional elevated walking layer; underlying water stays unchanged.</summary>
	public PoolBridgeWorld? PoolBridges { get; internal set; }
	private readonly byte[] types;
	// [EXT:coaster-marker-guard] Derived lease generations, not saved or hashed.
	private readonly int[] coasterGenerations;
	internal int CoasterGenerationAt( int x, int y ) => coasterGenerations[Index( x, y )];
	internal int ClaimCoasterGeneration( int x, int y ) => ++coasterGenerations[Index( x, y )];
	private readonly ushort[] flags;
	private readonly short[] placements;
	private readonly byte[] links;
	private readonly byte[] queueLinks;
	private readonly ushort[] owners;
	private readonly int[] typeCounts = new int[256];
	private readonly int[] blockStamps;
	/// <summary>Cells per side of a stamp block (the original's 16 × 16-cell blocks).</summary>
	public const int StampBlock = 16;

	public ParkCellMap( int width, int height )
	{
		if ( width <= 0 || height <= 0 || width > 1024 || height > 1024 )
			throw new ArgumentOutOfRangeException( nameof( width ) );
		Width = width;
		Height = height;
		types = new byte[width * height];
		coasterGenerations = new int[width * height];
		Tracks = new TrackCellLayer( width, height );
		flags = new ushort[width * height];
		placements = new short[width * height];
		links = new byte[width * height];
		queueLinks = new byte[width * height];
		owners = new ushort[width * height];
		typeCounts[0] = width * height;
		BlocksX = (width + StampBlock - 1) / StampBlock;
		BlocksY = (height + StampBlock - 1) / StampBlock;
		blockStamps = new int[BlocksX * BlocksY];
	}

	/// <summary>Stamp blocks across and down the map.</summary>
	public int BlocksX { get; }
	public int BlocksY { get; }

	/// <summary>
	/// The stamp counter (tp.exe <c>0x7CDB98</c>): raised by every write that changes a cell type, which stamps the cell's block with it.
	/// A walk keeps the counter of its set-up (<see cref="GuestNavigation.Stamp"/>). The original also raises it at every walk set-up and
	/// route test (<c>0x4D8C50</c>), which changes no comparison of a block stamp with a walk's, so OpenTPW counts the edits only.
	/// </summary>
	public int StampCounter { get; private set; }

	/// <summary>
	/// The stamp of the 16 × 16-cell block (<paramref name="blockX"/>, <paramref name="blockY"/>): the <see cref="StampCounter"/> of the last
	/// write that changed a cell type in it (or cleared one), 0 when none did (or outside the map). A walk set up at a lower counter searches again when it
	/// stands in the block (tp.exe <c>0x4D8CD0</c>, follow_path <c>0x50ED6C</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x004D8C60 block stamp] a cell type change (0x5346D0, 0x5348D0, 0x538FC0; the queue length change 0x4DE1F0 stamps its back cell) writes the counter 0x7CDB98, raised by one (0x4D8C50), into the block (y >> 4) × 33 + (x >> 4); every walk set-up takes the counter raised by one too (0x50F8E0), so a later edit always stamps above the walk; an unchanged non-zero type (0x5346D0: the same-type return at 0x5346F5, only the placement count rises) and a link write (0x522700) stamp nothing, but clearing stamps even an empty cell (0x5346EC skips that return for type 0, the stamp at 0x5347AF)
	public int BlockStamp( int blockX, int blockY ) =>
		(uint)blockX < (uint)BlocksX && (uint)blockY < (uint)BlocksY ? blockStamps[blockY * BlocksX + blockX] : 0;

	/// <summary>Raises the stamp counter and writes it into the block of (<paramref name="x"/>, <paramref name="y"/>) without a cell change (the queue re-evaluation 0x4DE1F0: 0x4D8C50, 0x4D8C60); outside the map nothing is stamped.</summary>
	public void StampBlockAt( int x, int y )
	{
		var stamp = ++StampCounter;
		if ( InBounds( x, y ) )
			blockStamps[y / StampBlock * BlocksX + x / StampBlock] = stamp;
	}

	/// <summary>Restores the stamp counter and the block stamps a snapshot recorded (row by row); null leaves every block unstamped.</summary>
	internal void RestoreBlockStamps( int counter, IReadOnlyList<int>? stamps )
	{
		Array.Clear( blockStamps );
		StampCounter = counter >= 0 ? counter : throw new InvalidDataException( "the stamp counter is negative" );
		if ( stamps == null )
			return;
		if ( stamps.Count != blockStamps.Length )
			throw new InvalidDataException( $"the cell map has {blockStamps.Length} stamp blocks, the snapshot {stamps.Count}" );
		for ( var index = 0; index < blockStamps.Length; index++ )
			blockStamps[index] = stamps[index] >= 0 ? stamps[index] : throw new InvalidDataException( $"stamp block {index} is negative" );
	}

	/// <summary>The block stamps row by row, or null while no block is stamped.</summary>
	internal int[]? CaptureBlockStamps() => Array.Exists( blockStamps, stamp => stamp != 0 ) ? (int[])blockStamps.Clone() : null;

	/// <summary>
	/// The map of an original level (PATH-plan §9.1): Easymode save path cells with their saved links, else MAP
	/// InitialPath cells linked to every cardinal neighbour; MAP InitialPath path cells carry NoModify (§5.4).
	/// </summary>
	// [BIN:STP-PPC:0x10085538 map initialisation] MAP attribute bit 0x08 (InitialPath) makes the cell type 1 with flag 0x20 (NOMODIFY)
	/// <param name="terrain">The level's heightfield: its hole bit types the cells outside the terrain (<see cref="MapStartCell"/>); without it every cell
	/// is inside, as in the original when no terrain is loaded.</param>
	/// <remarks>
	/// The cells without terrain keep the world init's unowned flag 0x40; the cells over the terrain stay owned here, and the level's land
	/// (<see cref="FromOriginal(MapFile, OriginalParkImport?, ParkLandSetup?, ModelHeightfield?)"/>) sets 0x40 on them and claims the fenced land.
	/// </remarks>
	public static ParkCellMap FromOriginal( MapFile map, OriginalParkImport? save, ModelHeightfield? terrain = null )
	{
		var cells = new ParkCellMap( map.CellCountX, map.CellCountY );
		for ( var y = 0; y < map.CellCountY; y++ )
		{
			for ( var x = 0; x < map.CellCountX; x++ )
			{
				var inside = HasTerrain( terrain, x, y );
				var initial = inside && map.GetFlagsAt( x, y ).HasFlag( MapCellFlags.InitialPath );
				var savedFlags = ParkCellFlags.None;
				if ( inside && save != null && save.Cells[x, y].IsPath )
				{
					cells.SetPath( x, y, GuestPathGrid.ToCellLinks( GuestPathGrid.ToLinks( save.Cells[x, y].PathConnections ) ) );
					// [BIN:TPW-EXE:0x0052989A saved NoModify path cells] a path cell the placement laid outside an entrance or exit carries the flag word 0x20 in the save as the initial path cells do (18 of the Jungle Easymode's 78 path cells: ten initial, eight laid outside its rides' entrances and exits)
					if ( !GameEditions.IsThemeParkInc )
						savedFlags = (ParkCellFlags)save.Cells[x, y].MapFlags & ParkCellFlags.NoModify;
				}
				// [BIN:TPW-EXE:0x004D0B30 cell base serializer] the load (mode 0) reads mDirection (+0x0D), mFlags (+0x0E), mNeighbours (+0x0C), mOverlapCounter (+0x20), mParentID (+0x10) and mType (+8) of
				// every cell straight into the map, so a saved queue cell (type 3) comes back with its links, its queue link toward the entrance, its placement counter, its ride's owner word and the NoModify flag the
				// placement gave the cell outside the entrance; the ride's front (0x4DE040) and back walk (0x4DE130) then read these cells (Jungle Easymode: Belly Bounce's four cells)
				else if ( inside && save != null && !GameEditions.IsThemeParkInc && save.Cells[x, y].MapType == (byte)ParkCellType.Queue )
				{
					var saved = save.Cells[x, y];
					cells.TakeSavedQueueCell( x, y, saved );
					savedFlags = (ParkCellFlags)saved.MapFlags & ParkCellFlags.NoModify;
				}
				else if ( initial )
				{
					// [BIN:TPW-EXE:0x00536490 map init] an InitialPath cell (attribute bit 0x08) is typed 1 and then linked by the link writer 0x5348D0 called with mode 1 (0x53653B)
					// [BIN:TPW-EXE:0x004D7F3D map init loop] the cells are initialised in index order, 16384 of them (id = index + 1, x = index & 0x7F, y = index >> 7), each one built on a scratch cell and stored after its writer call (0x4D7F5A, then 0x50C1F0), so the writer sees only the cells before it: a later neighbour is still type 0 and links when its own call runs
					cells.SetType( x, y, ParkCellType.Path );
					PathLinkWriter.LinkPath( cells, x, y );
				}
				var (type, flags) = MapStartCell( map.GetCellAt( x, y ), inside );
				// the init's 0x40 over the terrain is the land's to set (ParkLand.ApplyToNewPark) or an imported save's to restore (ImportLand)
				// [EXT:no-land-maps] a map that gets no land (a level without a fence setup, a Theme Park Inc map, a read-only visit) keeps its cells over the terrain owned: the fence needs the theme's Hoardings.sam and entrance cells, which those maps lack, and a visit is OpenTPW's read-only extension
				if ( inside )
					flags &= ~ParkCellFlags.Unowned;
				if ( type != (byte)ParkCellType.Path && type != 0 )
					cells.SetType( x, y, type );
				flags |= savedFlags;
				if ( flags != ParkCellFlags.None )
					cells.SetFlags( x, y, flags );
			}
		}
		// The level's own cells are written without stamps (map init 0x536490 sets the types directly).
		cells.RestoreBlockStamps( 0, null );
		return cells;
	}

	/// <summary>
	/// The map of an original level with its land: <see cref="FromOriginal(MapFile, OriginalParkImport?, ModelHeightfield?)"/>, then the ownership of
	/// an imported save (its map flags 0x40 and its fence, walked from post A) or, for a new park, the level's own fence
	/// (<paramref name="land"/>) and the land it encloses. Without <paramref name="land"/> every cell over the terrain stays owned.
	/// </summary>
	/// <param name="terrain">The level's heightfield, for the world init's types and flags of the cells without terrain.</param>
	public static ParkCellMap FromOriginal( MapFile map, OriginalParkImport? save, ParkLandSetup? land, ModelHeightfield? terrain = null )
	{
		var cells = FromOriginal( map, save, terrain );
		if ( land != null )
			cells.SetUpLand( land, save );
		return cells;
	}

	/// <summary>
	/// Sets up the land of an original level (see <see cref="FromOriginal(MapFile, OriginalParkImport?, ParkLandSetup?, ModelHeightfield?)"/>):
	/// an imported save keeps its own ownership; its fence is walked from its hoarding cells, and without a walkable fence it has
	/// no land (<see cref="ImportLand"/>). A new park gets the level's fence and its land (<see cref="ParkLand.ApplyToNewPark"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x4d0b30 cell base serializer] a save keeps each map cell's mFlags, so its 0x40 cells are the saved park's unowned land, and each hoarding's mHoardingNeighbours, its fence
	public void SetUpLand( ParkLandSetup setup, OriginalParkImport? save )
	{
		ArgumentNullException.ThrowIfNull( setup );
		if ( save != null && save.Cells.CountX == Width && save.Cells.CountY == Height )
		{
			ImportLand( setup, ( x, y ) => (save.Cells[x, y].MapFlags & (ushort)ParkCellFlags.Unowned) != 0,
				( x, y ) => InBounds( x, y ) && save.Cells[x, y].StructureType == SaveCell.HoardingType ? save.Cells[x, y].HoardingNeighbours : null );
			return;
		}
		ParkLand fence;
		try
		{
			fence = ParkLand.FromClicks( setup );
		}
		catch ( InvalidDataException exception )
		{
			Log.Warning( $"The level's park fence cannot be laid: {exception.Message} Every cell stays owned." );
			return;
		}
		fence.ApplyToNewPark( this );
		SetLand( fence );
	}

	/// <summary>
	/// The land of an imported save: every cell takes the saved unowned flag (<paramref name="unowned"/>) and the fence is
	/// walked from post A by the saved hoardings' links (<paramref name="links"/>). When that walk fails the map has no land
	/// (Buy Land is unavailable) and keeps the saved flags.
	/// </summary>
	internal void ImportLand( ParkLandSetup setup, Func<int, int, bool> unowned, Func<int, int, byte?> links )
	{
		ArgumentNullException.ThrowIfNull( setup );
		var imported = ParkLand.FromFenceLinks( setup.PostA, setup.PostB, links );
		for ( var y = 0; y < Height; y++ )
		{
			for ( var x = 0; x < Width; x++ )
			{
				var saved = unowned( x, y );
				var flags = FlagsAt( x, y );
				if ( saved != flags.HasFlag( ParkCellFlags.Unowned ) )
					SetFlags( x, y, saved ? flags | ParkCellFlags.Unowned : flags & ~ParkCellFlags.Unowned );
			}
		}
		// The level's starting fence does not stand in: it would not enclose the saved land, so Buy Land would merge new
		// rectangles into a ring that disagrees with the saved flags.
		if ( imported == null )
			Log.Warning( "The imported park's fence does not run from post A to post B; Buy Land is unavailable for this park." );
		SetLand( imported );
	}


	/// <summary>
	/// The type and flags the world init gives a cell (tp.exe <c>0x00536490</c>) from its MAP attribute byte and whether the terrain is under it.
	/// Outside the terrain: water (MAP 0x02), the fixed walkway (MAP 0x80) or no terrain. Inside: a path with NoModify (MAP 0x08, InitialPath)
	/// or an empty cell. Every cell without NoModify gets the unowned flag 0x40; MAP 0x10 adds flag 0x80 and MAP 0x04 flag 0x400.
	/// </summary>
	// [BIN:TPW-EXE:0x00536490 cell init] the MAP byte is cell +0x26; when 0x00450420 answers 0 (no terrain) the type is 2 (MAP & 2), else 0x1E (MAP & 0x80), else 7; otherwise the type is 1 with links (0x005348D0) and flags 0x20 for MAP & 8, else 0, the types 5, 2 and 0x1E written first being overwritten; both branches then set flag 0x40 unless the flags hold 0x20 (0x0053654B, 0x005365AD); MAP & 0x10 sets flag 0x80 and MAP & 4 flag 0x400; Easymode's saved map types agree on all 16,384 cells but those of objects
	public static (byte Type, ParkCellFlags Flags) MapStartCell( byte mapByte, bool hasTerrain )
	{
		byte type;
		var flags = ParkCellFlags.None;
		if ( !hasTerrain )
		{
			type = (mapByte & 0x02) != 0 ? ParkCellTypes.Water : (mapByte & 0x80) != 0 ? ParkCellTypes.FixedWalkway : ParkCellTypes.NoTerrain;
			flags = ParkCellFlags.Unowned;
		}
		else if ( (mapByte & (byte)MapCellFlags.InitialPath) != 0 )
		{
			type = (byte)ParkCellType.Path;
			flags = ParkCellFlags.NoModify;
		}
		else
		{
			type = 0;
			flags = ParkCellFlags.Unowned;
		}
		if ( (mapByte & 0x10) != 0 )
			flags |= ParkCellFlags.EntranceArea;
		if ( (mapByte & 0x04) != 0 )
			flags |= ParkCellFlags.MapBit04;
		return (type, flags);
	}

	/// <summary>Whether the terrain lies under a cell (tp.exe <c>0x00450420</c>): no terrain at all answers yes, a cell beyond the heightfield or a hole answers no.</summary>
	// [BIN:TPW-EXE:0x00450420 terrain test] without the terrain model (0x7A0854, +0x6C) it returns 1; in bounds (heightfield +0x18 × +0x1C) it returns the inverted bit 0 of the cell word (+0x2C, 4 bytes per cell); beyond them 0
	public static bool HasTerrain( ModelHeightfield? terrain, int x, int y ) =>
		terrain == null || (uint)x < (uint)terrain.CellCountX && (uint)y < (uint)terrain.CellCountZ && (terrain.GetCellWord( x, y ) & 1) == 0;

	/// <summary>
	/// The cells a placed object writes over its footprint (tp.exe's placement commit <c>0x00528A70</c>): each shape cell's type from the symbol table, with the track and overlay
	/// types 11, 16 and 23 stored as 4, its owner word (<paramref name="owner"/>, <see cref="CellWord"/> of the object's anchor; 0 writes none), and by shape type the flags:
	/// '@' (1) a path cell with the NoModify flag linked to its path neighbours, '+' (11) and '#' (16) the flag word 0x10 (<see cref="ParkCellFlags.AddOn"/>) on the cell and on
	/// the track layer's cell beneath, and an entrance or exit cell's queue link its rotated direction byte (<paramref name="rotation"/>). Only empty cells are typed, after a path cell without
	/// NoModify has been cleared (an imported park's saved types stay).
	/// </summary>
	// [BIN:TPW-EXE:0x00528A70 placement commit] for every shape cell of a nonzero type (the table 0x818C18) it clears the cell (0x005367A0(0, 0)) and calls 0x00532FC0 with the type, 0xB, 0x10 and 0x17 replaced by 4 (0x5290C7..0x5290EE); a plain type is SetCellType 0x005346D0, which writes cell +8; Easymode's object cells are saved as 4, 9 and 10
	// [BIN:TPW-EXE:0x00529138 footprint owner] the commit then stores the new object's cell word as the cell's owner (0x539210, +0x10) and clears the track layer's bridge bits (0x53A530(0xF)); Easymode's saved cells carry y × 128 + x + 1 of the object's anchor on every cell of its footprint (all 67 object cells of the Jungle park)
	// [BIN:TPW-EXE:0x00529173 '@' footprint cell] on commit a type-1 shape cell gets the flag word 0x20 (0x53A510, 0x529182), the path link writer in mode 1 (0x5348D0, 0x529198) and, for each cardinal link it then has to a path cell, the same writer on that neighbour (0x5291C5..0x5291DD, the loop over 1, 4, 16, 64 by 0x4D8C20)
	// [BIN:TPW-EXE:0x0052950D '+' and '#' footprint cell] on commit the flag word of the cell and of the second-layer cell at the same coordinates is set to 0x10 (0x53A510(0x10) twice, 0x52950D..0x52951C); the 'Q' cell (3) takes the generic stamp only
	// [BIN:TPW-EXE:0x005293B3 entrance and exit cells] on commit an entrance or exit cell's queue link +0xD is the symbol's direction byte turned by the build rotation (0x4D8C20, 0x5227E0: 0x5293B3..0x5293C3, 0x5292A7..0x5292B2); Easymode's saved entrance and exit cells carry these bytes (the path cell in front of them is linked by ObjectAccessCells.Lay)
	public void WriteFootprint( IEnumerable<(int X, int Y, ObjectShapeCell Cell)> footprint, ushort owner = 0, int rotation = 0, LinkEffects? effects = null )
	{
		foreach ( var (x, y, cell) in footprint )
		{
			var type = FootprintType( cell.CellType );
			if ( type == 0 || !InBounds( x, y ) )
				continue;
			// A path cell that is not NoModify gives way: the commit clears the cell (0x5367A0(0, 0)) before it writes the type.
			if ( RawTypeAt( x, y ) == (byte)ParkCellType.Path && !FlagsAt( x, y ).HasFlag( ParkCellFlags.NoModify ) )
			{
				PathLinkWriter.Unlink( this, x, y, cardinalOnly: false, effects );
				SetType( x, y, ParkCellType.Empty );
			}
			var fresh = RawTypeAt( x, y ) == 0;
			if ( fresh )
				SetType( x, y, type );
			// An add-on stands on its ride's track: OpenTPW's map keeps the track's own type on such a cell (the original's first layer holds the object type 4 above the track on the second layer).
			var onTrack = cell.CellType is ObjectShapeSymbols.TypeUpgrade or ObjectShapeSymbols.TypeHash && (TrackCellTypes.IsWalkable( RawTypeAt( x, y ) ) || RawTypeAt( x, y ) == TrackCellTypes.Extension);
			if ( RawTypeAt( x, y ) != type && !onTrack )
				continue;
			if ( owner != 0 && !onTrack && OwnerAt( x, y ) != owner )
				SetOwner( x, y, owner );
			switch ( cell.CellType )
			{
				case ObjectShapeSymbols.TypeAt when FlagsAt( x, y ) != ParkCellFlags.NoModify:
					SetFlags( x, y, ParkCellFlags.NoModify );
					PathLinkWriter.LinkPath( this, x, y );
					for ( var direction = 0; direction < 4; direction++ )
					{
						var (nx, ny) = (x + GuestPathGrid.Directions[direction].DX, y + GuestPathGrid.Directions[direction].DY);
						if ( (LinksAt( x, y ) & GuestPathGrid.LinkValue( direction )) != 0 && InBounds( nx, ny ) && RawTypeAt( nx, ny ) == (byte)ParkCellType.Path )
							PathLinkWriter.LinkPath( this, nx, ny );
					}
					break;
				case ObjectShapeSymbols.TypeUpgrade or ObjectShapeSymbols.TypeHash when FlagsAt( x, y ) != ParkCellFlags.AddOn:
					SetFlags( x, y, ParkCellFlags.AddOn );
					if ( Tracks.Get( x, y ) is { Type: not 0 } track && track.Flags != TrackCell.FlagAddOn )
						Tracks.Set( x, y, track with { Flags = TrackCell.FlagAddOn } );
					break;
				case ObjectShapeSymbols.TypeEntrance or ObjectShapeSymbols.TypeExit when fresh && cell.Direction != 0:
					// The cell's queue link is the symbol's direction byte turned by the build rotation.
					SetQueueLink( x, y, ObjectFootprint.RotateDirectionByte( cell.Direction, rotation ) );
					break;
			}
		}
	}

	/// <summary>
	/// Makes (x, y) the queue cell a saved type-3 record describes: its type, link byte <c>+0x0C</c>, queue link <c>+0x0D</c>, owner word <c>+0x10</c> and placement counter <c>+0x20</c>
	/// (the fields tp.exe's cell load 0x4D0B30 reads). The caller sets the record's NoModify flag with the cell's other flags.
	/// </summary>
	public void TakeSavedQueueCell( int x, int y, SaveCell saved )
	{
		SetType( x, y, ParkCellType.Queue );
		SetLinks( x, y, (byte)saved.PathConnections );
		SetQueueLink( x, y, saved.Direction );
		SetOwner( x, y, saved.MapParentId );
		SetPlacementCount( x, y, saved.OverlapCounter );
	}

	/// <summary>
	/// Gives the entrance and exit cells of an imported object the link byte its saved cell record holds (the cells the load reads whole, where the placement's own writers lay the byte).
	/// A cell is taken only when the record's type and owner word are the object's own.
	/// </summary>
	// [BIN:TPW-EXE:0x004D0B30 cell base serializer] the load reads each cell's record, link byte (+0x0C, record byte 8) included, so an entrance or exit cell of an imported park carries the bit toward the path outside it, which the placement lays through the outside cell's link writer in mode 1 (0x5348D0 reaching the type-9 cell along its queue link, 0x5292A7..0x5293FE and 0x5298D5..0x5299AC); Easymode's nine entrance cells carry one bit each and its exit cell one, which the edge test 0x4D8750 reads to let a walk step into the stand point
	public void TakeSavedAccessLinks( IEnumerable<(int X, int Y, ObjectShapeCell Cell)> footprint, ushort owner, SaveCellGrid saved )
	{
		ArgumentNullException.ThrowIfNull( footprint );
		ArgumentNullException.ThrowIfNull( saved );
		foreach ( var (x, y, cell) in footprint )
		{
			if ( cell.CellType is not (ObjectShapeSymbols.TypeEntrance or ObjectShapeSymbols.TypeExit) || !InBounds( x, y ) || x >= saved.CountX || y >= saved.CountY )
				continue;
			var record = saved[x, y];
			var type = RawTypeAt( x, y );
			if ( type is not (ParkCellTypes.Entrance or ParkCellTypes.Exit) || record.MapType != type || record.MapParentId != owner )
				continue;
			SetLinks( x, y, (byte)(LinksAt( x, y ) | (byte)record.PathConnections) );
		}
	}

	/// <summary>
	/// Empties the footprint cells a removed object typed (those still of its footprint type), unlinking an '@' path cell and taking the add-on flag off a '+' or '#' cell and its track
	/// layer cell.
	/// </summary>
	// [APPROX:ECON-050] the guest grid takes the world init's types and flags (tp.exe 0x00536490 with the terrain test 0x00450420: 7, 2 or 30 where no terrain lies, 1 with flag 0x20 for an InitialPath cell and 0 inside, flag 0x40 on every cell without 0x20, which the level's land then clears inside the fence) and the footprint types a placement writes (0x00528A70: 4, 9 and 10, the owner word, the '@' and add-on cells, the cells outside the entrance and exit), but a removed object empties the cells it typed and its queue (the level clears the ride's queue cells, ObjectAccessCells.ClearQueue), where the original's clear of them is not found (its destructor writes no cell word; the demolition replays the queue tool from the entrance in the Stranger-than-paradise twin, 0x100748A0) and the cells its demolition writes are not traced, and a read-only visit or a Theme Park Inc layout keeps the cells it was given — evidence needed: the cell writes of the sale's clear: tp.exe's destructor 0x004DD0A0 writes no cell word (it credits the scrap value, takes the RegionFX row off, kills the mesh 0x00559060 and posts message type 10, read only by the staff and guest handlers 0x004DA0CC, 0x004D6CFC and 0x004FB36D); the clear is the demolition 0x00527EE0, a caller of 0x0050B780, which credits the scrap percentage 0x004E2290 (0x004D01F0), replays the structure's removal through the loops of 0x0052FE50, then walks the footprint mask over the cells and for each cell inside the map clears the flag 0x20 (0x0053A530), runs the cell remover 0x005367A0 (a switch on the cell type at +8: types 1 and 10 step the path count down, 'Removing path cell with no neighbours') and 0x0053B280 (or clears the flag 0x10 for a moved object), before it lets the thing go; the per-type writes of those helpers are not traced
	public void ClearFootprint( IEnumerable<(int X, int Y, ObjectShapeCell Cell)> footprint )
	{
		foreach ( var (x, y, cell) in footprint )
		{
			var type = FootprintType( cell.CellType );
			if ( type == 0 || !InBounds( x, y ) )
				continue;
			var addOn = cell.CellType is ObjectShapeSymbols.TypeUpgrade or ObjectShapeSymbols.TypeHash;
			var onTrack = addOn && (TrackCellTypes.IsWalkable( RawTypeAt( x, y ) ) || RawTypeAt( x, y ) == TrackCellTypes.Extension);
			if ( RawTypeAt( x, y ) != type && !onTrack )
				continue;
			if ( cell.CellType == ObjectShapeSymbols.TypeAt )
			{
				PathLinkWriter.Unlink( this, x, y, cardinalOnly: false );
				SetFlags( x, y, FlagsAt( x, y ) & ~ParkCellFlags.NoModify );
			}
			else if ( addOn )
			{
				SetFlags( x, y, FlagsAt( x, y ) & ~ParkCellFlags.AddOn );
				if ( Tracks.Get( x, y ) is { Type: not 0 } track && (track.Flags & TrackCell.FlagAddOn) != 0 )
					Tracks.Set( x, y, track with { Flags = (byte)(track.Flags & ~TrackCell.FlagAddOn) } );
			}
			if ( !onTrack )
				SetType( x, y, ParkCellType.Empty );
		}
	}

	/// <summary>The map type a placement stores for a shape cell type: 11, 16 and 23 become 4.</summary>
	public static byte FootprintType( int shapeType ) => shapeType is ObjectShapeSymbols.TypeUpgrade or ObjectShapeSymbols.TypeHash or ObjectShapeSymbols.TypeTrackEnd
		? ParkCellTypes.Object
		: (byte)Math.Clamp( shapeType, 0, 255 );

	private void SetPath( int x, int y, byte cellLinks )
	{
		SetType( x, y, ParkCellType.Path );
		SetLinks( x, y, cellLinks );
	}

	public int Width { get; }
	public int Height { get; }
	/// <summary>The track layer (kart and water track tiles, docs/TRACKS.md); empty until a track is laid.</summary>
	public TrackCellLayer Tracks { get; }
	/// <summary>The park's fence and enclosed land (Buy Land); null for a map without one, where no cell is made unowned.</summary>
	public ParkLand? Land { get; private set; }

	/// <summary>Replaces the land model (a purchase, a load); the cells' ownership flags are written separately.</summary>
	public void SetLand( ParkLand? land )
	{
		Land = land;
		Version++;
	}
	/// <summary>Incremented by every write.</summary>
	public int Version { get; private set; }

	/// <summary>Number of cells whose type byte is <paramref name="type"/>.</summary>
	public int CountOf( ParkCellType type ) => typeCounts[(byte)type];

	public bool InBounds( int x, int y ) => (uint)x < (uint)Width && (uint)y < (uint)Height;
	private int Index( int x, int y ) => InBounds( x, y ) ? y * Width + x : throw new ArgumentOutOfRangeException( nameof( x ) );

	/// <summary>The original type byte (map cell <c>+8</c>); 0 outside the map.</summary>
	public byte RawTypeAt( int x, int y ) => InBounds( x, y ) ? types[y * Width + x] : (byte)0;
	public ParkCellType TypeAt( int x, int y ) => (ParkCellType)RawTypeAt( x, y );
	public ParkCellFlags FlagsAt( int x, int y ) => InBounds( x, y ) ? (ParkCellFlags)flags[y * Width + x] : ParkCellFlags.None;
	/// <summary>Extra placements of the same type (map cell <c>+32</c>).</summary>
	public short PlacementCountAt( int x, int y ) => InBounds( x, y ) ? placements[y * Width + x] : (short)0;
	/// <summary>Cardinal link bits 1 (−Y), 4 (+X), 16 (+Y), 64 (−X) (map cell <c>+12</c>).</summary>
	public byte LinksAt( int x, int y ) => InBounds( x, y ) ? links[y * Width + x] : (byte)0;
	/// <summary>The queue link (map cell <c>+13</c>): one of 1, 4, 16, 64 toward the queue's predecessor, or 0.</summary>
	public byte QueueLinkAt( int x, int y ) => InBounds( x, y ) ? queueLinks[y * Width + x] : (byte)0;

	/// <summary>
	/// The cell's owner word (map cell <c>+0x10</c>, the save's <c>mParentID</c>): the original cell word <see cref="CellWord"/> of the object's anchor cell for the cells of an object's
	/// footprint (types 4, 9 and 10, and the path and queue cells a footprint lays), of the ride for its queue cells; 0 when nothing owns the cell. Readers test the cell's type first.
	/// </summary>
	// [BIN:TPW-EXE:0x00528F26 owner word] the placement stores the word of the new object's cell (its x byte +5 and y byte +7, y × 128 + x + 1) through 0x539210 (+0x10) on every footprint cell it types (0x529138), the entrance's outside queue cell and its entrance cell's owner (0x52983C); the pass 0x83 stores the tool's ride, 0x818698, on a line's queue cell (0x5340E9); Easymode's saved map cells carry exactly these words (mParentID, record +11) on every object, entrance, exit and queue cell
	public ushort OwnerAt( int x, int y ) => InBounds( x, y ) ? owners[y * Width + x] : (ushort)0;

	/// <summary>The original cell word of (x, y): <c>y × 128 + x + 1</c>, what <c>+0x10</c> holds for an owner and what the cell lookups (0x4D0AF0, 0x532FC0) decode as <c>cell − 1</c>, <c>&amp; 0x7F</c> and <c>&gt;&gt; 7</c>.</summary>
	public static ushort CellWord( int x, int y ) => unchecked((ushort)(y * 128 + x + 1));

	/// <summary>
	/// [EXT:pools-render] How many of the writes counted in <see cref="Version"/> were type writes that touched a pool water cell (painting or erasing one). The drawing of paths and queues
	/// depends on <c>Version - PoolWrites</c> alone, so a stroke of pool cells does not rebuild them. Not saved and not hashed.
	/// </summary>
	internal int PoolWrites { get; private set; }

	public void SetType( int x, int y, byte type )
	{
		var index = Index( x, y );
		coasterGenerations[index]++;
		if ( types[index] == ParkCellTypes.PoolWater || type == ParkCellTypes.PoolWater )
			PoolWrites++;
		// tp.exe's cell setter 0x5346D0 returns early for the same non-zero type only: clearing always stamps.
		var changed = types[index] != type || type == 0;
		typeCounts[types[index]]--;
		typeCounts[type]++;
		types[index] = type;
		queuePassCells.Remove( index );
		if ( !ParkCellTypes.CarriesQueueLink( type ) )
			queueLinks[index] = 0;
		if ( type == 0 )
		{
			links[index] = 0;
			placements[index] = 0;
			owners[index] = 0;
		}
		Version++;
		if ( changed )
			blockStamps[y / StampBlock * BlocksX + x / StampBlock] = ++StampCounter;
	}

	public void SetType( int x, int y, ParkCellType type ) => SetType( x, y, (byte)type );

	public void SetLinks( int x, int y, byte value )
	{
		links[Index( x, y )] = value;
		NormalizeQueuePassCell( Index( x, y ) );
		Version++;
	}

	public void SetQueueLink( int x, int y, byte value )
	{
		queueLinks[Index( x, y )] = value;
		NormalizeQueuePassCell( Index( x, y ) );
		Version++;
	}

	public void SetFlags( int x, int y, ParkCellFlags value )
	{
		var index = Index( x, y );
		// [EXT:pools-river] a change of a pool cell's flags (river, waves) counts as a pool write, so the water's regions and mesh are made again
		if ( types[index] == ParkCellTypes.PoolWater && flags[index] != (ushort)value )
			PoolWrites++;
		flags[index] = (ushort)value;
		Version++;
	}

	public void SetPlacementCount( int x, int y, short value )
	{
		placements[Index( x, y )] = value;
		Version++;
	}

	/// <summary>Sets the owner word (<see cref="OwnerAt"/>). Not part of the state digest: it is derived from the placed objects and the queues' rides.</summary>
	public void SetOwner( int x, int y, ushort value )
	{
		owners[Index( x, y )] = value;
		NormalizeQueuePassCell( Index( x, y ) );
		Version++;
	}

	/// <summary>Marks the map changed without a cell write (cached walk fields are rebuilt).</summary>
	public void Touch() => Version++;

	/// <summary>
	/// Sets the change counter a <see cref="ParkLayoutSnapshot"/> recorded: restoring writes every cell with the
	/// normal setters (each bumping <see cref="Version"/>), then aligns the counter with the saved park so cached
	/// state derived from it (the guests' walk graph) keeps the saved epoch.
	/// </summary>
	internal void RestoreVersion( int version ) => Version = version >= 0 ? version : throw new ArgumentOutOfRangeException( nameof( version ) );

	/// <summary>
	/// Cell-map part of <see cref="WorldStateHash"/>: every non-empty cell in row order with its type, flags,
	/// placement counter, links and queue link (the cells guests walk and queue on, and the build tools read).
	/// </summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( Width );
		hash.Add( Height );
		var count = 0;
		for ( var index = 0; index < types.Length; index++ )
			count += types[index] != 0 || flags[index] != 0 || placements[index] != 0 || links[index] != 0 || queueLinks[index] != 0 ? 1 : 0;
		hash.Add( count );
		for ( var index = 0; index < types.Length; index++ )
		{
			if ( types[index] == 0 && flags[index] == 0 && placements[index] == 0 && links[index] == 0 && queueLinks[index] == 0 )
				continue;
			hash.Add( index );
			hash.Add( types[index] | flags[index] << 8 | (long)links[index] << 24 | (long)queueLinks[index] << 32 | (long)(ushort)placements[index] << 40 );
		}
		AddQueuePassCanonicalState( hash );
		// The track layer joins the digest only while a track exists, so every trackless park keeps its earlier pins.
		if ( Tracks.Count > 0 )
			Tracks.AddCanonicalState( hash );
		// The block stamps (WALK-007) join it once a block is stamped.
		if ( Array.Exists( blockStamps, stamp => stamp != 0 ) )
		{
			hash.Add( StampCounter );
			hash.Add( blockStamps.Length );
			foreach ( var stamp in blockStamps )
				hash.Add( stamp );
		}
		// The land joins it only on a map that has one (an original level), for the same reason.
		Land?.AddCanonicalState( hash );
	}

	/// <summary>
	/// <c>CanChangeCellType</c> (PATH-plan §3.1), in the original order: clearing; not 4 over 4; 21 over 21;
	/// path over queue; same type or an empty cell; 4 over path; queue over path except on a line's last cell.
	/// </summary>
	// [BIN:STP-PPC:0x1008408C CanChangeCellType] the seven rules of PATH-plan §3.1; everything else is refused. Rule 7 allows queue over path while data:0x84b2c is 0; the line tool 0x10084EA4 sets that word to 1 only for a line's last cell
	public static bool CanChangeCellType( byte old, byte type, bool lastCell )
	{
		if ( type == 0 )
			return true;
		// [EXT:pools-cell-rules] pool water goes on empty cells only (or stays); no original rule names it, and every other type over a pool cell falls through to the refusal below
		if ( type == ParkCellTypes.PoolWater )
			return old is 0 or ParkCellTypes.PoolWater;
		if ( type == 4 && old == 4 )
			return false;
		if ( type == 21 && old == 21 )
			return true;
		if ( type == 1 && old == 3 )
			return true;
		if ( type == old || old == 0 )
			return true;
		if ( type == 4 && old == 1 )
			return true;
		return type == 3 && old == 1 && !lastCell;
	}
}
