namespace OpenTPW;

/// <summary>
/// The things of a new park whose bodies do not depend on the theme, built from the original's constructors and serializers instead of carried from a template save: the park analyser, the advisor
/// controller, the tag system, the UI message receiver, the mechanic HQ and the staff HQ. The four <c>restart.INTS</c> the original writes at the start of a park (one per theme) hold these six things
/// byte for byte alike, which <c>SaveNewParkThingsTests</c> checks the builders against. A thing's base is its position (the singletons stand at the map's middle, 128, 128) and its two chain links.
/// </summary>
public static class SaveNewParkThings
{
	/// <summary>The thing base serializer 0xfa808: X, Y, map child and map parent as u16 (the singletons of a new park sit at 128, 128 and link the thing ids before and after them).</summary>
	private static void Base( SaveByteWriter writer, ushort mapChild, ushort mapParent )
	{
		writer.U16( 128 );
		writer.U16( 128 );
		writer.U16( mapChild );
		writer.U16( mapParent );
	}

	/// <summary>
	/// The tag system (constructor <c>0x509B40</c>): the base alone. The staff HQ (<c>0x5089C0</c>) and the mechanic HQ (<c>0x4DA920</c>) write zeros after it and the UI message receiver (<c>0x50C9B0</c>)
	/// nothing, in a park nothing has happened in.
	/// </summary>
	public static byte[] TagSystem( ushort mapChild, ushort mapParent ) => OnlyBase( mapChild, mapParent );

	public static byte[] UIMsgReceiver( ushort mapChild, ushort mapParent ) => OnlyBase( mapChild, mapParent );

	// [BIN:TPW-EXE:0x4DA920 mechanic HQ] two bytes after the base, zero in a new park
	public static byte[] MechanicHq( ushort mapChild, ushort mapParent ) => Zeros( OnlyBase( mapChild, mapParent ), 2 );

	// [BIN:TPW-EXE:0x5089C0 staff HQ] the force-strike word 4, the picked-up id 2, five 12-byte strike levels, five 4-byte training budgets and the trained byte (SaveStaffHq): all zero in a new park
	public static byte[] StaffHq( ushort mapChild, ushort mapParent ) => Zeros( OnlyBase( mapChild, mapParent ), 4 + 2 + SaveStaffHq.Roles * 12 + SaveStaffHq.Roles * 4 + 1 );

	private static byte[] OnlyBase( ushort mapChild, ushort mapParent )
	{
		var writer = new SaveByteWriter();
		Base( writer, mapChild, mapParent );
		return writer.ToArray();
	}

	private static byte[] Zeros( byte[] prefix, int count ) => prefix.Concat( new byte[count] ).ToArray();

	/// <summary>
	/// The advisor controller of a new park (constructor <c>0x599E70</c>, body <see cref="SaveAdvisorController"/>): the eight pending records and the 351 history entries as the constructor leaves them (free
	/// records, history of tick 0, previous variant -1, not played, no slaps) and the controller's words 0. The first pending record is valid in all four restart saves: message 0 at score 100,000 with
	/// no response yet; the code that posts it at the park's start is not read.
	/// </summary>
	// [BIN:TPW-EXE:0x599E70 advisor constructor] the valid byte of each of the eight 0x18-byte pending slots (from +0x28) is 0, +0xC, +0x10 (time signature, sound), +0xD4, +0xD8 and +0xDC are 0, each of the 0x15F (351) history entries from +0xE0 is {0, 0xFFFFFFFF, 0, 0} and +0x16E4 (requested last tutorial) is 0
	// [APPROX:SAVE-012] the first pending record (valid, message 0, score 100,000, response -1) is observed in the four restart.INTS, not read from tp.exe: the constructor leaves every slot free and the code that posts it at the park's start is not found
	public static byte[] AdvisorController( ushort mapChild, ushort mapParent )
	{
		var writer = new SaveByteWriter();
		Base( writer, mapChild, mapParent );
		writer.U32( 0 );
		writer.U32( 0 );
		for ( var slot = 0; slot < SaveAdvisorController.PendingRecords; slot++ )
		{
			var first = slot == 0;
			writer.U16( 0 );
			writer.U32( 0 );
			writer.U32( 0 );
			writer.I32( first ? 100_000 : 0 );
			writer.U8( first ? (byte)1 : (byte)0 );
			writer.I32( first ? -1 : 0 );
			writer.U8( 0 );
			writer.U8( 0 );
		}
		for ( var word = 0; word < 3; word++ )
			writer.U32( 0 );
		for ( var entry = 0; entry < SaveAdvisorController.HistoryEntries; entry++ )
		{
			writer.U32( 0 );
			writer.I32( -1 );
			writer.U32( 0 );
			writer.U32( 0 );
		}
		// the current record is free and so is the last tutorial request
		for ( var index = 0; index < SaveAdvisorRecord.Bytes; index++ )
			writer.U8( 0 );
		writer.U8( 0 );
		writer.U32( 0 );
		return writer.ToArray();
	}

	/// <summary>The park analyser's grid: 4 map squares per cell, 32 × 32 cells over the 128 × 128 map.</summary>
	public const int MapSquaresPerCell = 4, GridSize = 32, MapSize = 128;

	/// <summary>The calendar timestamp the analyser's six "last time" stamps hold in a new park, 100 ns since 1601-01-01 of the calendar's start date (<c>0x4F8690</c> at 2000-01-01).</summary>
	public static long ZeroTimestamp => (ParkCalendarEpoch - new DateTime( 1601, 1, 1 )).Ticks;

	private static readonly DateTime ParkCalendarEpoch = new( 2000, 1, 1 );

	/// <summary>
	/// The park analyser of a new park (constructor <c>0x4C58C0</c>, serializer <c>0x4C5D70</c>): the grid fields, 1,024 cell records whose extents the setup <c>0x4C7A90</c> fills from the grid, the history
	/// rings and extreme trackers at their constructor values, and the counters 0. 73,536 bytes with the base.
	/// </summary>
	// [BIN:TPW-EXE:0x4C58C0 analyser constructor] mMapSquaresPerCell +0xC = 4, mGridSizeX/Y +0x10/+0x14 = 0x20, mMapSizeX/Y +0x18/+0x1C = 0x80; 0x400 cell records (0x4C5D10): two trackers at 0xC479C000 and seven at -999 (0xFFFFFC19), each with a flag byte 0; four small rings (0x4CDB60: current -1, 3 entries), six -999 trackers, sixteen rings of 0x90 entries, one of 0x32 and one of 5 (-1 current), twenty byte rings of 0x32 entries (0x4CE9E0), the counters 0 and the six timestamps zeroed (0x4C5B54-0x4C5B9C) and then stamped with the park time of the new park's turn 0 (0x4F8690, 0x4C5BF3-0x4C5CDE)
	// [BIN:TPW-EXE:0x4C7A90 analyser setup] cell i of the grid has StartX = (i mod 0x20) x 4, StartY = (i / 0x20) x 4 and the extent 4 x 4, cut short at the map's edge
	// [BIN:TPW-EXE:0x4C5D70 analyser serializer] the order of the fields: the five grid words, the 0x400 cell records (0x4C85B0: StartX, StartY, SizeX, SizeY and nine {i32, u8} pairs), 4 x 0x4CDB80 (current i32, entries i32, wrapped u8, temp u16, entries u16), 6 x 0x4CD9C0 ({i32, u8}), 16 + 2 rings of i32 (0x4CDF00, 0x4CE2D0, 0x4CE660: current, entries, wrapped u8, temp i32, entries i32), 20 x 0x4CEA00 (current, entries, wrapped, temp u8, entries u8), mMisbehavingKids, mMisbehavingKidsThatGotAwayWithIt, mMisbehavingKidsEver, mLifetimeVisitors (i32), six 8-byte timestamps, mMostPaidForTicket, mLargestBusLoad, mLongestStay (i32)
	public static byte[] ParkAnalyser( ushort mapChild, ushort mapParent )
	{
		var writer = new SaveByteWriter();
		Base( writer, mapChild, mapParent );
		writer.I32( MapSquaresPerCell );
		writer.I32( GridSize );
		writer.I32( GridSize );
		writer.I32( MapSize );
		writer.I32( MapSize );
		for ( var index = 0; index < GridSize * GridSize; index++ )
		{
			var column = index % GridSize;
			var row = index / GridSize;
			writer.I32( column * MapSquaresPerCell );
			writer.I32( row * MapSquaresPerCell );
			writer.I32( Math.Min( MapSquaresPerCell, MapSize - column * MapSquaresPerCell ) );
			writer.I32( Math.Min( MapSquaresPerCell, MapSize - row * MapSquaresPerCell ) );
			for ( var tracker = 0; tracker < 9; tracker++ )
				Tracker( writer, tracker < 2 ? unchecked( (int)0xC479C000 ) : -999 );
		}
		for ( var ring = 0; ring < 4; ring++ )
		{
			writer.I32( -1 );
			writer.I32( 3 );
			writer.U8( 0 );
			writer.U16( 0 );
			for ( var entry = 0; entry < 3; entry++ )
				writer.U16( 0 );
		}
		for ( var tracker = 0; tracker < 6; tracker++ )
			Tracker( writer, -999 );
		for ( var ring = 0; ring < 16; ring++ )
			WordRing( writer, 0x90 );
		WordRing( writer, 0x32 );
		WordRing( writer, 5 );
		for ( var ring = 0; ring < 20; ring++ )
		{
			writer.I32( -1 );
			writer.I32( 0x32 );
			writer.U8( 0 );
			writer.U8( 0 );
			writer.Bytes( new byte[0x32] );
		}
		for ( var counter = 0; counter < 4; counter++ )
			writer.I32( 0 );
		for ( var stamp = 0; stamp < 6; stamp++ )
			writer.I64( ZeroTimestamp );
		for ( var counter = 0; counter < 3; counter++ )
			writer.I32( 0 );
		return writer.ToArray();
	}

	private static void Tracker( SaveByteWriter writer, int value )
	{
		writer.I32( value );
		writer.U8( 0 );
	}

	private static void WordRing( SaveByteWriter writer, int entries )
	{
		writer.I32( -1 );
		writer.I32( entries );
		writer.U8( 0 );
		writer.I32( 0 );
		writer.Bytes( new byte[entries * 4] );
	}
}
