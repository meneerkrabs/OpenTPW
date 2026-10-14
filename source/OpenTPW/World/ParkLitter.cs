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
	public ParkCellMap? Cells { get; set; }
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
	// [APPROX:ECON-020] litter lies on map cells (one item per cell, dropped by guests' full litter meters, vomit and bins they cannot reach) as in the original, but the drop's sprite reseeds the original's world generator with the cell id (0x004D94E2), which OpenTPW's separate streams do not follow (DET-014), and the handymen's strike share counts the live map where the analyser caches 4 × 4 blocks for a few turns — evidence needed: none (read): the reseed is DET-014's stream decision, the cache lag 0x004C8BA0 / 0x004C8AE0
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
	// Approximation (ECON-020, registered at Drop): the analyser recounts a 4 × 4 block only when (turn >> 2) − (stamp >> 2) exceeds the caller's age (1 here), so the original's share can lag a few turns; OpenTPW counts the live map
	public LitterStatistic? Statistic => Cells == null ? null : new( Cells.CountOf( ParkCellType.Path ) + Cells.CountOf( ParkCellType.Queue ) + Cells.CountOf( (ParkCellType)9 ), Count );

	/// <summary>
	/// The sprite's offset inside the cell in tenths of a cell (1..9 each way): the original reseeds the world generator with the cell id
	/// (<c>y × 128 + x + 1</c>) and draws twice (tp.exe <c>0x004D94E2</c>), so the offset depends on the cell alone.
	/// </summary>
	public static (int X, int Z) SpriteOffset( int x, int y )
	{
		var state = (uint)(y * 128 + x + 1);
		return ((int)(Next( ref state ) % 9) + 1, (int)(Next( ref state ) % 9) + 1);
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
