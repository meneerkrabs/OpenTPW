namespace OpenTPW;

/// <summary>
/// Theme Park Inc's staff route and wander and patrol destination (docs/reverse/TPI-NAVIGATOR.md, NV3 and NV4) on a staff member's walk: the staff route <c>0x62EB10</c>, the destination <c>0x609F70</c>
/// for the staff kinds (W1-W4 against Theme Park World's <c>0x004F9490</c>), the patrol rectangle test <c>0x62C610</c> and the fallback <c>0x62C720</c>. <see cref="ThemeParkIncStaffWorld"/> runs them
/// with the member's walk, his patrol rectangle (+0x24C..+0x258), the world generator and the park's open flag.
/// </summary>
internal static class ThemeParkIncPatrol
{
	/// <summary>
	/// The staff route (<c>0x62EB10</c>): a closed park refuses a walk to the entrance cells (47, 17) and (48, 17); else the walk mask is 1 when the member's cell or the target cell is empty (type 0),
	/// 0 otherwise, and the walk is set to the point (<c>0x60B8E0</c>). False when no route reaches it.
	/// </summary>
	// [BIN:TPI-EXE:0x62EB10 staff route] park+0x1EAB44 (0x64FA30, nonzero: the park is closed) with the target word's cell x (its high byte) 0x2F or 0x30 and the y word's cell 0x11 answers 0 and leaves the
	// walk; the member's cell record (+0x418 + 72 × (y << 7 + x + 1), from the bytes +5 and +7) of type 0 (0x67F6D0, its dword +8) or else the target's sets +0x1A0 = 1, else +0x1A0 = 0 (the walk's +0xB4,
	// the mask the search and the edge test read); then 0x60B8E0 with the two words
	// [BIN:TPI-EXE:0x60B8E0 staff steer] the stamp counter raised (0x5D81F0, ParkCellMap.NextStamp: counted with Theme Park Inc's navigator, else only the edits count); the stranded mark +0x1B0 is tested (0x60BB50) only while nonzero,
	// and a staff member's never is: its only setter with a stamp, 0x60A4AE in the wander, is reached by kind 1 (the guests) alone, the staff kinds 4, 5, 6, 8, 7 and 0x14 branching to 0x62C720 at
	// 0x60A454, and every other writer stores 0 (0x608F94, 0x60A56C, 0x60A84C, 0x60B4CA, 0x60B9FC); then +0x28, +0x2A = the words, +0x1B0 = 0 and SetDest 0x63EE80 (0x63E3A0 with the point << 8)
	internal static bool StaffRoute( GuestPathGrid grid, GuestNavigation walk, bool parkOpen, (int X, int Y) cell, int byteX, int byteY )
	{
		if ( !parkOpen && cell.Y == 0x11 && cell.X is 0x2F or 0x30 )
			return false;
		var (x, y) = walk.Cell;
		walk.Mask = grid.Cells.RawTypeAt( x, y ) == 0 || grid.Cells.RawTypeAt( cell.X, cell.Y ) == 0 ? 1 : 0;
		// 0x60B8F4: the counter raised (ParkCellMap.NextStamp); the mark it then tests is 0
		grid.Cells.NextStamp();
		return Route( grid, walk, cell, byteX, byteY );
	}

	/// <summary>Sets the walk to a point of a cell with the walk's mask (SetDest, <c>0x63EE80</c>); false when no route reaches it (the walk then stands arrived and stuck).</summary>
	internal static bool Route( GuestPathGrid grid, GuestNavigation walk, (int X, int Y) cell, int byteX, int byteY )
	{
		var (x, y) = GuestNavigation.BytePoint( cell, byteX, byteY );
		return walk.Navigate( grid.Cells, x, y );
	}

	/// <summary>
	/// The wander and patrol destination <c>0x609F70</c> for a staff member (TPI-NAVIGATOR.md, NV4): the entry gate, the step count, the patrol rectangle (a member outside it walks into it, or else
	/// wanders without it), the off-path search from a cell without links, else the random walk kept to the rectangle and the staff route to its point; a walk whose every candidate the rectangle or
	/// the other removals took walks into the rectangle instead. True when a walk is set.
	/// </summary>
	// [BIN:TPI-EXE:0x609FBE wander gate] the stamp counter raised (0x5D81F0, ParkCellMap.NextStamp); the mark +0x1B0 it tests is a staff member's, never set (StaffRoute), so the gate holds nothing and the
	// mark is cleared (0x60A004)
	// [BIN:TPI-EXE:0x60A010 step count] R % 5 (the world generator 0x6493E0) before the rectangle: the fallback's draws come after it
	// [BIN:TPI-EXE:0x60A052 staff kinds] the kinds 4, 5, 6, 8, 7 and 0x14 (W1: tp.exe's 0x004F9570 lacks the gardener 0x14): the start cell's word outside the rectangle (0x62C610, W3) tries 0x62C720,
	// whose success returns 1; its failure sets the flag [esp+0x2C] that keeps the rectangle out of the walk's removals
	// [BIN:TPI-EXE:0x60A09F links] the start cell's link count (0x65CBF0) below 1 takes the off-path search (0x60A5EF)
	// [BIN:TPI-EXE:0x60A1DD removal 1] a staff member with the flag clear drops each candidate whose word fails 0x62C610 (W3, the corners ordered); the drop that leaves the counter at 0 calls 0x62C720
	// at once (0x60A401), and so does a step with no candidate left (0x60A454): a walk to a cell of the rectangle; the steps and the other three removals are tp.exe's (GuestWander)
	// [BIN:TPI-EXE:0x60A547 staff final route] the bytes (0x60A3DA, 0x60A3EA: R & 0x7F clamped 5..123) and the staff route 0x62EB10 to (x << 8 | bx, y << 8 | by) (W2: tp.exe's staff take the guests'
	// gate and SetDest, 0x004F9983); its success sets +0xE8 = 1 (0x60A5D2), which no staff routine reads (the guests' state 7 at 0x61A9C7 does) and OpenTPW does not hold for staff (navigator plan NV5)
	internal static bool Destination( GuestPathGrid grid, GuestNavigation walk, (int X0, int Y0, int X1, int Y1) area, WorldRng world, bool parkOpen )
	{
		var cell = walk.Cell;
		grid.Cells.NextStamp();
		uint Draw() => world.NextWord();
		var remaining = Draw() % 5;
		var keepToArea = true;
		if ( !InArea( area, cell ) )
		{
			if ( Fallback( grid, walk, area, world, parkOpen ) )
				return true;
			keepToArea = false;
		}
		bool InRectangle( int x, int y ) => InArea( area, (x, y) );
		if ( (grid.Cells.LinksAt( cell.X, cell.Y ) & 0x55) == 0 )
		{
			// [BIN:TPI-EXE:0x60A6D4 off-path routes] the off-path search is the guests' (GuestWander.OffPath) for a staff member too: each candidate raises the counter (0x60A6FB, 0x60A83F), the gate
			// holds nothing, the mark is cleared and SetDest (0x63EE80) runs with the walk's own mask; no staff route (0x62EB10) and no park-closed refusal
			return GuestWander.OffPath( grid, cell, Draw, target =>
			{
				grid.Cells.NextStamp();
				return Route( grid, walk, target, GuestWander.CentreByte, GuestWander.CentreByte );
			} );
		}
		if ( GuestWander.PickAfterCount( grid, cell, remaining, Draw, keepToArea ? InRectangle : null, out var stranded ) is { } destination )
			return StaffRoute( grid, walk, parkOpen, destination.Cell, destination.ByteX, destination.ByteY );
		return stranded && Fallback( grid, walk, area, world, parkOpen );
	}

	/// <summary>The patrol rectangle test <c>0x62C610</c> of a cell: inside the rectangle, its corners ordered first (W3).</summary>
	internal static bool InArea( (int X0, int Y0, int X1, int Y1) area, (int X, int Y) cell ) => InArea( area, (cell.Y << 7) + cell.X + 1 );

	/// <summary>The patrol rectangle test <c>0x62C610</c> of a cell word: the word's cell inside the rectangle, its corners ordered first (W3).</summary>
	// [BIN:TPI-EXE:0x62C610 patrol rectangle] x = (w - 1) & 0x7F, y = (w - 1) >> 7 (unsigned) against +0x24C..+0x258 (x0, y0, x1, y1), each pair swapped when reversed (W3: tp.exe's 0x00506ED0 reads two
	// corner words and swaps nothing), inclusive, signed compares
	internal static bool InArea( (int X0, int Y0, int X1, int Y1) area, int word )
	{
		var w = unchecked( (uint)(ushort)word - 1 );
		var (x, y) = ((int)(w & 0x7F), unchecked( (int)(w >> 7) ));
		var (x0, y0, x1, y1) = area;
		if ( x0 > x1 )
			(x0, x1) = (x1, x0);
		if ( y0 > y1 )
			(y0, y1) = (y1, y0);
		return x >= x0 && x <= x1 && y >= y0 && y <= y1;
	}

	/// <summary>
	/// The patrol fallback <c>0x62C720</c>: a walk to a random path cell of the patrol rectangle, up to 30 tries of two world draws through the staff route. The corners are read as given (W4):
	/// reversed corners make the width or height zero or negative.
	/// </summary>
	// [BIN:TPI-EXE:0x62C720 patrol fallback] the corner words 0x4A63F0(x0, y0) and (x1, y1) ((y << 7) + x + 1); width = ((b - 1) & 0x7F) - ((a - 1) & 0x7F) + 1, height = ((b - 1) >> 7) - ((a - 1) >> 7) + 1,
	// neither ordered; per try x = R % width + x0 and y = R % height + y0 with unsigned divisions (div, 0x62C7CF / 0x62C7DC), a cell inside 0..127 (0x5D7260, signed) of type 1 (0x67F5B0) and the
	// staff route 0x62EB10 to its centre (0x80, 0x80) ends it with 1. A negative width or height is an unsigned divisor above every draw (at most 2^31), so x = R + x0 and nearly every try leaves the
	// map; a zero one (corners one cell apart, reversed) is a divide error, a crash in the original, which a restore refuses (ThemeParkIncStaffMember.IsValidWorld) and no OpenTPW setter writes.
	// After 30 tries: the debug print 0x9D2AD8 and the thought 0x1E (0x638960, presentation: tp.exe's 0x00506F30 asks for 0x16), 0
	internal static bool Fallback( GuestPathGrid grid, GuestNavigation walk, (int X0, int Y0, int X1, int Y1) area, WorldRng world, bool parkOpen )
	{
		var (x0, y0, x1, y1) = area;
		var width = unchecked( (uint)(x1 - x0 + 1) );
		var height = unchecked( (uint)(y1 - y0 + 1) );
		for ( var tries = 0; tries < 30; tries++ )
		{
			var x = unchecked( (int)(world.NextWord() % width + (uint)x0) );
			var y = unchecked( (int)(world.NextWord() % height + (uint)y0) );
			// [BIN:TPI-EXE:0x62C84E patrol entry route] the staff route 0x62EB10 to the cell's centre
			if ( x is >= 0 and <= WalkEdges.LastCell && y is >= 0 and <= WalkEdges.LastCell && grid.InBounds( x, y ) && grid.IsWalkable( x, y )
				&& StaffRoute( grid, walk, parkOpen, (x, y), GuestWander.CentreByte, GuestWander.CentreByte ) )
				return true;
		}
		return false;
	}
}
