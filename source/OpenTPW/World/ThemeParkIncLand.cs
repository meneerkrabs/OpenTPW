namespace OpenTPW;

/// <summary>
/// Theme Park Inc's owned land (docs/reverse/TPI-BOUNDARY.md): one bit per map square, the flag 0x40 (<see cref="ParkCellFlags.Unowned"/>). A new world marks every
/// square unowned but the NoModify ones, opens the gate strip beside the entrance and clears the zone's <c>hoardings.map</c>; each mission that loads new hoardings
/// clears <c>extents/extent.NNN</c> with the same loader. Land is never taken away, no fence is laid in career mode and there is no Buy Land tool.
/// </summary>
public static class ThemeParkIncLand
{
	/// <summary>The bytes of a <c>hoardings.map</c> or <c>extent.NNN</c> file: one per cell of the 128 x 128 map.</summary>
	public const int MapFileBytes = 128 * 128;

	/// <summary>The world's starting land file in a zone folder.</summary>
	public static string HoardingsMapPath( string level ) => $"/levels/{level}/hoardings.map";

	/// <summary>The land file the mission tick loads for the mission counter <paramref name="index"/> (the counter before it advances).</summary>
	// [BIN:TPI-EXE:0x5F0B66 extent path] the data root (0x771420) + "\" + "extents" + "\", formatted "%sextent.%03d" (0x9CF714) with career+0x105C
	public static string ExtentPath( int index ) =>
		index is >= 0 and <= 999 ? $"/extents/extent.{index:D3}" : throw new ArgumentOutOfRangeException( nameof( index ), index, "an extent number has three digits" );

	/// <summary>
	/// The new world's land (the world init and the fence setup <c>0x677CF0</c> for a game that is not coaster-edit mode): every square without NoModify becomes unowned,
	/// the gate strip beside the entrance is owned, then <paramref name="hoardingsMap"/> clears the zone's starting land (<see cref="ApplyLandFile"/>). Returns the loader's result.
	/// </summary>
	/// <param name="entranceA">The park entrance's first cell (<c>FixedItemInfo.EntranceAPos</c>).</param>
	/// <param name="entranceB">Its second cell (<c>FixedItemInfo.EntranceBPos</c>).</param>
	// [BIN:TPI-EXE:0x67F970 map square init] every square ends with 'or 0x40' on its flag word unless the word holds 0x20 (0x67FA7D..0x67FA89 on terrain, 0x67FAEB..0x67FAF5 off it)
	// [BIN:TPI-EXE:0x677D35 fence posts] entrance 0 (0x5D7A70: FixedItemInfo.EntranceAPosX/Y at 0xA025D0/0xA025D4, the key order of 0x99BCC8) moved 4 cells west is post B (0x9D68AC/0x9D68B0); entrance 1 (0xA025D8/0xA025DC) moved 3 cells east is post A (0x9D68A4/0x9D68A8)
	// [BIN:TPI-EXE:0x677E90 no career fence] the clicks run only with the debug flag 0xA2738C & 0x400 or in coaster-edit mode ([0xC65B80] == 3): a career or online game lays no posts
	// [BIN:TPI-EXE:0x677F89 gate strip] for x from post B.x + 2 while x < post A.x the squares (x, A.y) and (x, A.y + 1) clear 0x40 (0x67ED10) and refresh (0x67FB80)
	// [BIN:TPI-EXE:0x67806A starting land] outside debug and coaster-edit mode the fence setup loads <level>\hoardings.map (0xB1DFF8) with 0x678B00
	public static bool SetUpNewWorld( ParkCellMap cells, (int X, int Y) entranceA, (int X, int Y) entranceB, byte[] hoardingsMap )
	{
		ArgumentNullException.ThrowIfNull( cells );
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				var flags = cells.FlagsAt( x, y );
				if ( (flags & ParkCellFlags.NoModify) == 0 && (flags & ParkCellFlags.Unowned) == 0 )
					cells.SetFlags( x, y, flags | ParkCellFlags.Unowned );
			}
		}
		cells.HasThemeParkIncLand = true;
		var postB = (X: entranceA.X - 4, Y: entranceA.Y);
		var postA = (X: entranceB.X + 3, Y: entranceB.Y);
		for ( var x = postB.X + 2; x < postA.X; x++ )
		{
			Own( cells, x, postA.Y );
			Own( cells, x, postA.Y + 1 );
		}
		return ApplyLandFile( cells, hoardingsMap );
	}

	/// <summary>
	/// The land loader <c>0x678B00</c> for <c>hoardings.map</c> and <c>extent.NNN</c>: byte k is cell (x = k / 128, y = k % 128), x-major; a 0 byte off the map border
	/// clears the square's 0x40, any other byte does nothing. A file shorter than <see cref="MapFileBytes"/> keeps the cells read before it ended and answers false.
	/// </summary>
	// [BIN:TPI-EXE:0x678B2F land loader] the outer loop is x (ebp, 0..0x7F), the inner y (edi, 0..0x7F), the square id y * 128 + x + 1 (0x678B3F); each byte is read alone (0x7CC930, 1 byte) and a failed read returns 0 at once (0x678B6F..0x678B7A), the earlier cells kept
	// [BIN:TPI-EXE:0x678B80 border skip] a byte is applied only when it is 0 and x is neither 0 nor width - 1 (0x64E8D0) and y neither 0 nor height - 1 (0x64E8F0): extent.000 has 36 border zeros, the water hoardings.map 13, and none of them is owned
	// [BIN:TPI-EXE:0x678BE0 owned square] the square clears 0x40 (0x67ED10(0x40)); the fence walk mark 0x676530(1) follows
	public static bool ApplyLandFile( ParkCellMap cells, byte[] bytes )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( bytes );
		if ( cells.Width != 128 || cells.Height != 128 )
			throw new ArgumentException( "Theme Park Inc's land files cover a 128 x 128 map.", nameof( cells ) );
		// the post check runs over every cell the file would own before any cell changes, so a refused file leaves the land as it was
		foreach ( var (x, y) in OwnedCells( cells, bytes ) )
			RefusePost( cells, x, y );
		foreach ( var (x, y) in OwnedCells( cells, bytes ) )
			Own( cells, x, y );
		return bytes.Length >= MapFileBytes;
	}

	/// <summary>The cells the loader owns from <paramref name="bytes"/> in its x-major order: 0 bytes off the border, up to the file's end.</summary>
	private static IEnumerable<(int X, int Y)> OwnedCells( ParkCellMap cells, byte[] bytes )
	{
		for ( var x = 0; x < 128; x++ )
		{
			for ( var y = 0; y < 128; y++ )
			{
				var index = x * 128 + y;
				if ( index >= bytes.Length )
					yield break;
				if ( bytes[index] != 0 || x == 0 || x == cells.Width - 1 || y == 0 || y == cells.Height - 1 )
					continue;
				yield return (x, y);
			}
		}
	}

	/// <summary>Reads <paramref name="path"/> through the game's file system and applies it (<see cref="ApplyLandFile"/>); a missing file answers false and changes nothing.</summary>
	// [BIN:TPI-EXE:0x678B29 open failure] a negative handle from 0x7CC390 returns 0 before any cell is touched
	public static bool ApplyLandFile( ParkCellMap cells, string path )
	{
		ArgumentNullException.ThrowIfNull( cells );
		if ( !FileSystem.FileExists( path ) )
			return false;
		return ApplyLandFile( cells, FileSystem.ReadAllBytes( path ) );
	}

	/// <summary>The mission tick's land growth: <c>extents/extent.NNN</c> for <paramref name="index"/>, the mission counter before it advances (<see cref="ExtentPath"/>).</summary>
	// [BIN:TPI-EXE:0x5F0B66 mission extent] the tick loads the extent only when the mission's Completion.LoadNewHoardings (career+0x10DC) is 1, before 0x74C1B0 advances the counter
	public static bool ApplyExtent( ParkCellMap cells, int index ) => ApplyLandFile( cells, ExtentPath( index ) );

	/// <summary>The land of an imported Theme Park Inc park save: every map square takes its saved flag 0x40, nothing is reloaded.</summary>
	// [BIN:TPI-EXE:0x678B00 land loader callers] only the new-world fence setup (0x67809F) and the mission tick (0x5F0B66) call the loader, so a loaded park keeps the squares' saved mFlags (the cell serializer 0x5C19C9..0x5C1D1A writes mFlags)
	// [DATA:PreBuilt/prebuilt.TPWS:map flags] the prebuilt Science park's 1259 squares without 0x40 are exactly the science hoardings.map's 0 bytes off the border, and no square carries a fence post
	public static void ImportSaved( ParkCellMap cells, Func<int, int, bool> savedUnowned )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( savedUnowned );
		cells.HasThemeParkIncLand = true;
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				var flags = cells.FlagsAt( x, y );
				var unowned = savedUnowned( x, y );
				if ( unowned != ((flags & ParkCellFlags.Unowned) != 0) )
					cells.SetFlags( x, y, unowned ? flags | ParkCellFlags.Unowned : flags & ~ParkCellFlags.Unowned );
			}
		}
	}

	/// <summary>The number of map squares without the flag 0x40.</summary>
	public static int OwnedCount( ParkCellMap cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var count = 0;
		for ( var y = 0; y < cells.Height; y++ )
			for ( var x = 0; x < cells.Width; x++ )
				if ( (cells.FlagsAt( x, y ) & ParkCellFlags.Unowned) == 0 )
					count++;
		return count;
	}

	private static void Own( ParkCellMap cells, int x, int y )
	{
		if ( !cells.InBounds( x, y ) )
			return;
		var flags = cells.FlagsAt( x, y );
		if ( (flags & ParkCellFlags.Unowned) != 0 )
			cells.SetFlags( x, y, flags & ~ParkCellFlags.Unowned );
	}

	/// <summary>
	/// The loader's structure-record step for a newly owned cell: a hoarding post there is removed and the record's own 0x40 cleared before its mesh refresh. OpenTPW's
	/// Theme Park Inc worlds hold no posts (career mode lays none and the prebuilt park has none), and its track layer has no 0x40 bit, so a post here is refused
	/// before any cell changes; porting the post branch belongs with coaster-edit mode, the only mode that lays posts.
	/// </summary>
	// [BIN:TPI-EXE:0x678BFB post removal] the record of the square (0x5C17F0; an extension, 0x685760, through its parent +0x10) is tested for a hoarding post (0x685710, type 0x19): clear 0x20 (0x684A30), remove it (0x685DE0(0,0,0); the 0x15 branch 0x686880 cannot follow a type-0x19 test), 0x65CCE0(0) and 0x680A10 twice; every record then clears 0x40 (0x684A30(0x40)) and refreshes its mesh (0x685A70)
	private static void RefusePost( ParkCellMap cells, int x, int y )
	{
		if ( QueueCellValidator.StructureTypeAt( cells, x, y ) == ParkCellTypes.Hoarding )
			throw new InvalidOperationException( $"Theme Park Inc land cell ({x}, {y}) holds a fence post, which a career world never lays." );
	}
}
