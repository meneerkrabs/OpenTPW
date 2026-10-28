namespace OpenTPW;

/// <summary>
/// The structure records of the park fence, the part of tp.exe's second map layer (the 0x28-byte records at game +0x1102D8, one per cell, 0x004D0AF0) that the fence routines read and write:
/// per cell the structure type (0 free, 0x19 a fence post, 0xC one of the three cells next to a post that the post's walk types as its extension), the fence links (+0x22, 1 -Y, 4 +X, 16 +Y, 64 -X),
/// the walk direction (+0xD), the flag word (+0xE; 0x20 is the fill's mark, 1 a post at the water), the owner word (+0x10, the post's cell word for an extension) and the placement counter (+0x20).
/// Immutable to everything but <see cref="FenceEngine"/> and the land that owns it.
/// </summary>
// [BIN:TPW-EXE:0x004D0AF0 structure records] the record of a cell is not part of the cell: an array of its own indexed by the cell id; the fence uses the type (+8), the links (+0x22 while the fence mode byte 0x81B4CC is set and the record's class byte +2 is 2, 0x522730, 0x522770), +0xD (0x5227E0, 0x522850), the flags (+0xE), the owner (+0x10, 0x539210) and the counter (+0x20)
public sealed class FenceLayer
{
	/// <summary>The structure type of a fence post.</summary>
	public const byte PostType = 0x19;
	/// <summary>The structure type of the cells the walk gives a post as its extension.</summary>
	public const byte ExtensionType = 0xC;

	internal readonly byte[] Type;
	internal readonly ushort[] Owner;
	internal readonly ushort[] Flags;
	internal readonly byte[] Links;
	internal readonly byte[] Direction;
	internal readonly short[] Counter;

	public FenceLayer( int width, int height )
	{
		Width = width;
		Height = height;
		var count = width * height;
		Type = new byte[count];
		Owner = new ushort[count];
		Flags = new ushort[count];
		Links = new byte[count];
		Direction = new byte[count];
		Counter = new short[count];
	}

	public int Width { get; }
	public int Height { get; }

	/// <summary>Whether a post stands on the cell.</summary>
	public bool IsPost( int x, int y ) => (uint)x < (uint)Width && (uint)y < (uint)Height && Type[y * Width + x] == PostType;

	/// <summary>
	/// Whether the cell's structure record counts 50 for the net worth (tp.exe 0x5363F0): a post, or an extension (0xC) whose parent record is a post. Every hoarding post therefore counts the
	/// four cells of its block, each judged by its own flag 0x40 (the caller's).
	/// </summary>
	public bool CountsAsStructure( int x, int y )
	{
		switch ( TypeAt( x, y ) )
		{
			case PostType:
				return true;
			case ExtensionType:
				var parent = OwnerAt( x, y ) - 1;
				return parent >= 0 && parent < Width * Height && Type[parent] == PostType;
			default:
				return false;
		}
	}

	/// <summary>The structure type the layer holds for the cell (0, <see cref="PostType"/> or <see cref="ExtensionType"/>).</summary>
	public byte TypeAt( int x, int y ) => (uint)x < (uint)Width && (uint)y < (uint)Height ? Type[y * Width + x] : (byte)0;

	/// <summary>The fence links of the cell (+0x22).</summary>
	public byte LinksAt( int x, int y ) => (uint)x < (uint)Width && (uint)y < (uint)Height ? Links[y * Width + x] : (byte)0;

	/// <summary>The walk direction of the cell (+0xD).</summary>
	public byte DirectionAt( int x, int y ) => (uint)x < (uint)Width && (uint)y < (uint)Height ? Direction[y * Width + x] : (byte)0;

	/// <summary>The flag word of the cell's record (+0xE).</summary>
	public ushort FlagsAt( int x, int y ) => (uint)x < (uint)Width && (uint)y < (uint)Height ? Flags[y * Width + x] : (ushort)0;

	/// <summary>The owner word of the cell's record (+0x10).</summary>
	public ushort OwnerAt( int x, int y ) => (uint)x < (uint)Width && (uint)y < (uint)Height ? Owner[y * Width + x] : (ushort)0;

	/// <summary>The placement counter of the cell's record (+0x20).</summary>
	public short CounterAt( int x, int y ) => (uint)x < (uint)Width && (uint)y < (uint)Height ? Counter[y * Width + x] : (short)0;

	/// <summary>Every cell whose record holds something, in row order.</summary>
	public IEnumerable<int> Occupied()
	{
		for ( var index = 0; index < Type.Length; index++ )
		{
			if ( Type[index] != 0 || Links[index] != 0 || Direction[index] != 0 || Flags[index] != 0 || Owner[index] != 0 || Counter[index] != 0 )
				yield return index;
		}
	}

	/// <summary>A copy of the layer.</summary>
	public FenceLayer Clone()
	{
		var copy = new FenceLayer( Width, Height );
		Array.Copy( Type, copy.Type, Type.Length );
		Array.Copy( Owner, copy.Owner, Owner.Length );
		Array.Copy( Flags, copy.Flags, Flags.Length );
		Array.Copy( Links, copy.Links, Links.Length );
		Array.Copy( Direction, copy.Direction, Direction.Length );
		Array.Copy( Counter, copy.Counter, Counter.Length );
		return copy;
	}
}

/// <summary>
/// Raised when one of the original's fence routines fails on a fence: its walk or scan never ends, or the fill reads the cell beyond the map's edge (0x4D96F0 answers null there and 0x5311C0 does not test it, so tp.exe
/// faults). The original hangs or crashes there; OpenTPW stops instead and leaves everything as it was.
/// </summary>
public sealed class FenceFaultException( string message ) : InvalidOperationException( message );

/// <summary>
/// tp.exe's fence routines on a park's cell map and its <see cref="FenceLayer"/>: the line tool in mode 0x19 (the click handler 0x524960, the line walker 0x53AAC0 and the step 0x5370E0 with its
/// passes 0x19, 0x80, 0x82, 0x85, 0x86 and 0x81), the ring walk 0x52BA50, the fill 0x5311C0 with its mark 0x531040, and the park fence set-up 0x5323F0 and the Buy Land purchase 0x5327E0 that run them.
/// Every write goes to a buffer first (<see cref="Commit"/> applies it), so a walk that never ends in the original leaves the park as it was.
/// </summary>
// [BIN:TPW-EXE:0x005323F0 park fence] set-up, [BIN:TPW-EXE:0x005327E0 land purchase] purchase; both checked against the original's saved start states (restart.INTS) cell for cell
public sealed class FenceEngine
{
	private const int North = 1, East = 4, South = 0x10, West = 0x40;
	private const int Straight = 0, Corner = 1;
	/// <summary>The most steps a walk or scan takes before it counts as never ending (a fence has at most 128 × 128 / 2 posts).</summary>
	private const int StepLimit = 1 << 20;

	private readonly ParkCellMap cells;
	private readonly FenceLayer layer;
	private readonly int width, height;
	private readonly Dictionary<int, ushort> pendingFlags = new();
	private readonly Dictionary<int, TrackCell> pendingTracks = new();
	private readonly List<int> pendingStamps = new();

	// the line tool's globals: 0x816D64 the last op, 0x763B00/04 the previous step, 0x820AC8 first step, 0x820AD0 last step, 0x820AE8 segment end, 0x81EDE4/8 the stored click
	private int lastOp;
	private int previousDx = 0x80, previousDy = 0x80;
	private bool firstStep, lastStep, segmentEnd;
	private int startX = -1, startY = -1;
	private bool toolActive = true;

	/// <summary>The fence's gate posts (0x7639F0/F4 and 0x7639F8/FC).</summary>
	public (int X, int Y) PostA { get; }
	public (int X, int Y) PostB { get; }

	/// <summary>Whether the bank balance is not negative: 0x538FC0 refuses to type an extension cell while it is.</summary>
	public bool BankNotNegative { get; set; } = true;

	/// <summary>An engine over <paramref name="cells"/> and the fence <paramref name="layer"/> (changed in place by <see cref="Commit"/> only).</summary>
	public FenceEngine( ParkCellMap cells, FenceLayer layer, (int X, int Y) postA, (int X, int Y) postB )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( layer );
		if ( layer.Width != cells.Width || layer.Height != cells.Height )
			throw new ArgumentException( "The fence layer and the cell map differ in size." );
		this.cells = cells;
		this.layer = layer;
		width = cells.Width;
		height = cells.Height;
		PostA = postA;
		PostB = postB;
	}

	// ---------------------------------------------------------------- the records

	private int At( int x, int y )
	{
		var flat = y * width + x;
		return (uint)flat < (uint)(width * height) ? flat : -1;
	}

	private bool InMap( int x, int y ) => cells.InBounds( x, y );
	private int X( int index ) => index % width;
	private int Y( int index ) => index / width;

	private static (int Dx, int Dy) Offset( int bit ) => bit switch
	{
		1 => (0, -1),
		2 => (1, -1),
		4 => (1, 0),
		8 => (1, 1),
		0x10 => (0, 1),
		0x20 => (-1, 1),
		0x40 => (-1, 0),
		0x80 => (-1, -1),
		_ => (0, 0)
	};

	/// <summary>0x4D8C00: the opposite link of a one-bit direction (the byte shifted by four).</summary>
	private static int Opposite( int direction ) => direction == 0 ? 0 : direction < 0x10 ? (direction & 0xFF) << 4 : (direction & 0xFF) >> 4;

	/// <summary>0x4D8C20: <paramref name="value"/> turned by the bit number of <paramref name="direction"/> (an eight-bit rotate).</summary>
	private static int Rotate( int direction, int value )
	{
		var count = 0;
		var bits = (byte)direction;
		if ( bits > 1 )
		{
			do
			{
				bits >>= 1;
				count++;
			}
			while ( bits > 1 );
		}
		var shifted = (value & 0xFF) << count;
		return shifted > 0x80 ? shifted >> 8 : (byte)(value << count);
	}

	// ---------------------------------------------------------------- one record, two layers
	// The fence's structure record and a track tool's record are the same 0x28 bytes of tp.exe's second layer (0x4D0AF0). The fence routines read and write the type (+8), the walk direction (+0xD, a track tile's
	// flow link), the flag word (+0xE), the word (+0x10: the walk's parent in a fence record, the ride word of a track origin, the origin's word in a track extension) and the counter (+0x20, a track tile's overlap
	// counter) of a cell that holds a track tile exactly as they do on a post, and its links in +0x22 (the fence mode's link byte, 0x522770). OpenTPW keeps a tile's record in its own layer (TrackCell: Type, Link, Flags,
	// Owner or Parent, Overlap), so every access goes to the tile's record where one exists and to the fence layer where none does; the writes are buffered and <see cref="Commit"/> applies them. A record the fence
	// retypes (0x538FC0 with type 0 from the post clear, or a record flagged 0x40) leaves the track layer with the fields it had and goes on as a fence record. Checked against the x86 oracle (ORACLE.md X37).
	// [BIN:TPW-EXE:0x00538FC0 type write] +8 is written unless the record's flag word has no 0x40 and holds another type than the one asked for (the fence tool's 0x19 then still answers true), so a tile is never retyped by a post
	// [BIN:TPW-EXE:0x00539670 direction] op 0x82 (called from 0x538146 in the step 0x5370E0) writes +0xD of every record but the types 0xB, 0xC, 0x10, 0x11 (and a cell of type 0x15): a station (0xD, 0x12) takes the walk direction in its flow link
	// [BIN:TPW-EXE:0x0052BC43 owner word] the walk 0x52BA50 writes the post's word into +0x10 of the three cells next to it (0x539210 at 0x52BC43, 0x52BC82, 0x52BCC1) and then types them 0xC (0x538FC0 at 0x52BC4C, 0x52BC8B, 0x52BCCA), whatever they hold; the post clear 0x53B280 reads that word back and clears the neighbours that name the post (0x53B739 and the three after it)

	private TrackCell TrackOf( int index )
	{
		if ( index < 0 )
			return default;
		return pendingTracks.TryGetValue( index, out var pending ) ? pending : cells.Tracks.Get( X( index ), Y( index ) );
	}

	private bool HasTrack( int index ) => TrackOf( index ).Type != 0;

	private static int WordOf( in TrackCell track ) => track.IsExtension ? track.Parent + 1 : track.Owner;

	private int TypeOf( int index )
	{
		if ( index < 0 )
			return 0;
		var track = TrackOf( index ).Type;
		return track != 0 ? track : layer.Type[index];
	}

	/// <summary>0x538FC0's write of the record's type (+8). A tile's record that changes type leaves the track layer with its fields and goes on as a fence record.</summary>
	private void SetTypeOf( int index, int type )
	{
		var track = TrackOf( index );
		if ( track.Type != 0 )
		{
			layer.Direction[index] = track.Link;
			layer.Flags[index] = track.Flags;
			layer.Owner[index] = (ushort)Math.Clamp( WordOf( track ), 0, ushort.MaxValue );
			layer.Counter[index] = track.Overlap;
			pendingTracks[index] = default;
		}
		layer.Type[index] = (byte)type;
	}

	private int FlagsOf( int index )
	{
		var track = TrackOf( index );
		return track.Type != 0 ? track.Flags : layer.Flags[index];
	}

	private void SetFlagsOf( int index, int value )
	{
		var track = TrackOf( index );
		if ( track.Type != 0 )
		{
			track.Flags = (byte)value;
			pendingTracks[index] = track;
		}
		else
			layer.Flags[index] = (ushort)value;
	}

	private int DirectionOf( int index )
	{
		var track = TrackOf( index );
		return track.Type != 0 ? track.Link : layer.Direction[index];
	}

	private void SetDirectionOf( int index, int value )
	{
		var track = TrackOf( index );
		if ( track.Type != 0 )
		{
			track.Link = (byte)value;
			pendingTracks[index] = track;
		}
		else
			layer.Direction[index] = (byte)value;
	}

	private int CounterOf( int index )
	{
		var track = TrackOf( index );
		return track.Type != 0 ? track.Overlap : layer.Counter[index];
	}

	private void SetCounterOf( int index, int value )
	{
		var track = TrackOf( index );
		if ( track.Type != 0 )
		{
			track.Overlap = (short)value;
			pendingTracks[index] = track;
		}
		else
			layer.Counter[index] = (short)value;
	}

	/// <summary>The ride word the tool compares an owner with (0x818698): the fence tool runs with none.</summary>
	// [BIN:TPW-EXE:0x539220 link back] the other record links only when its +0x10 is 0 or 0x818698, the tool's ride; the fence setup and a purchase run with 0x818698 = 0
	// [BIN:TPW-EXE:0x0052F175 tool ride] the level start stores 0 into 0x818698 just before the set-up 0x5323F0, whose own tool switch 0x5276D0 (0x532483) clears it again; only the track tools write it (0x524C94, 0x5251D2, 0x525815, 0x527751, 0x527859) and every tool switch 0x5276D0 (the HUD's tool buttons, e.g. 0x4988F8) stores 0 into it (0x5276DF)
	private const int ToolOwner = 0;

	/// <summary>The word of the record (+0x10): a track tile's ride word (origin) or origin word (extension, OpenTPW's Parent), else the fence record's.</summary>
	private int OwnerOf( int index )
	{
		var track = TrackOf( index );
		return track.Type != 0 ? Math.Clamp( WordOf( track ), 0, ushort.MaxValue ) : layer.Owner[index];
	}

	// 0x539210: the record's word; a tile's extension names its origin by Parent (index), an origin keeps the ride word in Owner (the EXT field track-tile-owner)
	private void SetOwnerOf( int index, int value )
	{
		var track = TrackOf( index );
		if ( track.Type == 0 )
		{
			layer.Owner[index] = (ushort)value;
			return;
		}
		if ( track.IsExtension )
			track.Parent = value - 1;
		else
			track.Owner = value;
		pendingTracks[index] = track;
	}

	private int CellFlags( int index ) => pendingFlags.TryGetValue( index, out var pending ) ? pending : (ushort)cells.FlagsAt( X( index ), Y( index ) );
	private void SetCellFlags( int index, int value ) => pendingFlags[index] = (ushort)value;
	private int CellType( int index ) => cells.RawTypeAt( X( index ), Y( index ) );
	private bool Marked( int index ) => index >= 0 && (FlagsOf( index ) & 0x20) != 0;

	/// <summary>0x50C320: the record <paramref name="steps"/> cells away in direction <paramref name="direction"/>, -1 beyond the map.</summary>
	private int Neighbour( int index, int direction, int steps )
	{
		var (dx, dy) = Offset( direction );
		var (x, y) = (X( index ) + dx * steps, Y( index ) + dy * steps);
		return InMap( x, y ) ? At( x, y ) : -1;
	}

	/// <summary>0x4D96F0: the map cell one step away, -1 beyond the map.</summary>
	private int CellNeighbour( int index, int direction ) => Neighbour( index, direction, 1 );

	// links (0x522770 reads +0x22 in the fence mode), their counts (0x522810 over the four cardinal bits, 0x522790 over all eight) and the shapes the walk tests
	private int LinksOf( int index ) => index < 0 ? 0 : layer.Links[index];

	private static int PopCardinal( int links ) => ((links & 1) != 0 ? 1 : 0) + ((links & 4) != 0 ? 1 : 0) + ((links & 0x10) != 0 ? 1 : 0) + ((links & 0x40) != 0 ? 1 : 0);
	private static int PopAll( int links ) => System.Numerics.BitOperations.PopCount( (uint)(links & 0xFF) );

	/// <summary>0x53AE00: exactly two links at a right angle.</summary>
	private bool IsCorner( int index )
	{
		var links = LinksOf( index );
		if ( PopCardinal( links ) != 2 )
			return false;
		return ((links & 1) != 0 && (links & 4) != 0) || ((links & 1) != 0 && (links & 0x40) != 0) || ((links & 0x10) != 0 && (links & 4) != 0) || ((links & 0x10) != 0 && (links & 0x40) != 0);
	}

	/// <summary>The straight test inside 0x5370E0's op 0x81: exactly two links on one line.</summary>
	private bool IsStraight( int index )
	{
		var links = LinksOf( index );
		return PopCardinal( links ) == 2 && (((links & 1) != 0 && (links & 0x10) != 0) || ((links & 4) != 0 && (links & 0x40) != 0));
	}

	/// <summary>0x53AE90: three or more links.</summary>
	private bool IsJunction( int index ) => PopCardinal( LinksOf( index ) ) >= 3 && LinksOf( index ) != 0;

	// ---------------------------------------------------------------- 0x538FC0, the typing of a structure record

	/// <summary>
	/// 0x538FC0(<paramref name="op"/>): types the record. A record of another structure than a post (or the no-terrain type 7) that is not flagged 0x40 refuses a different type (the fence tool's
	/// op 0x19 still answers true); the same type counts one more placement; the new type stamps the cell's block (0x4D8C50, 0x4D8C60).
	/// </summary>
	private bool Place( int index, int op )
	{
		var type = TypeOf( index );
		if ( type != 0x19 && type != 7 && (FlagsOf( index ) & 0x40) == 0 && op != 0 && type != 0 && op != type )
			return op == 0x19;
		if ( type == op )
		{
			SetCounterOf( index, CounterOf( index ) + 1 );
			return true;
		}
		if ( op != 0x19 && !BankNotNegative && op != 0 && op != 7 )
			return false;
		SetTypeOf( index, op );
		pendingStamps.Add( index );
		return true;
	}

	private void Mark( int index, bool on )
	{
		if ( index < 0 )
			return;
		SetFlagsOf( index, on ? FlagsOf( index ) | 0x20 : FlagsOf( index ) & ~0x20 );
	}

	// ---------------------------------------------------------------- the line tool

	/// <summary>
	/// The click handler's mode 0x19 branch (0x524960): the first click stores the point; a later one is snapped to the longer axis, and the passes 0x19, 0x80, 0x82, 0x85, 0x86 and 0x81 of the line
	/// walker run from the stored point to it; a click on the stored point ends the tool.
	/// </summary>
	public void Click( int x, int y )
	{
		if ( !toolActive )
			return;
		if ( startX == -1 && startY == -1 )
		{
			startX = x;
			startY = y;
			return;
		}
		var distanceX = Math.Abs( x - startX );
		var distanceY = Math.Abs( y - startY );
		if ( distanceY <= distanceX )
			y = startY;
		if ( distanceX <= Math.Abs( y - startY ) )
			x = startX;
		if ( startX != x || startY != y || (PostA.X == x && PostA.Y == y) )
		{
			foreach ( var op in new[] { 0x19, 0x80, 0x82, 0x85, 0x86, 0x81 } )
				Line( op, startX, startY, ref x, ref y );
		}
		if ( startX == x && startY == y )
		{
			startX = startY = -1;
			previousDx = previousDy = 0x80;
			toolActive = false;
			return;
		}
		startX = x;
		startY = y;
	}

	/// <summary>
	/// 0x53AAC0: runs <paramref name="op"/> over the posts from (<paramref name="x0"/>, <paramref name="y0"/>) to the end, which it rounds to a whole number of two-cell steps along the longer axis; every cell takes the step
	/// as its direction, the first one flagged (0x820AC8) and the last one flagged (0x820AD0).
	/// </summary>
	private bool Line( int op, int x0, int y0, ref int x1, ref int y1 )
	{
		firstStep = true;
		lastStep = false;
		if ( Math.Abs( y1 - y0 ) < Math.Abs( x1 - x0 ) )
		{
			var distance = x0 - x1;
			if ( distance > 0 && (distance & 1) != 0 )
				distance++;
			var end = x0 + distance / 2 * -2;
			x1 = end;
			var step = end < x0 ? -2 : end <= x0 ? 0 : 2;
			var x = x0;
			var guard = 0;
			if ( x != end )
			{
				do
				{
					if ( InMap( x, y0 ) )
					{
						var result = Step( op, At( x, y0 ), step, 0 );
						firstStep = false;
						if ( !result )
						{
							firstStep = false;
							return false;
						}
					}
					x += step;
					if ( ++guard > StepLimit )
						throw new FenceFaultException( "The fence line never reaches its end." );
				}
				while ( x != end );
			}
			lastStep = true;
			if ( InMap( x, y0 ) )
			{
				var result = Step( op, At( x, y0 ), step, 0 );
				firstStep = false;
				if ( result )
				{
					firstStep = false;
					lastStep = false;
					return true;
				}
				lastStep = false;
				return false;
			}
			return true;
		}
		else
		{
			var distance = y0 - y1;
			if ( distance > 0 && (distance & 1) != 0 )
				distance++;
			var end = y0 + distance / 2 * -2;
			y1 = end;
			var step = end < y0 ? -2 : end <= y0 ? 0 : 2;
			var y = y0;
			var guard = 0;
			if ( y != end )
			{
				do
				{
					if ( InMap( x0, y ) )
					{
						var result = Step( op, At( x0, y ), 0, step );
						firstStep = false;
						if ( !result )
							return false;
					}
					y += step;
					if ( ++guard > StepLimit )
						throw new FenceFaultException( "The fence line never reaches its end." );
				}
				while ( y != end );
			}
			lastStep = true;
			if ( InMap( x0, y ) )
			{
				var result = Step( op, At( x0, y ), 0, step );
				firstStep = false;
				lastStep = false;
				if ( !result )
					return false;
			}
			return true;
		}
	}

	/// <summary>0x5370E0 without its hover flag: the op on the structure record <paramref name="index"/> with the step (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
	private bool Step( int op, int index, int dx, int dy )
	{
		switch ( op )
		{
			case 0x80:
				LinkBack( index, dx, dy );
				return true;
			case 0x81:
				return Finish( index );
			case 0x82:
				SetDirection( index, dx, dy );
				return true;
			case 0x85:
				MarkWater( index );
				return true;
			case 0x86:
				// the pass changes nothing in the fence mode: its work is for the track tiles outside it
				return true;
			default:
				segmentEnd = false;
				var type = TypeOf( index );
				if ( (lastStep && type is 0xD or 0x12 or 0xB or 0x10) || type == 0x19 )
					segmentEnd = true;
				if ( !Place( index, op ) )
					return op == 0x19;
				lastOp = op;
				return true;
		}
	}

	private bool IsFenceStructure( int index, bool trackToo )
	{
		var type = TypeOf( index );
		return type == 0x19 || (trackToo && type is 0xB or 0xD or 0x10 or 0x12);
	}

	/// <summary>
	/// 0x539220 (op 0x80): after a post (or a track piece) the post links to the cell it came from, when that is a post too (or a track piece after a track piece), both links below four and the other's owner free.
	/// The first cell of a line comes from the previous line's last step.
	/// </summary>
	private void LinkBack( int index, int dx0, int dy0 )
	{
		var (dx, dy) = (dx0, dy0);
		if ( firstStep )
			(dx, dy) = previousDx == 0x80 || previousDy == 0x80 ? (0, 0) : (previousDx, previousDy);
		var trackToo = lastOp is 0xB or 0xD or 0x10 or 0x12;
		if ( lastOp == 0x19 || trackToo )
		{
			void TryLink( int direction, int ownBit, int otherBit )
			{
				var other = Neighbour( index, direction, 2 );
				if ( other < 0 || !IsFenceStructure( other, trackToo ) || PopAll( LinksOf( index ) ) >= 4 || PopAll( LinksOf( other ) ) >= 4 || (OwnerOf( other ) != 0 && OwnerOf( other ) != ToolOwner) )
					return;
				layer.Links[index] |= (byte)ownBit;
				layer.Links[other] |= (byte)otherBit;
			}
			if ( dy == 2 )
				TryLink( North, 1, 0x10 );
			if ( dx == -2 )
				TryLink( East, 4, 0x40 );
			if ( dy == -2 )
				TryLink( South, 0x10, 1 );
			if ( dx == 2 )
				TryLink( West, 0x40, 4 );
		}
		previousDx = dx0;
		previousDy = dy0;
	}

	/// <summary>0x539670 (op 0x82): the record's walk direction (+0xD) from the step; a record with three or more links keeps the bits it had.</summary>
	private void SetDirection( int index, int dx, int dy )
	{
		var type = TypeOf( index );
		if ( type is 0xB or 0xC or 0x10 or 0x11 || CellType( index ) == 0x15 )
			return;
		var current = DirectionOf( index );
		if ( PopCardinal( LinksOf( index ) ) < 3 )
		{
			if ( dx == -2 )
				SetDirectionOf( index, 0x40 );
			if ( dx == 2 )
				SetDirectionOf( index, 4 );
			if ( dy == -2 )
				SetDirectionOf( index, 1 );
			if ( dy == 2 )
				SetDirectionOf( index, 0x10 );
		}
		else
		{
			if ( dx == -2 )
				SetDirectionOf( index, current | 0x40 );
			if ( dx == 2 )
				SetDirectionOf( index, current | 4 );
			if ( dy == -2 )
				SetDirectionOf( index, current | 1 );
			if ( dy == 2 )
				SetDirectionOf( index, current | 0x10 );
		}
	}

	private static bool IsWet( int cellType ) => cellType is 2 or 7 or 0x1E;

	/// <summary>0x5370E0 op 0x85 (0x538A97..): writes the flag word of a fence post, or of a track record (bridge bits), when water or no terrain lies under its cells; a record at a corner or a junction is skipped. A record not at a corner or a junction takes the flag word 1 when one of the four cells of its post lies on water or without terrain.</summary>
	private void MarkWater( int index )
	{
		if ( TypeOf( index ) == 0x19 )
		{
			if ( IsCorner( index ) || (PopCardinal( LinksOf( index ) ) >= 3 && LinksOf( index ) != 0) )
				return;
			if ( IsWet( CellType( index ) ) )
				SetFlagsOf( index, 1 );
			foreach ( var direction in new[] { South, East, 8 } )
			{
				var other = CellNeighbour( index, direction );
				if ( other >= 0 && IsWet( CellType( other ) ) )
					SetFlagsOf( index, 1 );
			}
			return;
		}
		if ( TypeOf( index ) is 0xB or 0xC or 0x10 or 0x11 )
		{
			// the extension and origin records of a track tile take the same branch in the original; their records belong to the track layer, which this op leaves as it is
		}
		if ( IsCorner( index ) || (PopCardinal( LinksOf( index ) ) >= 3 && LinksOf( index ) != 0) )
			return;
		var type = CellType( index );
		if ( type is 1 or 3 )
			SetFlagsOf( index, 1 );
		var empty = type == 0 ? 1 : 0;
		foreach ( var direction in new[] { South, East, 8 } )
		{
			var other = CellNeighbour( index, direction );
			if ( other < 0 )
				continue;
			type = CellType( other );
			if ( type is 1 or 3 )
				SetFlagsOf( index, 1 );
			if ( type == 0 )
				empty++;
		}
		if ( empty == 4 )
			SetFlagsOf( index, FlagsOf( index ) & ~0xF );
	}

	/// <summary>
	/// 0x5370E0 op 0x81 on a post: which of the four cells of its 2 x 2 block the park owns, from the shape of the fence there. A cell becomes owned (flag 0x40 cleared) or unowned (the post's own flag
	/// word with 0x40 set, copied whole) by the direction the walk gave the post (+0xD), the links and whether it is a gate post.
	/// </summary>
	private bool Finish( int index )
	{
		var type = TypeOf( index );
		if ( type is 0xB or 0xC or 0x10 or 0x11 || CellType( index ) == 0x15 )
			return true;
		if ( type == 0x19 )
		{
			bool owned = false, east = false, south = false, southEast = false;
			var direction = (byte)DirectionOf( index );
			var links = LinksOf( index );
			var special = (direction == 1 && links == 0x41) || (direction == 0x10 && links == 0x14) || (direction == 4 && links == 5) || (direction == 0x40 && (links == 0x50 || links == 0x14))
				|| (direction == 1 && links == 0x50) || (direction == 4 && links == 0x41) || (direction == 0x10 && links == 5);
			var (x, y) = (X( index ), Y( index ));
			var gate = (x == PostA.X && y == PostA.Y) || (x == PostB.X && y == PostB.Y);
			var straight = IsStraight( index );
			var corner = IsCorner( index );
			switch ( direction )
			{
				case 1:
					if ( straight || gate )
					{
						east = true;
						southEast = true;
					}
					else if ( corner )
					{
						if ( special )
							owned = east = south = southEast = true;
						else
							east = true;
					}
					break;
				case 4:
					if ( straight || gate )
					{
						south = true;
						southEast = true;
					}
					else if ( corner )
					{
						southEast = true;
						if ( special )
							owned = east = south = true;
					}
					break;
				case 0x10:
					if ( straight || gate )
					{
						owned = true;
						south = true;
					}
					else if ( corner )
					{
						if ( special )
							owned = east = south = southEast = true;
						else
							south = true;
					}
					break;
				case 0x40:
					if ( straight || gate )
					{
						owned = true;
						east = true;
					}
					else if ( corner )
					{
						owned = true;
						if ( special )
							east = south = southEast = true;
					}
					break;
			}
			SetCellFlags( index, owned ? CellFlags( index ) & ~0x40 : CellFlags( index ) | 0x40 );
			foreach ( var (flag, offset) in new[] { (east, East), (south, South), (southEast, 8) } )
			{
				var other = CellNeighbour( index, offset );
				if ( other < 0 )
					continue;
				SetCellFlags( other, flag ? CellFlags( other ) & ~0x40 : CellFlags( index ) | 0x40 );
			}
		}
		else if ( IsCorner( index ) || (PopCardinal( LinksOf( index ) ) >= 3 && LinksOf( index ) != 0) )
			SetFlagsOf( index, FlagsOf( index ) & ~0xF );
		lastOp = 0;
		if ( lastStep && segmentEnd )
		{
			segmentEnd = false;
			return false;
		}
		return true;
	}

	// ---------------------------------------------------------------- the walks

	private static bool AdvanceTo( ref int x, ref int y, int heading )
	{
		switch ( heading )
		{
			case 1:
				y -= 2;
				return true;
			case 4:
				x += 2;
				return true;
			case 0x10:
				y += 2;
				return true;
			case 0x40:
				x -= 2;
				return true;
		}
		return false;
	}

	/// <summary>
	/// 0x531040: walks the fence from post A heading east and sets (<paramref name="on"/>) or clears the fill's mark 0x20 on every record it stands on. At a corner the walk turns to the other link, at any other post it
	/// keeps its heading; it stops when the cell two steps on holds no post, or at a post with one link.
	/// </summary>
	public void MarkFence( bool on )
	{
		var (x, y) = PostA;
		var heading = East;
		var steps = 0;
		for ( var guard = 0; ; guard++ )
		{
			if ( guard > StepLimit )
				throw new FenceFaultException( "The fence mark walk never stops." );
			var index = At( x, y );
			Mark( index, on );
			if ( index >= 0 && TypeOf( index ) == 0x19 && IsCorner( index ) )
				heading = LinksOf( index ) ^ Opposite( heading );
			var done = false;
			var ahead = index < 0 ? -1 : Neighbour( index, heading, 2 );
			if ( ahead >= 0 && TypeOf( ahead ) != 0x19 )
				done = true;
			if ( index >= 0 && PopAll( LinksOf( index ) ) == 1 && steps != 0 )
				done = true;
			if ( !done )
				AdvanceTo( ref x, ref y, heading );
			steps++;
			if ( done )
				return;
		}
	}

	/// <summary>The four cells (0,0), (0,1), (1,1), (1,0) of the post block at a record: when any has the unowned flag all of them lose it (0x5311C0).</summary>
	private void ClearBlock( int index )
	{
		var block = new[] { index, CellNeighbour( index, South ), CellNeighbour( index, 8 ), CellNeighbour( index, East ) };
		// 0x5311C0 reads and clears the four cells without a test of 0x4D96F0's null for the cell beyond the map's edge: tp.exe faults there
		if ( Array.Exists( block, cell => cell < 0 ) )
			throw new FenceFaultException( "The fence fill reaches the cell beyond the map's edge." );
		var any = false;
		foreach ( var cell in block )
			any |= (CellFlags( cell ) & 0x40) != 0;
		if ( !any )
			return;
		foreach ( var cell in block )
			SetCellFlags( cell, CellFlags( cell ) & ~0x40 );
	}

	/// <summary>
	/// One scan of the fill: from (<paramref name="x"/>, <paramref name="y"/>) in steps of (<paramref name="dx"/>, <paramref name="dy"/>) it clears the unowned flag on the block of every unmarked record until it meets a marked one;
	/// the scan towards the top of the map (<paramref name="limited"/>) also ends when the next step would leave the map's first row, and the flag <paramref name="edge"/> that records it stays set for every later scan of the fill (0x5311C0 never clears it).
	/// </summary>
	private void Scan( int x, int y, int dx, int dy, bool limited, ref bool edge )
	{
		for ( var guard = 0; ; guard++ )
		{
			if ( guard > StepLimit )
				throw new FenceFaultException( "The fence fill scan never meets a mark." );
			var record = At( x, y );
			var marked = Marked( record );
			if ( !marked && record >= 0 )
				ClearBlock( record );
			if ( limited && y + dy < 0 )
				edge = true;
			if ( marked || (limited && edge) )
				return;
			x += dx;
			y += dy;
		}
	}

	/// <summary>
	/// 0x5311C0: the fill. From post A heading east it walks the marked fence (as <see cref="MarkFence"/> does) and from every post scans to the right of the heading, two cells at a time, clearing the unowned flag on the
	/// 2 x 2 cells of each block until it meets a marked post (a scan to the left of the map ends at its edge); a last pass removes every post the walk did not mark.
	/// </summary>
	public void Fill()
	{
		var (x, y) = PostA;
		var heading = East;
		var steps = 0;
		var edge = false;
		for ( var guard = 0; ; guard++ )
		{
			if ( guard > StepLimit )
				throw new FenceFaultException( "The fence fill walk never stops." );
			var index = At( x, y );
			if ( index >= 0 && TypeOf( index ) == 0x19 )
			{
				switch ( heading )
				{
					case 1:
						Scan( x + 2, y, 2, 0, limited: false, ref edge );
						break;
					case 4:
						Scan( x, y + 2, 0, 2, limited: false, ref edge );
						break;
					case 0x10:
						Scan( x - 2, y, -2, 0, limited: false, ref edge );
						break;
					case 0x40:
						Scan( x, y - 2, 0, -2, limited: true, ref edge );
						break;
				}
				if ( IsCorner( index ) )
					heading = LinksOf( index ) ^ Opposite( heading );
			}
			var done = false;
			var ahead = index < 0 ? -1 : Neighbour( index, heading, 2 );
			if ( ahead >= 0 && TypeOf( ahead ) != 0x19 )
				done = true;
			if ( index >= 0 && PopAll( LinksOf( index ) ) == 1 && steps != 0 )
				done = true;
			if ( !done )
				AdvanceTo( ref x, ref y, heading );
			steps++;
			if ( done )
				break;
		}
		// the last pass: every post the walk did not mark goes (the posts of a branch the fence walk did not take, or of a stretch the purchase laid in vain)
		for ( var row = 0; row < height - 1; row++ )
		{
			for ( var column = 0; column < width - 1; column++ )
			{
				var record = At( column, row );
				if ( TypeOf( record ) is 0xC or 0x11 )
				{
					var parent = OwnerOf( record );
					record = parent == 0 ? -1 : parent - 1;
				}
				if ( record < 0 || TypeOf( record ) != 0x19 || Marked( record ) )
					continue;
				if ( CellType( record ) == 0x15 )
					ClearStructure( record );
				else
					ClearPost( record );
				SetCounterOf( record, 0 );
				ClearBlock( record, force: true );
			}
		}
	}

	private void ClearBlock( int index, bool force )
	{
		var block = new[] { index, CellNeighbour( index, South ), CellNeighbour( index, 8 ), CellNeighbour( index, East ) };
		if ( Array.Exists( block, cell => cell < 0 ) )
			throw new FenceFaultException( "The fence fill reaches the cell beyond the map's edge." );
		foreach ( var cell in block )
			SetCellFlags( cell, CellFlags( cell ) & ~0x40 );
	}

	/// <summary>0x53BB20: a structure record on a coaster marker cell and the three cells next to it go back to nothing.</summary>
	private void ClearStructure( int index )
	{
		layer.Links[index] = 0;
		SetDirectionOf( index, 0 );
		SetFlagsOf( index, 0 );
		Place( index, 0 );
		SetOwnerOf( index, 0 );
		foreach ( var direction in new[] { South, East, 8 } )
		{
			var other = Neighbour( index, direction, 1 );
			if ( other < 0 )
				continue;
			layer.Links[other] = 0;
			SetDirectionOf( other, 0 );
			SetFlagsOf( other, 0 );
			Place( other, 0 );
			SetOwnerOf( other, 0 );
		}
	}

	/// <summary>
	/// 0x53B280(post, 0, 0, 0): a post is forced out: each of its links goes, with the link back at the post two cells away (whose walk direction is straightened when it is left with one link), the record is
	/// emptied and the three cells next to it that name it as their owner go back to nothing.
	/// </summary>
	private void ClearPost( int index )
	{
		SetCounterOf( index, 0 );
		for ( var bit = 1; ; bit = Rotate( bit, 4 ) )
		{
			if ( (bit & LinksOf( index )) != 0 )
			{
				layer.Links[index] &= (byte)~bit;
				var other = Neighbour( index, bit, 2 );
				if ( other >= 0 )
				{
					layer.Links[other] &= (byte)~Opposite( bit );
					if ( PopAll( LinksOf( other ) ) == 1 )
					{
						var links = LinksOf( other );
						if ( DirectionOf( other ) != links && DirectionOf( other ) != Opposite( links ) )
							SetDirectionOf( other, Opposite( links ) );
					}
				}
			}
			if ( Rotate( bit, 4 ) == 1 )
				break;
		}
		SetDirectionOf( index, 0 );
		SetFlagsOf( index, 0 );
		Place( index, 0 );
		SetOwnerOf( index, 0 );
		var self = index + 1;
		foreach ( var direction in new[] { South, East, 8 } )
		{
			var other = Neighbour( index, direction, 1 );
			if ( other < 0 || OwnerOf( other ) != self )
				continue;
			layer.Links[other] = 0;
			SetDirectionOf( other, 0 );
			SetFlagsOf( other, 0 );
			Place( other, 0 );
			SetCounterOf( other, 0 );
			SetOwnerOf( other, 0 );
		}
	}

	/// <summary>
	/// 0x52BA50: walks the fence from (<paramref name="x"/>, <paramref name="y"/>) with the heading <paramref name="heading"/> until it stands on post B. A corner turns the walk to the link it did not come by; at a junction
	/// (three or more links) the walk takes the first link counting from its left (left, ahead, right) and the record keeps only the link it came by and that one (or, going ahead, only the line); the walk
	/// gives each post its direction (+0xD, finishing it with op 0x81 when <paramref name="rewrite"/>) and types the three cells next to it as its extension.
	/// </summary>
	public void Walk( int x, int y, int heading, bool rewrite )
	{
		var done = false;
		var heading_ = heading;
		for ( var guard = 0; ; guard++ )
		{
			if ( guard > StepLimit )
				throw new FenceFaultException( "The fence walk never reaches post B." );
			var index = At( x, y );
			if ( index >= 0 && (TypeOf( index ) == 0x19 || PopAll( LinksOf( index ) ) != 0) )
			{
				if ( IsCorner( index ) )
				{
					var back = Opposite( heading_ );
					if ( (back & LinksOf( index )) == 0 )
						back = heading_;
					heading_ = LinksOf( index ) ^ back;
				}
				if ( IsJunction( index ) )
				{
					var found = 0;
					var candidate = Rotate( heading_, 0x40 );
					do
					{
						if ( (candidate & LinksOf( index )) != 0 )
							found = candidate;
						candidate = Rotate( candidate, 4 );
					}
					while ( found == 0 );
					int clear;
					if ( heading_ == found )
					{
						clear = ~(Opposite( heading_ ) | heading_) & 0xFF;
					}
					else
					{
						SetDirectionOf( index, found );
						layer.Links[index] &= (byte)~heading_;
						clear = Opposite( found );
					}
					layer.Links[index] &= (byte)~clear;
					Finish( index );
					heading_ = found;
				}
			}
			else
				heading_ = 0;
			if ( x == PostB.X && y == PostB.Y )
				done = true;
			if ( rewrite && index >= 0 && DirectionOf( index ) != heading_ )
			{
				SetDirectionOf( index, heading_ );
				Finish( index );
			}
			if ( index >= 0 )
			{
				foreach ( var direction in new[] { South, East, 8 } )
				{
					var other = Neighbour( index, direction, 1 );
					if ( other < 0 )
						continue;
					SetOwnerOf( other, index + 1 );
					Place( other, 0xC );
				}
			}
			if ( !AdvanceTo( ref x, ref y, heading_ ) && !done )
				throw new FenceFaultException( "The fence walk stands on a cell without a post." );
			if ( done )
				return;
		}
	}

	// ---------------------------------------------------------------- the flows

	/// <summary>The world init's ownership (0x536490): the unowned flag 0x40 on every cell without the fixed flag 0x20, before the fence is laid.</summary>
	public void InitialiseOwnership()
	{
		for ( var index = 0; index < width * height; index++ )
		{
			var flags = CellFlags( index );
			if ( (flags & 0x60) == 0 )
				SetCellFlags( index, flags | 0x40 );
		}
	}

	/// <summary>
	/// The park fence of a new level (0x5323F0 after its set-up of the gate posts): the line tool lays the click list from post A, the fence is marked, filled and unmarked, the walk from post A types the
	/// posts' cells, and the strip in front of the gate (the two rows from post A's row, between the posts) becomes owned.
	/// </summary>
	// [BIN:TPW-EXE:0x0053259D gate strip] the cells (x, A.y) and (x, A.y + 1) for every x from post B + 2 up to post A - 1 lose the unowned flag after the walk
	public void LayInitialFence( IEnumerable<(int X, int Y)> clicks )
	{
		ArgumentNullException.ThrowIfNull( clicks );
		startX = startY = -1;
		toolActive = true;
		foreach ( var (x, y) in clicks )
			Click( PostA.X + x, PostA.Y + y );
		MarkFence( true );
		Fill();
		MarkFence( false );
		Walk( PostA.X, PostA.Y, East, rewrite: true );
		toolActive = false;
		previousDx = previousDy = 0x80;
		for ( var x = PostB.X + 2; x < PostA.X; x++ )
		{
			foreach ( var row in new[] { PostA.Y, PostA.Y + 1 } )
			{
				var index = At( x, row );
				if ( index >= 0 )
					SetCellFlags( index, CellFlags( index ) & ~0x40 );
			}
		}
	}

	/// <summary>
	/// The Buy Land purchase of the rectangle between two fence posts (0x5327E0): the line tool lays its four sides and the closing click, the walk from post A rewrites the fence, and the fill runs again.
	/// </summary>
	public void LayPurchase( int x0, int y0, int x1, int y1 )
	{
		var (xmin, xmax) = (Math.Min( x0, x1 ), Math.Max( x0, x1 ));
		var (ymin, ymax) = (Math.Min( y0, y1 ), Math.Max( y0, y1 ));
		startX = startY = -1;
		toolActive = true;
		Click( xmin, ymin );
		Click( xmax, ymin );
		Click( xmax, ymax );
		Click( xmin, ymax );
		Click( xmin, ymin );
		toolActive = false;
		previousDx = previousDy = 0x80;
		Walk( PostA.X, PostA.Y, East, rewrite: true );
		MarkFence( true );
		Fill();
		MarkFence( false );
	}

	/// <summary>Writes the buffered cell flags, track records and block stamps to the cell map.</summary>
	public void Commit()
	{
		foreach ( var (index, value) in pendingFlags )
		{
			var (x, y) = (X( index ), Y( index ));
			if ( (ushort)cells.FlagsAt( x, y ) != value )
				cells.SetFlags( x, y, (ParkCellFlags)value );
		}
		foreach ( var (index, track) in pendingTracks )
		{
			var (x, y) = (X( index ), Y( index ));
			var before = cells.Tracks.Get( x, y );
			if ( before.Type == 0 && track.Type == 0 )
				continue;
			if ( track.Type == 0 )
				cells.Tracks.Clear( x, y );
			else if ( !before.Equals( track ) )
				cells.Tracks.Set( x, y, track );
		}
		foreach ( var index in pendingStamps )
			cells.StampBlockAt( X( index ), Y( index ) );
		pendingFlags.Clear();
		pendingTracks.Clear();
		pendingStamps.Clear();
	}
}
