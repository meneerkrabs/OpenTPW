namespace OpenTPW;

/// <summary>What the queue tool's corner walk leaves: its stack of points, the value it returns and the line start it sets.</summary>
/// <param name="Stack">The points the walk pushed, in order (<see cref="QueueCornerWalk.StackCapacity"/> at most).</param>
/// <param name="Value">The returned byte: the walk's step when it ended on a cell that is no queue cell, else 0 (the callers never read it).</param>
/// <param name="Start">The line start the walk leaves for the queue tool (the last cell it reached).</param>
/// <param name="EndedOnPath">The walk stepped onto a path cell, so (with the writes) the link between that cell and the one before it went.</param>
internal sealed record CornerWalkResult( IReadOnlyList<(int X, int Y)> Stack, byte Value, (int X, int Y) Start, bool EndedOnPath = false );

/// <summary>
/// The queue tool's corner walk, tp.exe <c>0x00530120</c>, ported from the executable: it reads the ride's access cell (the entrance cell, thing <c>+0x36</c>) and the cells' own type, link byte,
/// queue link and owner, pushes the queue's corner points on the tool's stack and writes the placement counters and the link clear the walk leaves behind (ORACLE.md, X44).
/// </summary>
internal static class QueueCornerWalk
{
	/// <summary>Most points the stack keeps: the original stores a point while its count is at most 0x400, one past its 0x400-point array.</summary>
	public const int StackCapacity = 0x401;

	/// <summary>More cell visits than a 128 x 128 map holds: a walk past this never ends in the original either (a corner whose links lack the way back loops on its cell).</summary>
	private const int VisitGuard = 4 * 128 * 128;

	private static (int X, int Y) Delta( byte step ) => step switch { 1 => (0, -1), 4 => (1, 0), 0x10 => (0, 1), _ => (-1, 0) };

	private static bool IsStep( byte step ) => step is 1 or 4 or 0x10 or 0x40;

	/// <summary>A queue cell or an entrance cell (<c>0x536320</c>: type 3 or 9).</summary>
	private static bool IsQueueish( ParkCellMap cells, int x, int y ) => cells.RawTypeAt( x, y ) is (byte)ParkCellType.Queue or ParkCellTypes.Entrance;

	private static byte Reverse( byte link ) => link == 0 ? (byte)0 : link < 0x10 ? (byte)(link << 4) : (byte)(link >> 4);

	/// <summary>A cell with exactly two cardinal links that are not opposite (<c>0x53AE00</c>).</summary>
	internal static bool IsCorner( byte links ) =>
		(((links & 1) != 0 ? 1 : 0) + ((links & 4) != 0 ? 1 : 0) + ((links & 0x10) != 0 ? 1 : 0) + ((links & 0x40) != 0 ? 1 : 0)) == 2
		&& (links & (1 | 0x10)) != 0 && (links & (4 | 0x40)) != 0;

	/// <summary>The turn <c>0x4D8C20</c> makes: the rotation's link bit shifted left by the bit index of the queue link, wrapping past 0x80.</summary>
	private static byte Turn( byte queueLink, byte rotationBit )
	{
		var shift = 0;
		for ( var link = queueLink; link > 1; link >>= 1 )
			shift++;
		var shifted = rotationBit << shift;
		return (byte)(shifted > 0x80 ? shifted >> 8 : shifted);
	}

	/// <summary>
	/// Walks the queue of the ride whose access cell is <paramref name="entrance"/> (the thing's rotation <paramref name="rotation"/>). With <paramref name="write"/> the walk makes the writes the
	/// executable makes: the placement counter (<c>+0x20</c>) of every queue cell it visits (1 on a corner, 0 elsewhere, the entrance cell included), and, when it ends on a path cell, the link bit toward
	/// that path cell off the cell before it (unless that is the entrance) and the link bit back off the path cell; <paramref name="refreshed"/> reports each cell whose tile the executable then refreshes.
	/// </summary>
	// [BIN:TPW-EXE:0x00530120 corner walk] the stack count is reset (0x530150); the entrance's link byte (0x522770) names the first step: for the bits 1, 4, 0x10 and 0x40 the neighbour (0x4D96F0, null off the map) is pushed first when it is no queue cell (0x536320: 0x5304C0, 0x5304EC) or one of this ride (owner word +0x10 equal, 0x5301F5), and a neighbour that is the queue of another ride ends the walk with that neighbour as its only point (0x530215, 0x53048D); then each cell the walk stands on: a queue cell takes the counter 1 when it is a corner (0x53AE00, which also turns the step to link byte xor the reverse of the step, 0x530291..0x53029A, and pushes the cell, 0x5302AA) and 0 otherwise (0x5302C9), a cell that is no queue cell ends the walk (0x5302D4, return value = the step), an entrance cell is passed (0x5302EA), a cell with one link ends it (0x5302F2..0x5302FC)
	// [BIN:TPW-EXE:0x00530300 path end] a type-1 cell ends the walk on the cell before it (the step reversed, 0x530338..0x530341); that cell loses the link bit along the step (0x522730 at 0x530380, 0x5365D0) unless it is the entrance (0x536340 at 0x530366), which leaves the path cell itself as the last point, and the path cell loses the reverse bit (0x53039C..0x5303A3)
	// [BIN:TPW-EXE:0x005303F1 no step] an entrance without a link byte takes the cell its queue link (+0xD) turned by the rotation's bit (0x4D8C20; 0 → 1, 90 → 4, 180 → 0x10, 270 → 0x40) names; the last point is pushed at 0x530494 and is the line start (0x81EDE4, 0x81EDE8)
	public static CornerWalkResult Run( ParkCellMap cells, (int X, int Y) entrance, int rotation, bool write, Action<int, int>? refreshed = null )
	{
		var stack = new List<(int X, int Y)>();
		void Push( int px, int py )
		{
			if ( stack.Count < StackCapacity )
				stack.Add( (px, py) );
		}
		var map = new WalkCells( cells );
		var (x, y) = entrance;
		var step = map.Links( x, y );
		byte value = 0;
		var ended = false;
		var stopped = false;
		var onPath = false;
		if ( IsStep( step ) )
		{
			var (dx, dy) = Delta( step );
			var (nx, ny) = (x + dx, y + dy);
			// the neighbour lookup 0x4D96F0 is null off the map
			if ( cells.InBounds( nx, ny ) )
			{
				if ( !map.IsQueueish( nx, ny ) || map.Owner( nx, ny ) == map.Owner( x, y ) )
					Push( nx, ny );
				else
				{
					(x, y) = (nx, ny);
					stopped = true;
				}
			}
		}
		if ( !stopped && (step == 0 || !cells.InBounds( x, y )) )
		{
			var rotationBit = rotation switch { 0 => (byte)1, 90 => (byte)4, 180 => (byte)0x10, 270 => (byte)0x40, _ => (byte)0 };
			var turned = Turn( map.QueueLink( x, y ), rotationBit );
			if ( IsStep( turned ) )
			{
				var (dx, dy) = Delta( turned );
				(x, y) = (x + dx, y + dy);
			}
			stopped = true;
		}
		for ( var visits = 0; !stopped && visits < VisitGuard; visits++ )
		{
			if ( map.IsQueueish( x, y ) )
			{
				var links = map.Links( x, y );
				if ( IsCorner( links ) )
				{
					step = (byte)(links ^ Reverse( step ));
					Push( x, y );
					if ( write )
						map.SetCounter( x, y, 1 );
				}
				else if ( write )
					map.SetCounter( x, y, 0 );
			}
			else
			{
				ended = true;
				value = step;
			}
			if ( map.Type( x, y ) != ParkCellTypes.Entrance )
			{
				if ( System.Numerics.BitOperations.PopCount( map.Links( x, y ) ) == 1 )
					ended = true;
				if ( map.Type( x, y ) == (byte)ParkCellType.Path )
				{
					onPath = true;
					var (cx, cy) = (x, y);
					var (bx, by) = Delta( Reverse( step ) );
					(x, y) = (x + bx, y + by);
					if ( map.Type( x, y ) == ParkCellTypes.Entrance )
						(x, y) = (cx, cy);
					else if ( write )
					{
						map.SetLinks( x, y, (byte)(map.Links( x, y ) & ~step) );
						refreshed?.Invoke( x, y );
					}
					if ( write )
					{
						map.SetLinks( cx, cy, (byte)(map.Links( cx, cy ) & ~Reverse( step )) );
						refreshed?.Invoke( cx, cy );
					}
					ended = true;
				}
			}
			if ( ended || !IsStep( step ) )
				break;
			var (fx, fy) = Delta( step );
			(x, y) = (x + fx, y + fy);
		}
		Push( x, y );
		return new CornerWalkResult( stack, value, (x, y), onPath );
	}

	/// <summary>
	/// The cells as the walk reads them: the original finds a cell by its word (<c>y × 128 + x + 1</c>, 16 bits), so a step off the map reads the cell the word names (the previous or next row's end on a 128-wide
	/// map) and, past the array, an empty one.
	/// </summary>
	// [BIN:TPW-EXE:0x0053024A cell lookup] the loop's cell is the word (edi << 7) + esi + 1 & 0xFFFF (0x53024A..0x53025E) with no bounds test after the first cell (0x530238)
	private readonly struct WalkCells
	{
		private readonly ParkCellMap cells;

		public WalkCells( ParkCellMap cells ) => this.cells = cells;

		private (int X, int Y)? Find( int x, int y )
		{
			if ( cells.InBounds( x, y ) )
				return (x, y);
			if ( cells.Width != 128 )
				return null;
			var index = (unchecked((ushort)((y << 7) + x + 1))) - 1;
			return index >= 0 && index < cells.Width * cells.Height ? (index & 0x7F, index >> 7) : null;
		}

		public byte Type( int x, int y ) => Find( x, y ) is { } at ? cells.RawTypeAt( at.X, at.Y ) : (byte)0;
		public bool IsQueueish( int x, int y ) => Type( x, y ) is (byte)ParkCellType.Queue or ParkCellTypes.Entrance;
		public byte Links( int x, int y ) => Find( x, y ) is { } at ? cells.LinksAt( at.X, at.Y ) : (byte)0;
		public byte QueueLink( int x, int y ) => Find( x, y ) is { } at ? cells.QueueLinkAt( at.X, at.Y ) : (byte)0;
		public ushort Owner( int x, int y ) => Find( x, y ) is { } at ? cells.OwnerAt( at.X, at.Y ) : (ushort)0;
		public void SetCounter( int x, int y, short value )
		{
			if ( Find( x, y ) is { } at )
				cells.SetPlacementCount( at.X, at.Y, value );
		}
		public void SetLinks( int x, int y, byte value )
		{
			if ( Find( x, y ) is { } at )
				cells.SetLinks( at.X, at.Y, value );
		}
	}
}
