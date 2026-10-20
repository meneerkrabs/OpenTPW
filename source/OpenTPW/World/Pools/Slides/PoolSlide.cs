namespace OpenTPW;

/// <summary>The cross-section of a slide (docs/POOLS.md, "Water slides"): an open flume (a half pipe the rider lies in) or a closed tube.</summary>
public enum PoolSlideProfile
{
	/// <summary>An open half pipe: the rider slides on the body.</summary>
	Flume = 0,
	/// <summary>A closed tube.</summary>
	Tube = 1
}

/// <summary>One piece of a slide: it fills one cell. Turns are seen from above, from the rider's heading.</summary>
public enum PoolSlidePiece
{
	/// <summary>Runs straight through the cell.</summary>
	Straight = 0,
	/// <summary>A quarter turn to the left.</summary>
	Left = 1,
	/// <summary>A quarter turn to the right.</summary>
	Right = 2,
	/// <summary>The last piece: a chute from the cell's edge to its centre, which lies over pool water. Riders splash down at its end.</summary>
	Splash = 3
}

/// <summary>A point of a slide in 1/1024 of a cell (cell units, y growing south like the cell map) and its height in thousandths of an engine unit.</summary>
public readonly record struct PoolSlidePoint( int X, int Y, int Height );

/// <summary>
/// One water slide of the pools extension (docs/POOLS.md, "Water slides"): a tower on land beside a pool, and an open track of pieces, one per cell, from the tower down to a splash piece over
/// the pool. Pure data and integer geometry; nothing here reads the world. The track descends at one constant slope from the tower's top (<see cref="TopHeight"/>) to just over the water
/// (<see cref="EndHeight"/>), so a short slide is steep and a long one gentle, and riders run by gravity along it (<see cref="PoolSlideRide"/>).
/// </summary>
// [EXT:pools-slides] OpenTPW's own water slides; the original game has no pools
public sealed class PoolSlide
{
	/// <summary>Fixed-point units of a cell: positions are in 1/1024 of a cell.</summary>
	public const int Unit = 1024;
	/// <summary>The length of a straight piece.</summary>
	public const int StraightLength = Unit;
	/// <summary>The length of a quarter turn: an arc of radius half a cell (512 * pi / 2).</summary>
	public const int CurveLength = 804;
	/// <summary>The length of the splash chute: from the cell's edge to its centre.</summary>
	public const int SplashLength = Unit / 2;
	/// <summary>The most pieces a slide has, the splash piece included.</summary>
	public const int MaximumPieces = 24;
	/// <summary>Engine units of height are stored in thousandths.</summary>
	public const int HeightUnit = 1000;

	private static readonly (int X, int Y)[] Directions = { (1, 0), (0, 1), (-1, 0), (0, -1) };
	// the quarter circle in 16 steps, 1.0 = 1024: sin(i * 90 / 16 degrees) * 1024
	private static readonly int[] QuarterSine = BuildQuarterSine();

	private readonly PoolSlidePiece[] pieces;
	private readonly (int X, int Y)[] cells;
	private readonly int[] heading;
	private readonly int[] startLength;

	/// <summary>Builds a slide from its parts; use <see cref="PoolSlideRules"/> to know whether the parts make a valid one.</summary>
	/// <param name="towerX">The tower's anchor cell: the key of the slide.</param>
	/// <param name="towerY">The tower's anchor cell.</param>
	/// <param name="profile">The cross-section.</param>
	/// <param name="startX">The first piece's cell.</param>
	/// <param name="startY">The first piece's cell.</param>
	/// <param name="startHeading">The direction the track leaves the tower: 0 east (+x), 1 south (+y), 2 west, 3 north.</param>
	/// <param name="pieces">The pieces in riding order.</param>
	/// <param name="topHeight">The height of the track at the tower, thousandths of an engine unit.</param>
	/// <param name="endHeight">The height of the track at the splash, thousandths of an engine unit.</param>
	public PoolSlide( int towerX, int towerY, PoolSlideProfile profile, int startX, int startY, int startHeading, IEnumerable<PoolSlidePiece> pieces, int topHeight, int endHeight )
	{
		TowerX = towerX;
		TowerY = towerY;
		Profile = profile;
		StartX = startX;
		StartY = startY;
		StartHeading = startHeading & 3;
		TopHeight = topHeight;
		EndHeight = endHeight;
		this.pieces = pieces.ToArray();
		cells = new (int X, int Y)[this.pieces.Length];
		heading = new int[this.pieces.Length + 1];
		startLength = new int[this.pieces.Length + 1];
		var (x, y, h, length) = (startX, startY, StartHeading, 0);
		for ( var index = 0; index < this.pieces.Length; index++ )
		{
			cells[index] = (x, y);
			heading[index] = h;
			startLength[index] = length;
			h = (h + Turn( this.pieces[index] )) & 3;
			length += LengthOf( this.pieces[index] );
			(x, y) = (x + Directions[h].X, y + Directions[h].Y);
		}
		heading[this.pieces.Length] = h;
		startLength[this.pieces.Length] = length;
		Length = length;
	}

	/// <summary>The tower's anchor cell; with <see cref="TowerY"/> the key of the slide.</summary>
	public int TowerX { get; }
	public int TowerY { get; }
	public PoolSlideProfile Profile { get; }
	/// <summary>The first piece's cell and the direction the track leaves the tower.</summary>
	public int StartX { get; }
	public int StartY { get; }
	public int StartHeading { get; }
	/// <summary>The track's height at the tower and at the splash, thousandths of an engine unit.</summary>
	public int TopHeight { get; }
	public int EndHeight { get; }
	/// <summary>The pieces in riding order; the last is the splash piece of a complete slide.</summary>
	public IReadOnlyList<PoolSlidePiece> Pieces => pieces;
	/// <summary>The cell of every piece in riding order.</summary>
	public IReadOnlyList<(int X, int Y)> Cells => cells;
	/// <summary>The track's length in 1/1024 of a cell.</summary>
	public int Length { get; }
	/// <summary>Whether the last piece is a splash piece (a complete slide).</summary>
	public bool IsComplete => pieces.Length > 0 && pieces[^1] == PoolSlidePiece.Splash;
	/// <summary>The id the activity and the guests know the slide by: the tower's anchor packed into one number (never 0).</summary>
	public int Id => KeyOf( TowerX, TowerY );
	/// <summary>The cell of the splash piece, or null when the slide is not complete.</summary>
	public (int X, int Y)? LandingCell => IsComplete ? cells[^1] : null;

	/// <summary>The id of the slide whose tower is anchored at the cell.</summary>
	public static int KeyOf( int towerX, int towerY ) => (towerY << 12 | towerX) + 1;
	/// <summary>The tower anchor of a slide id.</summary>
	public static (int X, int Y) TowerOf( int id ) => ((id - 1) & 0xFFF, (id - 1) >> 12);

	/// <summary>The slope in thousandths: the height lost per length run, both in engine units (a cell is two units wide).</summary>
	public int SlopePermille => Length == 0 ? 0 : (int)((long)(TopHeight - EndHeight) * Unit * 1000 / ((long)Length * 2 * HeightUnit));

	/// <summary>The heading after the piece, or the heading of the track at its end.</summary>
	public int HeadingAfter( int index ) => (heading[index] + Turn( pieces[index] )) & 3;
	/// <summary>The heading a piece is entered with.</summary>
	public int HeadingOf( int index ) => heading[index];
	/// <summary>Where the piece starts on the track, 1/1024 of a cell.</summary>
	public int StartOf( int index ) => startLength[index];

	/// <summary>The direction vector of a heading: 0 east, 1 south, 2 west, 3 north.</summary>
	public static (int X, int Y) Direction( int heading ) => Directions[heading & 3];

	/// <summary>The turn of a piece in heading steps.</summary>
	public static int Turn( PoolSlidePiece piece ) => piece switch { PoolSlidePiece.Left => 3, PoolSlidePiece.Right => 1, _ => 0 };

	/// <summary>The length of a piece.</summary>
	public static int LengthOf( PoolSlidePiece piece ) => piece switch { PoolSlidePiece.Straight => StraightLength, PoolSlidePiece.Splash => SplashLength, _ => CurveLength };

	/// <summary>The index of the piece that holds the position (clamped to the last).</summary>
	public int PieceAt( int position )
	{
		for ( var index = pieces.Length - 1; index > 0; index-- )
			if ( position >= startLength[index] )
				return index;
		return 0;
	}

	/// <summary>The height at a position on the track: a straight line from the top to the end.</summary>
	public int HeightAt( int position )
	{
		if ( Length == 0 )
			return TopHeight;
		var clamped = Math.Clamp( position, 0, Length );
		return TopHeight - (int)((long)(TopHeight - EndHeight) * clamped / Length);
	}

	/// <summary>The point of the track at a position (1/1024 of a cell along it), integers only so every machine agrees.</summary>
	public PoolSlidePoint PointAt( int position )
	{
		if ( pieces.Length == 0 )
			return new PoolSlidePoint( StartX * Unit + Unit / 2, StartY * Unit + Unit / 2, TopHeight );
		var clamped = Math.Clamp( position, 0, Length );
		var index = PieceAt( clamped );
		var local = Math.Clamp( clamped - startLength[index], 0, LengthOf( pieces[index] ) );
		var (x, y) = PlanPoint( index, local );
		return new PoolSlidePoint( x, y, HeightAt( clamped ) );
	}

	/// <summary>The plan position (1/1024 of a cell) of the point <paramref name="local"/> along piece <paramref name="index"/>.</summary>
	private (int X, int Y) PlanPoint( int index, int local )
	{
		var (cx, cy) = (cells[index].X * Unit + Unit / 2, cells[index].Y * Unit + Unit / 2);
		var h = Directions[heading[index]];
		var half = Unit / 2;
		switch ( pieces[index] )
		{
			case PoolSlidePiece.Straight:
			case PoolSlidePiece.Splash:
				// from the edge the track comes in by, along the heading
				return (cx - h.X * half + h.X * local, cy - h.Y * half + h.Y * local);
			default:
			{
				var exit = Directions[(heading[index] + Turn( pieces[index] )) & 3];
				// the circle's centre is the corner where the entry and exit edges meet; the track runs a quarter turn round it
				var (px, py) = (cx - h.X * half + exit.X * half, cy - h.Y * half + exit.Y * half);
				var step = (long)local * 16 * 4096 / CurveLength;
				var index16 = (int)(step >> 12);
				var fraction = (int)(step & 4095);
				int sin, cos;
				if ( index16 >= 16 )
					(sin, cos) = (Unit, 0);
				else
				{
					sin = QuarterSine[index16] + (int)((long)(QuarterSine[index16 + 1] - QuarterSine[index16]) * fraction / 4096);
					cos = QuarterSine[16 - index16] + (int)((long)(QuarterSine[15 - index16] - QuarterSine[16 - index16]) * fraction / 4096);
				}
				// from -exit (angle 0) to +heading (angle 90 degrees): radius * ( -cos * exit + sin * heading )
				return (px + (-cos * exit.X + sin * h.X) * half / Unit, py + (-cos * exit.Y + sin * h.Y) * half / Unit);
			}
		}
	}

	private static int[] BuildQuarterSine()
	{
		var table = new int[17];
		for ( var step = 0; step <= 16; step++ )
			table[step] = (int)Math.Round( DetMath.Sin( step * Math.PI / 32 ) * Unit );
		return table;
	}

	/// <summary>The plan point of the landing: the centre of the splash cell (1/1024 of a cell), or null when the slide is not complete.</summary>
	public (int X, int Y)? LandingPoint => LandingCell is { } cell ? (cell.X * Unit + Unit / 2, cell.Y * Unit + Unit / 2) : null;

	private int[]? rideTable;

	/// <summary>
	/// The position of a rider <paramref name="turn"/> park turns after it left the tower (1/1024 cell along the track). Every rider runs the same physics (<see cref="PoolSlideRide.Step"/>), so the
	/// position is a pure function of the turn and a rider needs to save nothing but the turn. The table is built once from the slide's slope.
	/// </summary>
	public int PositionAtTurn( int turn )
	{
		if ( rideTable == null )
		{
			var table = new List<int> { 0 };
			int position = 0, speed = PoolSlideSettings.LaunchSpeed;
			while ( table.Count < 100_000 && !PoolSlideRide.Step( this, ref position, ref speed ) )
				table.Add( position );
			table.Add( Length );
			rideTable = table.ToArray();
		}
		return rideTable[Math.Clamp( turn, 0, rideTable.Length - 1 )];
	}

	/// <summary>The park turns from the tower to the end of the track: the turn at which <see cref="PositionAtTurn"/> reaches the length.</summary>
	public int RideTurns
	{
		get
		{
			PositionAtTurn( 0 );
			return rideTable!.Length - 1;
		}
	}

	/// <summary>The cells a rider can be seen over, for the footprint checks: every piece's cell.</summary>
	public bool Covers( int x, int y ) => cells.Contains( (x, y) );

	/// <summary>A copy of the slide with different pieces (the heights are kept).</summary>
	public PoolSlide With( IEnumerable<PoolSlidePiece> newPieces, int topHeight, int endHeight ) =>
		new( TowerX, TowerY, Profile, StartX, StartY, StartHeading, newPieces, topHeight, endHeight );
}
