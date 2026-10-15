namespace OpenTPW;

/// <summary>
/// Original map cell types that the build tools write (docs/reverse/PATH-plan.md §2). Other original
/// types (objects, tracks, terrain) are kept as raw bytes and named only when a tool needs them.
/// </summary>
public enum ParkCellType : byte
{
	Empty = 0,
	Path = 1,
	Queue = 3
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
}

/// <summary>Map cell flag bits the build tools read (PATH-plan §5.2, §5.4).</summary>
[Flags]
public enum ParkCellFlags : ushort
{
	None = 0,
	NoModify = 0x20,
	Unowned = 0x40,
	/// <summary>The world init's copy of MAP bit 0x10 (the entrance complex).</summary>
	EntranceArea = 0x80,
	/// <summary>The world init's copy of MAP bit 0x04 (high byte bit 0x04); its readers are not traced.</summary>
	MapBit04 = 0x400
}

/// <summary>
/// The CPU-only park cell map shared by the path and queue tools (PATH-plan §9.1/§9.6): per cell the
/// original type byte, flags, the same-type placement counter, the cardinal links (1, 4, 16, 64) and the
/// queue link. It is the source of truth for path and queue cells: <see cref="GuestPathGrid"/> is a view
/// that derives the guests' walk graph from it (path cells are walkable, their links are the connections).
/// </summary>
public sealed class ParkCellMap
{
	private readonly byte[] types;
	private readonly ushort[] flags;
	private readonly short[] placements;
	private readonly byte[] links;
	private readonly byte[] queueLinks;
	private readonly int[] typeCounts = new int[256];

	public ParkCellMap( int width, int height )
	{
		if ( width <= 0 || height <= 0 || width > 1024 || height > 1024 )
			throw new ArgumentOutOfRangeException( nameof( width ) );
		Width = width;
		Height = height;
		types = new byte[width * height];
		Tracks = new TrackCellLayer( width, height );
		flags = new ushort[width * height];
		placements = new short[width * height];
		links = new byte[width * height];
		queueLinks = new byte[width * height];
		typeCounts[0] = width * height;
	}

	/// <summary>
	/// The map of an original level (PATH-plan §9.1): Easymode save path cells with their saved links, else MAP
	/// InitialPath cells linked to every cardinal neighbour; MAP InitialPath path cells carry NoModify (§5.4).
	/// </summary>
	// [BIN:STP-PPC:0x10085538 map initialisation] MAP attribute bit 0x08 (InitialPath) makes the cell type 1 with flag 0x20 (NOMODIFY)
	// [APPROX:PATH-007] PATH-LAND: no cell over the terrain gets the unowned flag 0x40 (every in-bounds cell with terrain under it counts as owned; the cells without terrain get it at level load, as in the original); the original sets 0x40 on every non-InitialPath cell at level load (tp.exe 0x536490 from 0x52f050), clears it inside the park fence that 0x5323f0 lays from the level's outline table 0x784e3c anchored on the gate (fill 0x531040/0x5311c0 plus a strip at the gate), refuses 0x40 cells for paths (0x535670) and saves the flags as mFlags (0x4d0b30); OpenTPW decodes neither the outline nor the saved flags, and Buy Land (tool 0x39, 0x5327e0) is not modelled — still open: the level outline table 0x784e3c (Scape data) and the saved mFlags are not imported
	/// <param name="terrain">The level's heightfield: its hole bit types the cells outside the terrain (<see cref="MapStartCell"/>); without it every cell
	/// is inside, as in the original when no terrain is loaded.</param>
	public static ParkCellMap FromOriginal( MapFile map, OriginalParkImport? save, ModelHeightfield? terrain = null )
	{
		var cells = new ParkCellMap( map.CellCountX, map.CellCountY );
		for ( var y = 0; y < map.CellCountY; y++ )
		{
			for ( var x = 0; x < map.CellCountX; x++ )
			{
				var inside = HasTerrain( terrain, x, y );
				var initial = inside && map.GetFlagsAt( x, y ).HasFlag( MapCellFlags.InitialPath );
				if ( inside && save != null && save.Cells[x, y].IsPath )
					cells.SetPath( x, y, GuestPathGrid.ToCellLinks( GuestPathGrid.ToLinks( save.Cells[x, y].PathConnections ) ) );
				else if ( initial )
					cells.SetPath( x, y, GuestPathGrid.ToCellLinks( 0x0F ) );
				var (type, flags) = MapStartCell( map.GetCellAt( x, y ), inside );
				if ( type != (byte)ParkCellType.Path && type != 0 )
					cells.SetType( x, y, type );
				if ( flags != ParkCellFlags.None )
					cells.SetFlags( x, y, flags );
			}
		}
		return cells;
	}

	/// <summary>
	/// The type and flags the world init gives a cell (tp.exe <c>0x00536490</c>) from its MAP attribute byte and whether the terrain is under it.
	/// Outside the terrain: water (MAP 0x02), the fixed walkway (MAP 0x80) or no terrain, with the unowned flag 0x40. Inside: a path with
	/// NoModify (MAP 0x08, InitialPath) or an empty cell. MAP 0x10 adds flag 0x80 and MAP 0x04 flag 0x400.
	/// </summary>
	// [BIN:TPW-EXE:0x00536490 cell init] the MAP byte is cell +0x26; when 0x00450420 answers 0 (no terrain) the type is 2 (MAP & 2), else 0x1E (MAP & 0x80), else 7, and flag 0x40 is set; otherwise the type is 1 with links (0x005348D0) and flags 0x20 for MAP & 8, else 0, the types 5, 2 and 0x1E written first being overwritten; MAP & 0x10 sets flag 0x80 and MAP & 4 flag 0x400; Easymode's saved map types agree on all 16,384 cells but those of objects
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
			type = 0;
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
	/// The cell types a placed object writes over its footprint (tp.exe's placement commit <c>0x00528A70</c>): each shape cell's type from the
	/// symbol table, with the track and overlay types 11, 16 and 23 stored as 4. Only empty cells are typed (OpenTPW keeps objects off path and
	/// queue cells; an imported park's saved types stay).
	/// </summary>
	// [BIN:TPW-EXE:0x00528A70 placement commit] for every shape cell of a nonzero type (the table 0x818C18) it clears the cell (0x005367A0(0, 0)) and calls 0x00532FC0 with the type, 0xB, 0x10 and 0x17 replaced by 4; a plain type is SetCellType 0x005346D0, which writes cell +8; Easymode's object cells are saved as 4, 9 and 10
	public void WriteFootprint( IEnumerable<(int X, int Y, ObjectShapeCell Cell)> footprint )
	{
		foreach ( var (x, y, cell) in footprint )
		{
			var type = FootprintType( cell.CellType );
			if ( type != 0 && InBounds( x, y ) && RawTypeAt( x, y ) == 0 )
				SetType( x, y, type );
		}
	}

	/// <summary>Empties the footprint cells a removed object typed (those still of its footprint type).</summary>
	// [APPROX:ECON-050] the guest grid takes the world init's types (tp.exe 0x00536490 with the terrain test 0x00450420: 7, 2 or 30 with flag 0x40 where no terrain lies, 1 for an InitialPath cell and 0 inside) and the footprint types a placement writes (0x00528A70: 4, 9 and 10), but a removed object empties the cells it typed, where the original's removal path to its cells is not traced, the placement's other cell writes (the clear before the write, the link to the path in front of an entrance, the '@' path links) are not modelled, and a read-only visit or a Theme Park Inc layout keeps the cells it was given — evidence needed: the cell writes of tp.exe's object removal (0x004DD0A0 and the kill 0x00559060)
	public void ClearFootprint( IEnumerable<(int X, int Y, ObjectShapeCell Cell)> footprint )
	{
		foreach ( var (x, y, cell) in footprint )
		{
			var type = FootprintType( cell.CellType );
			if ( type != 0 && InBounds( x, y ) && RawTypeAt( x, y ) == type )
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

	public void SetType( int x, int y, byte type )
	{
		var index = Index( x, y );
		typeCounts[types[index]]--;
		typeCounts[type]++;
		types[index] = type;
		if ( type != (byte)ParkCellType.Queue )
			queueLinks[index] = 0;
		if ( type == 0 )
		{
			links[index] = 0;
			placements[index] = 0;
		}
		Version++;
	}

	public void SetType( int x, int y, ParkCellType type ) => SetType( x, y, (byte)type );

	public void SetLinks( int x, int y, byte value )
	{
		links[Index( x, y )] = value;
		Version++;
	}

	public void SetQueueLink( int x, int y, byte value )
	{
		queueLinks[Index( x, y )] = value;
		Version++;
	}

	public void SetFlags( int x, int y, ParkCellFlags value )
	{
		flags[Index( x, y )] = (ushort)value;
		Version++;
	}

	public void SetPlacementCount( int x, int y, short value )
	{
		placements[Index( x, y )] = value;
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
		// The track layer joins the digest only while a track exists, so every trackless park keeps its earlier pins.
		if ( Tracks.Count > 0 )
			Tracks.AddCanonicalState( hash );
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
