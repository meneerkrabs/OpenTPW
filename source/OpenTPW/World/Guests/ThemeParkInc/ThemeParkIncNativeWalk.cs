namespace OpenTPW;

/// <summary>
/// A Theme Park Inc walker's person words around its navigator (docs/reverse/TPI-NAVIGATOR.md, NV5): what the person serializer <c>0x609170</c> transfers for the walk and what its re-route
/// <c>0x609B55</c> reads and writes. A native guest (<see cref="Guest"/> with <see cref="Guest.Tpi"/>) and a Theme Park Inc staff member (<see cref="ThemeParkIncStaffMember"/>) are walkers.
/// </summary>
internal interface IThemeParkIncWalker
{
	/// <summary>The navigator (person <c>+0xEC</c>).</summary>
	GuestNavigation Walk { get; }
	/// <summary><c>mAccurateDestX/Y</c> (<c>+0x28</c>, <c>+0x2A</c>): the words of the last route request's point.</summary>
	int AccurateDestX { get; set; }
	int AccurateDestY { get; set; }
	/// <summary><c>mStrandedTime</c> (<c>+0x1B0</c>).</summary>
	int StrandedTime { get; set; }
	/// <summary><c>mPreviousX/Y</c> (<c>+0x1A8</c>, <c>+0x1AC</c>): the walk position the speed step copied.</summary>
	int PreviousX { get; set; }
	int PreviousY { get; set; }
	/// <summary><c>mBaseSpeed</c> (<c>+0xD4</c>), <c>mPurposeSpeed</c> (<c>+0xD6</c>), <c>mAdjustorSpeed</c> (<c>+0xD8</c>), <c>mPreviousSpeed</c> (<c>+0xDC</c>).</summary>
	int BaseSpeedWord { get; set; }
	int PurposeSpeed { get; set; }
	ushort AdjustorSpeed { get; set; }
	float PreviousSpeed { get; set; }
	/// <summary><c>mSpriteAngle</c> (<c>+0x2C</c>): the heading word.</summary>
	int SpriteAngle { get; set; }
	/// <summary><c>mSetDestSuccessfully</c> (<c>+0xE8</c>): the wander flag.</summary>
	bool SetDestSuccessfully { get; set; }
}

/// <summary>
/// The JSON form of a Theme Park Inc walker's native walk storage (navigator phase NV5): the person words OpenTPW's snapshot does not hold elsewhere and the navigator's 177-byte record. A native guest's
/// heading word, wander flag, speeds and stranded mark are its record's and the shared guest's (<see cref="ThemeParkIncGuestSnapshot.Heading"/>, <see cref="ThemeParkIncGuestSnapshot.Wandered"/>,
/// <see cref="GuestSnapshot.BaseSpeed"/>...); a staff member's are the staff world snapshot's. Every other walk field of OpenTPW (the route walk's bookkeeping, the cells, the offsets, the heading vector)
/// is derived on restore (<see cref="ThemeParkIncNativeWalk"/>).
/// </summary>
public sealed record ThemeParkIncPersonWalkSnapshot
{
	/// <summary><c>mAccurateDestX</c>, <c>mAccurateDestY</c> (<c>+0x28</c>, <c>+0x2A</c>), 0..0xFFFF.</summary>
	public required int AccurateDestX { get; init; }
	public required int AccurateDestY { get; init; }
	/// <summary><c>mPreviousX</c>, <c>mPreviousY</c> (<c>+0x1A8</c>, <c>+0x1AC</c>): the walk position (16.16 cells) the last speed step copied, the start of the turn's interpolation.</summary>
	public required int PreviousX { get; init; }
	public required int PreviousY { get; init; }
	/// <summary>The navigator (<c>+0xEC</c>) as its serializer <c>0x63AD30</c> writes it (<see cref="GuestNavigation.ToRecord"/>), 177 bytes.</summary>
	public required byte[] Navigator { get; init; }

	/// <summary>Why the snapshot cannot be a walker's on a <paramref name="width"/> × <paramref name="height"/> map, or null.</summary>
	internal string? Problem( int width, int height )
	{
		if ( AccurateDestX is < 0 or > ushort.MaxValue || AccurateDestY is < 0 or > ushort.MaxValue )
			return $"the accurate destination ({AccurateDestX}, {AccurateDestY}) is not two words";
		if ( Navigator is not { Length: SaveNavigation.Bytes } )
			return $"the navigator record is not {SaveNavigation.Bytes} bytes";
		var record = SaveNavigation.Read( Navigator );
		if ( GuestNavigation.RecordProblem( record ) is { } problem )
			return problem;
		if ( (uint)(record.Pos.X.Raw >> 16) >= (uint)width || (uint)(record.Pos.Y.Raw >> 16) >= (uint)height )
			return $"the walk position ({record.Pos.X.Raw >> 16}, {record.Pos.Y.Raw >> 16}) is outside the map";
		if ( record.NavMode is not (0 or 1) )
			return $"the walk mask {record.NavMode} is not 0 or 1 (Theme Park Inc writes no other)";
		// the interpolation's start converts exactly to OpenTPW's presentation floats below 2^24
		if ( Math.Abs( (long)PreviousX ) >= 1 << 24 || Math.Abs( (long)PreviousY ) >= 1 << 24 )
			return $"the turn's start point ({PreviousX}, {PreviousY}) is outside the map";
		return null;
	}

	internal static ThemeParkIncPersonWalkSnapshot Capture( IThemeParkIncWalker walker ) => new()
	{
		AccurateDestX = walker.AccurateDestX,
		AccurateDestY = walker.AccurateDestY,
		PreviousX = walker.PreviousX,
		PreviousY = walker.PreviousY,
		Navigator = walker.Walk.ToRecord().Raw
	};
}

/// <summary>
/// Theme Park Inc's route requests to a point and the stranded test (<c>0x60B8E0</c>, <c>0x60BB50</c>) on any walker, and the person serializer's walk semantics (<c>0x609170</c> with its re-route
/// <c>0x609B55</c>): the native walk storage of navigator phase NV5 (docs/reverse/TPI-NAVIGATOR.md). OpenTPW's own Theme Park Inc resume restores a walker verbatim; the park writer's native loader
/// (its phases N2 onwards) and native writer use <see cref="Load"/>, <see cref="LoadAll"/> and <see cref="ReRouteAfterSave"/>.
/// </summary>
internal static class ThemeParkIncNativeWalk
{
	/// <summary>
	/// The stranded test <c>0x60BB50</c>: the reference cell is <paramref name="start"/> (the person's cell, <c>+5</c>/<c>+7</c>), or for a queue cell the back of its queue; true (still stranded) while the
	/// largest block stamp of the nine cells around it is below <paramref name="mark"/> (unsigned).
	/// </summary>
	// [BIN:TPI-EXE:0x60BB50 stranded test] the cell word of +5/+7; a queue cell (0x67F5D0: type 3 or 9) is followed to its queue's back (0x5E0A50 until 0); the nine pairs 0xAA0060 (set by 0x60BA80: (-1, +1),
	// (0, +1), (+1, +1), (-1, 0), (0, 0), (+1, 0), (-1, -1), (0, -1), (+1, -1)) offset the word, each cell's block stamp (0x5D8260) taken while the maximum is not above the mark; 1 when the maximum is below the
	// mark (tp.exe's 0x004FA770, the same code)
	internal static bool StrandedHolds( GuestPathGrid grid, (int X, int Y) start, int mark )
	{
		var cell = start;
		if ( grid.IsQueue( cell.X, cell.Y ) )
		{
			for ( var guard = 0; guard < 1024; guard++ )
			{
				if ( RideVisitorBridge.NextQueueCell( grid, cell ) is not { } behind )
					break;
				cell = behind;
			}
		}
		var id = (ushort)(cell.Y * 128 + cell.X + 1);
		var largest = 0u;
		for ( var dy = -1; dy <= 1; dy++ )
		{
			for ( var dx = -1; dx <= 1; dx++ )
			{
				var offset = (uint)(ushort)(id + dy * 128 + dx) - 1;
				largest = Math.Max( largest, unchecked( (uint)grid.Cells.BlockStamp( (int)((offset & 0x7F) >> 4), (int)(offset >> 11) ) ) );
			}
		}
		return largest < unchecked( (uint)mark );
	}

	/// <summary>
	/// The route request to a point <c>0x60B8E0</c> (x word, y word) on <paramref name="walker"/>: the stamp counter raised, a counter below the mark clears it, a mark still held by the stranded test
	/// answers false with nothing else changed; else the accurate destination takes the words, the mark is cleared and SetDest (<c>0x63EE80</c>) walks to the words &lt;&lt; 8 (its answer).
	/// </summary>
	// [BIN:TPI-EXE:0x60B8E0 route to a point] 0x5D81F0 (0x60B8F4) below +0x1B0 (jae, unsigned) clears it; +0x1B0 nonzero and 0x60BB50 → 0; else +0x2A = y word (0x60B92A), +0x1B0 = 0, +0x28 = x word
	// (0x60B94C), SetDest 0x63EE80 (0x60B950) with the signed words << 8 (movsx, shl 8)
	internal static bool RouteToPoint( GuestPathGrid grid, IThemeParkIncWalker walker, int xWord, int yWord )
	{
		var map = grid.Cells;
		if ( ParkCellMap.StampBelow( map.NextStamp(), walker.StrandedTime ) )
			walker.StrandedTime = 0;
		var walk = walker.Walk;
		if ( walker.StrandedTime != 0 && StrandedHolds( grid, walk.Cell, walker.StrandedTime ) )
			return false;
		walker.AccurateDestY = yWord & 0xFFFF;
		walker.StrandedTime = 0;
		walker.AccurateDestX = xWord & 0xFFFF;
		return walk.Navigate( map, (short)xWord << 8, (short)yWord << 8 );
	}

	/// <summary>
	/// The re-route the person serializer ends with (<c>0x609B55</c>): the route request <c>0x60B8E0</c> to the person's own accurate destination. Its answer only selects a log line
	/// ("Couldn't renav to my accurate dest!!", a bare <c>ret</c> logger), so it is returned for the tests alone.
	/// </summary>
	// [BIN:TPI-EXE:0x609B55 person re-route] mov cx, [esi + 0x2A]; mov dx, [esi + 0x28]; push both; 0x60B8E0; a zero answer logs 0x9CFC84 through 0x4D15A0 (ret); the serializer then returns 0. Reached after the
	// save half (0x609756 jumps to the thought object at 0x609B3B), after the load half, and for a version outside 0..1 with nothing transferred; a failed transfer returns 1 at 0x609B48 and skips it
	internal static bool ReRoute( GuestPathGrid grid, IThemeParkIncWalker walker ) => RouteToPoint( grid, walker, walker.AccurateDestX, walker.AccurateDestY );

	/// <summary>
	/// The walk members of the person block (<see cref="ThemeParkIncPersonRecord"/>) as the save half writes them from the live walker: the cell bytes (the position &gt;&gt; 8), the accurate destination, the
	/// speed words, the navigator's record, the turn's start, the stranded mark, the wander flag and the heading word. The other members are <paramref name="rest"/>'s (zero without it).
	/// </summary>
	internal static ThemeParkIncPersonRecord Write( IThemeParkIncWalker walker, ThemeParkIncPersonRecord? rest = null )
	{
		var walk = walker.Walk;
		var record = rest ?? new ThemeParkIncPersonRecord { Navigator = new byte[SaveNavigation.Bytes] };
		return record with
		{
			X = unchecked( (ushort)(walk.X >> 8) ),
			Y = unchecked( (ushort)(walk.Y >> 8) ),
			AccurateDestX = (ushort)walker.AccurateDestX,
			AccurateDestY = (ushort)walker.AccurateDestY,
			AdjustorSpeed = walker.AdjustorSpeed,
			BaseSpeed = (ushort)walker.BaseSpeedWord,
			Navigator = walk.ToRecord().Raw,
			PreviousSpeed = BitConverter.SingleToUInt32Bits( walker.PreviousSpeed ),
			PreviousX = walker.PreviousX,
			PreviousY = walker.PreviousY,
			StrandedTime = walker.StrandedTime,
			PurposeSpeed = (ushort)walker.PurposeSpeed,
			SetDestSuccessfully = walker.SetDestSuccessfully ? 1u : 0u,
			SpriteAngle = unchecked( (uint)walker.SpriteAngle )
		};
	}

	/// <summary>
	/// The person serializer's save half on a live walker (<c>0x609170</c> mode 1, decision N-1): the block as written (<see cref="Write"/>), then the re-route on the live walker, which changes its navigator,
	/// its mark and the stamp counter but not the block. Returns the block.
	/// </summary>
	internal static ThemeParkIncPersonRecord SaveAndReRoute( GuestPathGrid grid, IThemeParkIncWalker walker, ThemeParkIncPersonRecord? rest = null )
	{
		var record = Write( walker, rest );
		ReRouteAfterSave( grid, walker );
		return record;
	}

	/// <summary>The re-route that follows the save half (<c>0x609B55</c>, navigator decision N-1): the live session continues after a save as the original's does.</summary>
	internal static bool ReRouteAfterSave( GuestPathGrid grid, IThemeParkIncWalker walker ) => ReRoute( grid, walker );

	/// <summary>
	/// The person serializer's load half (<c>0x609170</c> mode 0) on a walker: every walk member verbatim (the navigator by its serializer's load half, which leaves the unnamed <c>+0x30</c> zero: OpenTPW does
	/// not model it), then the re-route (<c>0x609B55</c>) with the park's stamp counter and block stamps as they stand. The record's other members are the park writer's. Returns the re-route's answer.
	/// </summary>
	// [BIN:TPI-EXE:0x60975B person load] the members in the save half's order; mSpriteID modulo the sprite count of mESPSprite (0x6902D0, 0x609AB0: presentation); the navigator 0x63AD30 mode 0 zeroes +0x30
	// (0x63B9AB) and +0x34 (0x63B64A); then 0x609B3B (the thought object) and the re-route 0x609B55, before the guest's (0x60D284) or the staff base's (0x6267E4) own members are read
	internal static bool Load( GuestPathGrid grid, IThemeParkIncWalker walker, ThemeParkIncPersonRecord record )
	{
		Apply( walker, record );
		return ReRoute( grid, walker );
	}

	/// <summary>
	/// Why <paramref name="record"/>'s walk members cannot be a walker's, or null: a navigator record OpenTPW's walk cannot hold (<see cref="GuestNavigation.RecordProblem"/>), cell bytes other than the
	/// navigator's position &gt;&gt; 8 (the chain update <c>0x637B70</c> writes them with every position; OpenTPW derives them), or a wander flag other than 0 or 1 (the only values the game stores).
	/// </summary>
	internal static string? Problem( ThemeParkIncPersonRecord record )
	{
		var navigator = SaveNavigation.Read( record.Navigator );
		if ( GuestNavigation.RecordProblem( navigator ) is { } problem )
			return problem;
		if ( record.X != unchecked( (ushort)(navigator.Pos.X.Raw >> 8) ) || record.Y != unchecked( (ushort)(navigator.Pos.Y.Raw >> 8) ) )
			return $"the cell bytes ({record.X:X4}, {record.Y:X4}) are not the navigator's position";
		if ( record.SetDestSuccessfully > 1 )
			return $"the wander flag {record.SetDestSuccessfully} is not 0 or 1";
		return null;
	}

	/// <summary>The walk members of <paramref name="record"/> on the walker, without the re-route; <see cref="Problem"/> must accept the record.</summary>
	internal static void Apply( IThemeParkIncWalker walker, ThemeParkIncPersonRecord record )
	{
		if ( Problem( record ) is { } problem )
			throw new InvalidDataException( $"The Theme Park Inc person block cannot be a walker's: {problem}." );
		var walk = walker.Walk;
		walk.RestoreNativeRecord( record.Navigator );
		walker.AccurateDestX = record.AccurateDestX;
		walker.AccurateDestY = record.AccurateDestY;
		walker.AdjustorSpeed = record.AdjustorSpeed;
		walker.BaseSpeedWord = record.BaseSpeed;
		walker.PreviousSpeed = BitConverter.UInt32BitsToSingle( record.PreviousSpeed );
		walker.PreviousX = record.PreviousX;
		walker.PreviousY = record.PreviousY;
		walker.StrandedTime = record.StrandedTime;
		walker.PurposeSpeed = record.PurposeSpeed;
		walker.SetDestSuccessfully = record.SetDestSuccessfully != 0;
		walker.SpriteAngle = unchecked( (int)record.SpriteAngle );
	}

	/// <summary>
	/// A native park load's persons (the park writer's loader, its phase N2 onwards): the map load zeroes the block stamps (<c>0x5D7126</c>) and a new process starts the stamp counter at 0
	/// (<paramref name="freshProcess"/>; a load inside a running game keeps it), then each person's block is loaded and re-routed the moment it is read, in the file's thing order.
	/// </summary>
	// [BIN:TPI-EXE:0x5D7126 block stamps] the load half of the map serializer 0x5D6D40 ends with rep stosd of 0x441 dwords at map +0x1C0000 (the 33 × 33 block stamps); the counter 0x5D81F0 ([0xA9D2E8]) is
	// written by no load: 0 in a new process. The world loader 0x64BC00 reads the thing chunks in file order (the save's used-thing walk from [0xAA116C], 0x64AFD1), each person re-routed as it is read
	internal static void LoadAll( GuestPathGrid grid, IEnumerable<(IThemeParkIncWalker Walker, ThemeParkIncPersonRecord Record)> persons, bool freshProcess )
	{
		var map = grid.Cells;
		map.RestoreBlockStamps( freshProcess ? 0 : map.StampCounter, null );
		foreach ( var (walker, record) in persons )
			Load( grid, walker, record );
	}
}
