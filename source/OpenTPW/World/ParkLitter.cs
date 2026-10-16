namespace OpenTPW;

/// <summary>The litter a cell can hold (tp.exe cell record <c>+0x2c</c>, <c>mLitter</c>); 0 is none.</summary>
public enum LitterType : byte
{
	None = 0,
	// 1..5: ordinary litter, dropped by a guest whose litter meter is full (1 + rand % 5) or by a littering prankster
	/// <summary>The fallback of the sprite switch ("Dodgy litter type"); nothing drops it.</summary>
	Dodgy = 6,
	Vomit = 7,
	Stinkbomb = 8
}

/// <summary>
/// Litter by map cell (docs/reverse/WANDER-plan.md §6): each cell holds at most one item, dropped by guests and removed only by a
/// handyman's sweep. The fields are the original's cell record words <c>+0x2c</c> (type), <c>+0x30</c> (the handyman who claimed
/// it, <c>mLitterCollector</c>) and <c>+0x34</c> (the claim's turn, <c>mTimeMarkedForLitterCollection</c>). The economy owns it and its
/// save; the level attaches its cell map, which gives the path and queue cells the litter share is measured against.
/// </summary>
public sealed class ParkLitter
{
	/// <summary>Ordinary litter kinds: a drop takes <c>1 + rand % OrdinaryKinds</c> (tp.exe <c>0x0070048C</c> holds 6, less one).</summary>
	public const int OrdinaryKinds = 5;
	/// <summary>The litter meter byte at which a guest in state 6 gets rid of its litter.</summary>
	public const int MeterThreshold = 90;

	private readonly byte[] types;
	private readonly int[] collectors;
	private readonly long[] claimTurns;

	/// <summary>The original's map size: cell ids are <c>y × 128 + x + 1</c>.</summary>
	public const int MapSize = 128;

	public ParkLitter( int width = MapSize, int height = MapSize )
	{
		if ( width <= 0 || height <= 0 || width > 1024 || height > 1024 )
			throw new ArgumentOutOfRangeException( nameof( width ) );
		Width = width;
		Height = height;
		types = new byte[width * height];
		collectors = new int[types.Length];
		claimTurns = new long[types.Length];
	}

	public int Width { get; }
	public int Height { get; }
	/// <summary>The environment grid that vomit and stinkbombs spread their <c>RegionFX</c> on while they lie.</summary>
	public ParkEnvironment Environment { get; } = new();
	/// <summary>The level's cell map, for the litter share (null until a level attaches it: the share is then 0).</summary>
	public ParkCellMap? Cells
	{
		get => cells;
		set
		{
			// another map's blocks hold other cells: the analyser's counts start again (attaching the first map, as after a load, keeps the restored counts)
			if ( cells != null && !ReferenceEquals( cells, value ) )
				ResetBlockCounts();
			cells = value;
		}
	}
	private ParkCellMap? cells;
	/// <summary>Cells holding litter.</summary>
	public int Count { get; private set; }
	/// <summary>Changes with every drop, removal or claim (for presentation caches).</summary>
	public int Version { get; private set; }
	/// <summary>True in an online visit: the original's game type 1 drops no litter (tp.exe <c>0x004D93B0</c> returns at once).</summary>
	public bool Disabled { get; set; }

	/// <summary>Raised after a drop or a removal, with the cell and its new type.</summary>
	public event Action<int, int, LitterType>? Changed;

	public LitterType TypeAt( int x, int y ) => InBounds( x, y ) ? (LitterType)types[Index( x, y )] : LitterType.None;
	public int CollectorAt( int x, int y ) => InBounds( x, y ) ? collectors[Index( x, y )] : 0;
	public long ClaimTurnAt( int x, int y ) => InBounds( x, y ) ? claimTurns[Index( x, y )] : 0;

	public bool InBounds( int x, int y ) => (uint)x < (uint)Width && (uint)y < (uint)Height;

	private int Index( int x, int y ) => y * Width + x;

	/// <summary>
	/// "Littering cell" (tp.exe <c>0x004D93B0</c>): a cell that already holds litter keeps it and the new item is lost; otherwise the cell
	/// takes the type and nobody's claim. True when the litter was placed.
	/// </summary>
	// [BIN:TPW-EXE:0x004D93B0 littering cell] nothing in game type 1 (online) or when the cell's +0x2c is nonzero; else a sprite of bank 11 frame type − 1 (+0x28), +0x30 = 0, +0x2c = type; vomit (7) and stinkbomb (8) also add their RegionFX (2, 5) through 0x004D8440 and a stinkbomb its particle (+0x38)
	// [APPROX:ECON-020] litter lies on map cells (one item per cell, dropped by guests' full litter meters, vomit and bins they cannot reach) with the original's sprite offset (0x004D93B0) and is counted through the analyser's 4 × 4 block caches (0x004C8BA0, 0x004C8AE0) as the strike (age 1) and the thought (age 4) read them, but the drop's reseed of the original's world generator with the cell id (0x004D94E2) is not followed by OpenTPW's separate streams (DET-014), and the caches are saved with the park where whether the original's analyser serializer keeps them is not traced — evidence needed: DET-014's stream decision for the reseed; the analyser's serializer for the block caches
	public bool Drop( int x, int y, LitterType type )
	{
		if ( Disabled || type == LitterType.None || !InBounds( x, y ) || types[Index( x, y )] != 0 )
			return false;
		var index = Index( x, y );
		Spread( x, y, type, add: true );
		types[index] = (byte)type;
		collectors[index] = 0;
		claimTurns[index] = 0;
		Count++;
		Version++;
		Changed?.Invoke( x, y, type );
		return true;
	}

	/// <summary>"Removing litter from cell" (tp.exe <c>0x004D9620</c>): the type, the sprite and the claim go.</summary>
	public bool Remove( int x, int y )
	{
		if ( !InBounds( x, y ) || types[Index( x, y )] == 0 )
			return false;
		var index = Index( x, y );
		Spread( x, y, (LitterType)types[index], add: false );
		types[index] = 0;
		collectors[index] = 0;
		claimTurns[index] = 0;
		Count--;
		Version++;
		Changed?.Invoke( x, y, LitterType.None );
		return true;
	}

	/// <summary>Vomit and a stinkbomb add their <c>RegionFX</c> (2 and 5) while they lie (tp.exe <c>0x004D8440</c>; the removal <c>0x004D9620</c> takes it back).</summary>
	private void Spread( int x, int y, LitterType type, bool add )
	{
		if ( type == LitterType.Vomit )
			Environment.Apply( RegionEffect.Vomit, x, y, add );
		else if ( type == LitterType.Stinkbomb )
			Environment.Apply( RegionEffect.Stinkbomb, x, y, add );
	}

	/// <summary>A handyman claims the litter (tp.exe <c>0x004D9600</c>: <c>+0x30</c> = his id, <c>+0x34</c> = the turn).</summary>
	public void Claim( int x, int y, int collector, long turn )
	{
		if ( !InBounds( x, y ) )
			return;
		collectors[Index( x, y )] = collector;
		claimTurns[Index( x, y )] = turn;
		Version++;
	}

	/// <summary>Littered cells in the original's search order: x outer, y inner, both ascending (tp.exe <c>0x004C8ED0</c>).</summary>
	public IEnumerable<(int X, int Y)> Littered()
	{
		if ( Count == 0 )
			yield break;
		for ( var x = 0; x < Width; x++ )
			for ( var y = 0; y < Height; y++ )
				if ( types[Index( x, y )] != 0 )
					yield return (x, y);
	}

	/// <summary>
	/// The park's litter share for the handymen's strike grievance (tp.exe <c>0x00509360</c> case 0): littered cells over path, queue and
	/// type-9 cells (the analyser sums <c>0x004C8180</c> / <c>0x004C8150</c> over its 4 × 4 blocks).
	/// </summary>
	// [BIN:TPW-EXE:0x00509360 handyman grievance] 0x004C8180(1) and 0x004C8150(1): the sums over the 1024 blocks, each read with age 1
	public LitterStatistic? StatisticAt( long turn )
	{
		if ( Cells == null )
			return null;
		int littered = 0, paths = 0;
		for ( var block = 0; block < BlockCount; block++ )
			littered += LitteredInBlock( block, 1, turn );
		for ( var block = 0; block < BlockCount; block++ )
			paths += PathCellsInBlock( block, 1, turn );
		return new LitterStatistic( paths, littered );
	}

	/// <summary>The analyser's block size: 4 × 4 cells (tp.exe <c>0x004C7B70</c>).</summary>
	public const int BlockSize = 4;
	/// <summary>Blocks per row of the original's 128 × 128 map.</summary>
	public const int BlocksPerRow = MapSize / BlockSize;
	/// <summary>The analyser's 1024 blocks.</summary>
	public const int BlockCount = BlocksPerRow * BlocksPerRow;

	private readonly int[] blockLittered = new int[BlockCount];
	private readonly long[] blockLitteredStamp = new long[BlockCount];
	private readonly bool[] blockLitteredValid = new bool[BlockCount];
	private readonly int[] blockPaths = new int[BlockCount];
	private readonly long[] blockPathsStamp = new long[BlockCount];
	private readonly bool[] blockPathsValid = new bool[BlockCount];

	/// <summary>The analyser block of a cell (tp.exe <c>0x004C7B70</c>: (y / 4) × 32 + x / 4).</summary>
	public static int BlockOf( int x, int y ) => y / BlockSize * BlocksPerRow + x / BlockSize;

	/// <summary>Whether a reader of age <paramref name="maximumAge"/> counts again at <paramref name="turn"/>: the analyser's (turn >> 2) − (stamp >> 2) test, unsigned.</summary>
	private static bool Stale( bool valid, long stamp, uint maximumAge, long turn ) =>
		!valid || maximumAge < unchecked( (uint)(turn >> 2) - (uint)(stamp >> 2) );

	/// <summary>
	/// A block's littered cells as the analyser reads them (tp.exe <c>0x004C8BA0</c>): the cached count while it is no older than the reader's age, else
	/// counted again and stamped. The caches are saved with the park (older saves count at their first reads).
	/// </summary>
	// [BIN:TPW-EXE:0x004C8BA0 block litter] counted again only when the block's flag (+0x60) is clear or the age (0x0041A990: (turn >> 2) − (stamp >> 2)) exceeds the caller's; then the 4 × 4 cells with litter are counted and stamped (0x0041A960)
	public int LitteredInBlock( int block, uint maximumAge, long turn )
	{
		if ( Stale( blockLitteredValid[block], blockLitteredStamp[block], maximumAge, turn ) )
		{
			var count = 0;
			ForBlock( block, ( x, y ) => count += TypeAt( x, y ) != LitterType.None ? 1 : 0 );
			blockLittered[block] = count;
			blockLitteredStamp[block] = turn;
			blockLitteredValid[block] = true;
		}
		return blockLittered[block];
	}

	/// <summary>A block's path, queue and entrance cells (types 1, 3, 9) as the analyser reads them (tp.exe <c>0x004C8AE0</c>), cached like <see cref="LitteredInBlock"/>.</summary>
	// [BIN:TPW-EXE:0x004C8AE0 block paths] the same cache test with its own flag (+0x54) and stamp; a cell counts when 0x00536310 (type 1) or 0x00536320 (type 3 or 9) answers
	public int PathCellsInBlock( int block, uint maximumAge, long turn )
	{
		if ( Stale( blockPathsValid[block], blockPathsStamp[block], maximumAge, turn ) )
		{
			var count = 0;
			ForBlock( block, ( x, y ) => count += Cells?.RawTypeAt( x, y ) is 1 or 3 or 9 ? 1 : 0 );
			blockPaths[block] = count;
			blockPathsStamp[block] = turn;
			blockPathsValid[block] = true;
		}
		return blockPaths[block];
	}

	private static void ForBlock( int block, Action<int, int> visit )
	{
		var x0 = block % BlocksPerRow * BlockSize;
		var y0 = block / BlocksPerRow * BlockSize;
		for ( var x = x0; x < x0 + BlockSize; x++ )
			for ( var y = y0; y < y0 + BlockSize; y++ )
				visit( x, y );
	}

	/// <summary>The valid littered-cell counts of the blocks with their stamps (the park save).</summary>
	public IEnumerable<(int Block, int Count, long Stamp)> LitteredBlockCache =>
		Enumerable.Range( 0, BlockCount ).Where( block => blockLitteredValid[block] ).Select( block => (block, blockLittered[block], blockLitteredStamp[block]) );

	/// <summary>The valid path-cell counts of the blocks with their stamps (the park save).</summary>
	public IEnumerable<(int Block, int Count, long Stamp)> PathBlockCache =>
		Enumerable.Range( 0, BlockCount ).Where( block => blockPathsValid[block] ).Select( block => (block, blockPaths[block], blockPathsStamp[block]) );

	/// <summary>Restores the saved block caches (after <see cref="Restore"/>, which empties them); the other blocks count at their first read.</summary>
	public void RestoreBlockCaches( IEnumerable<(int Block, int Count, long Stamp)> littered, IEnumerable<(int Block, int Count, long Stamp)> paths )
	{
		ResetBlockCounts();
		foreach ( var (block, count, stamp) in littered )
			(blockLittered[block], blockLitteredStamp[block], blockLitteredValid[block]) = (count, stamp, true);
		foreach ( var (block, count, stamp) in paths )
			(blockPaths[block], blockPathsStamp[block], blockPathsValid[block]) = (count, stamp, true);
	}

	/// <summary>Forgets the analyser's block counts (a load: the next reads count again).</summary>
	public void ResetBlockCounts()
	{
		Array.Clear( blockLitteredValid );
		Array.Clear( blockPathsValid );
	}

	/// <summary>
	/// The sprite's offset inside the cell in tenths of a cell (1..9 each way): the original reseeds the world generator with the cell id
	/// (<c>y × 128 + x + 1</c>) and draws twice (tp.exe <c>0x004D94E2</c>), so the offset depends on the cell alone.
	/// </summary>
	public static (int X, int Z) SpriteOffset( int x, int y )
	{
		var state = (uint)(y * 128 + x + 1);
		return ((int)(Next( ref state ) % 9) + 1, (int)(Next( ref state ) % 9) + 1);
	}

	/// <summary>
	/// The point in a littered cell a handyman walks to, in 1/256 of a cell: the job search reseeds the world generator with the cell id and
	/// draws twice (tp.exe <c>0x004D71CA</c>), each draw's low seven bits clamped to 1..9 and times 13, so the spot depends on the cell alone.
	/// </summary>
	public static (int X, int Y) SweepSpot( int x, int y )
	{
		var state = (uint)(y * 128 + x + 1);
		int Byte( uint draw ) => Math.Clamp( (int)(draw & 0x7F), 1, 9 ) * 13;
		var first = Byte( Next( ref state ) );
		return (first, Byte( Next( ref state ) ));
	}

	/// <summary>The world generator step (tp.exe <c>0x00516330</c>, ORACLE.md X1): LCG, rotate right by 13, signed absolute value.</summary>
	private static uint Next( ref uint state )
	{
		var value = unchecked(state * 1664525u + 1013904223u);
		state = (value >> 13) | (value << 19);
		var signed = (int)state;
		return (uint)(signed < 0 ? unchecked(-signed) : signed);
	}

	/// <summary>Every littered cell with its claim, in row order (saves).</summary>
	public IEnumerable<LitterCell> Snapshot()
	{
		for ( var index = 0; index < types.Length; index++ )
			if ( types[index] != 0 )
				yield return new LitterCell( index % Width, index / Width, (LitterType)types[index], collectors[index], claimTurns[index] );
	}

	/// <summary>Replaces the litter with <paramref name="cells"/> (loads); cells outside the map are rejected.</summary>
	public void Restore( IEnumerable<LitterCell> cells )
	{
		Array.Clear( types );
		Array.Clear( collectors );
		Array.Clear( claimTurns );
		Environment.Clear();
		ResetBlockCounts();
		Count = 0;
		foreach ( var cell in cells )
		{
			if ( !InBounds( cell.X, cell.Y ) || cell.Type is LitterType.None or > LitterType.Stinkbomb )
				throw new InvalidDataException( $"Litter at ({cell.X}, {cell.Y}) of type {(int)cell.Type} does not fit the map." );
			var index = Index( cell.X, cell.Y );
			if ( types[index] != 0 )
				throw new InvalidDataException( $"Two litter items at ({cell.X}, {cell.Y})." );
			Spread( cell.X, cell.Y, cell.Type, add: true );
			types[index] = (byte)cell.Type;
			collectors[index] = cell.Collector;
			claimTurns[index] = cell.ClaimTurn;
			Count++;
		}
		Version++;
	}
}

/// <summary>One littered cell as saved: its type and the handyman's claim (0 = none).</summary>
public readonly record struct LitterCell( int X, int Y, LitterType Type, int Collector, long ClaimTurn );
